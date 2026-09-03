using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.PowerManagement;

namespace BootCampPerformanceControl.Profiles;

internal sealed class GamingOptimisedRestoreCoordinator
{
    private readonly IPowerManagementService _powerManagementService;
    private readonly IFanExecutionSessionFactory _fanExecutionSessionFactory;

    public GamingOptimisedRestoreCoordinator(
        IPowerManagementService powerManagementService,
        IFanExecutionSessionFactory fanExecutionSessionFactory)
    {
        _powerManagementService = powerManagementService ?? throw new ArgumentNullException(nameof(powerManagementService));
        _fanExecutionSessionFactory = fanExecutionSessionFactory ?? throw new ArgumentNullException(nameof(fanExecutionSessionFactory));
    }

    public async Task<GamingOptimisedRestoreResult> RestoreAsync(
        string model,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                model,
                VerifiedHardwareModels.MacBookPro16_1,
                StringComparison.Ordinal))
        {
            return GamingOptimisedRestoreResult.Failed(
                model ?? string.Empty,
                $"Gaming Optimised restore requires the verified {VerifiedHardwareModels.MacBookPro16_1} model.");
        }

        FanControlCapabilityResult freshFanCapability;
        FanOverrideRecoveryDecision fanRecovery;

        var fanSession = await _fanExecutionSessionFactory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            freshFanCapability = await fanSession.CapabilityProbe
                .ProbeAsync(model, cancellationToken)
                .ConfigureAwait(false);

            fanRecovery = await fanSession.OverrideCoordinator
                .RecoverAsync(
                    model,
                    freshFanCapability,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception operationException)
        {
            await DisposeFanSessionAfterFailureAsync(
                    fanSession,
                    operationException,
                    "Fan execution session cleanup failed after Gaming Optimised restore fan recovery failed.")
                .ConfigureAwait(false);
            throw;
        }

        var fanBaselineVerified = IsVerifiedFanBaseline(
            freshFanCapability,
            fanRecovery);

        var fanPhaseFailure = CreateFanPhaseFailure(
            model,
            fanRecovery,
            fanBaselineVerified);

        await DisposeFanSessionAfterResultAsync(
                fanSession,
                fanPhaseFailure,
                fanRecovery)
            .ConfigureAwait(false);

        if (fanPhaseFailure is not null)
        {
            return fanPhaseFailure;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var powerOperation = await _powerManagementService
            .RestoreOriginalSettingsAsync(cancellationToken)
            .ConfigureAwait(false);

        if (powerOperation.IsSuccessful)
        {
            return GamingOptimisedRestoreResult.Successful(
                model,
                fanRecovery,
                powerOperation);
        }

        return GamingOptimisedRestoreResult.Failed(
            model,
            powerOperation.FailureMessage ?? "Original processor power restore failed.",
            isFanBaselineVerified: true,
            fanRecovery,
            powerOperation);
    }

    private static GamingOptimisedRestoreResult? CreateFanPhaseFailure(
        string model,
        FanOverrideRecoveryDecision fanRecovery,
        bool fanBaselineVerified)
    {
        if (fanRecovery.Action == FanOverrideRecoveryAction.Blocked)
        {
            return GamingOptimisedRestoreResult.Failed(
                model,
                "Gaming Optimised restore is blocked by fan recovery. "
                + fanRecovery.Reason,
                isFanBaselineVerified: false,
                fanRecovery);
        }

        if (!fanBaselineVerified)
        {
            return GamingOptimisedRestoreResult.Failed(
                model,
                "Gaming Optimised restore could not verify the fan Apple Auto baseline.",
                isFanBaselineVerified: false,
                fanRecovery);
        }

        return null;
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
        GamingOptimisedRestoreResult? fanPhaseFailure,
        FanOverrideRecoveryDecision fanRecovery)
    {
        try
        {
            await fanSession.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception cleanupException) when (fanPhaseFailure is not null)
        {
            throw new FanExecutionSessionCleanupException(
                "Fan execution session cleanup failed after Gaming Optimised restore fan phase returned a failed result.",
                fanPhaseFailure.FailureReason,
                fanRecovery,
                cleanupException);
        }
    }

    private static bool IsVerifiedFanBaseline(
        FanControlCapabilityResult capability,
        FanOverrideRecoveryDecision recovery)
    {
        return recovery.Action switch
        {
            FanOverrideRecoveryAction.RestoreAppleAuto => true,
            FanOverrideRecoveryAction.None => IsVerifiedAppleAuto(capability),
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
}
