using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.Logging;

namespace BootCampPerformanceControl.Tests.FanControl;

public sealed class VerifiedFanOverrideWriterTests
{
    private const string Model = VerifiedHardwareModels.MacBookPro16_1;
    private static readonly FanMaximumSafeRpmPlan Plan =
        CreatePerFanPlan([5616f, 5200f], [1836f, 1700f]);
    private static readonly FanOverrideOwnershipMarker Marker =
        new(Model, 5616f, 5200f, new DateTimeOffset(2026, 8, 18, 19, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task ApplyMaximumSafeRpmAsync_UsesConfirmedSequenceThenVerifiesReadback()
    {
        var events = new List<string>();
        var probe = new SequenceProbe(
            events,
            CreateCapability(),
            CreateCapability(
                fan0Mode: 1,
                fan1Mode: 1,
                fan0Target: 5616f,
                fan1Target: 5200f));
        var backend = new RecordingWriteBackend(events);
        var writer = CreateWriter(backend, probe);

        await writer.ApplyMaximumSafeRpmAsync(Plan, CancellationToken.None);

        Assert.Equal(
            new[]
            {
                "probe",
                "manual:Fan0",
                "manual:Fan1",
                "target:Fan0:5616",
                "target:Fan1:5200",
                "manual:Fan0",
                "manual:Fan1",
                "probe"
            },
            events);
    }

    [Fact]
    public async Task ApplyMaximumSafeRpmAsync_DynamicPlanPreservesAllModesTargetsModesOrdering()
    {
        var events = new List<string>();
        var maxima = new[] { 5616f, 5200f, 4800f };
        var plan = CreatePerFanPlan(maxima, [1500f, 1500f, 1500f]);
        var probe = new SequenceProbe(
            events,
            CreateDynamicCapability(maxima, manualMaximum: false),
            CreateDynamicCapability(maxima, manualMaximum: true));
        var backend = new RecordingWriteBackend(events);
        var writer = CreateWriter(backend, probe);

        await writer.ApplyMaximumSafeRpmAsync(plan, CancellationToken.None);

        Assert.Equal(
            new[]
            {
                "probe",
                "manual:Fan0",
                "manual:Fan1",
                "manual:Fan2",
                "target:Fan0:5616",
                "target:Fan1:5200",
                "target:Fan2:4800",
                "manual:Fan0",
                "manual:Fan1",
                "manual:Fan2",
                "probe"
            },
            events);
    }

    [Fact]
    public async Task OneFanTopology_AppliesAndRestoresOnlyFan0()
    {
        const string model = Model;
        var maxima = new[] { 2900f };
        var plan = CreatePerFanPlan(maxima, [1500f]);
        var applyEvents = new List<string>();
        var applyWriter = CreateWriter(
            new RecordingWriteBackend(applyEvents),
            new SequenceProbe(
                applyEvents,
                CreateDynamicCapability(maxima, manualMaximum: false),
                CreateDynamicCapability(maxima, manualMaximum: true)));

        await applyWriter.ApplyMaximumSafeRpmAsync(plan, CancellationToken.None);

        Assert.Equal(
            ["probe", "manual:Fan0", "target:Fan0:2900", "manual:Fan0", "probe"],
            applyEvents);

        var marker = new FanOverrideOwnershipMarker(
            model,
            [new FanOverrideOwnershipTarget(new FanIndex(0), maxima[0])],
            new DateTimeOffset(2026, 8, 18, 19, 0, 0, TimeSpan.Zero));
        var restoreEvents = new List<string>();
        var restoreWriter = CreateWriter(
            new RecordingWriteBackend(restoreEvents),
            new SequenceProbe(
                restoreEvents,
                CreateDynamicCapability(maxima, manualMaximum: true),
                CreateDynamicCapability(maxima, manualMaximum: false)));

        await restoreWriter.RestoreAppleAutoAsync(marker, CancellationToken.None);

        Assert.Equal(["probe", "auto:Fan0", "probe"], restoreEvents);
    }

    [Fact]
    public async Task ApplyMaximumSafeRpmAsync_FreshPlanMismatchBlocksBeforeAnyWrite()
    {
        var events = new List<string>();
        var probe = new SequenceProbe(events, CreateCapability());
        var backend = new RecordingWriteBackend(events);
        var writer = CreateWriter(backend, probe);
        var stalePlan = new FanMaximumSafeRpmPlan(
            Model,
            [
                PerFanTarget(0, 5600f, 1836f),
                PerFanTarget(1, 5200f, 1700f)
            ]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.ApplyMaximumSafeRpmAsync(stalePlan, CancellationToken.None));

        Assert.Contains("changed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "probe" }, events);
    }

    [Fact]
    public async Task ApplyMaximumSafeRpmAsync_UnknownFreshFingerprintIssuesZeroWrites()
    {
        var events = new List<string>();
        var probe = new SequenceProbe(
            events,
            FanControlCapabilityResult.Rejected(
                SmcTransportProtocol.Mmio,
                "Write capability not verified."));
        var writer = CreateWriter(new RecordingWriteBackend(events), probe);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.ApplyMaximumSafeRpmAsync(Plan, CancellationToken.None));

        Assert.Equal(["probe"], events);
    }

    [Fact]
    public async Task ApplyMaximumSafeRpmAsync_UnverifiedTransportCandidateIssuesZeroWrites()
    {
        var events = new List<string>();
        var diagnosticCandidate = CreateCapability() with
        {
            IsHardwareSafetyGateSatisfied = false,
            Protocol = SmcTransportProtocol.Unknown,
            Failures = ["MMIO (1) is required for writes."]
        };
        var writer = CreateWriter(
            new RecordingWriteBackend(events),
            new SequenceProbe(events, diagnosticCandidate));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.ApplyMaximumSafeRpmAsync(Plan, CancellationToken.None));

        Assert.True(diagnosticCandidate.IsReadSupported);
        Assert.Equal(FanCapabilityFamily.PerFanModeFloat32, diagnosticCandidate.Family);
        Assert.Equal(["probe"], events);
    }

    [Fact]
    public async Task ApplyMaximumSafeRpmAsync_GlobalModeReadFailureIssuesZeroWrites()
    {
        var events = new List<string>();
        var readable = CreateCapability();
        var failedSnapshot = new FanSmcSnapshot(
            readable.Snapshot!.FanCount,
            readable.Snapshot.Fans,
            SmcKeyObservation.ReadFailed(
                "FS! ",
                new IOException("transient DeviceIoControl failure")));
        var failedCapability = new FanSafetyPolicy().Evaluate(
            Model,
            SmcTransportProtocol.Mmio,
            failedSnapshot);
        var writer = CreateWriter(
            new RecordingWriteBackend(events),
            new SequenceProbe(events, failedCapability));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.ApplyMaximumSafeRpmAsync(Plan, CancellationToken.None));

        Assert.Equal(FanCapabilityFamily.Unknown, failedCapability.Family);
        Assert.Equal(["probe"], events);
    }

