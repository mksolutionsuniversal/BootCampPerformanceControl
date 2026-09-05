using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.Logging;

namespace BootCampPerformanceControl.Tests.FanControl;

public sealed class VerifiedFanOverrideWriterTests
{
    private const string Model = VerifiedHardwareModels.MacBookPro16_1;
    private static readonly FanMaximumSafeRpmPlan Plan =
        new(
            Model,
            [
                new FanMaximumSafeRpmTarget(new FanIndex(0), 5616f),
                new FanMaximumSafeRpmTarget(new FanIndex(1), 5200f)
            ]);
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
        var plan = new FanMaximumSafeRpmPlan(
            Model,
            maxima.Select((rpm, index) => new FanMaximumSafeRpmTarget(new FanIndex(index), rpm)));
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
    public async Task ApplyMaximumSafeRpmAsync_FreshPlanMismatchBlocksBeforeAnyWrite()
    {
        var events = new List<string>();
        var probe = new SequenceProbe(events, CreateCapability());
        var backend = new RecordingWriteBackend(events);
        var writer = CreateWriter(backend, probe);
        var stalePlan = new FanMaximumSafeRpmPlan(
            Model,
            [
                new FanMaximumSafeRpmTarget(new FanIndex(0), 5600f),
                new FanMaximumSafeRpmTarget(new FanIndex(1), 5200f)
            ]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.ApplyMaximumSafeRpmAsync(stalePlan, CancellationToken.None));

        Assert.Contains("changed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "probe" }, events);
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
            snapshot);
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
            snapshot);
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
