using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc.CrystalIdea;
using BootCampPerformanceControl.FanControl.Smc.Windows;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.Profiles;
using BootCampPerformanceControl.Tests.TestDoubles;

namespace BootCampPerformanceControl.Tests.Profiles;

public sealed class CleanExitFanRecoveryServiceTests
{
    [Fact]
    public async Task RestoreOwnedFansAsync_NoOwnershipMarker_SkipsHardwareAndPowerIo()
    {
        var ownershipStore = new TestFanOverrideOwnershipStore();
        var hardware = new StubHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new UnexpectedPowerManagementService();
        var sessionFactory = new TestFanExecutionSessionFactory(ownershipStore);
        var service = CreateService(
            hardware,
            ownershipStore,
            power,
            sessionFactory);

        await service.RestoreOwnedFansAsync(CancellationToken.None);

        Assert.Equal(1, ownershipStore.LoadCallCount);
        Assert.Equal(0, hardware.DetectCallCount);
        Assert.Equal(0, sessionFactory.OpenCallCount);
        Assert.Equal(0, power.TotalCallCount);
    }

    [Fact]
    public async Task RestoreOwnedFansAsync_OwnedOverride_RestoresFansAndNeverTouchesPower()
    {
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            Marker = OwnedMarker()
        };
        var hardware = new StubHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new UnexpectedPowerManagementService();
        var sessionFactory = new TestFanExecutionSessionFactory(ownershipStore);
        var logger = new TestApplicationLogger();
        var service = CreateService(
            hardware,
            ownershipStore,
            power,
            sessionFactory,
            logger);

        await service.RestoreOwnedFansAsync(CancellationToken.None);

