using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc.CrystalIdea;
using BootCampPerformanceControl.FanControl.Smc.Windows;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.Logging;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.Profiles;
using BootCampPerformanceControl.SettingsBackup;
using BootCampPerformanceControl.Tests.TestDoubles;

namespace BootCampPerformanceControl.Tests.Profiles;

public sealed class ProfileRestoreServiceTests
{
    [Fact]
    public async Task RestoreAsync_ExactVerifiedMacBookPro16_1_RoutesToGamingOptimisedRestoreCoordinator()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new FakePowerManagementService();
        var sessionFactory = new TestFanExecutionSessionFactory();
        var coordinator = new GamingOptimisedRestoreCoordinator(power, sessionFactory);
        var service = CreateService(hardware, power, coordinator);

        var result = await service.RestoreAsync(CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal(1, hardware.DetectCallCount);
        Assert.Equal(1, sessionFactory.OpenCallCount);
        Assert.Equal(1, power.RestoreOriginalSettingsCallCount);
        Assert.NotNull(result.FanRecovery);
    }

    [Fact]
    public async Task RestoreAsync_ExactVerifiedMacBookPro16_1_WithoutCoordinator_FailsClosedAndDoesNotTouchPower()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new FakePowerManagementService();
        var service = CreateService(hardware, power, coordinator: null);

        var result = await service.RestoreAsync(CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Contains("Transactional fan restore coordinator is required", result.FailureMessage);
        Assert.Equal(0, power.RestoreOriginalSettingsCallCount);
    }

    [Fact]
    public async Task RestoreAsync_ExactVerifiedMacBookPro16_1_AppleSmcServiceStopped_FailsClosedWithClearMessage()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new FakePowerManagementService();
        var sessionFactory = new TestFanExecutionSessionFactory
        {
            OpenSessionHandler = () => throw new AppleSmcServiceStateException(AppleSmcServiceState.Stopped)
        };
        var coordinator = new GamingOptimisedRestoreCoordinator(power, sessionFactory);
        var service = CreateService(hardware, power, coordinator);

        var result = await service.RestoreAsync(CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Contains("AppleSMC service is not running", result.FailureMessage);
        Assert.Equal(0, power.RestoreOriginalSettingsCallCount);
    }

    [Fact]
    public async Task RestoreAsync_OtherSupportedModel_RoutesToPowerManagementRestoreOriginalSettings()
    {
        var verification = new ModelVerificationResult(
            "Apple Inc.",
            VerifiedHardwareModels.MacBookPro14_3,
            PlatformSupportStatus.SupportedIntelMac,
            ModelValidationLevel.NotIndividuallyTested,
            "Supported 14,3.");
        var hardware = new FakeHardwareDetectionService(verification);
        var power = new FakePowerManagementService();
        var sessionFactory = new TestFanExecutionSessionFactory();
        var coordinator = new GamingOptimisedRestoreCoordinator(power, sessionFactory);
        var service = CreateService(hardware, power, coordinator);

        var result = await service.RestoreAsync(CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal(1, hardware.DetectCallCount);
        Assert.Equal(0, sessionFactory.OpenCallCount);
        Assert.Equal(1, power.RestoreOriginalSettingsCallCount);
        Assert.Null(result.FanRecovery);
    }

    [Fact]
    public async Task RestoreAsync_ExactVerifiedMacBookPro16_1_UsesFreshHardwareDetection()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new FakePowerManagementService();
        var sessionFactory = new TestFanExecutionSessionFactory();
        var coordinator = new GamingOptimisedRestoreCoordinator(power, sessionFactory);
        var service = CreateService(hardware, power, coordinator);

        Assert.Equal(0, hardware.DetectCallCount);

        await service.RestoreAsync(CancellationToken.None);

        Assert.Equal(1, hardware.DetectCallCount);
        Assert.Equal(1, hardware.VerifyModelCallCount);
    }

