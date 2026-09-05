using System.ComponentModel;
using System.Globalization;
using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc;
using BootCampPerformanceControl.FanControl.Smc.CrystalIdea;
using BootCampPerformanceControl.FanControl.Smc.Windows;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.Logging;

const string expectedModel = VerifiedHardwareModels.MacBookPro16_1;
const string executeFlag = "--execute-macbookpro16-1-max-roundtrip";

Console.WriteLine("BootCamp Performance Control - SMC Write Round-Trip Test");
Console.WriteLine("RESEARCH ONLY: this tool can issue real AppleSMC fan writes only with the exact execution flag.");
Console.WriteLine();

using var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

try
{
    var hardwareDetection = new HardwareDetectionService(new ModelSupportRegistry());
    var hardware = await hardwareDetection.DetectAsync(CancellationToken.None);
    var model = hardware.ComputerSystem.Model;

    Console.WriteLine($"Model: {model}");

    if (!string.Equals(model, expectedModel, StringComparison.Ordinal))
    {
        Console.Error.WriteLine(
            $"REFUSED: the only verified write-research target is '{expectedModel}'.");
        return 2;
    }

    // The observed AppleSMC driver permits the research flow through one device
    // handle. Read and write adapters therefore share this single owned client.
    using var sharedDevice = new WindowsDeviceIoControlClient(
        CrystalIdeaAppleSmcTransport.DevicePath);
    await using var readTransport = new CrystalIdeaAppleSmcTransport(
        new NonOwningDeviceIoControlClient(sharedDevice));

    var probe = new FanCapabilityProbe(
        new AppleSmcProtocol(readTransport),
        new FanSafetyPolicy());

    var initialCapability = await probe.ProbeAsync(model, CancellationToken.None);
    PrintCapability("INITIAL", initialCapability);

    if (!initialCapability.IsReadSupported ||
        !initialCapability.IsHardwareSafetyGateSatisfied ||
        initialCapability.Snapshot is null)
    {
        Console.Error.WriteLine("REFUSED: hardware safety gate is not satisfied.");
        foreach (var failure in initialCapability.Failures)
        {
            Console.Error.WriteLine($"- {failure}");
        }

        return 3;
    }

    var executeRequested =
        args.Length == 1 &&
        string.Equals(args[0], executeFlag, StringComparison.Ordinal);

    if (!executeRequested)
    {
        Console.WriteLine();
        Console.WriteLine("WRITE PHASE: NOT ARMED");
        Console.WriteLine("No SMC write request was issued.");
        Console.WriteLine($"To arm the one-time physical round-trip, pass exactly: {executeFlag}");
        return 0;
    }

    Console.WriteLine();
    Console.WriteLine("WRITE PHASE: ARMED");
    Console.WriteLine("Target state is derived from verified live F0Mx/F1Mx and revalidated immediately before write.");
    Console.WriteLine("The tool will restore Apple Auto in a non-cancelable cleanup path.");

    var logger = new ConsoleApplicationLogger();
    var preflightPolicy = new FanOverridePreflightPolicy();
    var recoveryPolicy = new FanOverrideRecoveryPolicy();
    var ownershipStore = new JsonFanOverrideOwnershipStore(logger);

    await using var writeBackend = new CrystalIdeaFanSmcWriteBackend(
        new NonOwningDeviceIoControlClient(sharedDevice));

    var writer = new VerifiedFanOverrideWriter(
        writeBackend,
        probe,
        preflightPolicy,
        recoveryPolicy,
        logger);

    var coordinator = new FanOverrideCoordinator(
        preflightPolicy,
        recoveryPolicy,
        ownershipStore,
        writer,
        logger);

    var exitCode = 0;
    var applied = false;
    var proceedWithApply = true;
    ExpectedMaximumTargets? expectedMaximumTargets = null;

    try
    {
        // Recover an interrupted prior tool run before taking new ownership.
        var startupRecovery = await coordinator.RecoverAsync(
            model,
            initialCapability,
            cancellationSource.Token);

        Console.WriteLine($"Startup recovery: {startupRecovery.Action} - {startupRecovery.Reason}");
        if (startupRecovery.Action == FanOverrideRecoveryAction.Blocked)
        {
            Console.Error.WriteLine("REFUSED: existing ownership state could not be recovered safely.");
            exitCode = 4;
            proceedWithApply = false;
        }

        if (proceedWithApply)
        {
            var preApplyCapability = await probe.ProbeAsync(model, cancellationSource.Token);
            PrintCapability("PRE-APPLY", preApplyCapability);

            if (TryGetExpectedMaximumTargets(preApplyCapability, out var targets))
            {
                expectedMaximumTargets = targets;
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "Target state derived from verified live F0Mx/F1Mx: Fan 0 = {0:0.###} RPM; Fan 1 = {1:0.###} RPM.",
                    targets.Fan0TargetRpm,
                    targets.Fan1TargetRpm));
            }

            var result = await coordinator.ApplyMaximumSafeRpmAsync(
                model,
                preApplyCapability,
                cancellationSource.Token);

            if (!result.IsApplied || result.Marker is null)
            {
                Console.Error.WriteLine($"REFUSED: {result.Message}");
                exitCode = 5;
                proceedWithApply = false;
            }
        }

        if (proceedWithApply)
        {
            applied = true;
            Console.WriteLine();
            Console.WriteLine("MAXIMUM SAFE RPM APPLY: VERIFIED");
            Console.WriteLine("Ownership marker is active.");

            // Briefly allow the physical fans to ramp, then observe actual RPM.
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationSource.Token);
            var maximumCapability = await probe.ProbeAsync(model, cancellationSource.Token);
            PrintCapability("MAXIMUM", maximumCapability);

            if (expectedMaximumTargets is null ||
                !IsVerifiedMaximumState(maximumCapability, expectedMaximumTargets.Value))
            {
                throw new InvalidOperationException(
                    "Maximum state readback after the ramp delay did not remain at the live-derived manual targets.");
            }
        }
    }
    catch (OperationCanceledException exception)
    {
        Console.Error.WriteLine($"WRITE ROUND-TRIP CANCELED: {exception.Message}");
        exitCode = 6;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"WRITE ROUND-TRIP FAILED: {exception}");
        exitCode = 7;
    }
    finally
    {
        try
        {
            Console.WriteLine();
            Console.WriteLine("=== NON-CANCELABLE APPLE AUTO CLEANUP ===");

            var cleanupCapability = await probe.ProbeAsync(model, CancellationToken.None);
            var cleanupDecision = await coordinator.RecoverAsync(
                model,
                cleanupCapability,
                CancellationToken.None);

            Console.WriteLine($"Cleanup recovery: {cleanupDecision.Action} - {cleanupDecision.Reason}");

            var finalCapability = await probe.ProbeAsync(model, CancellationToken.None);
            PrintCapability("FINAL", finalCapability);

            if (!IsVerifiedAppleAutoState(finalCapability))
            {
                Console.Error.WriteLine("CRITICAL: final SMC readback did not verify Apple Auto on both fans.");
                exitCode = 8;
            }
            else
            {
                Console.WriteLine("APPLE AUTO RESTORE: VERIFIED");
                if (applied && exitCode == 0)
                {
                    Console.WriteLine("PHYSICAL SMC WRITE ROUND-TRIP: PASS");
                }
            }
        }
        catch (Exception cleanupException)
        {
            Console.Error.WriteLine("CRITICAL: Apple Auto cleanup could not be completed or verified.");
            Console.Error.WriteLine(cleanupException);
            exitCode = 9;
        }
    }

    return exitCode;
}
catch (Win32Exception exception)
{
    Console.Error.WriteLine($"Windows device access failed: {exception.Message}");
    Console.Error.WriteLine(
        "Close Macs Fan Control and make sure the locally installed AppleSMC service is running before retrying.");
    return 10;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"SMC WRITE ROUND-TRIP TOOL FAILED BEFORE WRITE: {exception}");
    return 11;
}