        Assert.Null(ownershipStore.Marker);
        Assert.Equal(2, ownershipStore.LoadCallCount);
        Assert.Equal(1, ownershipStore.ClearCallCount);
        Assert.Equal(1, hardware.DetectCallCount);
        Assert.Equal(1, sessionFactory.OpenCallCount);
        Assert.Equal(0, power.TotalCallCount);
        Assert.Contains(
            logger.InformationMessages,
            message => message.Contains(
                "Processor power settings were left unchanged",
                StringComparison.Ordinal));
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public async Task RestoreOwnedFansAsync_MarkerModelMismatch_BlocksAndRetainsMarker()
    {
        var marker = OwnedMarker(VerifiedHardwareModels.MacBookPro14_3);
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            Marker = marker
        };
        var hardware = new StubHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new UnexpectedPowerManagementService();
        var sessionFactory = new TestFanExecutionSessionFactory(ownershipStore);
        var logger = new TestApplicationLogger();
        var service = CreateService(
            hardware,
            ownershipStore,
            power,
            sessionFactory,
            logger);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RestoreOwnedFansAsync(CancellationToken.None));

        Assert.Contains("does not match", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Same(marker, ownershipStore.Marker);
        Assert.Equal(1, hardware.DetectCallCount);
        Assert.Equal(0, sessionFactory.OpenCallCount);
        Assert.Equal(0, power.TotalCallCount);
        Assert.Single(logger.Errors);
    }

    [Fact]
    public async Task RestoreOwnedFansAsync_AppleSmcStopped_RetainsMarkerAndNeverTouchesPower()
    {
        var marker = OwnedMarker();
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            Marker = marker
        };
        var hardware = new StubHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new UnexpectedPowerManagementService();
        var sessionFactory = new TestFanExecutionSessionFactory(ownershipStore)
        {
            OpenSessionHandler = () => throw new AppleSmcServiceStateException(
                AppleSmcServiceState.Stopped)
        };
        var logger = new TestApplicationLogger();
        var service = CreateService(
            hardware,
            ownershipStore,
            power,
            sessionFactory,
            logger);

        await Assert.ThrowsAsync<AppleSmcServiceStateException>(
            () => service.RestoreOwnedFansAsync(CancellationToken.None));

        Assert.Same(marker, ownershipStore.Marker);
        Assert.Equal(1, sessionFactory.OpenCallCount);
        Assert.Equal(0, power.TotalCallCount);
        Assert.Single(logger.Errors);
    }

    [Fact]
    public async Task RestoreOwnedFansAsync_RecoveryReportsSuccessButMarkerRemains_FailsClosed()
    {
        var marker = OwnedMarker();
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            Marker = marker
        };
        var hardware = new StubHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new UnexpectedPowerManagementService();
        var sessionFactory = new TestFanExecutionSessionFactory(ownershipStore)
        {
            OpenSessionHandler = () => Task.FromResult<IFanExecutionSession>(
                new TestFanExecutionSession(
                    overrideCoordinator: new TestFanOverrideCoordinator
                    {
                        RecoverHandler = (_, _, _) => Task.FromResult(
                            new FanOverrideRecoveryDecision(
                                FanOverrideRecoveryAction.RestoreAppleAuto,
                                "Apple Auto restored without clearing the marker in this negative test."))
                    }))
        };
        var logger = new TestApplicationLogger();
        var service = CreateService(
            hardware,
            ownershipStore,
            power,
            sessionFactory,
            logger);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RestoreOwnedFansAsync(CancellationToken.None));

        Assert.Contains("marker is still present", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Same(marker, ownershipStore.Marker);
        Assert.Equal(2, ownershipStore.LoadCallCount);
        Assert.Equal(0, power.TotalCallCount);
        Assert.Single(logger.Errors);
    }

    private static CleanExitFanRecoveryService CreateService(
        IHardwareDetectionService hardwareDetectionService,
        IFanOverrideOwnershipReader ownershipReader,
        IPowerManagementService powerManagementService,
        IFanExecutionSessionFactory fanExecutionSessionFactory,
        TestApplicationLogger? logger = null)
    {
        var gamingRestoreCoordinator = new GamingOptimisedRestoreCoordinator(
            powerManagementService,
            fanExecutionSessionFactory);

        return new CleanExitFanRecoveryService(
            hardwareDetectionService,
            ownershipReader,
            gamingRestoreCoordinator,
            logger ?? new TestApplicationLogger());
    }

    private static FanOverrideOwnershipMarker OwnedMarker(
        string model = VerifiedHardwareModels.MacBookPro16_1)
    {
        return new FanOverrideOwnershipMarker(
            model,
            fan0ExpectedTargetRpm: 5321.25f,
            fan1ExpectedTargetRpm: 4789.5f,
            createdAtUtc: DateTimeOffset.Parse("2026-09-05T09:37:15+00:00"));
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

    private sealed class StubHardwareDetectionService : IHardwareDetectionService
    {
        private readonly ModelVerificationResult _verificationResult;

        public StubHardwareDetectionService(ModelVerificationResult verificationResult)
        {
            _verificationResult = verificationResult;
        }

        public int DetectCallCount { get; private set; }

        public Task<HardwareSnapshot> DetectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DetectCallCount++;

            return Task.FromResult(new HardwareSnapshot(
                new ComputerSystemInfo(
                    _verificationResult.Manufacturer,
                    _verificationResult.Model,
                    "x64-based PC"),
                Processor: null,
                VideoControllers: [],
                OperatingSystem: null,
                CapturedAt: DateTimeOffset.UtcNow));
        }

        public ModelVerificationResult VerifyModel(HardwareSnapshot snapshot)
        {
            return _verificationResult;
        }
    }

    private sealed class UnexpectedPowerManagementService : IPowerManagementService
    {
        public int TotalCallCount { get; private set; }

        public Task<PowerStateSnapshot> ReadCurrentStateAsync(CancellationToken cancellationToken)
        {
            return Unexpected<PowerStateSnapshot>();
        }

        public Task<PowerOperationResult> ApplyProcessorSettingsAsync(
            ProcessorPowerSettings requestedSettings,
            CancellationToken cancellationToken)
        {
            return Unexpected<PowerOperationResult>();
        }

        public Task<PowerOperationResult> ApplyProcessorSettingsAsync(
            ProcessorPowerSettings requestedSettings,
            PowerStateSnapshot expectedStateBefore,
            CancellationToken cancellationToken)
        {
            return Unexpected<PowerOperationResult>();
        }

        public Task<PowerOperationResult> RestoreOriginalSettingsAsync(
            CancellationToken cancellationToken)
        {
            return Unexpected<PowerOperationResult>();
        }

        private Task<T> Unexpected<T>()
        {
            TotalCallCount++;
            throw new InvalidOperationException(
                "Clean exit fan recovery must not call power management.");
        }
    }
}