    [Fact]
    public async Task GlobalMaskFpe2_ModeReadFailureIssuesZeroWrites()
    {
        var events = new List<string>();
        var readable = CreateGlobalCapability([0x60DC], 0x0000, [0x248C]);
        var fan = readable.Snapshot!.Fans[0];
        var failedFan = new FanSmcChannelSnapshot(
            fan.Index,
            fan.Minimum,
            fan.MaximumObservation,
            fan.ActualObservation,
            SmcKeyObservation.ReadFailed(
                "F0Md",
                new IOException("transient DeviceIoControl failure")),
            fan.TargetObservation);
        var failedCapability = new FanSafetyPolicy().Evaluate(
            Model,
            SmcTransportProtocol.Mmio,
            new FanSmcSnapshot(
                readable.Snapshot.FanCount,
                [failedFan],
                readable.Snapshot.GlobalMode));
        var writer = CreateWriter(
            new RecordingWriteBackend(events),
            new SequenceProbe(events, failedCapability));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.ApplyMaximumSafeRpmAsync(
                CreateGlobalPlan(0x60DC),
                CancellationToken.None));

        Assert.Equal(FanCapabilityFamily.Unknown, failedCapability.Family);
        Assert.Equal(["probe"], events);
    }

    [Fact]
    public async Task ApplyMaximumSafeRpmAsync_MalformedFreshMetadataIssuesZeroWrites()
    {
        var events = new List<string>();
        var malformed = CreateCapability() with
        {
            IsReadSupported = false,
            IsHardwareSafetyGateSatisfied = false,
            Family = FanCapabilityFamily.Unknown,
            Failures = ["F0Mx metadata mismatch."]
        };
        var writer = CreateWriter(
            new RecordingWriteBackend(events),
            new SequenceProbe(events, malformed));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.ApplyMaximumSafeRpmAsync(Plan, CancellationToken.None));

        Assert.Equal(["probe"], events);
    }

    [Fact]
    public async Task GlobalMaskFpe2_UnprovenTopologyIssuesZeroWrites()
    {
        var events = new List<string>();
        var plan = CreateGlobalPlan(0x60DC, 0x58DC, 0x50DC);
        var unsupported = CreateGlobalCapability(
            [0x60DC, 0x58DC, 0x50DC],
            0x0000,
            [0x248C, 0x248C, 0x248C]) with
        {
            IsHardwareSafetyGateSatisfied = false,
            Failures = ["Write capability not verified for this topology."]
        };
        var writer = CreateWriter(
            new RecordingWriteBackend(events),
            new SequenceProbe(events, unsupported));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.ApplyMaximumSafeRpmAsync(plan, CancellationToken.None));

        Assert.Equal(["probe"], events);
    }

    [Fact]
    public async Task ApplyMaximumSafeRpmAsync_PartialWriteFailureRollsBackToVerifiedAppleAuto()
    {
        var events = new List<string>();
        var probe = new SequenceProbe(
            events,
            CreateCapability(),
            CreateCapability());
        var backend = new RecordingWriteBackend(events)
        {
            ThrowOnEvent = "target:Fan0:5616"
        };
        var writer = CreateWriter(backend, probe);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.ApplyMaximumSafeRpmAsync(Plan, CancellationToken.None));

        Assert.Equal(
            new[]
            {
                "probe",
                "manual:Fan0",
                "manual:Fan1",
                "target:Fan0:5616",
                "auto:Fan0",
                "auto:Fan1",
                "probe"
            },
            events);
    }

    [Fact]
    public async Task ApplyMaximumSafeRpmAsync_ReadbackFailureTriggersEmergencyRollback()
    {
        var events = new List<string>();
        var probe = new SequenceProbe(
            events,
            CreateCapability(),
            CreateCapability(
                fan0Mode: 1,
                fan1Mode: 1,
                fan0Target: 5000f,
                fan1Target: 5200f),
            CreateCapability());
        var backend = new RecordingWriteBackend(events);
        var writer = CreateWriter(backend, probe);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.ApplyMaximumSafeRpmAsync(Plan, CancellationToken.None));

        Assert.Contains("auto:Fan0", events);
        Assert.Contains("auto:Fan1", events);
        Assert.Equal(3, probe.Calls);
    }

    [Fact]
    public async Task ApplyMaximumSafeRpmAsync_RollbackVerificationFailureReportsBothFailures()
    {
        var events = new List<string>();
        var probe = new SequenceProbe(
            events,
            CreateCapability(),
            CreateCapability(
                fan0Mode: 1,
                fan1Mode: 1,
                fan0Target: 5000f,
                fan1Target: 5200f),
            CreateCapability(
                fan0Mode: 1,
                fan1Mode: 1,
                fan0Target: 5616f,
                fan1Target: 5200f));
        var backend = new RecordingWriteBackend(events);
        var writer = CreateWriter(backend, probe);

        var exception = await Assert.ThrowsAsync<FanOverrideRollbackException>(
            () => writer.ApplyMaximumSafeRpmAsync(Plan, CancellationToken.None));

        Assert.IsType<InvalidOperationException>(exception.OperationException);
        Assert.IsType<InvalidOperationException>(exception.RollbackException);
    }

    [Fact]
    public async Task RestoreAppleAutoAsync_RechecksOwnershipBeforeAnyWrite()
    {
        var events = new List<string>();
        var probe = new SequenceProbe(
            events,
            CreateCapability(
                fan0Mode: 1,
                fan1Mode: 1,
                fan0Target: 5000f,
                fan1Target: 5200f));
        var backend = new RecordingWriteBackend(events);
        var writer = CreateWriter(backend, probe);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.RestoreAppleAutoAsync(Marker, CancellationToken.None));

        Assert.Contains("blocked", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "probe" }, events);
    }

    [Fact]
    public async Task RestoreAppleAutoAsync_MatchingOwnershipRestoresBothFansThenVerifies()
    {
        var events = new List<string>();
        var probe = new SequenceProbe(
            events,
            CreateCapability(
                fan0Mode: 1,
                fan1Mode: 1,
                fan0Target: 5616f,
                fan1Target: 5200f),
            CreateCapability());
        var backend = new RecordingWriteBackend(events);
        var writer = CreateWriter(backend, probe);

        await writer.RestoreAppleAutoAsync(Marker, CancellationToken.None);

        Assert.Equal(
            new[]
            {
                "probe",
                "auto:Fan0",
                "auto:Fan1",
                "probe"
            },
            events);
    }

    [Fact]
    public async Task RestoreAppleAutoAsync_AlreadyAppleAutoDoesNotWrite()
    {
        var events = new List<string>();
        var probe = new SequenceProbe(events, CreateCapability());
        var backend = new RecordingWriteBackend(events);
        var writer = CreateWriter(backend, probe);

        await writer.RestoreAppleAutoAsync(Marker, CancellationToken.None);

        Assert.Equal(new[] { "probe" }, events);
    }

    [Fact]
    public async Task RestoreAppleAutoAsync_DynamicMarkerRestoresEveryOwnedFan()
    {
        var events = new List<string>();
        var maxima = new[] { 5616f, 5200f, 4800f };
        var marker = new FanOverrideOwnershipMarker(
            Model,
            maxima.Select((rpm, index) => new FanOverrideOwnershipTarget(new FanIndex(index), rpm)),
            new DateTimeOffset(2026, 8, 18, 19, 0, 0, TimeSpan.Zero));
        var probe = new SequenceProbe(
            events,
            CreateDynamicCapability(maxima, manualMaximum: true),
            CreateDynamicCapability(maxima, manualMaximum: false));
        var backend = new RecordingWriteBackend(events);
        var writer = CreateWriter(backend, probe);

        await writer.RestoreAppleAutoAsync(marker, CancellationToken.None);

        Assert.Equal(
            new[]
            {
                "probe",
                "auto:Fan0",
                "auto:Fan1",
                "auto:Fan2",
                "probe"
            },
            events);
    }

    [Fact]
    public async Task RestoreAppleAutoAsync_WriteExceptionStillAttemptsEveryFanAndTrustsReadback()
    {
        var events = new List<string>();
        var maxima = new[] { 5616f, 5200f, 4800f };
        var marker = new FanOverrideOwnershipMarker(
            Model,
            maxima.Select((rpm, index) => new FanOverrideOwnershipTarget(new FanIndex(index), rpm)),
            new DateTimeOffset(2026, 8, 18, 19, 0, 0, TimeSpan.Zero));
        var probe = new SequenceProbe(
            events,
            CreateDynamicCapability(maxima, manualMaximum: true),
            CreateDynamicCapability(maxima, manualMaximum: false));
        var backend = new RecordingWriteBackend(events)
        {
            ThrowOnEvent = "auto:Fan1"
        };
        var writer = CreateWriter(backend, probe);

        await writer.RestoreAppleAutoAsync(marker, CancellationToken.None);

        Assert.Contains("auto:Fan0", events);
        Assert.Contains("auto:Fan1", events);
        Assert.Contains("auto:Fan2", events);
        Assert.Equal("probe", events[^1]);
    }

    [Fact]
    public async Task GlobalMaskFpe2_OneFanWritesModeVerifiesThenCopiesFreshMaximumExactly()
    {
        var events = new List<string>();
        var plan = CreateGlobalPlan(0x60DC);
        var probe = new SequenceProbe(
            events,
            CreateGlobalCapability([0x60DC], 0x0000, [0x248C]),
            CreateGlobalCapability([0x60DC], 0x0001, [0x248C]),
            CreateGlobalCapability([0x60DC], 0x0001, [0x60DC]));
        var writer = CreateWriter(new RecordingWriteBackend(events), probe);

        await writer.ApplyMaximumSafeRpmAsync(plan, CancellationToken.None);

        Assert.Equal(
            ["probe", "global:0001", "probe", "fpe2:Fan0:60DC", "probe"],
            events);
    }

    [Fact]
    public async Task GlobalMaskFpe2_TwoFansUsesOnlyProvenMaskAndExactPerFanPayloads()
    {
        var events = new List<string>();
        var plan = CreateGlobalPlan(0x60DC, 0x58DC);
        var probe = new SequenceProbe(
            events,
            CreateGlobalCapability([0x60DC, 0x58DC], 0x0000, [0x248C, 0x248C]),
            CreateGlobalCapability([0x60DC, 0x58DC], 0x0003, [0x248C, 0x248C]),
            CreateGlobalCapability([0x60DC, 0x58DC], 0x0003, [0x60DC, 0x58DC]));
        var writer = CreateWriter(new RecordingWriteBackend(events), probe);

        await writer.ApplyMaximumSafeRpmAsync(plan, CancellationToken.None);

        Assert.Equal(
            [
                "probe", "global:0003", "probe",
                "fpe2:Fan0:60DC", "fpe2:Fan1:58DC", "probe"
            ],
            events);
    }

    [Fact]
    public async Task GlobalMaskFpe2_FsReadbackFailureRollsBackBeforeAnyTargetWrite()
    {
        var events = new List<string>();
        var plan = CreateGlobalPlan(0x60DC);
        var probe = new SequenceProbe(
            events,
            CreateGlobalCapability([0x60DC], 0x0000, [0x248C]),
            CreateGlobalCapability([0x60DC], 0x0000, [0x248C]),
            CreateGlobalCapability([0x60DC], 0x0000, [0x248C]));
        var writer = CreateWriter(new RecordingWriteBackend(events), probe);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.ApplyMaximumSafeRpmAsync(plan, CancellationToken.None));

        Assert.Equal(
            ["probe", "global:0001", "probe", "global:auto", "probe"],
            events);
        Assert.DoesNotContain(events, value => value.StartsWith("fpe2:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GlobalMaskFpe2_TargetReadbackFailureTriggersGlobalAppleAutoRollback()
    {
        var events = new List<string>();
        var plan = CreateGlobalPlan(0x60DC);
        var probe = new SequenceProbe(
            events,
            CreateGlobalCapability([0x60DC], 0x0000, [0x248C]),
            CreateGlobalCapability([0x60DC], 0x0001, [0x248C]),
            CreateGlobalCapability([0x60DC], 0x0001, [0x248C]),
            CreateGlobalCapability([0x60DC], 0x0000, [0x248C]));
        var writer = CreateWriter(new RecordingWriteBackend(events), probe);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.ApplyMaximumSafeRpmAsync(plan, CancellationToken.None));

        Assert.Contains("global:auto", events);
        Assert.Equal("probe", events[^1]);
    }

    [Fact]
    public async Task GlobalMaskFpe2_PartialTargetFailureUsesNonCancellableGlobalRollback()
    {
        var events = new List<string>();
        var plan = CreateGlobalPlan(0x60DC, 0x58DC);
        var probe = new SequenceProbe(
            events,
            CreateGlobalCapability([0x60DC, 0x58DC], 0x0000, [0x248C, 0x248C]),
            CreateGlobalCapability([0x60DC, 0x58DC], 0x0003, [0x248C, 0x248C]),
            CreateGlobalCapability([0x60DC, 0x58DC], 0x0000, [0x60DC, 0x248C]));
        var backend = new RecordingWriteBackend(events)
        {
            ThrowOnEvent = "fpe2:Fan1:58DC"
        };
        var writer = CreateWriter(backend, probe);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.ApplyMaximumSafeRpmAsync(plan, CancellationToken.None));

        Assert.Equal(
            [
                "probe", "global:0003", "probe", "fpe2:Fan0:60DC", "fpe2:Fan1:58DC",
                "global:auto", "probe"
            ],
            events);
    }

    [Fact]
    public async Task GlobalMaskFpe2_FreshMaximumChangeBlocksBeforeAnyWrite()
    {
        var events = new List<string>();
        var plan = CreateGlobalPlan(0x60DC);
        var probe = new SequenceProbe(
            events,
            CreateGlobalCapability([0x60E0], 0x0000, [0x248C]));
        var writer = CreateWriter(new RecordingWriteBackend(events), probe);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.ApplyMaximumSafeRpmAsync(plan, CancellationToken.None));

        Assert.Equal(["probe"], events);
    }

    [Fact]
    public async Task GlobalMaskFpe2_RestoreRelinquishesOnlyGlobalManualOwnership()
    {
        var events = new List<string>();
        var plan = CreateGlobalPlan(0x60DC);
        var marker = FanOverrideOwnershipMarker.FromPlan(
            plan,
            new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero));
        var probe = new SequenceProbe(
            events,
            CreateGlobalCapability([0x60DC], 0x0001, [0x60DC]),
            CreateGlobalCapability([0x60DC], 0x0000, [0x60DC]));
        var writer = CreateWriter(new RecordingWriteBackend(events), probe);

        await writer.RestoreAppleAutoAsync(marker, CancellationToken.None);

        Assert.Equal(["probe", "global:auto", "probe"], events);
    }

    private static VerifiedFanOverrideWriter CreateWriter(
        IFanSmcWriteBackend backend,
        IFanCapabilityProbe probe)
    {
        return new VerifiedFanOverrideWriter(
            backend,
            probe,
            new FanOverridePreflightPolicy(),
            new FanOverrideRecoveryPolicy(),
            new TestLogger(),
            verificationAttempts: 1,
            verificationDelay: TimeSpan.Zero);
    }

    private static FanMaximumSafeRpmPlan CreateGlobalPlan(params ushort[] maxima)
    {
        return new FanMaximumSafeRpmPlan(
            Model,
            FanCapabilityFamily.GlobalMaskFpe2,
            maxima.Select((raw, index) =>
                new FanMaximumSafeRpmTarget(new FanIndex(index), raw / 4f)
                {
                    ExactTargetPayload = new[]
                    {
                        checked((byte)(raw >> 8)),
                        checked((byte)(raw & 0xFF))
                    },
                    BaselineTargetPayload = new byte[] { 0x24, 0x8C }
                }),
            baselineGlobalModeMask: 0);
    }

    private static FanMaximumSafeRpmPlan CreatePerFanPlan(
        IReadOnlyList<float> maxima,
        IReadOnlyList<float> baselines)
    {
        return new FanMaximumSafeRpmPlan(
            Model,
            maxima.Select((rpm, index) => PerFanTarget(index, rpm, baselines[index])));
    }

    private static FanMaximumSafeRpmTarget PerFanTarget(
        int index,
        float maximum,
        float baseline)
    {
        return new FanMaximumSafeRpmTarget(new FanIndex(index), maximum)
        {
            ExactTargetPayload = BitConverter.GetBytes(maximum),
            BaselineTargetPayload = BitConverter.GetBytes(baseline),
            BaselineMode = 0
        };
    }

    private static FanControlCapabilityResult CreateGlobalCapability(
        IReadOnlyList<ushort> maxima,
        ushort mask,
        IReadOnlyList<ushort> targets)
    {
        var fans = maxima.Select((maximum, index) =>
        {
            var fan = new FanIndex(index);
            return new FanSmcChannelSnapshot(
                fan,
                Available(Fpe2(fan.GetSmcKey("Mn"), 0x144C, 0xC0)),
                Available(Fpe2(fan.GetSmcKey("Mx"), maximum, 0xC0)),
                Available(Fpe2(fan.GetSmcKey("Ac"), 0x2404, 0x90)),
                SmcKeyObservation.ConfirmedAbsent(fan.GetSmcKey("Md")),
                Available(Fpe2(fan.GetSmcKey("Tg"), targets[index], 0xD0)));
        });
        var snapshot = new FanSmcSnapshot(
            UInt8("FNum", checked((byte)maxima.Count), 0x80),
            fans,
            Available(UInt16("FS! ", mask, 0xC0)));

        return new FanControlCapabilityResult(
            true,
            true,
            Array.Empty<string>(),
            SmcTransportProtocol.Mmio,
            snapshot,
            FanCapabilityFamily.GlobalMaskFpe2);
    }

    private static SmcKeyObservation Available(SmcValue value) =>
        SmcKeyObservation.Available(value);

    private static SmcValue Fpe2(string key, ushort raw, byte attributes)
    {
        return new SmcValue(
            new SmcKeyInfo(key, 2, "fpe2", attributes),
            [checked((byte)(raw >> 8)), checked((byte)(raw & 0xFF))]);
    }

    private static SmcValue UInt16(string key, ushort value, byte attributes)
    {
        return new SmcValue(
            new SmcKeyInfo(key, 2, "ui16", attributes),
            [checked((byte)(value >> 8)), checked((byte)(value & 0xFF))]);
    }

    private static FanControlCapabilityResult CreateCapability(
        byte fan0Mode = 0,
        byte fan1Mode = 0,
        float fan0Target = 1836f,
        float fan1Target = 1700f)
    {
        var snapshot = new FanSmcSnapshot(
            UInt8("FNum", 2, 0x80),
            [
                new FanSmcChannelSnapshot(new FanIndex(0),
                    Float32("F0Mx", 5616f, 0x85), Float32("F0Ac", 1837f, 0x84),
                    UInt8("F0Md", fan0Mode, 0xD0), Float32("F0Tg", fan0Target, 0xD4)),
                new FanSmcChannelSnapshot(new FanIndex(1),
                    Float32("F1Mx", 5200f, 0x85), Float32("F1Ac", 1701f, 0x84),
                    UInt8("F1Md", fan1Mode, 0xD0), Float32("F1Tg", fan1Target, 0xD4))
            ]);

        return new FanControlCapabilityResult(
            IsReadSupported: true,
            IsHardwareSafetyGateSatisfied: true,
            Array.Empty<string>(),
            SmcTransportProtocol.Mmio,
            snapshot,
            FanCapabilityFamily.PerFanModeFloat32);
    }

    private static FanControlCapabilityResult CreateDynamicCapability(
        IReadOnlyList<float> maxima,
        bool manualMaximum)
    {
        var fans = maxima.Select((maximum, value) =>
        {
            var index = new FanIndex(value);
            return new FanSmcChannelSnapshot(
                index,
                Float32(index.GetSmcKey("Mx"), maximum, 0x85),
                Float32(index.GetSmcKey("Ac"), 1500f, 0x84),
                UInt8(index.GetSmcKey("Md"), manualMaximum ? (byte)1 : (byte)0, 0xD0),
                Float32(index.GetSmcKey("Tg"), manualMaximum ? maximum : 1500f, 0xD4));
        });
        var snapshot = new FanSmcSnapshot(
            UInt8("FNum", checked((byte)maxima.Count), 0x80),
            fans);

        return new FanControlCapabilityResult(
            IsReadSupported: true,
            IsHardwareSafetyGateSatisfied: true,
            Array.Empty<string>(),
            SmcTransportProtocol.Mmio,
            snapshot,
            FanCapabilityFamily.PerFanModeFloat32);
    }

    private static SmcValue UInt8(string key, byte value, byte attributes)
    {
        return new SmcValue(
            new SmcKeyInfo(key, 1, "ui8 ", attributes),
            [value]);
    }

    private static SmcValue Float32(string key, float value, byte attributes)
    {
        return new SmcValue(
            new SmcKeyInfo(key, 4, "flt ", attributes),
            BitConverter.GetBytes(value));
    }

    private sealed class SequenceProbe : IFanCapabilityProbe
    {
        private readonly List<string> _events;
        private readonly Queue<FanControlCapabilityResult> _results;

        public SequenceProbe(
            List<string> events,
            params FanControlCapabilityResult[] results)
        {
            _events = events;
            _results = new Queue<FanControlCapabilityResult>(results);
        }

        public int Calls { get; private set; }

        public Task<FanControlCapabilityResult> ProbeAsync(
            string model,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(Model, model);
            Calls++;
            _events.Add("probe");

            if (_results.Count == 0)
            {
                throw new InvalidOperationException("No fake capability result remains.");
            }

            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class RecordingWriteBackend : IFanSmcWriteBackend
    {
        private readonly List<string> _events;

        public RecordingWriteBackend(List<string> events)
        {
            _events = events;
        }

        public string? ThrowOnEvent { get; init; }

        public bool SupportsFamily(FanCapabilityFamily family) =>
            family is FanCapabilityFamily.PerFanModeFloat32
                or FanCapabilityFamily.GlobalMaskFpe2;

        public Task SetManualModeAsync(
            FanIndex fan,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Record($"manual:{fan}");
            return Task.CompletedTask;
        }

        public Task SetTargetRpmAsync(
            FanIndex fan,
            float targetRpm,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Record($"target:{fan}:{targetRpm:0}");
            return Task.CompletedTask;
        }

        public Task SetAppleAutoAsync(
            FanIndex fan,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Record($"auto:{fan}");
            return Task.CompletedTask;
        }

        public Task SetGlobalManualMaskAsync(
            ushort mask,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Record($"global:{mask:X4}");
            return Task.CompletedTask;
        }

        public Task SetFpe2TargetPayloadAsync(
            FanIndex fan,
            ReadOnlyMemory<byte> exactPayload,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Record($"fpe2:{fan}:{Convert.ToHexString(exactPayload.Span)}");
            return Task.CompletedTask;
        }

        public Task SetGlobalAppleAutoAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Record("global:auto");
            return Task.CompletedTask;
        }

        private void Record(string value)
        {
            _events.Add(value);
            if (string.Equals(value, ThrowOnEvent, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Simulated backend failure at {value}.");
            }
        }
    }

    private sealed class TestLogger : IApplicationLogger
    {
        public void Info(string message)
        {
        }

        public void Error(string message, Exception exception)
        {
        }
    }
}
