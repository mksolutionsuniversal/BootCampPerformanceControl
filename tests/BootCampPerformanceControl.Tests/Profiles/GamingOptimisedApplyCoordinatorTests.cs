using System.Buffers.Binary;
using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc;
using BootCampPerformanceControl.FanControl.Smc.CrystalIdea;
using BootCampPerformanceControl.FanControl.Smc.Windows;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.Profiles;

namespace BootCampPerformanceControl.Tests.Profiles;

public sealed class GamingOptimisedApplyCoordinatorTests
{
    private const string Model = VerifiedHardwareModels.MacBookPro16_1;
    private static readonly PowerStateSnapshot ExpectedPowerState = CurrentPowerState();

    [Fact]
    public async Task ApplyAsync_InvalidProcessorProfile_FailsBeforeFanProbeOrPowerWrite()
    {
        var events = new List<string>();
        var power = new RecordingPowerManagementService(events);
        var fanProbe = new RecordingFanCapabilityProbe(events);
        var fanCoordinator = new RecordingFanOverrideCoordinator(events);
        var sessionFactory = new RecordingFanExecutionSessionFactory(
            fanProbe,
            fanCoordinator,
            events);
        var coordinator = CreateCoordinator(power, sessionFactory);

        var result = await coordinator.ApplyAsync(
            GamingOptimisedProfile(
                new ProcessorPowerProfileTarget(100, 100, 2, 2, ProfileUnspecifiedValueSource.None)),
            PerformanceValidatedMacBookPro16_1(),
            CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Null(result.FanResolution);
        Assert.Null(result.FanExecution);
        Assert.Null(result.PowerOperation);
        Assert.Equal(0, power.ReadCurrentStateCallCount);
        Assert.Equal(0, power.GuardedApplyCallCount);
        Assert.Equal(0, fanProbe.ProbeCallCount);
        Assert.Equal(0, fanCoordinator.ApplyCallCount);
        Assert.Equal(0, sessionFactory.OpenCallCount);
        Assert.Empty(events);
    }

    [Fact]
    public async Task ApplyAsync_PowerStateReadFailure_DoesNotProbeOrWriteFans()
    {
        var events = new List<string>();
        var power = new RecordingPowerManagementService(events)
        {
            ReadException = new InvalidOperationException("read failed")
        };
        var fanProbe = new RecordingFanCapabilityProbe(events);
        var fanCoordinator = new RecordingFanOverrideCoordinator(events);
        var sessionFactory = new RecordingFanExecutionSessionFactory(
            fanProbe,
            fanCoordinator,
            events);
        var coordinator = CreateCoordinator(power, sessionFactory);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ApplyAsync(
                GamingOptimisedProfile(),
                PerformanceValidatedMacBookPro16_1(),
                CancellationToken.None));

        Assert.Equal(1, power.ReadCurrentStateCallCount);
        Assert.Equal(0, power.GuardedApplyCallCount);
        Assert.Equal(0, fanProbe.ProbeCallCount);
        Assert.Equal(0, fanCoordinator.ApplyCallCount);
        Assert.Equal(0, sessionFactory.OpenCallCount);
        Assert.Equal(["power-read"], events);
    }

    [Fact]
    public async Task ApplyAsync_FanSessionOpenFailure_DoesNotProbeFansOrApplyCpu()
    {
        var events = new List<string>();
        var openException = new InvalidOperationException("session open failed");
        var power = new RecordingPowerManagementService(events);
        var fanProbe = new RecordingFanCapabilityProbe(events);
        var fanCoordinator = new RecordingFanOverrideCoordinator(events);
        var sessionFactory = new RecordingFanExecutionSessionFactory(
            fanProbe,
            fanCoordinator,
            events)
        {
            OpenException = openException
        };
        var coordinator = CreateCoordinator(power, sessionFactory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ApplyAsync(
                GamingOptimisedProfile(),
                PerformanceValidatedMacBookPro16_1(),
                CancellationToken.None));

        Assert.Same(openException, exception);
        Assert.Equal(1, sessionFactory.OpenCallCount);
        Assert.Equal(0, fanProbe.ProbeCallCount);
        Assert.Equal(0, fanCoordinator.ApplyCallCount);
        Assert.Equal(0, power.GuardedApplyCallCount);
        Assert.Equal(["power-read", "fan-session-open"], events);
    }

