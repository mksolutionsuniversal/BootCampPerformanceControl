using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.Logging;

namespace BootCampPerformanceControl.Tests.FanControl;

public sealed class FanOverrideCoordinatorTests
{
    private const string Model = VerifiedHardwareModels.MacBookPro16_1;
    private static readonly DateTimeOffset FixedUtc =
        new(2026, 8, 18, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ApplyMaximumSafeRpmAsync_PersistsOwnershipBeforeWriterCall()
    {
        var events = new List<string>();
        var store = new InMemoryOwnershipStore(events);
        var writer = new FakeWriter(events);
        var coordinator = CreateCoordinator(store, writer);

        var result = await coordinator.ApplyMaximumSafeRpmAsync(
            Model,
            CreateCapability(),
            CancellationToken.None);

        Assert.True(result.IsApplied);
        Assert.NotNull(result.Marker);
        Assert.Equal(FixedUtc, result.Marker.CreatedAtUtc);
        Assert.Equal(new[] { "load", "save", "apply" }, events);
        Assert.NotNull(store.Marker);
        Assert.Equal(1, writer.ApplyCalls);
    }

    [Fact]
    public async Task ApplyMaximumSafeRpmAsync_BlockedPreflightDoesNotPersistOrWrite()
    {
        var store = new InMemoryOwnershipStore();
        var writer = new FakeWriter();
        var coordinator = CreateCoordinator(store, writer);
        var capability = CreateCapability(fan0Mode: 1);

        var result = await coordinator.ApplyMaximumSafeRpmAsync(
            Model,
            capability,
            CancellationToken.None);

        Assert.False(result.IsApplied);
        Assert.Null(result.Marker);
        Assert.Null(store.Marker);
        Assert.Equal(0, store.SaveCalls);
        Assert.Equal(0, writer.ApplyCalls);
    }

    [Fact]
    public async Task ApplyMaximumSafeRpmAsync_ExistingMarkerBlocksSecondOwnership()
    {
        var store = new InMemoryOwnershipStore
        {
            Marker = CreateMarker()
        };
        var writer = new FakeWriter();
        var coordinator = CreateCoordinator(store, writer);

        var result = await coordinator.ApplyMaximumSafeRpmAsync(
            Model,
            CreateCapability(),
            CancellationToken.None);

        Assert.False(result.IsApplied);
        Assert.Contains("already exists", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, store.SaveCalls);
        Assert.Equal(0, writer.ApplyCalls);
    }

    [Fact]
    public async Task ApplyMaximumSafeRpmAsync_WriterFailureRetainsOwnershipMarker()
    {
        var store = new InMemoryOwnershipStore();
        var writer = new FakeWriter
        {
            ApplyException = new InvalidOperationException("simulated write failure")
        };
        var coordinator = CreateCoordinator(store, writer);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ApplyMaximumSafeRpmAsync(
                Model,
                CreateCapability(),
                CancellationToken.None));

        Assert.NotNull(store.Marker);
        Assert.Equal(1, store.SaveCalls);
        Assert.Equal(0, store.ClearCalls);
        Assert.Equal(1, writer.ApplyCalls);
    }

    [Fact]
    public async Task RecoverAsync_NoMarkerDoesNothing()
    {
        var store = new InMemoryOwnershipStore();
        var writer = new FakeWriter();
        var coordinator = CreateCoordinator(store, writer);

        var decision = await coordinator.RecoverAsync(
            Model,
            CreateCapability(),
            CancellationToken.None);

        Assert.Equal(FanOverrideRecoveryAction.None, decision.Action);
        Assert.Equal(0, writer.RestoreCalls);
        Assert.Equal(0, store.ClearCalls);
    }

    [Fact]
    public async Task RecoverAsync_AppleAutoClearsStaleMarkerWithoutWriter()
    {
        var store = new InMemoryOwnershipStore
        {
            Marker = CreateMarker()
        };
        var writer = new FakeWriter();
        var coordinator = CreateCoordinator(store, writer);

        var decision = await coordinator.RecoverAsync(
            Model,
            CreateCapability(),
            CancellationToken.None);

        Assert.Equal(FanOverrideRecoveryAction.None, decision.Action);
        Assert.Null(store.Marker);
        Assert.Equal(1, store.ClearCalls);
        Assert.Equal(0, writer.RestoreCalls);
    }

    [Fact]
    public async Task RecoverAsync_MatchingOwnedManualStateRestoresAndClearsMarker()
    {
        var events = new List<string>();
        var store = new InMemoryOwnershipStore(events)
        {
            Marker = CreateMarker()
        };
        var writer = new FakeWriter(events);
        var coordinator = CreateCoordinator(store, writer);
        var capability = CreateCapability(
            fan0Mode: 1,
            fan1Mode: 1,
            fan0Target: 5616f,
            fan1Target: 5200f);

        var decision = await coordinator.RecoverAsync(
            Model,
            capability,
            CancellationToken.None);

        Assert.Equal(FanOverrideRecoveryAction.RestoreAppleAuto, decision.Action);
        Assert.Equal(new[] { "load", "restore", "clear" }, events);
        Assert.Equal(1, writer.RestoreCalls);
        Assert.Null(store.Marker);
    }