    [Fact]
    public async Task RestoreAsync_ExactVerifiedMacBookPro16_1_CoordinationCleanupException_Propagates()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new FakePowerManagementService();
        var cleanupException = new FanExecutionSessionCleanupException(
            "Disposal failed.",
            new Exception("Operation error"),
            new Exception("Cleanup error"));
        var sessionFactory = new TestFanExecutionSessionFactory
        {
            OpenSessionHandler = () => Task.FromResult<IFanExecutionSession>(new TestFanExecutionSession
            {
                DisposeHandler = () => throw cleanupException
            })
        };
        var coordinator = new GamingOptimisedRestoreCoordinator(power, sessionFactory);
        var service = CreateService(hardware, power, coordinator);

        var thrown = await Assert.ThrowsAsync<FanExecutionSessionCleanupException>(
            () => service.RestoreAsync(CancellationToken.None));

        Assert.Same(cleanupException, thrown);
    }

    [Fact]
    public async Task RestoreAsync_ExactVerifiedMacBookPro16_1_MarkerWithoutPowerSnapshot_PerformsFanOnlyRecovery_ReturnsSuccessfulFanOnly()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new FakePowerManagementService();
        var sessionFactory = new TestFanExecutionSessionFactory();
        var coordinator = new GamingOptimisedRestoreCoordinator(power, sessionFactory);
        var snapshotStore = new FakeRestoreSnapshotStore { HasOriginalRestoreSnapshot = false };
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            Marker = new FanOverrideOwnershipMarker(
                VerifiedHardwareModels.MacBookPro16_1,
                5321.25f,
                4789.5f,
                DateTimeOffset.UtcNow)
        };
        var service = new ProfileRestoreService(
            hardware,
            power,
            coordinator,
            snapshotStore,
            ownershipStore);

        var result = await service.RestoreAsync(CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Null(result.PowerOperation);
        Assert.NotNull(result.FanRecovery);
        Assert.Contains("No original processor snapshot required restoration", result.SuccessMessage);
        Assert.Equal(1, sessionFactory.OpenCallCount);
        Assert.Equal(0, power.RestoreOriginalSettingsCallCount);
    }

    [Fact]
    public async Task RestoreAsync_ExactVerifiedMacBookPro16_1_MarkerWithPowerSnapshot_PerformsFansThenPower_ReturnsSuccessful()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new FakePowerManagementService();
        var sessionFactory = new TestFanExecutionSessionFactory();
        var coordinator = new GamingOptimisedRestoreCoordinator(power, sessionFactory);
        var snapshotStore = new FakeRestoreSnapshotStore { HasOriginalRestoreSnapshot = true };
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            Marker = new FanOverrideOwnershipMarker(
                VerifiedHardwareModels.MacBookPro16_1,
                5321.25f,
                4789.5f,
                DateTimeOffset.UtcNow)
        };
        var service = new ProfileRestoreService(
            hardware,
            power,
            coordinator,
            snapshotStore,
            ownershipStore);

        var result = await service.RestoreAsync(CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.NotNull(result.PowerOperation);
        Assert.NotNull(result.FanRecovery);
        Assert.Equal(1, sessionFactory.OpenCallCount);
        Assert.Equal(1, power.RestoreOriginalSettingsCallCount);
    }

    [Fact]
    public async Task RestoreAsync_MismatchedMarkerModel_FailsClosedAndDoesNotTouchPowerOrSMC()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new FakePowerManagementService();
        var sessionFactory = new TestFanExecutionSessionFactory();
        var coordinator = new GamingOptimisedRestoreCoordinator(power, sessionFactory);
        var snapshotStore = new FakeRestoreSnapshotStore { HasOriginalRestoreSnapshot = true };
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            Marker = new FanOverrideOwnershipMarker(
                "MacBookPro15,1", // Different model
                5000f,
                4500f,
                DateTimeOffset.UtcNow)
        };
        var service = new ProfileRestoreService(
            hardware,
            power,
            coordinator,
            snapshotStore,
            ownershipStore);

        var result = await service.RestoreAsync(CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Contains("current hardware state does not match the ownership marker", result.FailureMessage);
        Assert.Equal(0, sessionFactory.OpenCallCount);
        Assert.Equal(0, power.RestoreOriginalSettingsCallCount);
        Assert.NotNull(ownershipStore.Marker); // Marker is retained intact
    }

    [Fact]
    public async Task RestoreAsync_MarkerReadException_FailsClosedAndDoesNotTouchPower()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new FakePowerManagementService();
        var sessionFactory = new TestFanExecutionSessionFactory();
        var coordinator = new GamingOptimisedRestoreCoordinator(power, sessionFactory);
        var snapshotStore = new FakeRestoreSnapshotStore { HasOriginalRestoreSnapshot = true };
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            LoadException = new IOException("Disk error")
        };
        var service = new ProfileRestoreService(
            hardware,
            power,
            coordinator,
            snapshotStore,
            ownershipStore);

        var result = await service.RestoreAsync(CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Contains("Could not verify fan override ownership state", result.FailureMessage);
        Assert.Equal(0, sessionFactory.OpenCallCount);
        Assert.Equal(0, power.RestoreOriginalSettingsCallCount);
    }

    private static ProfileRestoreService CreateService(
        IHardwareDetectionService hardware,
        IPowerManagementService power,
        GamingOptimisedRestoreCoordinator? coordinator = null,
        IRestoreSnapshotStore? snapshotStore = null,
        IFanOverrideOwnershipReader? ownershipReader = null,
        IApplicationLogger? logger = null)
    {
        return new ProfileRestoreService(
            hardware,
            power,
            coordinator,
            snapshotStore ?? new FakeRestoreSnapshotStore(),
            ownershipReader ?? new TestFanOverrideOwnershipStore(),
            logger ?? NullApplicationLogger.Instance);
    }

    private static ModelVerificationResult VerifiedMacBookPro16_1()
    {
        return new ModelVerificationResult(
            "Apple Inc.",
            VerifiedHardwareModels.MacBookPro16_1,
            PlatformSupportStatus.SupportedIntelMac,
            ModelValidationLevel.PerformanceValidated,
            "Verified.");
    }

    private sealed class FakeHardwareDetectionService : IHardwareDetectionService
    {
        private readonly ModelVerificationResult _verificationResult;

        public FakeHardwareDetectionService(ModelVerificationResult verificationResult)
        {
            _verificationResult = verificationResult;
        }

        public int DetectCallCount { get; private set; }
        public int VerifyModelCallCount { get; private set; }

        public Task<HardwareSnapshot> DetectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DetectCallCount++;
            return Task.FromResult(new HardwareSnapshot(
                new ComputerSystemInfo(_verificationResult.Manufacturer, _verificationResult.Model, "x64-based PC"),
                new ProcessorInfo("Intel Core", "GenuineIntel", 8, 16, 2400),
                [],
                null,
                DateTimeOffset.UtcNow));
        }

        public ModelVerificationResult VerifyModel(HardwareSnapshot snapshot)
        {
            VerifyModelCallCount++;
            return _verificationResult;
        }
    }

    private sealed class FakePowerManagementService : IPowerManagementService
    {
        public int RestoreOriginalSettingsCallCount { get; private set; }

        public Task<PowerStateSnapshot> ReadCurrentStateAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new PowerStateSnapshot(
                Guid.NewGuid(), 100, 100, 2, 2, DateTimeOffset.UtcNow));
        }

        public Task<PowerOperationResult> ApplyProcessorSettingsAsync(
            ProcessorPowerSettings requestedSettings,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PowerOperationResult> ApplyProcessorSettingsAsync(
            ProcessorPowerSettings requestedSettings,
            PowerStateSnapshot expectedStateBefore,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PowerOperationResult> RestoreOriginalSettingsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RestoreOriginalSettingsCallCount++;
            var snapshot = new PowerStateSnapshot(Guid.NewGuid(), 100, 100, 2, 2, DateTimeOffset.UtcNow);
            var settings = new ProcessorPowerSettings(100, 100, 2, 2);
            return Task.FromResult(new PowerOperationResult(
                PowerOperationKind.RestoreOriginalSnapshot,
                IsSuccessful: true,
                snapshot.SchemeId,
                snapshot,
                settings,
                snapshot,
                PowerStateVerification.Compare(snapshot.SchemeId, settings, snapshot),
                Rollback: null,
                FailureMessage: null));
        }
    }
}
