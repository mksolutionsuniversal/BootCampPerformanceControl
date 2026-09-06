using System.Globalization;
using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc;
using BootCampPerformanceControl.FanControl.Smc.Windows;
using BootCampPerformanceControl.HardwareDetection;

namespace BootCampPerformanceControl.GlobalMaskFpe2Qualification;

internal sealed class GlobalMaskQualificationOrchestrator
{
    internal const string RequiredModel = "MacBookPro12,1";
    internal const string HistoricalMaximumRawHex = "60DC";
    private const float MaximumReportedFanRpm = 10000f;

    private readonly IGlobalMaskQualificationSession _session;
    private readonly IQualificationOutput _output;
    private readonly QualificationWriteLedger _writes;
    private readonly bool _printBanner;

    public GlobalMaskQualificationOrchestrator(
        IGlobalMaskQualificationSession session,
        IQualificationOutput output,
        bool printBanner = true)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _writes = new QualificationWriteLedger(session, output);
        _printBanner = printBanner;
    }

    public QualificationWriteLedger Writes => _writes;

    public async Task<QualificationRunResult> RunAsync(
        QualificationOptions options,
        QualificationMachine machine,
        IReadOnlyList<QualificationProcessConflict> conflicts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(conflicts);

        if (_printBanner)
        {
            PrintBanner(_output, options);
        }
        PrintMachine(machine);

        QualificationRunResult result;
        try
        {
            result = await RunCoreAsync(
                    options,
                    machine,
                    conflicts,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _writes.PrintSummary();
        }

        if (!result.IsPass)
        {
            return result;
        }

        if (_writes.HasReportingFailures)
        {
            return ReportingIntegrityFailure();
        }

        if (!_writes.ReportBestEffort(
                "PHYSICAL QUALIFICATION: PASS",
                "final PASS result"))
        {
            return ReportingIntegrityFailure();
        }

        return result;
    }

    private async Task<QualificationRunResult> RunCoreAsync(
        QualificationOptions options,
        QualificationMachine machine,
        IReadOnlyList<QualificationProcessConflict> conflicts,
        CancellationToken cancellationToken)
    {
        if (!machine.IsAppleIntel ||
            !string.Equals(machine.Manufacturer, "Apple Inc.", StringComparison.Ordinal))
        {
            return Refused("Manufacturer/platform is not exactly Apple Inc. with an Intel CPU.");
        }

        if (options.IsExecuteArmed && conflicts.Count > 0)
        {
            foreach (var conflict in conflicts)
            {
                _output.WriteLine(
                    $"BLOCKING PROCESS: {conflict.ProcessName} (PID {conflict.ProcessId})");
            }

            return Refused("A known competing fan-control process is running.");
        }

        var serviceState = _session.GetServiceState();
        _output.WriteLine($"AppleSMC service: {serviceState}");
        if (serviceState != AppleSmcServiceState.Running)
        {
            return Refused("AppleSMC must already be Running; the tool will not start or alter it.");
        }

        var initialCapability = await _session
            .ProbeAsync(machine.Model, cancellationToken)
            .ConfigureAwait(false);
        PrintCapability("INITIAL FINGERPRINT", machine.Model, initialCapability);
        var initialFailures = GetEligibilityFailures(machine, serviceState, initialCapability);
        PrintEligibility(initialFailures);
        PrintBaseline(initialCapability, machine);
        PrintProposedTransaction(initialCapability);

        if (!options.IsExecuteArmed)
        {
            _output.WriteLine();
            _output.WriteLine("READ ONLY / DRY RUN COMPLETE. Execute gates were not both satisfied.");
            return new QualificationRunResult(
                QualificationOutcome.DryRun,
                "Dry-run completed with zero SMC writes.");
        }

        _output.WriteLine();
        _output.WriteLine("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
        _output.WriteLine("PHYSICAL WRITE MODE ARMED - CONTROLLED QUALIFICATION ONLY");
        _output.WriteLine("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");

        if (initialFailures.Count > 0)
        {
            return Refused("Initial physical qualification preconditions failed.");
        }

        // Re-check the service and the complete production capability fingerprint
        // immediately before crossing the first physical write boundary.
        serviceState = _session.GetServiceState();
        var preWriteCapability = await _session
            .ProbeAsync(machine.Model, cancellationToken)
            .ConfigureAwait(false);
        PrintCapability("FRESH PRE-WRITE FINGERPRINT", machine.Model, preWriteCapability);
        var preWriteUtc = DateTimeOffset.UtcNow;
        PrintBaseline(
            preWriteCapability,
            machine with
            {
                CapturedAtUtc = preWriteUtc,
                CapturedAtLocal = preWriteUtc.ToLocalTime()
            });
        var preWriteFailures = GetEligibilityFailures(
            machine,
            serviceState,
            preWriteCapability);
        if (preWriteFailures.Count > 0)
        {
            PrintEligibility(preWriteFailures);
            return Refused("Fresh physical qualification preconditions failed.");
        }

        var expectedMaximumRaw = preWriteCapability.Snapshot!.Fans[0]
            .Maximum.RawData.ToArray();

        if (_session.GetServiceState() != AppleSmcServiceState.Running)
        {
            return Refused(
                "AppleSMC stopped after the fresh preflight and before the first write.");
        }

        var physicalWriteAttempted = false;
        Exception? primaryFailure = null;
        Exception? rollbackFailure = null;

        try
        {
            physicalWriteAttempted = true;
            await _writes.WriteAsync(
                    "FS! ",
                    new byte[] { 0x00, 0x01 },
                    cancellationToken)
                .ConfigureAwait(false);

            var manualMode = await ReadAndLogAsync("FS! ", cancellationToken)
                .ConfigureAwait(false);
            RequireValue(manualMode, "FS! ", 2, "ui16", 0xC0, "0001");

            var freshMaximum = await ReadAndLogAsync("F0Mx", cancellationToken)
                .ConfigureAwait(false);
            RequireValue(
                freshMaximum,
                "F0Mx",
                2,
                "fpe2",
                0xC0,
                Convert.ToHexString(expectedMaximumRaw));

            await _writes.WriteAsync(
                    "F0Tg",
                    freshMaximum.RawData,
                    cancellationToken)
                .ConfigureAwait(false);

            var target = await ReadAndLogAsync("F0Tg", cancellationToken)
                .ConfigureAwait(false);
            RequireValue(
                target,
                "F0Tg",
                2,
                "fpe2",
                0xD0,
                Convert.ToHexString(expectedMaximumRaw));

            var actual = await ReadAndLogAsync("F0Ac", cancellationToken)
                .ConfigureAwait(false);
            if (!Matches(actual, "F0Ac", 2, "fpe2", 0x90))
            {
                throw new InvalidOperationException(
                    "F0Ac observation changed metadata during the qualification transaction.");
            }
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            _output.WriteLine(
                $"PRIMARY TRANSACTION FAILURE: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            if (physicalWriteAttempted)
            {
                try
                {
                    _writes.ReportBestEffort(
                        "NON-CANCELLABLE EMERGENCY APPLE AUTO RESTORE",
                        "emergency Apple Auto restore start");
                    await _writes.WriteAsync(
                            "FS! ",
                            new byte[] { 0x00, 0x00 },
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    var restoredMode = await _session
                        .ReadKeyAsync("FS! ", CancellationToken.None)
                        .ConfigureAwait(false);
                    RequireValue(restoredMode, "FS! ", 2, "ui16", 0xC0, "0000");
                    _writes.ReportBestEffort(
                        $"READBACK: {FormatValue(restoredMode)}",
                        "emergency Apple Auto readback");
                    _writes.ReportBestEffort(
                        "Emergency/final Apple Auto readback: VERIFIED 0000",
                        "emergency Apple Auto verification result");
                }
                catch (Exception exception)
                {
                    rollbackFailure = exception;
                    _writes.ReportBestEffort(
                        $"ROLLBACK FAILURE: {exception.GetType().Name}: {exception.Message}",
                        "rollback failure result");
                }
            }
        }

        FanControlCapabilityResult? finalCapability = null;
        Exception? finalProbeFailure = null;
        try
        {
            finalCapability = await _session
                .ProbeAsync(machine.Model, CancellationToken.None)
                .ConfigureAwait(false);
            PrintCapability("FINAL FINGERPRINT", machine.Model, finalCapability);
        }
        catch (Exception exception)
        {
            finalProbeFailure = exception;
            _output.WriteLine(
                $"FINAL PROBE FAILURE: {exception.GetType().Name}: {exception.Message}");
        }

        if (primaryFailure is not null ||
            rollbackFailure is not null ||
            finalProbeFailure is not null ||
            finalCapability is null ||
            !IsVerifiedFinalAppleAuto(finalCapability))
        {
            var message = rollbackFailure is not null
                ? "Physical qualification failed and emergency Apple Auto could not be verified."
                : primaryFailure is not null
                    ? "Physical qualification failed; the primary failure is retained in the log."
                    : "Physical qualification is inconclusive because final Apple Auto was not verified.";
            _output.WriteLine($"PHYSICAL QUALIFICATION: FAIL/INCONCLUSIVE - {message}");
            return new QualificationRunResult(
                QualificationOutcome.Fail,
                message,
                primaryFailure ?? finalProbeFailure,
                rollbackFailure);
        }

        return new QualificationRunResult(
            QualificationOutcome.Pass,
            "GlobalMaskFpe2 one-fan physical write/readback/Apple Auto round trip passed.");
    }

    private QualificationRunResult ReportingIntegrityFailure()
    {
        var firstFailure = _writes.ReportingFailures[0];
        _writes.ReportBestEffort(
            $"PHYSICAL QUALIFICATION: FAIL/INCONCLUSIVE - reporting integrity failed during {firstFailure.Context}: {firstFailure.Exception.GetType().Name}: {firstFailure.Exception.Message}",
            "reporting-integrity failure result");
        return new QualificationRunResult(
            QualificationOutcome.Fail,
            "Physical hardware rollback was verified, but qualification reporting integrity failed.",
            firstFailure.Exception);
    }

    private QualificationRunResult Refused(string message)
    {
        _output.WriteLine($"REFUSED: {message}");
        return new QualificationRunResult(QualificationOutcome.Refused, message);
    }

    private static IReadOnlyList<string> GetEligibilityFailures(
        QualificationMachine machine,
        AppleSmcServiceState serviceState,
        FanControlCapabilityResult capability)
    {
        var failures = new List<string>();
        if (!string.Equals(machine.Manufacturer, "Apple Inc.", StringComparison.Ordinal))
        {
            failures.Add("Manufacturer must equal 'Apple Inc.'.");
        }

        if (!machine.IsAppleIntel)
        {
            failures.Add("Platform must be Apple with an Intel CPU.");
        }

        if (!string.Equals(machine.Model, RequiredModel, StringComparison.Ordinal))
        {
            failures.Add($"Model must equal '{RequiredModel}'.");
        }

        if (serviceState != AppleSmcServiceState.Running)
        {
            failures.Add("AppleSMC service must already be Running.");
        }

        if (capability.Protocol != SmcTransportProtocol.Mmio)
        {
            failures.Add("Transport must be MMIO protocol 1.");
        }

        if (!capability.IsHardwareSafetyGateSatisfied)
        {
            failures.Add("Production hardware safety gate must be satisfied.");
        }

        if (capability.Family != FanCapabilityFamily.GlobalMaskFpe2)
        {
            failures.Add("Capability family must be GlobalMaskFpe2.");
        }

        var snapshot = capability.Snapshot;
        if (snapshot is null)
        {
            failures.Add("A complete live capability snapshot is required.");
            return failures;
        }

        if (!Matches(snapshot.FanCount, "FNum", 1, "ui8 ", 0x80) ||
            snapshot.FanCount.GetUInt8() != 1 ||
            snapshot.Fans.Count != 1)
        {
            failures.Add("FNum and discovered topology must both equal exactly one fan.");
            return failures;
        }

        var fan = snapshot.Fans[0];
        if (fan.ModeObservation.State != SmcKeyObservationState.ConfirmedAbsent)
        {
            failures.Add("F0Md must be ConfirmedAbsent.");
        }

        if (snapshot.GlobalMode.Value is not { } globalMode ||
            !Matches(globalMode, "FS! ", 2, "ui16", 0xC0) ||
            !globalMode.RawData.Span.SequenceEqual(new byte[] { 0x00, 0x00 }))
        {
            failures.Add("FS! must be ui16/len2/attrs 0xC0 with initial raw 0000.");
        }

        CheckMetadata(fan.Minimum.Value, "F0Mn", 2, "fpe2", 0xC0, failures);
        CheckMetadata(fan.MaximumObservation.Value, "F0Mx", 2, "fpe2", 0xC0, failures);
        CheckMetadata(fan.ActualObservation.Value, "F0Ac", 2, "fpe2", 0x90, failures);
        CheckMetadata(fan.TargetObservation.Value, "F0Tg", 2, "fpe2", 0xD0, failures);

        if (fan.MaximumObservation.Value is { } maximum &&
            Matches(maximum, "F0Mx", 2, "fpe2", 0xC0))
        {
            var rpm = maximum.GetFpe2();
            if (!float.IsFinite(rpm) || rpm <= 0 || rpm > MaximumReportedFanRpm)
            {
                failures.Add(
                    $"F0Mx decoded RPM must be > 0 and <= {MaximumReportedFanRpm}.");
            }

            var maximumRaw = Convert.ToHexString(maximum.RawData.Span);
            if (!string.Equals(
                    maximumRaw,
                    HistoricalMaximumRawHex,
                    StringComparison.Ordinal))
            {
                failures.Add(
                    $"F0Mx raw {maximumRaw} differs from reviewed qualification evidence {HistoricalMaximumRawHex}; review again before any write.");
            }
        }

        return failures;
    }

    private static void CheckMetadata(
        SmcValue? value,
        string key,
        byte length,
        string type,
        byte attributes,
        ICollection<string> failures)
    {
        if (!Matches(value, key, length, type, attributes))
        {
            failures.Add(
                $"{key} must be {type}/len{length}/attrs 0x{attributes:X2} with matching raw length.");
        }
    }

    private static bool Matches(
        SmcValue? value,
        string key,
        byte length,
        string type,
        byte attributes)
    {
        return value is not null &&
            string.Equals(value.Info.Key, key, StringComparison.Ordinal) &&
            value.Info.Length == length &&
            string.Equals(value.Info.Type, type, StringComparison.Ordinal) &&
            value.Info.Attributes == attributes &&
            value.RawData.Length == length;
    }

    private static void RequireValue(
        SmcValue value,
        string key,
        byte length,
        string type,
        byte attributes,
        string expectedRawHex)
    {
        if (!Matches(value, key, length, type, attributes) ||
            !string.Equals(
                Convert.ToHexString(value.RawData.Span),
                expectedRawHex,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{key} readback did not exactly match {type}/len{length}/attrs 0x{attributes:X2}/raw {expectedRawHex}.");
        }
    }

    private async Task<SmcValue> ReadAndLogAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var value = await _session.ReadKeyAsync(key, cancellationToken)
            .ConfigureAwait(false);
        _output.WriteLine($"READBACK: {FormatValue(value)}");
        return value;
    }

    private static bool IsVerifiedFinalAppleAuto(
        FanControlCapabilityResult capability)
    {
        if (capability.Family != FanCapabilityFamily.GlobalMaskFpe2 ||
            !capability.IsHardwareSafetyGateSatisfied ||
            capability.Snapshot?.GlobalMode.Value is not { } globalMode ||
            !globalMode.RawData.Span.SequenceEqual(new byte[] { 0x00, 0x00 }))
        {
            return false;
        }

        try
        {
            return new GlobalMaskFpe2Strategy().IsAppleAuto(capability);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    internal static void PrintBanner(
        IQualificationOutput output,
        QualificationOptions options)
    {
        output.WriteLine("============================================================");
        output.WriteLine("GLOBALMASK FPE2 PHYSICAL QUALIFICATION FIXTURE");
        output.WriteLine(options.IsExecuteArmed
            ? "DANGER: BOTH PHYSICAL WRITE GATES ARE PRESENT"
            : "READ ONLY / DRY RUN - ZERO SMC WRITES");
        output.WriteLine("============================================================");
    }

    private void PrintMachine(QualificationMachine machine)
    {
        _output.WriteLine($"UTC timestamp:   {machine.CapturedAtUtc:O}");
        _output.WriteLine($"Local timestamp: {machine.CapturedAtLocal:O}");
        _output.WriteLine($"Manufacturer:    {machine.Manufacturer}");
        _output.WriteLine($"Model:           {machine.Model}");
        _output.WriteLine($"Apple + Intel:   {machine.IsAppleIntel}");
        _output.WriteLine($"Windows:         {machine.WindowsVersion}");
    }

    private void PrintCapability(
        string label,
        string model,
        FanControlCapabilityResult capability)
    {
        _output.WriteLine();
        _output.WriteLine($"=== {label} ===");
        _output.WriteLine($"Model: {model}");
        _output.WriteLine(
            $"Protocol: {capability.Protocol?.ToString() ?? "unknown"} ({(capability.Protocol.HasValue ? (int)capability.Protocol.Value : -1)})");
        _output.WriteLine($"Family: {capability.Family}");
        _output.WriteLine($"Read supported: {capability.IsReadSupported}");
        _output.WriteLine($"Hardware safety gate: {capability.IsHardwareSafetyGateSatisfied}");

        if (capability.Snapshot is { } snapshot)
        {
            _output.WriteLine($"FNum: {FormatValue(snapshot.FanCount)}");
            _output.WriteLine($"FS! : {FormatObservation(snapshot.GlobalMode)}");
            if (snapshot.Fans.Count > 0)
            {
                var fan = snapshot.Fans[0];
                _output.WriteLine($"F0Mn: {FormatObservation(fan.Minimum)}");
                _output.WriteLine($"F0Mx: {FormatObservation(fan.MaximumObservation)}");
                _output.WriteLine($"F0Ac: {FormatObservation(fan.ActualObservation)}");
                _output.WriteLine($"F0Tg: {FormatObservation(fan.TargetObservation)}");
                _output.WriteLine($"F0Md observation: {FormatObservation(fan.ModeObservation)}");
            }
        }

        if (capability.Failures.Count == 0)
        {
            _output.WriteLine("Capability failures/warnings: none");
        }
        else
        {
            foreach (var failure in capability.Failures)
            {
                _output.WriteLine($"Capability failure/warning: {failure}");
            }
        }
    }

    private void PrintEligibility(IReadOnlyList<string> failures)
    {
        _output.WriteLine();
        if (failures.Count == 0)
        {
            _output.WriteLine("FIRST PHYSICAL QUALIFICATION ELIGIBILITY: YES");
            return;
        }

        _output.WriteLine("FIRST PHYSICAL QUALIFICATION ELIGIBILITY: NO");
        foreach (var failure in failures)
        {
            _output.WriteLine($"- {failure}");
        }
    }

    private void PrintBaseline(
        FanControlCapabilityResult capability,
        QualificationMachine machine)
    {
        _output.WriteLine();
        _output.WriteLine($"IN-MEMORY BASELINE UTC: {machine.CapturedAtUtc:O}");
        _output.WriteLine($"IN-MEMORY BASELINE LOCAL: {machine.CapturedAtLocal:O}");
        if (capability.Snapshot is not { Fans.Count: > 0 } snapshot)
        {
            _output.WriteLine("Baseline unavailable: capability snapshot is incomplete.");
            return;
        }

        _output.WriteLine($"FS! : {FormatObservation(snapshot.GlobalMode)}");
        _output.WriteLine($"F0Mx: {FormatObservation(snapshot.Fans[0].MaximumObservation)}");
        _output.WriteLine($"F0Tg: {FormatObservation(snapshot.Fans[0].TargetObservation)}");
        _output.WriteLine($"F0Ac: {FormatObservation(snapshot.Fans[0].ActualObservation)}");
        _output.WriteLine($"F0Mn: {FormatObservation(snapshot.Fans[0].Minimum)}");
    }

    private void PrintProposedTransaction(FanControlCapabilityResult capability)
    {
        var rawMaximum = capability.Snapshot is { Fans.Count: > 0 } snapshot &&
            snapshot.Fans[0].MaximumObservation.Value is { } maximum
            ? Convert.ToHexString(maximum.RawData.Span)
            : "<unavailable>";
        _output.WriteLine();
        _output.WriteLine("EXACT TRANSACTION THIS TOOL WOULD PERFORM:");
        _output.WriteLine("1. fresh full preflight and exact baseline capture");
        _output.WriteLine("2. WRITE FS!  0001; read back exact 0001");
        _output.WriteLine($"3. re-read F0Mx; WRITE exact raw F0Mx {rawMaximum} -> F0Tg; verify exact readback");
        _output.WriteLine("4. observe F0Ac");
        _output.WriteLine("5. finally WRITE FS!  0000 with CancellationToken.None; verify exact 0000");
        _output.WriteLine("6. final capability probe and Apple Auto verification");
    }

    private static string FormatObservation(SmcKeyObservation observation)
    {
        return observation.State switch
        {
            SmcKeyObservationState.Available => FormatValue(observation.Value!),
            SmcKeyObservationState.ConfirmedAbsent => "ConfirmedAbsent",
            _ => $"ReadFailed: {observation.Failure}"
        };
    }

    private static string FormatValue(SmcValue value)
    {
        var decoded = value.Info.Type switch
        {
            "fpe2" when value.Info.Length == 2 && value.RawData.Length == 2 => string.Format(
                CultureInfo.InvariantCulture,
                "; decodedRpm={0:0.###}",
                value.GetFpe2()),
            "ui8 " when value.Info.Length == 1 && value.RawData.Length == 1 =>
                $"; value={value.GetUInt8().ToString(CultureInfo.InvariantCulture)}",
            "ui16" when value.Info.Length == 2 && value.RawData.Length == 2 =>
                $"; value=0x{value.GetUInt16BigEndian():X4}",
            _ => string.Empty
        };
        return $"type='{value.Info.Type}'; len={value.Info.Length}; attrs=0x{value.Info.Attributes:X2}; raw={Convert.ToHexString(value.RawData.Span)}{decoded}";
    }
}