    [Fact]
    public async Task RecoverAsync_RestoreFailureRetainsMarker()
    {
        var store = new InMemoryOwnershipStore
        {
            Marker = CreateMarker()
        };
        var writer = new FakeWriter
        {
            RestoreException = new InvalidOperationException("simulated verification failure")
        };
        var coordinator = CreateCoordinator(store, writer);
        var capability = CreateCapability(
            fan0Mode: 1,
            fan1Mode: 1,
            fan0Target: 5616f,
            fan1Target: 5200f);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.RecoverAsync(
                Model,
                capability,
                CancellationToken.None));

        Assert.NotNull(store.Marker);
        Assert.Equal(0, store.ClearCalls);
        Assert.Equal(1, writer.RestoreCalls);
    }

    [Fact]
    public async Task RecoverAsync_ChangedTargetBlocksAndRetainsMarker()
    {
        var store = new InMemoryOwnershipStore
        {
            Marker = CreateMarker()
        };
        var writer = new FakeWriter();
        var coordinator = CreateCoordinator(store, writer);
        var capability = CreateCapability(
            fan0Mode: 1,
            fan1Mode: 1,
            fan0Target: 5000f,
            fan1Target: 5200f);

        var decision = await coordinator.RecoverAsync(
            Model,
            capability,
            CancellationToken.None);

        Assert.Equal(FanOverrideRecoveryAction.Blocked, decision.Action);
        Assert.NotNull(store.Marker);
        Assert.Equal(0, store.ClearCalls);
        Assert.Equal(0, writer.RestoreCalls);
    }

    private static FanOverrideCoordinator CreateCoordinator(
        InMemoryOwnershipStore store,
        FakeWriter writer)
    {
        return new FanOverrideCoordinator(
            new FanOverridePreflightPolicy(),
            new FanOverrideRecoveryPolicy(),
            store,
            writer,
            new TestLogger(),
            new FixedTimeProvider(FixedUtc));
    }

    private static FanOverrideOwnershipMarker CreateMarker()
    {
        return new FanOverrideOwnershipMarker(
            Model,
            5616f,
            5200f,
            FixedUtc);
    }

    private static FanControlCapabilityResult CreateCapability(
        byte fan0Mode = 0,
        byte fan1Mode = 0,
        float fan0Target = 1836f,
        float fan1Target = 1700f)
    {
        var snapshot = new FanSmcSnapshot(
            UInt8("FNum", 2, 0x80),
            Float32("F0Mx", 5616f, 0x85),
            Float32("F1Mx", 5200f, 0x85),
            Float32("F0Ac", 1837f, 0x84),
            Float32("F1Ac", 1701f, 0x84),
            UInt8("F0Md", fan0Mode, 0xD0),
            UInt8("F1Md", fan1Mode, 0xD0),
            Float32("F0Tg", fan0Target, 0xD4),
            Float32("F1Tg", fan1Target, 0xD4));

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

    private sealed class InMemoryOwnershipStore : IFanOverrideOwnershipStore
    {
        private readonly List<string>? _events;

        public InMemoryOwnershipStore(List<string>? events = null)
        {
            _events = events;
        }

        public FanOverrideOwnershipMarker? Marker { get; set; }

        public int SaveCalls { get; private set; }

        public int ClearCalls { get; private set; }

        public Task<FanOverrideOwnershipMarker?> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _events?.Add("load");
            return Task.FromResult(Marker);
        }

        public Task SaveNewAsync(
            FanOverrideOwnershipMarker marker,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Marker is not null)
            {
                throw new InvalidOperationException("Marker already exists.");
            }

            SaveCalls++;
            Marker = marker;
            _events?.Add("save");
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClearCalls++;
            Marker = null;
            _events?.Add("clear");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeWriter : IFanOverrideWriter
    {
        private readonly List<string>? _events;

        public FakeWriter(List<string>? events = null)
        {
            _events = events;
        }

        public Exception? ApplyException { get; init; }

        public Exception? RestoreException { get; init; }

        public int ApplyCalls { get; private set; }

        public int RestoreCalls { get; private set; }

        public Task ApplyMaximumSafeRpmAsync(
            FanMaximumSafeRpmPlan plan,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyCalls++;
            _events?.Add("apply");

            if (ApplyException is not null)
            {
                throw ApplyException;
            }

            return Task.CompletedTask;
        }

        public Task RestoreAppleAutoAsync(
            FanOverrideOwnershipMarker ownershipMarker,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(ownershipMarker);
            cancellationToken.ThrowIfCancellationRequested();
            RestoreCalls++;
            _events?.Add("restore");

            if (RestoreException is not null)
            {
                throw RestoreException;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
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