static bool TryGetExpectedMaximumTargets(
    FanControlCapabilityResult capability,
    out ExpectedMaximumTargets expectedTargets)
{
    expectedTargets = default;

    if (!capability.IsReadSupported ||
        !capability.IsHardwareSafetyGateSatisfied ||
        capability.Snapshot is null)
    {
        return false;
    }

    var snapshot = capability.Snapshot;
    expectedTargets = new ExpectedMaximumTargets(
        snapshot.Fans[0].Maximum.GetFloat32(),
        snapshot.Fans[1].Maximum.GetFloat32());
    return true;
}

static bool IsVerifiedMaximumState(
    FanControlCapabilityResult capability,
    ExpectedMaximumTargets expectedTargets)
{
    if (!capability.IsHardwareSafetyGateSatisfied || capability.Snapshot is null)
    {
        return false;
    }

    var snapshot = capability.Snapshot;
    return snapshot.Fans[0].Mode.GetUInt8() == 1 &&
           snapshot.Fans[1].Mode.GetUInt8() == 1 &&
           Math.Abs(snapshot.Fans[0].Maximum.GetFloat32() - expectedTargets.Fan0TargetRpm) <= 1f &&
           Math.Abs(snapshot.Fans[1].Maximum.GetFloat32() - expectedTargets.Fan1TargetRpm) <= 1f &&
           Math.Abs(snapshot.Fans[0].Target.GetFloat32() - expectedTargets.Fan0TargetRpm) <= 1f &&
           Math.Abs(snapshot.Fans[1].Target.GetFloat32() - expectedTargets.Fan1TargetRpm) <= 1f;
}

