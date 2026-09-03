using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.PowerManagement;

namespace BootCampPerformanceControl.Profiles;

internal sealed class GamingOptimisedRestoreCoordinator
{
    private readonly IPowerManagementService _powerManagementService;
    private readonly IFanCapabilityProbe _fanCapabilityProbe;
    private readonly IFanOverrideCoordinator _fanOverrideCoordinator;

    public GamingOptimisedRestoreCoordinator(
        IPowerManagementService powerManagementService,
        IFanCapabilityProbe fanCapabilityProbe,
        IFanOverrideCoordinator fanOverrideCoordinator)
    {
        _powerManagementService = powerManagementService ?? throw new ArgumentNullException(nameof(powerManagementService));
        _fanCapabilityProbe = fanCapabilityProbe ?? throw new ArgumentNullException(nameof(fanCapabilityProbe));
        _fanOverrideCoordinator = fanOverrideCoordinator ?? throw new ArgumentNullException(nameof(fanOverrideCoordinator));
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

        var freshFanCapability = await _fanCapabilityProbe
            .ProbeAsync(model, cancellationToken)
            .ConfigureAwait(false);

        var fanRecovery = await _fanOverrideCoordinator
            .RecoverAsync(
                model,
                freshFanCapability,
                CancellationToken.None)
            .ConfigureAwait(false);

        var fanBaselineVerified = IsVerifiedFanBaseline(
            freshFanCapability,
            fanRecovery);

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
