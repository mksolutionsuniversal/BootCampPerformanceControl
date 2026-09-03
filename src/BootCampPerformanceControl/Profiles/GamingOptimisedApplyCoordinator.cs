using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.PowerManagement;

namespace BootCampPerformanceControl.Profiles;

internal sealed class GamingOptimisedApplyCoordinator
{
    private readonly ProfileExecutionResolver _profileExecutionResolver;
    private readonly FanProfileExecutionResolver _fanProfileExecutionResolver;
    private readonly IPowerManagementService _powerManagementService;
    private readonly IFanExecutionSessionFactory _fanExecutionSessionFactory;

    public GamingOptimisedApplyCoordinator(
        ProfileExecutionResolver profileExecutionResolver,
        FanProfileExecutionResolver fanProfileExecutionResolver,
        IPowerManagementService powerManagementService,
        IFanExecutionSessionFactory fanExecutionSessionFactory)
    {
        _profileExecutionResolver = profileExecutionResolver ?? throw new ArgumentNullException(nameof(profileExecutionResolver));
        _fanProfileExecutionResolver = fanProfileExecutionResolver ?? throw new ArgumentNullException(nameof(fanProfileExecutionResolver));
        _powerManagementService = powerManagementService ?? throw new ArgumentNullException(nameof(powerManagementService));
        _fanExecutionSessionFactory = fanExecutionSessionFactory ?? throw new ArgumentNullException(nameof(fanExecutionSessionFactory));
    }

