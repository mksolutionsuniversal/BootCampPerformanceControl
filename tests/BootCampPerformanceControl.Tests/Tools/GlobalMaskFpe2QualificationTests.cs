using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc;
using BootCampPerformanceControl.FanControl.Smc.Windows;
using BootCampPerformanceControl.GlobalMaskFpe2Qualification;

namespace BootCampPerformanceControl.Tests.Tools;

public sealed class GlobalMaskFpe2QualificationTests
{
    [Fact]
    public async Task DefaultDryRunIssuesZeroWrites()
    {
        var fixture = new Fixture();

        var result = await fixture.RunAsync(QualificationOptions.Parse([]));

        Assert.Equal(QualificationOutcome.DryRun, result.Outcome);
        Assert.Empty(fixture.Session.Writes);
        Assert.Contains(fixture.Output.Lines, line =>
            line.Contains("READ ONLY / DRY RUN", StringComparison.Ordinal));
        Assert.Contains("Total SMC write attempts: 0", fixture.Output.Lines);
    }

    [Fact]
    public async Task MissingExecuteFlagIssuesZeroWrites()
    {
        var fixture = new Fixture();
        var options = QualificationOptions.Parse(
            ["--confirm", QualificationOptions.ConfirmationToken]);

        var result = await fixture.RunAsync(options);

        Assert.Equal(QualificationOutcome.DryRun, result.Outcome);
        Assert.Empty(fixture.Session.Writes);
    }

    [Fact]
    public async Task WrongConfirmationTokenIssuesZeroWrites()
    {
        var fixture = new Fixture();
        var options = QualificationOptions.Parse(
            ["--execute", "--confirm", "WRONG"]);

        var result = await fixture.RunAsync(options);

        Assert.Equal(QualificationOutcome.DryRun, result.Outcome);
        Assert.Empty(fixture.Session.Writes);
    }

    [Fact]
    public async Task ExecuteWithoutConfirmationIssuesZeroWrites()
    {
        var fixture = new Fixture();

        var result = await fixture.RunAsync(QualificationOptions.Parse(["--execute"]));

        Assert.Equal(QualificationOutcome.DryRun, result.Outcome);
        Assert.Empty(fixture.Session.Writes);
    }

    [Fact]
    public async Task WrongModelIssuesZeroWrites()
    {
        var fixture = new Fixture
        {
            Machine = GoodMachine() with { Model = "MacBookPro14,3" }
        };

        var result = await fixture.RunAsync(Armed());

        Assert.Equal(QualificationOutcome.Refused, result.Outcome);
        Assert.Empty(fixture.Session.Writes);
    }

    [Fact]
    public async Task NonAppleManufacturerIssuesZeroWrites()
    {
        var fixture = new Fixture
        {
            Machine = GoodMachine() with
            {
                Manufacturer = "Other",
                IsAppleIntel = false
            }
        };

        var result = await fixture.RunAsync(Armed());

        Assert.Equal(QualificationOutcome.Refused, result.Outcome);
        Assert.Empty(fixture.Session.Writes);
    }

    [Fact]
    public async Task ManufacturerMustMatchAppleIncExactly()
    {
        var fixture = new Fixture
        {
            Machine = GoodMachine() with { Manufacturer = "Apple" }
        };

        var result = await fixture.RunAsync(Armed());

        Assert.Equal(QualificationOutcome.Refused, result.Outcome);
        Assert.Empty(fixture.Session.Writes);
    }

    [Fact]
    public async Task AppleSmcNotRunningIssuesZeroWrites()
    {
        var fixture = new Fixture();
        fixture.Session.ServiceState = AppleSmcServiceState.Stopped;

        var result = await fixture.RunAsync(Armed());

        Assert.Equal(QualificationOutcome.Refused, result.Outcome);
        Assert.Empty(fixture.Session.Writes);
    }

