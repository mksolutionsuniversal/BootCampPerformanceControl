using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.Profiles;

namespace BootCampPerformanceControl.Tests.Profiles;

public sealed class ProfileApplyServiceTests
{
    [Fact]
    public async Task ApplyProfileAsync_VerifiedMacBookPro16_1GamingOptimised_UsesGuardedApply()
    {
        var verification = VerifiedMacBookPro16_1();
        var hardware = new FakeHardwareDetectionService(verification);
        var expectedStateBefore = CurrentPowerState();
        var requestedSettings = new ProcessorPowerSettings(95, 95, 0, 0);
        var powerOperation = SuccessfulPowerOperation(expectedStateBefore, requestedSettings);
        var powerManagement = new FakePowerManagementService(expectedStateBefore, powerOperation);
        var service = CreateService(hardware, powerManagement);

        var result = await service.ApplyProfileAsync("gaming-optimised", CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Same(powerOperation, result.PowerOperation);
        Assert.True(result.ProfileExecutionResolution?.IsExecutable);
        Assert.Equal(requestedSettings, result.ProfileExecutionResolution?.Settings);
        Assert.Equal(1, hardware.DetectCallCount);
        Assert.Equal(1, hardware.VerifyModelCallCount);
        Assert.Same(hardware.DetectedSnapshot, hardware.VerifiedSnapshot);
        Assert.Equal(1, powerManagement.ReadCurrentStateCallCount);
        Assert.Equal(1, powerManagement.GuardedApplyCallCount);
        Assert.Equal(0, powerManagement.UnguardedApplyCallCount);
        Assert.Equal(requestedSettings, powerManagement.LastGuardedSettings);
        Assert.Same(expectedStateBefore, powerManagement.LastExpectedStateBefore);
    }

    [Fact]
    public async Task ApplyProfileAsync_MatchingModelStringButUnverifiedHardware_FailsClosedBeforePowerRead()
    {
        var verification = new ModelVerificationResult(
            "Apple Inc.",
            VerifiedHardwareModels.MacBookPro16_1,
            IsApple: true,
            IsVerified: false,
            HardwareVerificationStatus.UnverifiedAppleModel,
            "Matching model string without verification.");
        var powerManagement = new FakePowerManagementService();
        var service = CreateService(new FakeHardwareDetectionService(verification), powerManagement);

        var result = await service.ApplyProfileAsync("gaming-optimised", CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.NotNull(result.ProfileExecutionResolution);
        Assert.Null(result.PowerOperation);
        Assert.Equal(0, powerManagement.ReadCurrentStateCallCount);
        Assert.Equal(0, powerManagement.GuardedApplyCallCount);
        Assert.Equal(0, powerManagement.UnguardedApplyCallCount);
    }

    [Fact]
    public async Task ApplyProfileAsync_DifferentMacModel_FailsClosedWithoutApply()
    {
        var verification = new ModelVerificationResult(
            "Apple Inc.",
            "MacBookPro15,1",
            IsApple: true,
            IsVerified: false,
            HardwareVerificationStatus.UnverifiedAppleModel,
            "Different Apple model.");
        var powerManagement = new FakePowerManagementService();
        var service = CreateService(new FakeHardwareDetectionService(verification), powerManagement);

        var result = await service.ApplyProfileAsync("gaming-optimised", CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Null(result.PowerOperation);
        Assert.Equal(0, powerManagement.ReadCurrentStateCallCount);
        Assert.Equal(0, powerManagement.GuardedApplyCallCount);
        Assert.Equal(0, powerManagement.UnguardedApplyCallCount);
    }

    [Fact]
    public async Task ApplyProfileAsync_NonAppleHardware_FailsClosedWithoutApply()
    {
        var verification = new ModelVerificationResult(
            "PC Manufacturer",
            "PC Model",
            IsApple: false,
            IsVerified: false,
            HardwareVerificationStatus.NonAppleHardware,
            "Not Apple hardware.");
        var powerManagement = new FakePowerManagementService();
        var service = CreateService(new FakeHardwareDetectionService(verification), powerManagement);

        var result = await service.ApplyProfileAsync("gaming-optimised", CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Null(result.PowerOperation);
        Assert.Equal(0, powerManagement.ReadCurrentStateCallCount);
        Assert.Equal(0, powerManagement.GuardedApplyCallCount);
        Assert.Equal(0, powerManagement.UnguardedApplyCallCount);
    }

    [Fact]
    public async Task ApplyProfileAsync_Balanced_IsRejectedBeforePowerReadOrWrite()
    {
        var powerManagement = new FakePowerManagementService();
        var service = CreateService(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagement);

        var result = await service.ApplyProfileAsync("balanced", CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Null(result.PowerOperation);
        Assert.NotNull(result.ProfileExecutionResolution);
        Assert.Contains(
            "configurable placeholder",
            result.FailureReason,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, powerManagement.ReadCurrentStateCallCount);
        Assert.Equal(0, powerManagement.GuardedApplyCallCount);
        Assert.Equal(0, powerManagement.UnguardedApplyCallCount);
    }

    [Fact]
    public async Task ApplyProfileAsync_FullPerformance_IsRejectedBeforePowerReadOrWriteAndDoesNotInventBoost()
    {
        var powerManagement = new FakePowerManagementService();
        var service = CreateService(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagement);

        var result = await service.ApplyProfileAsync("full-performance", CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Null(result.ProfileExecutionResolution?.Settings);
        Assert.Null(result.PowerOperation);
        Assert.Contains("restore snapshot", result.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, powerManagement.ReadCurrentStateCallCount);
        Assert.Equal(0, powerManagement.GuardedApplyCallCount);
        Assert.Equal(0, powerManagement.UnguardedApplyCallCount);
    }

    [Fact]
    public async Task ApplyProfileAsync_RestoreProfileId_IsRejectedThroughApplyPathWithoutWrite()
    {
        var powerManagement = new FakePowerManagementService();
        var service = CreateService(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagement);

        var result = await service.ApplyProfileAsync("restore", CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Null(result.PowerOperation);
        Assert.Contains("Restore is not resolved", result.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, powerManagement.ReadCurrentStateCallCount);
        Assert.Equal(0, powerManagement.GuardedApplyCallCount);
        Assert.Equal(0, powerManagement.UnguardedApplyCallCount);
    }

    [Fact]
    public async Task ApplyProfileAsync_UnknownProfileId_FailsClosedBeforePowerReadOrWrite()
    {
        var powerManagement = new FakePowerManagementService();
        var service = CreateService(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagement);

        var result = await service.ApplyProfileAsync("unknown-profile", CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Null(result.ProfileExecutionResolution);
        Assert.Null(result.PowerOperation);
        Assert.Contains("not found", result.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, powerManagement.ReadCurrentStateCallCount);
        Assert.Equal(0, powerManagement.GuardedApplyCallCount);
        Assert.Equal(0, powerManagement.UnguardedApplyCallCount);
    }

    [Fact]
    public async Task ApplyProfileAsync_BackendApplyFailure_PreservesPowerOperationFailure()
    {
        var verification = VerifiedMacBookPro16_1();
        var expectedStateBefore = CurrentPowerState();
        var requestedSettings = new ProcessorPowerSettings(95, 95, 0, 0);
        var powerOperation = FailedPowerOperation(
            expectedStateBefore,
            requestedSettings,
            "Backend verification failed.");
        var powerManagement = new FakePowerManagementService(expectedStateBefore, powerOperation);
        var service = CreateService(new FakeHardwareDetectionService(verification), powerManagement);

        var result = await service.ApplyProfileAsync("gaming-optimised", CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal("Backend verification failed.", result.FailureReason);
        Assert.Same(powerOperation, result.PowerOperation);
        Assert.Equal(1, powerManagement.GuardedApplyCallCount);
        Assert.Equal(0, powerManagement.UnguardedApplyCallCount);
    }

    [Fact]
    public async Task ApplyProfileAsync_GamingOptimised_DoesNotCallUnguardedApplyOverload()
    {
        var expectedStateBefore = CurrentPowerState();
        var requestedSettings = new ProcessorPowerSettings(95, 95, 0, 0);
        var powerManagement = new FakePowerManagementService(
            expectedStateBefore,
            SuccessfulPowerOperation(expectedStateBefore, requestedSettings));
        var service = CreateService(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagement);

        await service.ApplyProfileAsync("gaming-optimised", CancellationToken.None);

        Assert.Equal(1, powerManagement.GuardedApplyCallCount);
        Assert.Equal(0, powerManagement.UnguardedApplyCallCount);
    }

    private static ProfileApplyService CreateService(
        FakeHardwareDetectionService hardwareDetectionService,
        FakePowerManagementService powerManagementService)
    {
        return new ProfileApplyService(
            hardwareDetectionService,
            new ProfileCatalog(),
            new ProfileExecutionResolver(),
            powerManagementService);
    }

    private static ModelVerificationResult VerifiedMacBookPro16_1()
    {
        return new ModelVerificationResult(
            "Apple Inc.",
            VerifiedHardwareModels.MacBookPro16_1,
            IsApple: true,
            IsVerified: true,
            HardwareVerificationStatus.Verified,
            "Verified.");
    }

    private static PowerStateSnapshot CurrentPowerState()
    {
        return new PowerStateSnapshot(
            Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"),
            ProcessorMaximumAc: 95,
            ProcessorMaximumDc: 95,
            BoostModeAc: 2,
            BoostModeDc: 2,
            DateTimeOffset.Parse("2026-01-01T00:00:00+00:00"));
    }

    private static PowerOperationResult SuccessfulPowerOperation(
        PowerStateSnapshot stateBefore,
        ProcessorPowerSettings requestedSettings)
    {
        var stateAfter = stateBefore with
        {
            ProcessorMaximumAc = requestedSettings.ProcessorMaximumAc,
            ProcessorMaximumDc = requestedSettings.ProcessorMaximumDc,
            BoostModeAc = requestedSettings.BoostModeAc,
            BoostModeDc = requestedSettings.BoostModeDc
        };

        return new PowerOperationResult(
            PowerOperationKind.ApplyProcessorSettings,
            IsSuccessful: true,
            stateBefore.SchemeId,
            stateBefore,
            requestedSettings,
            stateAfter,
            PowerStateVerification.Compare(stateBefore.SchemeId, requestedSettings, stateAfter),
            Rollback: null,
            FailureMessage: null);
    }

    private static PowerOperationResult FailedPowerOperation(
        PowerStateSnapshot stateBefore,
        ProcessorPowerSettings requestedSettings,
        string failureMessage)
    {
        return SuccessfulPowerOperation(stateBefore, requestedSettings) with
        {
            IsSuccessful = false,
            FailureMessage = failureMessage
        };
    }

    private sealed class FakeHardwareDetectionService : IHardwareDetectionService
    {
        private readonly ModelVerificationResult _verificationResult;

        public FakeHardwareDetectionService(ModelVerificationResult verificationResult)
        {
            _verificationResult = verificationResult;
            DetectedSnapshot = new HardwareSnapshot(
                new ComputerSystemInfo(
                    verificationResult.Manufacturer,
                    verificationResult.Model,
                    "x64-based PC"),
                Processor: null,
                VideoControllers: [],
                OperatingSystem: null,
                DateTimeOffset.Parse("2026-01-01T00:00:00+00:00"));
        }

        public int DetectCallCount { get; private set; }

        public int VerifyModelCallCount { get; private set; }

        public HardwareSnapshot DetectedSnapshot { get; }

        public HardwareSnapshot? VerifiedSnapshot { get; private set; }

        public Task<HardwareSnapshot> DetectAsync(CancellationToken cancellationToken)
        {
            DetectCallCount++;
            return Task.FromResult(DetectedSnapshot);
        }

        public ModelVerificationResult VerifyModel(HardwareSnapshot snapshot)
        {
            VerifyModelCallCount++;
            VerifiedSnapshot = snapshot;
            return _verificationResult;
        }
    }

    private sealed class FakePowerManagementService : IPowerManagementService
    {
        private readonly PowerStateSnapshot _currentState;
        private readonly PowerOperationResult _applyResult;

        public FakePowerManagementService()
            : this(CurrentPowerState(), FailedPowerOperation(
                CurrentPowerState(),
                new ProcessorPowerSettings(95, 95, 0, 0),
                "Apply should not have been called."))
        {
        }

        public FakePowerManagementService(
            PowerStateSnapshot currentState,
            PowerOperationResult applyResult)
        {
            _currentState = currentState;
            _applyResult = applyResult;
        }

        public int ReadCurrentStateCallCount { get; private set; }

        public int GuardedApplyCallCount { get; private set; }

        public int UnguardedApplyCallCount { get; private set; }

        public ProcessorPowerSettings? LastGuardedSettings { get; private set; }

        public PowerStateSnapshot? LastExpectedStateBefore { get; private set; }

        public Task<PowerStateSnapshot> ReadCurrentStateAsync(CancellationToken cancellationToken)
        {
            ReadCurrentStateCallCount++;
            return Task.FromResult(_currentState);
        }

        public Task<PowerOperationResult> ApplyProcessorSettingsAsync(
            ProcessorPowerSettings requestedSettings,
            CancellationToken cancellationToken)
        {
            UnguardedApplyCallCount++;
            return Task.FromResult(_applyResult);
        }

        public Task<PowerOperationResult> ApplyProcessorSettingsAsync(
            ProcessorPowerSettings requestedSettings,
            PowerStateSnapshot expectedStateBefore,
            CancellationToken cancellationToken)
        {
            GuardedApplyCallCount++;
            LastGuardedSettings = requestedSettings;
            LastExpectedStateBefore = expectedStateBefore;
            return Task.FromResult(_applyResult);
        }

        public Task<PowerOperationResult> RestoreOriginalSettingsAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Restore should not be called by ProfileApplyService.");
        }
    }
}
