using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc.CrystalIdea;
using BootCampPerformanceControl.FanControl.Smc.Windows;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.Profiles;
using BootCampPerformanceControl.SettingsBackup;
using BootCampPerformanceControl.Tests.TestDoubles;

namespace BootCampPerformanceControl.Tests.Profiles;

public sealed class GamingOptimisedFanResumeServiceTests
{
    [Fact]
    public async Task ResumeAsync_PartialGaming_AppliesVerifiedFansWithoutPowerMutationOrSnapshotChange()
    {
        var originalSnapshot = PowerState(80, 70, 2, 2);
        var restoreStore = new TrackingRestoreSnapshotStore(originalSnapshot);
        var power = new ReadOnlyTrackingPowerManagementService(PowerState(95, 95, 0, 0));
        var ownershipStore = new TestFanOverrideOwnershipStore();
        var sessionFactory = new TestFanExecutionSessionFactory(ownershipStore);
        var service = CreateService(power, restoreStore, sessionFactory);

        var result = await service.ResumeAsync(CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.NotNull(ownershipStore.Marker);
        Assert.Equal(originalSnapshot, restoreStore.Snapshot);
        Assert.Equal(0, restoreStore.SaveCallCount);
        Assert.Equal(0, restoreStore.ReplaceCallCount);
        Assert.Equal(0, restoreStore.ClearCallCount);
        Assert.Equal(1, power.ReadCallCount);
        Assert.Equal(0, power.ApplyCallCount);
        Assert.Equal(0, power.RestoreCallCount);
    }

    [Fact]
    public async Task ResumeAsync_AppleSmcStopped_FailsClosedWithoutFanOrPowerWriteAndPreservesSnapshot()
    {
        var originalSnapshot = PowerState(80, 70, 2, 2);
        var restoreStore = new TrackingRestoreSnapshotStore(originalSnapshot);
        var power = new ReadOnlyTrackingPowerManagementService(PowerState(95, 95, 0, 0));
        var ownershipStore = new TestFanOverrideOwnershipStore();
        var sessionFactory = new TestFanExecutionSessionFactory(ownershipStore)
        {
            OpenSessionHandler = () => throw new AppleSmcServiceStateException(
                AppleSmcServiceState.Stopped)
        };
        var service = CreateService(power, restoreStore, sessionFactory);

        var result = await service.ResumeAsync(CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Contains("Enable fan monitoring/control", result.FailureReason);
        Assert.Null(ownershipStore.Marker);
        Assert.Equal(originalSnapshot, restoreStore.Snapshot);
        Assert.Equal(0, restoreStore.SaveCallCount);
        Assert.Equal(0, restoreStore.ReplaceCallCount);
        Assert.Equal(0, restoreStore.ClearCallCount);
        Assert.Equal(0, power.ApplyCallCount);
        Assert.Equal(0, power.RestoreCallCount);
    }

    private static GamingOptimisedFanResumeService CreateService(
        IPowerManagementService powerManagementService,
        IRestoreSnapshotStore restoreSnapshotStore,
        IFanExecutionSessionFactory fanExecutionSessionFactory)
    {
        return new GamingOptimisedFanResumeService(
            new FixedHardwareDetectionService(),
            new ProfileCatalog(),
            new ProfileExecutionResolver(),
            new FanProfileExecutionResolver(),
            powerManagementService,
            restoreSnapshotStore,
            fanExecutionSessionFactory);
    }

    private static PowerStateSnapshot PowerState(
        uint maximumAc,
        uint maximumDc,
        uint boostAc,
        uint boostDc)
    {
        return new PowerStateSnapshot(
            Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"),
            maximumAc,
            maximumDc,
            boostAc,
            boostDc,
            DateTimeOffset.Parse("2026-01-01T00:00:00+00:00"));
    }

    private sealed class FixedHardwareDetectionService : IHardwareDetectionService
    {
        private static readonly ModelVerificationResult Verification = new(
            "Apple Inc.",
            VerifiedHardwareModels.MacBookPro16_1,
            PlatformSupportStatus.SupportedIntelMac,
            ModelValidationLevel.PerformanceValidated,
            "Verified in test.");

        public Task<HardwareSnapshot> DetectAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new HardwareSnapshot(
                new ComputerSystemInfo(
                    Verification.Manufacturer,
                    Verification.Model,
                    "x64-based PC"),
                Processor: null,
                VideoControllers: [],
                OperatingSystem: null,
                DateTimeOffset.UtcNow));
        }

        public ModelVerificationResult VerifyModel(HardwareSnapshot snapshot) => Verification;
    }

    private sealed class ReadOnlyTrackingPowerManagementService : IPowerManagementService
    {
        private readonly PowerStateSnapshot _currentState;

        public ReadOnlyTrackingPowerManagementService(PowerStateSnapshot currentState)
        {
            _currentState = currentState;
        }

        public int ReadCallCount { get; private set; }

        public int ApplyCallCount { get; private set; }

        public int RestoreCallCount { get; private set; }

        public Task<PowerStateSnapshot> ReadCurrentStateAsync(CancellationToken cancellationToken)
        {
            ReadCallCount++;
            return Task.FromResult(_currentState);
        }

        public Task<PowerOperationResult> ApplyProcessorSettingsAsync(
            ProcessorPowerSettings requestedSettings,
            CancellationToken cancellationToken)
        {
            ApplyCallCount++;
            throw new InvalidOperationException("Fan-only resume must not mutate power settings.");
        }

        public Task<PowerOperationResult> ApplyProcessorSettingsAsync(
            ProcessorPowerSettings requestedSettings,
            PowerStateSnapshot expectedStateBefore,
            CancellationToken cancellationToken)
        {
            ApplyCallCount++;
            throw new InvalidOperationException("Fan-only resume must not mutate power settings.");
        }

        public Task<PowerOperationResult> RestoreOriginalSettingsAsync(
            CancellationToken cancellationToken)
        {
            RestoreCallCount++;
            throw new InvalidOperationException("Fan-only resume must not restore power settings.");
        }
    }

    private sealed class TrackingRestoreSnapshotStore : IRestoreSnapshotStore
    {
        public TrackingRestoreSnapshotStore(PowerStateSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public bool HasOriginalRestoreSnapshot => Snapshot is not null;

        public PowerStateSnapshot? Snapshot { get; private set; }

        public int SaveCallCount { get; private set; }

        public int ReplaceCallCount { get; private set; }

        public int ClearCallCount { get; private set; }

        public Task<bool> TrySaveOriginalRestoreSnapshotAsync(
            PowerStateSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            SaveCallCount++;
            throw new InvalidOperationException("Fan-only resume must not save a restore snapshot.");
        }

        public Task<PowerStateSnapshot?> GetOriginalRestoreSnapshotAsync(
            CancellationToken cancellationToken) => Task.FromResult(Snapshot);

        public Task ReplaceOriginalRestoreSnapshotAsync(
            PowerStateSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            ReplaceCallCount++;
            throw new InvalidOperationException("Fan-only resume must not replace a restore snapshot.");
        }

        public Task ClearOriginalRestoreSnapshotAsync(CancellationToken cancellationToken)
        {
            ClearCallCount++;
            throw new InvalidOperationException("Fan-only resume must not clear a restore snapshot.");
        }
    }
}