    [Fact]
    public async Task ApplyAsync_AppleSmcStopped_AppliesCpuOnlyWithoutFanWrites()
    {
        var events = new List<string>();
        var power = new RecordingPowerManagementService(events);
        var fanProbe = new RecordingFanCapabilityProbe(events);
        var fanCoordinator = new RecordingFanOverrideCoordinator(events);
        var sessionFactory = new RecordingFanExecutionSessionFactory(
            fanProbe,
            fanCoordinator,
            events)
        {
            OpenException = new AppleSmcServiceStateException(AppleSmcServiceState.Stopped)
        };
        var coordinator = CreateCoordinator(power, sessionFactory);

        var result = await coordinator.ApplyAsync(
            GamingOptimisedProfile(),
            PerformanceValidatedMacBookPro16_1(),
            CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Null(result.FanExecution);
        Assert.Equal(new ProcessorPowerSettings(95, 95, 0, 0), power.LastRequestedSettings);
        Assert.Equal(1, power.GuardedApplyCallCount);
        Assert.Equal(0, fanProbe.ProbeCallCount);
        Assert.Equal(0, fanCoordinator.ApplyCallCount);
        Assert.Equal(["power-read", "fan-session-open", "power-apply"], events);
    }

    [Fact]
    public async Task ApplyAsync_FanCapabilityBlocked_AppliesCpuOnly()
    {
        var events = new List<string>();
        var power = new RecordingPowerManagementService(events);
        var fanProbe = new RecordingFanCapabilityProbe(
            events,
            Capability(
                isReadSupported: true,
                isHardwareSafetyGateSatisfied: false,
                snapshot: ValidFanSnapshot()));
        var fanCoordinator = new RecordingFanOverrideCoordinator(events);
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        var result = await coordinator.ApplyAsync(
            GamingOptimisedProfile(),
            PerformanceValidatedMacBookPro16_1(),
            CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.False(result.FanResolution?.IsExecutable);
        Assert.Null(result.FanExecution);
        Assert.NotNull(result.PowerOperation);
        Assert.Equal(1, power.ReadCurrentStateCallCount);
        Assert.Equal(1, fanProbe.ProbeCallCount);
        Assert.Equal(0, fanCoordinator.ApplyCallCount);
        Assert.Equal(1, power.GuardedApplyCallCount);
        Assert.Equal(["power-read", "fan-session-open", "fan-probe", "power-apply", "fan-session-dispose"], events);
    }

    [Fact]
    public async Task ApplyAsync_SuccessfulTransaction_RecordsFansBeforeCpuOrder()
    {
        var events = new List<string>();
        var power = new RecordingPowerManagementService(events);
        var fanProbe = new RecordingFanCapabilityProbe(events);
        var fanCoordinator = new RecordingFanOverrideCoordinator(events);
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        var result = await coordinator.ApplyAsync(
            GamingOptimisedProfile(),
            PerformanceValidatedMacBookPro16_1(),
            CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal(
            ["power-read", "fan-session-open", "fan-probe", "fan-apply", "power-apply", "fan-session-dispose"],
            events);
        Assert.True(events.IndexOf("fan-apply") < events.IndexOf("power-apply"));
    }

    [Fact]
    public async Task ApplyAsync_FanCoordinatorReturnsBlocked_AppliesCpuOnly()
    {
        var events = new List<string>();
        var power = new RecordingPowerManagementService(events);
        var fanProbe = new RecordingFanCapabilityProbe(events);
        var fanCoordinator = new RecordingFanOverrideCoordinator(events)
        {
            ApplyResult = FanOverrideExecutionResult.Blocked("fan blocked")
        };
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        var result = await coordinator.ApplyAsync(
            GamingOptimisedProfile(),
            PerformanceValidatedMacBookPro16_1(),
            CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.NotNull(result.FanExecution);
        Assert.False(result.FanExecution!.IsApplied);
        Assert.NotNull(result.PowerOperation);
        Assert.Equal(1, power.GuardedApplyCallCount);
        Assert.Equal(
            ["power-read", "fan-session-open", "fan-probe", "fan-apply", "power-apply", "fan-session-dispose"],
            events);
    }

    [Fact]
    public async Task ApplyAsync_PassiveFNumZeroTopology_AppliesCpuOnlyWithoutFanWrites()
    {
        var events = new List<string>();
        var power = new RecordingPowerManagementService(events);
        var passiveSnapshot = new FanSmcSnapshot(UInt8("FNum", 0, 0x80), []);
        var fanProbe = new RecordingFanCapabilityProbe(
            events,
            Capability(
                isReadSupported: true,
                isHardwareSafetyGateSatisfied: false,
                snapshot: passiveSnapshot));
        var fanCoordinator = new RecordingFanOverrideCoordinator(events);
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        var result = await coordinator.ApplyAsync(
            GamingOptimisedProfile(),
            PerformanceValidatedMacBookPro16_1(),
            CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal(new ProcessorPowerSettings(95, 95, 0, 0), power.LastRequestedSettings);
        Assert.Equal(0, fanCoordinator.ApplyCallCount);
        Assert.Equal(1, power.GuardedApplyCallCount);
    }

    [Fact]
    public async Task ApplyAsync_ForeignManualMode_DoesNotTakeFanOwnershipAndAppliesCpuOnly()
    {
        var events = new List<string>();
        var power = new RecordingPowerManagementService(events);
        var fanProbe = new RecordingFanCapabilityProbe(
            events,
            ValidFanCapability(fan0Mode: 1));
        var fanCoordinator = new RecordingFanOverrideCoordinator(events);
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        var result = await coordinator.ApplyAsync(
            GamingOptimisedProfile(),
            PerformanceValidatedMacBookPro16_1(),
            CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.False(result.FanResolution?.IsExecutable);
        Assert.Equal(0, fanCoordinator.ApplyCallCount);
        Assert.Equal(new ProcessorPowerSettings(95, 95, 0, 0), power.LastRequestedSettings);
    }

    [Fact]
    public async Task ApplyAsync_FanApplyThrowsAndRecoveryVerifies_AppliesCpuOnly()
    {
        var events = new List<string>();
        var power = new RecordingPowerManagementService(events);
        var fanProbe = new RecordingFanCapabilityProbe(events);
        var originalException = new InvalidOperationException("fan apply failed");
        var fanCoordinator = new RecordingFanOverrideCoordinator(events)
        {
            ApplyException = originalException
        };
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        var result = await coordinator.ApplyAsync(
            GamingOptimisedProfile(),
            PerformanceValidatedMacBookPro16_1(),
            CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal(1, power.GuardedApplyCallCount);
        Assert.Equal(FanOverrideRecoveryAction.RestoreAppleAuto, result.FanCompensation?.Action);
        Assert.Equal(
            [
                "power-read",
                "fan-session-open",
                "fan-probe",
                "fan-apply",
                "fresh-fan-probe",
                "fan-recover",
                "power-apply",
                "fan-session-dispose"
            ],
            events);
    }

    [Fact]
    public async Task ApplyAsync_FanApplyThrowsAndSessionDisposeSucceeds_ReturnsCpuOnlySuccess()
    {
        var originalException = new InvalidOperationException("fan apply failed");
        var power = new RecordingPowerManagementService();
        var fanProbe = new RecordingFanCapabilityProbe();
        var fanCoordinator = new RecordingFanOverrideCoordinator
        {
            ApplyException = originalException
        };
        var sessionFactory = new RecordingFanExecutionSessionFactory(
            fanProbe,
            fanCoordinator);
        var coordinator = CreateCoordinator(power, sessionFactory);

        var result = await coordinator.ApplyAsync(
            GamingOptimisedProfile(),
            PerformanceValidatedMacBookPro16_1(),
            CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal(1, power.GuardedApplyCallCount);
        Assert.Single(sessionFactory.Sessions);
        Assert.Equal(1, sessionFactory.Sessions[0].DisposeCallCount);
    }

    [Fact]
    public async Task ApplyAsync_FanApplyRecoversThenSessionDisposeThrows_SurfacesCleanupFailure()
    {
        var operationException = new InvalidOperationException("fan apply failed");
        var cleanupException = new InvalidOperationException("session dispose failed");
        var power = new RecordingPowerManagementService();
        var fanProbe = new RecordingFanCapabilityProbe();
        var fanCoordinator = new RecordingFanOverrideCoordinator
        {
            ApplyException = operationException
        };
        var sessionFactory = new RecordingFanExecutionSessionFactory(
            fanProbe,
            fanCoordinator)
        {
            DisposeException = cleanupException
        };
        var coordinator = CreateCoordinator(power, sessionFactory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ApplyAsync(
                GamingOptimisedProfile(),
                PerformanceValidatedMacBookPro16_1(),
                CancellationToken.None));

        Assert.Same(cleanupException, exception);
        Assert.Equal(1, power.GuardedApplyCallCount);
    }

    [Fact]
    public async Task ApplyAsync_CompensationExceptionAndSessionDisposeThrows_PreservesCompensationDetails()
    {
        var operationException = new InvalidOperationException("fan apply failed");
        var cleanupException = new InvalidOperationException("session dispose failed");
        var power = new RecordingPowerManagementService();
        var fanProbe = new RecordingFanCapabilityProbe(
            results:
            [
                ValidFanCapability(),
                ValidFanCapability(fan0Mode: 1, fan1Mode: 1)
            ]);
        var recoveryDecision = new FanOverrideRecoveryDecision(
            FanOverrideRecoveryAction.None,
            "No fan override ownership marker exists.");
        var fanCoordinator = new RecordingFanOverrideCoordinator
        {
            ApplyException = operationException,
            RecoveryResult = recoveryDecision
        };
        var sessionFactory = new RecordingFanExecutionSessionFactory(
            fanProbe,
            fanCoordinator)
        {
            DisposeException = cleanupException
        };
        var coordinator = CreateCoordinator(power, sessionFactory);

        var exception = await Assert.ThrowsAsync<FanExecutionSessionCleanupException>(
            () => coordinator.ApplyAsync(
                GamingOptimisedProfile(),
                PerformanceValidatedMacBookPro16_1(),
                CancellationToken.None));

        var compensation = Assert.IsType<GamingOptimisedApplyCompensationException>(
            exception.OperationException);
        Assert.Same(operationException, compensation.OperationException);
        Assert.Equal(recoveryDecision, compensation.RecoveryDecision);
        Assert.Null(compensation.CompensationException);
        Assert.Same(cleanupException, exception.CleanupException);
    }

    [Fact]
    public async Task ApplyAsync_SuccessfulApplyAndSessionDisposeThrows_SurfacesCleanupFailure()
    {
        var cleanupException = new InvalidOperationException("session dispose failed");
        var power = new RecordingPowerManagementService();
        var fanProbe = new RecordingFanCapabilityProbe();
        var fanCoordinator = new RecordingFanOverrideCoordinator();
        var sessionFactory = new RecordingFanExecutionSessionFactory(
            fanProbe,
            fanCoordinator)
        {
            DisposeException = cleanupException
        };
        var coordinator = CreateCoordinator(power, sessionFactory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ApplyAsync(
                GamingOptimisedProfile(),
                PerformanceValidatedMacBookPro16_1(),
                CancellationToken.None));

        Assert.Same(cleanupException, exception);
        Assert.Equal(1, power.GuardedApplyCallCount);
        Assert.Single(sessionFactory.Sessions);
        Assert.Equal(1, sessionFactory.Sessions[0].DisposeCallCount);
    }

    [Fact]
    public async Task ApplyAsync_FanCancellationAttemptsNonCancelableRecoveryAndDoesNotApplyCpu()
    {
        var events = new List<string>();
        var power = new RecordingPowerManagementService(events);
        var fanProbe = new RecordingFanCapabilityProbe(events);
        var fanCoordinator = new RecordingFanOverrideCoordinator(events)
        {
            ApplyException = new OperationCanceledException("fan apply canceled")
        };
        var sessionFactory = new RecordingFanExecutionSessionFactory(
            fanProbe,
            fanCoordinator,
            events);
        var coordinator = CreateCoordinator(power, sessionFactory);
        using var cancellationSource = new CancellationTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => coordinator.ApplyAsync(
                GamingOptimisedProfile(),
                PerformanceValidatedMacBookPro16_1(),
                cancellationSource.Token));

        Assert.Equal(0, power.GuardedApplyCallCount);
        Assert.Equal(2, fanProbe.ProbeCallCount);
        Assert.Equal(1, fanCoordinator.RecoverCallCount);
        Assert.True(fanProbe.ProbeTokens[0].CanBeCanceled);
        Assert.False(fanProbe.ProbeTokens[1].CanBeCanceled);
        Assert.False(fanCoordinator.RecoverTokens[0].CanBeCanceled);
        Assert.Equal(1, sessionFactory.OpenCallCount);
        Assert.Single(sessionFactory.Sessions);
        Assert.Equal(1, sessionFactory.Sessions[0].DisposeCallCount);
        Assert.Equal(
            [
                "power-read",
                "fan-session-open",
                "fan-probe",
                "fan-apply",
                "fresh-fan-probe",
                "fan-recover",
                "fan-session-dispose"
            ],
            events);
    }

    [Fact]
    public async Task ApplyAsync_FanApplyThrowsAndRecoveryReturnsNoneWithManualFans_ThrowsCompensationException()
    {
        var originalException = new InvalidOperationException("fan apply failed");
        var events = new List<string>();
        var power = new RecordingPowerManagementService(events);
        var fanProbe = new RecordingFanCapabilityProbe(
            events,
            ValidFanCapability(),
            ValidFanCapability(fan0Mode: 1, fan1Mode: 1));
        var fanCoordinator = new RecordingFanOverrideCoordinator(events)
        {
            ApplyException = originalException,
            RecoveryResult = new FanOverrideRecoveryDecision(
                FanOverrideRecoveryAction.None,
                "No fan override ownership marker exists.")
        };
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);
        using var cancellationSource = new CancellationTokenSource();

        var exception = await Assert.ThrowsAsync<GamingOptimisedApplyCompensationException>(
            () => coordinator.ApplyAsync(
                GamingOptimisedProfile(),
                PerformanceValidatedMacBookPro16_1(),
                cancellationSource.Token));

        Assert.Same(originalException, exception.OperationException);
        Assert.Equal(FanOverrideRecoveryAction.None, exception.RecoveryDecision?.Action);
        Assert.Equal(0, power.GuardedApplyCallCount);
        Assert.Equal(2, fanProbe.ProbeCallCount);
        Assert.Equal(1, fanCoordinator.RecoverCallCount);
        Assert.False(fanProbe.ProbeTokens[1].CanBeCanceled);
        Assert.False(fanCoordinator.RecoverTokens[0].CanBeCanceled);
        Assert.Equal(
            [
                "power-read",
                "fan-session-open",
                "fan-probe",
                "fan-apply",
                "fresh-fan-probe",
                "fan-recover",
                "fan-session-dispose"
            ],
            events);
    }

    [Fact]
    public async Task ApplyAsync_FanAndPowerApplySucceed_SucceedsWithoutFanCompensationAndUsesGuardedPowerApply()
    {
        var power = new RecordingPowerManagementService();
        var fanProbe = new RecordingFanCapabilityProbe();
        var fanCoordinator = new RecordingFanOverrideCoordinator();
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        var result = await coordinator.ApplyAsync(
            GamingOptimisedProfile(),
            PerformanceValidatedMacBookPro16_1(),
            CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Null(result.FanCompensation);
        Assert.Equal(1, power.GuardedApplyCallCount);
        Assert.Equal(0, power.UnguardedApplyCallCount);
        Assert.NotNull(power.LastExpectedStateBefore);
    }

    [Fact]
    public async Task ApplyAsync_PowerOperationFailure_RecoversFansAndDoesNotRestoreOriginalPowerSettings()
    {
        var events = new List<string>();
        var failedPowerOperation = FailedPowerOperation(
            ExpectedPowerState,
            GamingOptimisedSettings(),
            "power apply failed");
        var power = new RecordingPowerManagementService(events)
        {
            ApplyResult = failedPowerOperation
        };
        var fanProbe = new RecordingFanCapabilityProbe(events);
        var fanCoordinator = new RecordingFanOverrideCoordinator(events)
        {
            RecoveryResult = new FanOverrideRecoveryDecision(
                FanOverrideRecoveryAction.RestoreAppleAuto,
                "Apple Auto restored.")
        };
        var sessionFactory = new RecordingFanExecutionSessionFactory(
            fanProbe,
            fanCoordinator,
            events);
        var coordinator = CreateCoordinator(power, sessionFactory);

        var result = await coordinator.ApplyAsync(
            GamingOptimisedProfile(),
            PerformanceValidatedMacBookPro16_1(),
            CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Same(failedPowerOperation, result.PowerOperation);
        Assert.NotNull(result.FanCompensation);
        Assert.Equal(1, fanCoordinator.RecoverCallCount);
        Assert.Equal(0, power.RestoreOriginalSettingsCallCount);
        Assert.Equal(1, sessionFactory.OpenCallCount);
        Assert.Single(sessionFactory.Sessions);
        Assert.Equal(1, sessionFactory.Sessions[0].DisposeCallCount);
        Assert.Equal(
            [
                "power-read",
                "fan-session-open",
                "fan-probe",
                "fan-apply",
                "power-apply",
                "fresh-fan-probe",
                "fan-recover",
                "fan-session-dispose"
            ],
            events);
    }

    [Fact]
    public async Task ApplyAsync_PowerOperationFailureAndCompensationBlockedAndSessionDisposeThrows_PreservesFailureResultDetails()
    {
        var cleanupException = new InvalidOperationException("session dispose failed");
        var failedPowerOperation = FailedPowerOperation(
            ExpectedPowerState,
            GamingOptimisedSettings(),
            "power apply failed");
        var power = new RecordingPowerManagementService
        {
            ApplyResult = failedPowerOperation
        };
        var fanProbe = new RecordingFanCapabilityProbe();
        var recoveryDecision = new FanOverrideRecoveryDecision(
            FanOverrideRecoveryAction.Blocked,
            "owned state changed");
        var fanCoordinator = new RecordingFanOverrideCoordinator
        {
            RecoveryResult = recoveryDecision
        };
        var sessionFactory = new RecordingFanExecutionSessionFactory(
            fanProbe,
            fanCoordinator)
        {
            DisposeException = cleanupException
        };
        var coordinator = CreateCoordinator(power, sessionFactory);

        var exception = await Assert.ThrowsAsync<FanExecutionSessionCleanupException>(
            () => coordinator.ApplyAsync(
                GamingOptimisedProfile(),
                PerformanceValidatedMacBookPro16_1(),
                CancellationToken.None));

        Assert.Null(exception.OperationException);
        Assert.Equal(
            "Processor power operation failed and fan compensation was blocked. owned state changed",
            exception.OperationFailureReason);
        Assert.Same(recoveryDecision, exception.RecoveryDecision);
        Assert.Same(cleanupException, exception.CleanupException);
        Assert.Equal(0, power.RestoreOriginalSettingsCallCount);
    }

    [Fact]
    public async Task ApplyAsync_PowerOperationFailureAndRecoveryReturnsNoneWithManualFans_ReturnsCompensationFailure()
    {
        var power = new RecordingPowerManagementService
        {
            ApplyResult = FailedPowerOperation(
                ExpectedPowerState,
                GamingOptimisedSettings(),
                "power apply failed")
        };
        var fanProbe = new RecordingFanCapabilityProbe(
            results:
            [
                ValidFanCapability(),
                ValidFanCapability(fan0Mode: 1, fan1Mode: 1)
            ]);
        var fanCoordinator = new RecordingFanOverrideCoordinator
        {
            RecoveryResult = new FanOverrideRecoveryDecision(
                FanOverrideRecoveryAction.None,
                "No fan override ownership marker exists.")
        };
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        var result = await coordinator.ApplyAsync(
            GamingOptimisedProfile(),
            PerformanceValidatedMacBookPro16_1(),
            CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Contains("fan compensation", result.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not verified", result.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(FanOverrideRecoveryAction.None, result.FanCompensation?.Action);
        Assert.Equal(0, power.RestoreOriginalSettingsCallCount);
    }

    [Fact]
    public async Task ApplyAsync_UnverifiedCompensationAndSessionDisposeThrows_PreservesFailureResultDetails()
    {
        var cleanupException = new InvalidOperationException("session dispose failed");
        var power = new RecordingPowerManagementService
        {
            ApplyResult = FailedPowerOperation(
                ExpectedPowerState,
                GamingOptimisedSettings(),
                "power apply failed")
        };
        var fanProbe = new RecordingFanCapabilityProbe(
            results:
            [
                ValidFanCapability(),
                ValidFanCapability(fan0Mode: 1, fan1Mode: 1)
            ]);
        var recoveryDecision = new FanOverrideRecoveryDecision(
            FanOverrideRecoveryAction.None,
            "No fan override ownership marker exists.");
        var fanCoordinator = new RecordingFanOverrideCoordinator
        {
            RecoveryResult = recoveryDecision
        };
        var sessionFactory = new RecordingFanExecutionSessionFactory(
            fanProbe,
            fanCoordinator)
        {
            DisposeException = cleanupException
        };
        var coordinator = CreateCoordinator(power, sessionFactory);

        var exception = await Assert.ThrowsAsync<FanExecutionSessionCleanupException>(
            () => coordinator.ApplyAsync(
                GamingOptimisedProfile(),
                PerformanceValidatedMacBookPro16_1(),
                CancellationToken.None));

        Assert.Null(exception.OperationException);
        Assert.Equal(
            "Processor power operation failed and fan compensation was not verified. No fan override ownership marker exists.",
            exception.OperationFailureReason);
        Assert.Same(recoveryDecision, exception.RecoveryDecision);
        Assert.Same(cleanupException, exception.CleanupException);
        Assert.Equal(0, power.RestoreOriginalSettingsCallCount);
    }

    [Fact]
    public async Task ApplyAsync_PowerOperationFailureAndRecoveryReturnsNoneWithAppleAutoFans_AcceptsCompensation()
    {
        var power = new RecordingPowerManagementService
        {
            ApplyResult = FailedPowerOperation(
                ExpectedPowerState,
                GamingOptimisedSettings(),
                "power apply failed")
        };
        var fanProbe = new RecordingFanCapabilityProbe(
            results:
            [
                ValidFanCapability(),
                ValidFanCapability(fan0Mode: 0, fan1Mode: 0)
            ]);
        var fanCoordinator = new RecordingFanOverrideCoordinator
        {
            RecoveryResult = new FanOverrideRecoveryDecision(
                FanOverrideRecoveryAction.None,
                "Both fans are already in Apple Auto.")
        };
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        var result = await coordinator.ApplyAsync(
            GamingOptimisedProfile(),
            PerformanceValidatedMacBookPro16_1(),
            CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal("power apply failed", result.FailureReason);
        Assert.Equal(FanOverrideRecoveryAction.None, result.FanCompensation?.Action);
        Assert.Equal(0, power.RestoreOriginalSettingsCallCount);
    }

    [Fact]
    public async Task ApplyAsync_PowerOperationFailureAndRecoveryThrows_ThrowsCompensationException()
    {
        var recoveryException = new InvalidOperationException("recovery failed");
        var power = new RecordingPowerManagementService
        {
            ApplyResult = FailedPowerOperation(
                ExpectedPowerState,
                GamingOptimisedSettings(),
                "power apply failed")
        };
        var fanProbe = new RecordingFanCapabilityProbe();
        var fanCoordinator = new RecordingFanOverrideCoordinator
        {
            RecoveryException = recoveryException
        };
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        var exception = await Assert.ThrowsAsync<GamingOptimisedApplyCompensationException>(
            () => coordinator.ApplyAsync(
                GamingOptimisedProfile(),
                PerformanceValidatedMacBookPro16_1(),
                CancellationToken.None));

        Assert.Same(recoveryException, exception.CompensationException);
        Assert.Null(exception.OperationException);
        Assert.Null(exception.RecoveryDecision);
        Assert.Equal(0, power.RestoreOriginalSettingsCallCount);
    }

    [Fact]
    public async Task ApplyAsync_PowerOperationFailure_CompensationRunsAfterPowerApply()
    {
        var events = new List<string>();
        var power = new RecordingPowerManagementService(events)
        {
            ApplyResult = FailedPowerOperation(
                ExpectedPowerState,
                GamingOptimisedSettings(),
                "power apply failed")
        };
        var fanProbe = new RecordingFanCapabilityProbe(events);
        var fanCoordinator = new RecordingFanOverrideCoordinator(events);
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        await coordinator.ApplyAsync(
            GamingOptimisedProfile(),
            PerformanceValidatedMacBookPro16_1(),
            CancellationToken.None);

        Assert.Equal(
            ["fan-apply", "power-apply", "fresh-fan-probe", "fan-recover"],
            events.Where(
                    name => name is "fan-apply" or "power-apply" or "fresh-fan-probe" or "fan-recover")
                .ToArray());
    }

    [Fact]
    public async Task ApplyAsync_CpuStageCancellationAfterFanApply_RecoversFansWithNonCancelableToken()
    {
        var events = new List<string>();
        var power = new RecordingPowerManagementService(events)
        {
            ApplyException = new OperationCanceledException("cpu apply canceled")
        };
        var fanProbe = new RecordingFanCapabilityProbe(events);
        var fanCoordinator = new RecordingFanOverrideCoordinator(events);
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);
        using var cancellationSource = new CancellationTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => coordinator.ApplyAsync(
                GamingOptimisedProfile(),
                PerformanceValidatedMacBookPro16_1(),
                cancellationSource.Token));

        Assert.Equal(1, power.GuardedApplyCallCount);
        Assert.Equal(2, fanProbe.ProbeCallCount);
        Assert.Equal(1, fanCoordinator.RecoverCallCount);
        Assert.False(fanProbe.ProbeTokens[1].CanBeCanceled);
        Assert.False(fanCoordinator.RecoverTokens[0].CanBeCanceled);
        Assert.Equal(
            [
                "power-read",
                "fan-session-open",
                "fan-probe",
                "fan-apply",
                "power-apply",
                "fresh-fan-probe",
                "fan-recover",
                "fan-session-dispose"
            ],
            events);
    }

    [Fact]
    public async Task ApplyAsync_FanCompensationBlockedAfterPowerFailure_ReturnsDistinctCompensationFailure()
    {
        var power = new RecordingPowerManagementService
        {
            ApplyResult = FailedPowerOperation(
                ExpectedPowerState,
                GamingOptimisedSettings(),
                "power apply failed")
        };
        var fanProbe = new RecordingFanCapabilityProbe();
        var fanCoordinator = new RecordingFanOverrideCoordinator
        {
            RecoveryResult = new FanOverrideRecoveryDecision(
                FanOverrideRecoveryAction.Blocked,
                "owned state changed")
        };
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        var result = await coordinator.ApplyAsync(
            GamingOptimisedProfile(),
            PerformanceValidatedMacBookPro16_1(),
            CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Contains("fan compensation", result.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.FanCompensation);
        Assert.Equal(FanOverrideRecoveryAction.Blocked, result.FanCompensation!.Action);
    }

    [Fact]
    public async Task ApplyAsync_PowerApplyReceivesPreviouslyReadPowerStateAsExpectedPrecondition()
    {
        var expectedStateBefore = CurrentPowerState() with
        {
            CapturedAt = DateTimeOffset.Parse("2026-02-03T04:05:06+00:00")
        };
        var power = new RecordingPowerManagementService
        {
            StateToRead = expectedStateBefore
        };
        var fanProbe = new RecordingFanCapabilityProbe();
        var fanCoordinator = new RecordingFanOverrideCoordinator();
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        await coordinator.ApplyAsync(
            GamingOptimisedProfile(),
            PerformanceValidatedMacBookPro16_1(),
            CancellationToken.None);

        Assert.Same(expectedStateBefore, power.LastExpectedStateBefore);
        Assert.Equal(1, power.GuardedApplyCallCount);
        Assert.Equal(0, power.UnguardedApplyCallCount);
    }

    private static GamingOptimisedApplyCoordinator CreateCoordinator(
        RecordingPowerManagementService powerManagementService,
        RecordingFanCapabilityProbe fanCapabilityProbe,
        RecordingFanOverrideCoordinator fanOverrideCoordinator)
    {
        return CreateCoordinator(
            powerManagementService,
            new RecordingFanExecutionSessionFactory(
                fanCapabilityProbe,
                fanOverrideCoordinator));
    }

    private static GamingOptimisedApplyCoordinator CreateCoordinator(
        RecordingPowerManagementService powerManagementService,
        IFanExecutionSessionFactory fanExecutionSessionFactory)
    {
        return new GamingOptimisedApplyCoordinator(
            new ProfileExecutionResolver(),
            new FanProfileExecutionResolver(),
            powerManagementService,
            fanExecutionSessionFactory);
    }

    private static PerformanceProfile GamingOptimisedProfile(
        ProcessorPowerProfileTarget? powerTarget = null)
    {
        return new PerformanceProfile(
            "gaming-optimised",
            "Gaming Optimised",
            IsAvailableForDetectedModel: true,
            powerTarget ?? new ProcessorPowerProfileTarget(95, 95, 0, 0, ProfileUnspecifiedValueSource.None),
            [],
            "Test Gaming Optimised profile.");
    }

    private static ModelVerificationResult PerformanceValidatedMacBookPro16_1()
    {
        return new ModelVerificationResult(
            "Apple Inc.",
            Model,
            PlatformSupportStatus.SupportedIntelMac,
            ModelValidationLevel.PerformanceValidated,
            "Performance validated.");
    }

    private static ProcessorPowerSettings GamingOptimisedSettings()
    {
        return new ProcessorPowerSettings(95, 95, 0, 0);
    }

    private static PowerStateSnapshot CurrentPowerState()
    {
        return new PowerStateSnapshot(
            Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"),
            ProcessorMaximumAc: 100,
            ProcessorMaximumDc: 100,
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

    private static FanControlCapabilityResult ValidFanCapability(
        byte fan0Mode = 0,
        byte fan1Mode = 0)
    {
        return Capability(
            isReadSupported: true,
            isHardwareSafetyGateSatisfied: true,
            snapshot: ValidFanSnapshot(fan0Mode, fan1Mode));
    }

    private static FanControlCapabilityResult Capability(
        bool isReadSupported,
        bool isHardwareSafetyGateSatisfied,
        FanSmcSnapshot? snapshot)
    {
        return new FanControlCapabilityResult(
            isReadSupported,
            isHardwareSafetyGateSatisfied,
            [],
            SmcTransportProtocol.Mmio,
            snapshot);
    }

    private static FanSmcSnapshot ValidFanSnapshot(
        byte fan0Mode = 0,
        byte fan1Mode = 0)
    {
        return new FanSmcSnapshot(
            UInt8("FNum", 2, 0x80),
            [
                new FanSmcChannelSnapshot(new FanIndex(0),
                    Float32("F0Mx", 5321.25f, 0x85), Float32("F0Ac", 1800f, 0x84),
                    UInt8("F0Md", fan0Mode, 0xD0), Float32("F0Tg", 1800f, 0xD4)),
                new FanSmcChannelSnapshot(new FanIndex(1),
                    Float32("F1Mx", 4789.5f, 0x85), Float32("F1Ac", 1700f, 0x84),
                    UInt8("F1Md", fan1Mode, 0xD0), Float32("F1Tg", 1700f, 0xD4))
            ]);
    }

    private static SmcValue Float32(
        string key,
        float value,
        byte attributes)
    {
        Span<byte> rawData = stackalloc byte[sizeof(float)];
        BinaryPrimitives.WriteInt32LittleEndian(
            rawData,
            BitConverter.SingleToInt32Bits(value));

        return new SmcValue(
            new SmcKeyInfo(key, 4, "flt ", attributes),
            rawData);
    }

    private static SmcValue UInt8(
        string key,
        byte value,
        byte attributes)
    {
        return new SmcValue(
            new SmcKeyInfo(key, 1, "ui8 ", attributes),
            [value]);
    }

    private sealed class RecordingPowerManagementService : IPowerManagementService
    {
        private readonly List<string>? _events;

        public RecordingPowerManagementService(List<string>? events = null)
        {
            _events = events;
        }

        public PowerStateSnapshot StateToRead { get; init; } = ExpectedPowerState;

        public PowerOperationResult? ApplyResult { get; init; }

        public Exception? ReadException { get; init; }

        public Exception? ApplyException { get; init; }

        public int ReadCurrentStateCallCount { get; private set; }

        public int GuardedApplyCallCount { get; private set; }

        public int UnguardedApplyCallCount { get; private set; }

        public int RestoreOriginalSettingsCallCount { get; private set; }

        public ProcessorPowerSettings? LastRequestedSettings { get; private set; }

        public PowerStateSnapshot? LastExpectedStateBefore { get; private set; }

        public Task<PowerStateSnapshot> ReadCurrentStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCurrentStateCallCount++;
            _events?.Add("power-read");

            if (ReadException is not null)
            {
                throw ReadException;
            }

            return Task.FromResult(StateToRead);
        }

        public Task<PowerOperationResult> ApplyProcessorSettingsAsync(
            ProcessorPowerSettings requestedSettings,
            CancellationToken cancellationToken)
        {
            UnguardedApplyCallCount++;
            _events?.Add("power-apply-unguarded");
            throw new InvalidOperationException("Transactional apply must use the expected-state overload.");
        }

        public Task<PowerOperationResult> ApplyProcessorSettingsAsync(
            ProcessorPowerSettings requestedSettings,
            PowerStateSnapshot expectedStateBefore,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GuardedApplyCallCount++;
            LastRequestedSettings = requestedSettings;
            LastExpectedStateBefore = expectedStateBefore;
            _events?.Add("power-apply");

            if (ApplyException is not null)
            {
                throw ApplyException;
            }

            return Task.FromResult(
                ApplyResult ?? SuccessfulPowerOperation(expectedStateBefore, requestedSettings));
        }

        public Task<PowerOperationResult> RestoreOriginalSettingsAsync(CancellationToken cancellationToken)
        {
            RestoreOriginalSettingsCallCount++;
            _events?.Add("power-restore");
            throw new InvalidOperationException("Transactional apply must not call restore.");
        }
    }

    private sealed class RecordingFanExecutionSessionFactory : IFanExecutionSessionFactory
    {
        private readonly RecordingFanCapabilityProbe _fanCapabilityProbe;
        private readonly RecordingFanOverrideCoordinator _fanOverrideCoordinator;
        private readonly List<string>? _events;

        public RecordingFanExecutionSessionFactory(
            RecordingFanCapabilityProbe fanCapabilityProbe,
            RecordingFanOverrideCoordinator fanOverrideCoordinator,
            List<string>? events = null)
        {
            _fanCapabilityProbe = fanCapabilityProbe;
            _fanOverrideCoordinator = fanOverrideCoordinator;
            _events = events ?? fanCapabilityProbe.Events ?? fanOverrideCoordinator.Events;
        }

        public Exception? OpenException { get; init; }

        public Exception? DisposeException { get; init; }

        public int OpenCallCount { get; private set; }

        public List<CancellationToken> OpenTokens { get; } = [];

        public List<RecordingFanExecutionSession> Sessions { get; } = [];

        public Task<IFanExecutionSession> OpenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenCallCount++;
            OpenTokens.Add(cancellationToken);
            _events?.Add("fan-session-open");

            if (OpenException is not null)
            {
                throw OpenException;
            }

            var session = new RecordingFanExecutionSession(
                _fanCapabilityProbe,
                _fanOverrideCoordinator,
                _events)
            {
                DisposeException = DisposeException
            };
            Sessions.Add(session);

            return Task.FromResult<IFanExecutionSession>(session);
        }
    }

    private sealed class RecordingFanExecutionSession : IFanExecutionSession
    {
        private readonly List<string>? _events;

        public RecordingFanExecutionSession(
            IFanCapabilityProbe capabilityProbe,
            IFanOverrideCoordinator overrideCoordinator,
            List<string>? events)
        {
            CapabilityProbe = capabilityProbe;
            OverrideCoordinator = overrideCoordinator;
            _events = events;
        }

        public IFanCapabilityProbe CapabilityProbe { get; }

        public IFanOverrideCoordinator OverrideCoordinator { get; }

        public int DisposeCallCount { get; private set; }

        public Exception? DisposeException { get; init; }

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            _events?.Add("fan-session-dispose");

            if (DisposeException is not null)
            {
                throw DisposeException;
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingFanCapabilityProbe : IFanCapabilityProbe
    {
        private readonly List<string>? _events;
        private readonly Queue<FanControlCapabilityResult> _results;

        public RecordingFanCapabilityProbe(
            List<string>? events = null,
            params FanControlCapabilityResult[] results)
        {
            _events = events;
            _results = new Queue<FanControlCapabilityResult>(
                results.Length == 0 ? [ValidFanCapability()] : results);
        }

        public List<string>? Events => _events;

        public int ProbeCallCount { get; private set; }

        public List<CancellationToken> ProbeTokens { get; } = [];

        public Task<FanControlCapabilityResult> ProbeAsync(
            string model,
            CancellationToken cancellationToken)
        {
            ProbeCallCount++;
            ProbeTokens.Add(cancellationToken);
            _events?.Add(ProbeCallCount == 1 ? "fan-probe" : "fresh-fan-probe");

            return Task.FromResult(
                _results.Count > 1
                    ? _results.Dequeue()
                    : _results.Peek());
        }
    }

    private sealed class RecordingFanOverrideCoordinator : IFanOverrideCoordinator
    {
        private readonly List<string>? _events;

        public RecordingFanOverrideCoordinator(List<string>? events = null)
        {
            _events = events;
        }

        public List<string>? Events => _events;

        public FanOverrideExecutionResult? ApplyResult { get; init; }

        public FanOverrideRecoveryDecision RecoveryResult { get; init; } =
            new(FanOverrideRecoveryAction.RestoreAppleAuto, "Apple Auto restored.");

        public Exception? ApplyException { get; init; }

        public Exception? RecoveryException { get; init; }

        public int ApplyCallCount { get; private set; }

        public int RecoverCallCount { get; private set; }

        public List<CancellationToken> ApplyTokens { get; } = [];

        public List<CancellationToken> RecoverTokens { get; } = [];

        public Task<FanOverrideExecutionResult> ApplyMaximumSafeRpmAsync(
            string model,
            FanControlCapabilityResult capability,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyCallCount++;
            ApplyTokens.Add(cancellationToken);
            _events?.Add("fan-apply");

            if (ApplyException is not null)
            {
                throw ApplyException;
            }

            return Task.FromResult(ApplyResult ?? AppliedFanExecution());
        }

        public Task<FanOverrideRecoveryDecision> RecoverAsync(
            string currentModel,
            FanControlCapabilityResult capability,
            CancellationToken cancellationToken)
        {
            RecoverCallCount++;
            RecoverTokens.Add(cancellationToken);
            _events?.Add("fan-recover");

            if (RecoveryException is not null)
            {
                throw RecoveryException;
            }

            return Task.FromResult(RecoveryResult);
        }

        private static FanOverrideExecutionResult AppliedFanExecution()
        {
            return FanOverrideExecutionResult.Applied(
                new FanOverrideOwnershipMarker(
                    Model,
                    5321.25f,
                    4789.5f,
                    DateTimeOffset.Parse("2026-01-01T00:00:00+00:00")));
        }
    }
}