static bool IsVerifiedAppleAutoState(FanControlCapabilityResult capability)
{
    return capability.IsHardwareSafetyGateSatisfied &&
           capability.Snapshot is not null &&
           capability.Snapshot.Fans.All(fan => fan.Mode.GetUInt8() == 0);
}

static void PrintCapability(string label, FanControlCapabilityResult capability)
{
    Console.WriteLine();
    Console.WriteLine($"=== {label} SMC STATE ===");
    Console.WriteLine($"Read support: {capability.IsReadSupported}");
    Console.WriteLine($"Hardware safety gate: {capability.IsHardwareSafetyGateSatisfied}");
    Console.WriteLine($"Protocol: {capability.Protocol?.ToString() ?? "unknown"}");

    if (capability.Snapshot is null)
    {
        foreach (var failure in capability.Failures)
        {
            Console.WriteLine($"Failure: {failure}");
        }

        return;
    }

    var snapshot = capability.Snapshot;
    foreach (var fan in snapshot.Fans)
    {
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "Fan {0}: actual={1:0.###} max={2:0.###} mode={3} target={4:0.###}",
            fan.Index.Value,
            fan.Actual.GetFloat32(),
            fan.Maximum.GetFloat32(),
            fan.Mode.GetUInt8(),
            fan.Target.GetFloat32()));
    }
}

file readonly record struct ExpectedMaximumTargets(
    float Fan0TargetRpm,
    float Fan1TargetRpm);

file sealed class NonOwningDeviceIoControlClient : IDeviceIoControlClient
{
    private readonly IDeviceIoControlClient _inner;

    public NonOwningDeviceIoControlClient(IDeviceIoControlClient inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public byte[] Invoke(
        uint controlCode,
        ReadOnlyMemory<byte> input,
        int outputBufferLength)
    {
        return _inner.Invoke(controlCode, input, outputBufferLength);
    }

    public void Dispose()
    {
        // The smoke tool owns and disposes the single shared device client.
    }
}

file sealed class ConsoleApplicationLogger : IApplicationLogger
{
    public void Info(string message)
    {
        Console.WriteLine($"[INFO] {message}");
    }

    public void Error(string message, Exception exception)
    {
        Console.Error.WriteLine($"[ERROR] {message} {exception.Message}");
    }
}