    public async Task<GamingOptimisedApplyResult> ApplyAsync(
        PerformanceProfile profile,
        ModelVerificationResult verificationResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(verificationResult);

        var processorResolution = _profileExecutionResolver.ResolveProcessorSettings(
            profile,
            verificationResult);

        if (!processorResolution.IsExecutable || processorResolution.Settings is null)
        {
            return GamingOptimisedApplyResult.Failed(
                profile.Id,
                processorResolution.FailureReason,
                processorResolution);
        }

        var expectedStateBefore = await _powerManagementService
            .ReadCurrentStateAsync(cancellationToken)
            .ConfigureAwait(false);

        var fanSession = await _fanExecutionSessionFactory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        GamingOptimisedApplyResult result;
        try
        {
            result = await ApplyWithFanSessionAsync(
                    profile,
                    verificationResult,
                    processorResolution,
                    expectedStateBefore,
                    fanSession,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception operationException)
        {
            await DisposeFanSessionAfterFailureAsync(
                    fanSession,
                    operationException,
                    "Fan execution session cleanup failed after Gaming Optimised apply failed.")
                .ConfigureAwait(false);
            throw;
        }

        await DisposeFanSessionAfterResultAsync(fanSession, result)
            .ConfigureAwait(false);
        return result;
    }

    private async Task<GamingOptimisedApplyResult> ApplyWithFanSessionAsync(
        PerformanceProfile profile,
        ModelVerificationResult verificationResult,
        ProfileExecutionResolution processorResolution,
        PowerStateSnapshot expectedStateBefore,
        IFanExecutionSession fanSession,
        CancellationToken cancellationToken)
    {
        var processorSettings = processorResolution.Settings
            ?? throw new InvalidOperationException(
                "Executable processor resolution must include settings.");

        var fanCapability = await fanSession.CapabilityProbe
            .ProbeAsync(verificationResult.Model, cancellationToken)
            .ConfigureAwait(false);
        var fanResolution = _fanProfileExecutionResolver.ResolveMaximumSafeRpmPlan(
            profile,
            verificationResult,
            fanCapability);

        if (!fanResolution.IsExecutable || fanResolution.Plan is null)
        {
            return GamingOptimisedApplyResult.Failed(
                profile.Id,
                fanResolution.FailureReason,
                processorResolution,
                fanResolution);
        }

        FanOverrideExecutionResult fanExecution;
        try
        {
            fanExecution = await fanSession.OverrideCoordinator
                .ApplyMaximumSafeRpmAsync(
                    verificationResult.Model,
                    fanCapability,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await RecoverFanOrThrowAsync(
                    fanSession,
                    verificationResult.Model,
                    "Fan apply failed and fan compensation could not be verified.",
                    exception)
                .ConfigureAwait(false);
            throw;
        }

        if (!fanExecution.IsApplied)
        {
            return GamingOptimisedApplyResult.Failed(
                profile.Id,
                fanExecution.Message,
                processorResolution,
                fanResolution,
                fanExecution);
        }

        PowerOperationResult powerOperation;
        try
        {
            powerOperation = await _powerManagementService
                .ApplyProcessorSettingsAsync(
                    processorSettings,
                    expectedStateBefore,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await RecoverFanOrThrowAsync(
                    fanSession,
                    verificationResult.Model,
                    "Processor apply failed after fan ownership and fan compensation could not be verified.",
                    exception)
                .ConfigureAwait(false);
            throw;
        }

        if (powerOperation.IsSuccessful)
        {
            return GamingOptimisedApplyResult.Successful(
                profile.Id,
                processorResolution,
                fanResolution,
                fanExecution,
                powerOperation);
        }

        var fanCompensation = await RecoverFanAfterPowerFailureOrThrowAsync(
                fanSession,
                verificationResult.Model)
            .ConfigureAwait(false);

        if (fanCompensation.Decision.Action == FanOverrideRecoveryAction.Blocked)
        {
            return GamingOptimisedApplyResult.Failed(
                profile.Id,
                "Processor power operation failed and fan compensation was blocked. "
                + fanCompensation.Decision.Reason,
                processorResolution,
                fanResolution,
                fanExecution,
                powerOperation,
                fanCompensation.Decision);
        }

        if (!IsVerifiedCompensation(fanCompensation))
        {
            return GamingOptimisedApplyResult.Failed(
                profile.Id,
                "Processor power operation failed and fan compensation was not verified. "
                + fanCompensation.Decision.Reason,
                processorResolution,
                fanResolution,
                fanExecution,
                powerOperation,
                fanCompensation.Decision);
        }

        return GamingOptimisedApplyResult.Failed(
            profile.Id,
            powerOperation.FailureMessage ?? "Processor power operation failed.",
            processorResolution,
            fanResolution,
            fanExecution,
            powerOperation,
            fanCompensation.Decision);
    }

    private static async Task DisposeFanSessionAfterFailureAsync(
        IFanExecutionSession fanSession,
        Exception operationException,
        string message)
    {
        try
        {
            await fanSession.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception cleanupException)
        {
            throw new FanExecutionSessionCleanupException(
                message,
                operationException,
                cleanupException);
        }
    }

    private static async Task DisposeFanSessionAfterResultAsync(
        IFanExecutionSession fanSession,
        GamingOptimisedApplyResult result)
    {
        try
        {
            await fanSession.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception cleanupException) when (!result.IsSuccessful)
        {
            throw new FanExecutionSessionCleanupException(
                "Fan execution session cleanup failed after Gaming Optimised apply returned a failed result.",
                result.FailureReason,
                result.FanCompensation,
                cleanupException);
        }
    }

    private async Task<FanRecoveryResult> RecoverFanAfterPowerFailureOrThrowAsync(
        IFanExecutionSession fanSession,
        string model)
    {
        try
        {
            return await RecoverFanCoreAsync(fanSession, model)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw new GamingOptimisedApplyCompensationException(
                "Processor power operation failed and fan compensation failed.",
                operationException: null,
                recoveryDecision: null,
                compensationException: exception);
        }
    }

    private async Task<FanRecoveryResult> RecoverFanOrThrowAsync(
        IFanExecutionSession fanSession,
        string model,
        string failureMessage,
        Exception operationException)
    {
        try
        {
            var recovery = await RecoverFanCoreAsync(fanSession, model)
                .ConfigureAwait(false);

            if (recovery.Decision.Action == FanOverrideRecoveryAction.Blocked)
            {
                throw new GamingOptimisedApplyCompensationException(
                    failureMessage + " " + recovery.Decision.Reason,
                    operationException,
                    recovery.Decision);
            }

            if (!IsVerifiedCompensation(recovery))
            {
                throw new GamingOptimisedApplyCompensationException(
                    failureMessage + " " + recovery.Decision.Reason,
                    operationException,
                    recovery.Decision);
            }

            return recovery;
        }
        catch (GamingOptimisedApplyCompensationException)
        {
            throw;
        }
        catch (Exception compensationException)
        {
            throw new GamingOptimisedApplyCompensationException(
                failureMessage,
                operationException,
                recoveryDecision: null,
                compensationException: compensationException);
        }
    }

    private static async Task<FanRecoveryResult> RecoverFanCoreAsync(
        IFanExecutionSession fanSession,
        string model)
    {
        var freshFanCapability = await fanSession.CapabilityProbe
            .ProbeAsync(model, CancellationToken.None)
            .ConfigureAwait(false);

        var decision = await fanSession.OverrideCoordinator
            .RecoverAsync(
                model,
                freshFanCapability,
                CancellationToken.None)
            .ConfigureAwait(false);

        return new FanRecoveryResult(freshFanCapability, decision);
    }

    private static bool IsVerifiedCompensation(FanRecoveryResult recovery)
    {
        return recovery.Decision.Action switch
        {
            FanOverrideRecoveryAction.RestoreAppleAuto => true,
            FanOverrideRecoveryAction.None => IsVerifiedAppleAuto(recovery.FreshCapability),
            FanOverrideRecoveryAction.Blocked => false,
            _ => false
        };
    }

    private static bool IsVerifiedAppleAuto(FanControlCapabilityResult capability)
    {
        if (!capability.IsReadSupported ||
            !capability.IsHardwareSafetyGateSatisfied ||
            capability.Snapshot is null)
        {
            return false;
        }

        try
        {
            return capability.Snapshot.Fan0Mode.GetUInt8() == 0 &&
                   capability.Snapshot.Fan1Mode.GetUInt8() == 0;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private sealed record FanRecoveryResult(
        FanControlCapabilityResult FreshCapability,
        FanOverrideRecoveryDecision Decision);
}
