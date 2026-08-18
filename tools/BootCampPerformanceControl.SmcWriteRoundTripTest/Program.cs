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

    await using var readTransport = CrystalIdeaAppleSmcTransport.OpenInstalledDriver();
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
    Console.WriteLine("Target state is fixed to the verified MacBookPro16,1 maxima: Fan 0 = 5616 RPM; Fan 1 = 5200 RPM.");
    Console.WriteLine("The tool will restore Apple Auto in a non-cancelable cleanup path.");

    var logger = new ConsoleApplicationLogger();
    var preflightPolicy = new FanOverridePreflightPolicy();
    var recoveryPolicy = new FanOverrideRecoveryPolicy();
    var ownershipStore = new JsonFanOverrideOwnershipStore(logger);

    await using var writeBackend = new CrystalIdeaResearchFanSmcWriteBackend(
        new WindowsDeviceIoControlClient(CrystalIdeaAppleSmcTransport.DevicePath));

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

            if (!IsVerifiedMaximumState(maximumCapability))
            {
                throw new InvalidOperationException(
                    "Maximum state readback after the ramp delay did not remain at the verified manual targets.");
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

static bool IsVerifiedMaximumState(FanControlCapabilityResult capability)
{
    if (!capability.IsHardwareSafetyGateSatisfied || capability.Snapshot is null)
    {
        return false;
    }

    var snapshot = capability.Snapshot;
    return snapshot.Fan0Mode.GetUInt8() == 1 &&
           snapshot.Fan1Mode.GetUInt8() == 1 &&
           Math.Abs(snapshot.Fan0Target.GetFloat32() - 5616f) <= 1f &&
           Math.Abs(snapshot.Fan1Target.GetFloat32() - 5200f) <= 1f;
}

static bool IsVerifiedAppleAutoState(FanControlCapabilityResult capability)
{
    return capability.IsHardwareSafetyGateSatisfied &&
           capability.Snapshot is not null &&
           capability.Snapshot.Fan0Mode.GetUInt8() == 0 &&
           capability.Snapshot.Fan1Mode.GetUInt8() == 0;
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
    Console.WriteLine(string.Format(
        CultureInfo.InvariantCulture,
        "Fan 0: actual={0:0.###} max={1:0.###} mode={2} target={3:0.###}",
        snapshot.Fan0Actual.GetFloat32(),
        snapshot.Fan0Maximum.GetFloat32(),
        snapshot.Fan0Mode.GetUInt8(),
        snapshot.Fan0Target.GetFloat32()));
    Console.WriteLine(string.Format(
        CultureInfo.InvariantCulture,
        "Fan 1: actual={0:0.###} max={1:0.###} mode={2} target={3:0.###}",
        snapshot.Fan1Actual.GetFloat32(),
        snapshot.Fan1Maximum.GetFloat32(),
        snapshot.Fan1Mode.GetUInt8(),
        snapshot.Fan1Target.GetFloat32()));
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