    [Fact]
    public async Task AppleSmcStoppingImmediatelyBeforeFirstWriteIssuesZeroWrites()
    {
        var fixture = new Fixture();
        fixture.Session.StopServiceBeforeFirstWrite = true;

        var result = await fixture.RunAsync(Armed());

        Assert.Equal(QualificationOutcome.Refused, result.Outcome);
        Assert.Empty(fixture.Session.Writes);
    }

    [Fact]
    public async Task CompetingProcessIssuesZeroWritesAndReportsExactBlocker()
    {
        var fixture = new Fixture
        {
            Conflicts = [new QualificationProcessConflict("MacsFanControl", 42)]
        };

        var result = await fixture.RunAsync(Armed());

        Assert.Equal(QualificationOutcome.Refused, result.Outcome);
        Assert.Empty(fixture.Session.Writes);
        Assert.Contains("BLOCKING PROCESS: MacsFanControl (PID 42)", fixture.Output.Lines);
    }

    [Fact]
    public async Task FanCountOtherThanOneIssuesZeroWrites()
    {
        var fixture = new Fixture();
        fixture.Session.FanCount = 2;

        var result = await fixture.RunAsync(Armed());

        Assert.Equal(QualificationOutcome.Refused, result.Outcome);
        Assert.Empty(fixture.Session.Writes);
    }

    [Fact]
    public async Task WrongFamilyIssuesZeroWrites()
    {
        var fixture = new Fixture();
        fixture.Session.Family = FanCapabilityFamily.Unknown;
        fixture.Session.HardwareGate = false;

        var result = await fixture.RunAsync(Armed());

        Assert.Equal(QualificationOutcome.Refused, result.Outcome);
        Assert.Empty(fixture.Session.Writes);
    }

    [Fact]
    public async Task FanModeNotConfirmedAbsentIssuesZeroWrites()
    {
        var fixture = new Fixture();
        fixture.Session.ModeReadFailed = true;

        var result = await fixture.RunAsync(Armed());

        Assert.Equal(QualificationOutcome.Refused, result.Outcome);
        Assert.Empty(fixture.Session.Writes);
    }

    [Fact]
    public async Task IncorrectTargetMetadataIssuesZeroWrites()
    {
        var fixture = new Fixture();
        fixture.Session.TargetAttributes = 0xC0;

        var result = await fixture.RunAsync(Armed());

        Assert.Equal(QualificationOutcome.Refused, result.Outcome);
        Assert.Empty(fixture.Session.Writes);
    }

    [Fact]
    public async Task NonMmioTransportIssuesZeroWrites()
    {
        var fixture = new Fixture();
        fixture.Session.Protocol = SmcTransportProtocol.Unknown;
        fixture.Session.HardwareGate = false;

        var result = await fixture.RunAsync(Armed());

        Assert.Equal(QualificationOutcome.Refused, result.Outcome);
        Assert.Empty(fixture.Session.Writes);
    }

    [Fact]
    public async Task InitialGlobalModeOtherThanAutoIssuesZeroWrites()
    {
        var fixture = new Fixture();
        fixture.Session.Mask = 0x0001;

        var result = await fixture.RunAsync(Armed());

        Assert.Equal(QualificationOutcome.Refused, result.Outcome);
        Assert.Empty(fixture.Session.Writes);
    }

    [Fact]
    public async Task MaximumDifferentFromReviewedEvidenceIssuesZeroWrites()
    {
        var fixture = new Fixture();
        fixture.Session.MaximumRaw = 0x60D8;

        var result = await fixture.RunAsync(Armed());

        Assert.Equal(QualificationOutcome.Refused, result.Outcome);
        Assert.Empty(fixture.Session.Writes);
        Assert.Contains(fixture.Output.Lines, line =>
            line.Contains("differs from reviewed qualification evidence", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MaximumChangingDuringFreshPreflightIssuesZeroWrites()
    {
        var fixture = new Fixture();
        fixture.Session.MaximumRawAfterFirstProbe = 0x60D8;

        var result = await fixture.RunAsync(Armed());

        Assert.Equal(QualificationOutcome.Refused, result.Outcome);
        Assert.Empty(fixture.Session.Writes);
    }

    [Fact]
    public async Task SuccessfulRunUsesExactBoundedPhysicalSequence()
    {
        var fixture = new Fixture();

        var result = await fixture.RunAsync(Armed());

        Assert.Equal(QualificationOutcome.Pass, result.Outcome);
        Assert.Equal(
            [
                ("FS! ", "0001"),
                ("F0Tg", "60DC"),
                ("FS! ", "0000")
            ],
            fixture.Session.Writes.Select(write => (write.Key, write.RawHex)));
        Assert.Equal((ushort)0, fixture.Session.Mask);
        Assert.Equal((ushort)0x60DC, fixture.Session.TargetRaw);
    }

    [Fact]
    public async Task ManualMaskReadbackFailureSkipsTargetAndAttemptsEmergencyAuto()
    {
        var fixture = new Fixture();
        fixture.Session.FailManualMaskReadback = true;

        var result = await fixture.RunAsync(Armed());

        Assert.Equal(QualificationOutcome.Fail, result.Outcome);
        Assert.Equal(
            [("FS! ", "0001"), ("FS! ", "0000")],
            fixture.Session.Writes.Select(write => (write.Key, write.RawHex)));
    }

    [Fact]
    public async Task TargetWriteFailureAttemptsEmergencyAutoAndRetainsPrimaryFailure()
    {
        var fixture = new Fixture();
        fixture.Session.FailTargetWrite = true;

        var result = await fixture.RunAsync(Armed());

        Assert.Equal(QualificationOutcome.Fail, result.Outcome);
        Assert.NotNull(result.PrimaryFailure);
        Assert.Equal(
            [("FS! ", "0001"), ("F0Tg", "60DC"), ("FS! ", "0000")],
            fixture.Session.Writes.Select(write => (write.Key, write.RawHex)));
        AssertNoPhysicalPassWasPrinted(fixture);
    }

    [Fact]
    public async Task TargetReadbackMismatchAttemptsEmergencyAuto()
    {
        var fixture = new Fixture();
        fixture.Session.MismatchTargetReadback = true;

        var result = await fixture.RunAsync(Armed());

        Assert.Equal(QualificationOutcome.Fail, result.Outcome);
        Assert.Equal("FS! ", fixture.Session.Writes[^1].Key);
        Assert.Equal("0000", fixture.Session.Writes[^1].RawHex);
    }

    [Fact]
    public async Task FinalAutoReadbackFailureCannotPass()
    {
        var fixture = new Fixture();
        fixture.Session.FailFinalAutoReadback = true;

        var result = await fixture.RunAsync(Armed());

        Assert.Equal(QualificationOutcome.Fail, result.Outcome);
        Assert.NotNull(result.RollbackFailure);
        Assert.False(result.IsPass);
    }

    [Fact]
    public async Task WriteLedgerRejectsEveryKeyOutsideFsAndFan0TargetBeforeBackendAccess()
    {
        var session = new FakeQualificationSession();
        var ledger = new QualificationWriteLedger(session, new RecordingOutput());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ledger.WriteAsync(
                "F1Tg",
                new byte[] { 0x60, 0xDC },
                CancellationToken.None));

        Assert.Empty(session.Writes);
        Assert.Empty(ledger.Attempts);
    }

    [Fact]
    public async Task CancellationAfterFirstWriteUsesNonCancellableEmergencyRollback()
    {
        using var cancellationSource = new CancellationTokenSource();
        var fixture = new Fixture();
        fixture.Session.CancelAfterManualWrite = cancellationSource;

        var result = await fixture.RunAsync(Armed(), cancellationSource.Token);

        Assert.Equal(QualificationOutcome.Fail, result.Outcome);
        Assert.IsAssignableFrom<OperationCanceledException>(result.PrimaryFailure);
        Assert.Equal(2, fixture.Session.Writes.Count);
        Assert.True(fixture.Session.Writes[0].CancellationToken.CanBeCanceled);
        Assert.False(fixture.Session.Writes[1].CancellationToken.CanBeCanceled);
        Assert.Equal("0000", fixture.Session.Writes[1].RawHex);
    }

    [Fact]
    public async Task FirstManualWriteReportingFailureStillPerformsBackendAndRollback()
    {
        var fixture = new Fixture();
        fixture.Output.ThrowOnceWhen = line =>
            line.StartsWith("WRITE #1: FS! ", StringComparison.Ordinal);

        var result = await fixture.RunAsync(Armed());

        Assert.Equal(QualificationOutcome.Fail, result.Outcome);
        Assert.IsType<IOException>(result.PrimaryFailure);
        Assert.Equal(
            [("FS! ", "0001"), ("F0Tg", "60DC"), ("FS! ", "0000")],
            fixture.Session.Writes.Select(write => (write.Key, write.RawHex)));
    }

    [Fact]
    public async Task OutputFailureAfterManualWriteStillAttemptsEmergencyAuto()
    {
        var fixture = new Fixture();
        fixture.Output.ThrowOnceWhen = line =>
            line.StartsWith("READBACK:", StringComparison.Ordinal) &&
            line.Contains("raw=0001", StringComparison.Ordinal);

        var result = await fixture.RunAsync(Armed());

        Assert.Equal(QualificationOutcome.Fail, result.Outcome);
        Assert.Equal(
            [("FS! ", "0001"), ("FS! ", "0000")],
            fixture.Session.Writes.Select(write => (write.Key, write.RawHex)));
        AssertNoPhysicalPassWasPrinted(fixture);
    }

    [Fact]
    public async Task EmergencyRestoreStartReportingFailureCannotPreventAutoWrite()
    {
        var fixture = new Fixture();
        fixture.Output.ThrowOnceWhen = line => string.Equals(
            line,
            "NON-CANCELLABLE EMERGENCY APPLE AUTO RESTORE",
            StringComparison.Ordinal);

        var result = await fixture.RunAsync(Armed());

        Assert.Equal(QualificationOutcome.Fail, result.Outcome);
        Assert.Equal("FS! ", fixture.Session.Writes[^1].Key);
        Assert.Equal("0000", fixture.Session.Writes[^1].RawHex);
        AssertNoPhysicalPassWasPrinted(fixture);
    }

    [Fact]
    public async Task RollbackWriteReportingFailureCannotPreventAutoBackendWrite()
    {
        var fixture = new Fixture();
        fixture.Output.ThrowOnceWhen = line =>
            line.StartsWith("WRITE #3: FS! ", StringComparison.Ordinal);

        var result = await fixture.RunAsync(Armed());

        Assert.Equal(QualificationOutcome.Fail, result.Outcome);
        Assert.Equal(3, fixture.Session.Writes.Count);
        Assert.Equal("FS! ", fixture.Session.Writes[^1].Key);
        Assert.Equal("0000", fixture.Session.Writes[^1].RawHex);
        Assert.Equal((ushort)0, fixture.Session.Mask);
        AssertNoPhysicalPassWasPrinted(fixture);
    }

    [Fact]
    public async Task RollbackReadbackReportingFailureOccursAfterExactVerificationAndCannotPass()
    {
        var fixture = new Fixture();
        fixture.Output.ThrowOnceWhen = line =>
            line.StartsWith("READBACK:", StringComparison.Ordinal) &&
            line.Contains("raw=0000", StringComparison.Ordinal);

        var result = await fixture.RunAsync(Armed());

        Assert.Equal(QualificationOutcome.Fail, result.Outcome);
        Assert.Equal(2, fixture.Session.GlobalModeReadCount);
        Assert.Equal((ushort)0, fixture.Session.Mask);
        Assert.Equal("0000", fixture.Session.Writes[^1].RawHex);
        AssertNoPhysicalPassWasPrinted(fixture);
    }

    private static void AssertNoPhysicalPassWasPrinted(Fixture fixture)
    {
        Assert.DoesNotContain(
            "PHYSICAL QUALIFICATION: PASS",
            fixture.Output.Lines);
    }

    private static QualificationOptions Armed() => QualificationOptions.Parse(
        ["--execute", "--confirm", QualificationOptions.ConfirmationToken]);

    private static QualificationMachine GoodMachine() => new(
        "Apple Inc.",
        GlobalMaskQualificationOrchestrator.RequiredModel,
        IsAppleIntel: true,
        "Windows test",
        new DateTimeOffset(2026, 9, 6, 20, 45, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 6, 21, 45, 0, TimeSpan.FromHours(1)));

    private sealed class Fixture
    {
        public FakeQualificationSession Session { get; } = new();

        public RecordingOutput Output { get; } = new();

        public QualificationMachine Machine { get; init; } = GoodMachine();

        public IReadOnlyList<QualificationProcessConflict> Conflicts { get; init; } = [];

        public Task<QualificationRunResult> RunAsync(
            QualificationOptions options,
            CancellationToken cancellationToken = default)
        {
            return new GlobalMaskQualificationOrchestrator(Session, Output).RunAsync(
                options,
                Machine,
                Conflicts,
                cancellationToken);
        }
    }

    private sealed class RecordingOutput : IQualificationOutput
    {
        public List<string> Lines { get; } = [];

        public List<string> AttemptedLines { get; } = [];

        public Func<string, bool>? ThrowOnceWhen { get; set; }

        private bool HasThrown { get; set; }

        public void WriteLine(string message = "")
        {
            AttemptedLines.Add(message);
            if (!HasThrown && ThrowOnceWhen?.Invoke(message) == true)
            {
                HasThrown = true;
                throw new IOException("simulated qualification output failure");
            }

            Lines.Add(message);
        }
    }

    private sealed class FakeQualificationSession : IGlobalMaskQualificationSession
    {
        private int _globalModeReadCount;
        private int _probeCount;
        private int _serviceStateReadCount;

        public AppleSmcServiceState ServiceState { get; set; } = AppleSmcServiceState.Running;

        public bool StopServiceBeforeFirstWrite { get; set; }

        public SmcTransportProtocol Protocol { get; set; } = SmcTransportProtocol.Mmio;

        public FanCapabilityFamily Family { get; set; } = FanCapabilityFamily.GlobalMaskFpe2;

        public bool HardwareGate { get; set; } = true;

        public bool ModeReadFailed { get; set; }

        public byte TargetAttributes { get; set; } = 0xD0;

        public int FanCount { get; set; } = 1;

        public ushort Mask { get; set; }

        public ushort MaximumRaw { get; set; } = 0x60DC;

        public ushort? MaximumRawAfterFirstProbe { get; set; }

        public ushort TargetRaw { get; set; } = 0x248C;

        public bool FailManualMaskReadback { get; set; }

        public bool FailTargetWrite { get; set; }

        public bool MismatchTargetReadback { get; set; }

        public bool FailFinalAutoReadback { get; set; }

        public CancellationTokenSource? CancelAfterManualWrite { get; set; }

        public List<QualificationWriteAttempt> Writes { get; } = [];

        public int GlobalModeReadCount => _globalModeReadCount;

        public AppleSmcServiceState GetServiceState()
        {
            _serviceStateReadCount++;
            return StopServiceBeforeFirstWrite && _serviceStateReadCount >= 3
                ? AppleSmcServiceState.Stopped
                : ServiceState;
        }

        public Task<FanControlCapabilityResult> ProbeAsync(
            string model,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _probeCount++;
            if (_probeCount > 1 && MaximumRawAfterFirstProbe is { } maximumRaw)
            {
                MaximumRaw = maximumRaw;
            }

            return Task.FromResult(CreateCapability());
        }

        public Task<SmcValue> ReadKeyAsync(
            string key,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (key)
            {
                case "FS! ":
                    _globalModeReadCount++;
                    if (FailManualMaskReadback && _globalModeReadCount == 1)
                    {
                        throw new IOException("simulated FS! readback failure");
                    }

                    var mask = FailFinalAutoReadback && Mask == 0
                        ? (ushort)1
                        : Mask;
                    return Task.FromResult(UInt16("FS! ", mask, 0xC0));
                case "F0Mx":
                    return Task.FromResult(Fpe2("F0Mx", MaximumRaw, 0xC0));
                case "F0Tg":
                    return Task.FromResult(Fpe2("F0Tg", TargetRaw, 0xD0));
                case "F0Ac":
                    return Task.FromResult(Fpe2("F0Ac", 0x2404, 0x90));
                default:
                    throw new InvalidOperationException($"Unexpected fake read '{key}'.");
            }
        }

        public Task WriteKeyAsync(
            string key,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            var rawHex = Convert.ToHexString(payload.Span);
            Writes.Add(new QualificationWriteAttempt(
                Writes.Count + 1,
                key,
                rawHex,
                cancellationToken));

            cancellationToken.ThrowIfCancellationRequested();
            if (key == "FS! " && rawHex == "0001")
            {
                Mask = 1;
                CancelAfterManualWrite?.Cancel();
            }
            else if (key == "FS! " && rawHex == "0000")
            {
                Mask = 0;
            }
            else if (key == "F0Tg")
            {
                if (FailTargetWrite)
                {
                    throw new IOException("simulated target write failure");
                }

                TargetRaw = MismatchTargetReadback
                    ? (ushort)0x60D8
                    : Convert.ToUInt16(rawHex, 16);
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private FanControlCapabilityResult CreateCapability()
        {
            var fans = Enumerable.Range(0, FanCount).Select(index =>
            {
                var fan = new FanIndex(index);
                var maximum = index == 0 ? MaximumRaw : (ushort)0x5CDC;
                var target = index == 0 ? TargetRaw : (ushort)0x228C;
                return new FanSmcChannelSnapshot(
                    fan,
                    SmcKeyObservation.Available(Fpe2(fan.GetSmcKey("Mn"), 0x144C, 0xC0)),
                    SmcKeyObservation.Available(Fpe2(fan.GetSmcKey("Mx"), maximum, 0xC0)),
                    SmcKeyObservation.Available(Fpe2(fan.GetSmcKey("Ac"), 0x2404, 0x90)),
                    ModeReadFailed
                        ? SmcKeyObservation.ReadFailed(
                            fan.GetSmcKey("Md"),
                            new IOException("simulated mode read failure"))
                        : SmcKeyObservation.ConfirmedAbsent(fan.GetSmcKey("Md")),
                    SmcKeyObservation.Available(Fpe2(
                        fan.GetSmcKey("Tg"),
                        target,
                        TargetAttributes)));
            }).ToArray();
            var snapshot = new FanSmcSnapshot(
                UInt8("FNum", checked((byte)FanCount), 0x80),
                fans,
                SmcKeyObservation.Available(UInt16("FS! ", Mask, 0xC0)));

            return new FanControlCapabilityResult(
                IsReadSupported: true,
                IsHardwareSafetyGateSatisfied: HardwareGate,
                HardwareGate ? [] : ["simulated hardware gate failure"],
                Protocol,
                snapshot,
                Family);
        }

        private static SmcValue UInt8(string key, byte value, byte attributes) =>
            new(new SmcKeyInfo(key, 1, "ui8 ", attributes), [value]);

        private static SmcValue UInt16(string key, ushort value, byte attributes) =>
            new(
                new SmcKeyInfo(key, 2, "ui16", attributes),
                [checked((byte)(value >> 8)), checked((byte)(value & 0xFF))]);

        private static SmcValue Fpe2(string key, ushort value, byte attributes) =>
            new(
                new SmcKeyInfo(key, 2, "fpe2", attributes),
                [checked((byte)(value >> 8)), checked((byte)(value & 0xFF))]);
    }
}
