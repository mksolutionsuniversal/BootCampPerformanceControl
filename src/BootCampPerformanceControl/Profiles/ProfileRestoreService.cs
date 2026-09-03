using BootCampPerformanceControl.FanControl.Smc.CrystalIdea;
using BootCampPerformanceControl.FanControl.Smc.Windows;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.PowerManagement;

namespace BootCampPerformanceControl.Profiles;

public sealed class ProfileRestoreService
{
    private readonly IHardwareDetectionService _hardwareDetectionService;
    private readonly IPowerManagementService _powerManagementService;
    private readonly GamingOptimisedRestoreCoordinator? _gamingOptimisedRestoreCoordinator;

    internal ProfileRestoreService(
        IHardwareDetectionService hardwareDetectionService,
        IPowerManagementService powerManagementService,
        GamingOptimisedRestoreCoordinator gamingOptimisedRestoreCoordinator)
    {
        _hardwareDetectionService = hardwareDetectionService ?? throw new ArgumentNullException(nameof(hardwareDetectionService));
        _powerManagementService = powerManagementService ?? throw new ArgumentNullException(nameof(powerManagementService));
        _gamingOptimisedRestoreCoordinator = gamingOptimisedRestoreCoordinator ?? throw new ArgumentNullException(nameof(gamingOptimisedRestoreCoordinator));
    }

    public ProfileRestoreService(
        IHardwareDetectionService hardwareDetectionService,
        IPowerManagementService powerManagementService)
    {
        _hardwareDetectionService = hardwareDetectionService ?? throw new ArgumentNullException(nameof(hardwareDetectionService));
        _powerManagementService = powerManagementService ?? throw new ArgumentNullException(nameof(powerManagementService));
        _gamingOptimisedRestoreCoordinator = null;
    }

    public async Task<ProfileRestoreResult> RestoreAsync(CancellationToken cancellationToken)
    {
        var hardwareSnapshot = await _hardwareDetectionService
            .DetectAsync(cancellationToken)
            .ConfigureAwait(false);
        var verificationResult = _hardwareDetectionService.VerifyModel(hardwareSnapshot);

        if (IsExactVerifiedMacBookPro16_1(verificationResult))
        {
            if (_gamingOptimisedRestoreCoordinator is null)
            {
                // CRITICAL: MacBookPro16,1 must NEVER fall back to CPU-only.
                // If coordinator is missing, fail closed without touching power.
                return ProfileRestoreResult.Failed(
                    $"Transactional fan restore coordinator is required for verified {VerifiedHardwareModels.MacBookPro16_1}.",
                    verificationResult);
            }

            try
            {
                var gamingResult = await _gamingOptimisedRestoreCoordinator
                    .RestoreAsync(verificationResult.Model, cancellationToken)
                    .ConfigureAwait(false);

                if (!gamingResult.IsSuccessful)
                {
                    return ProfileRestoreResult.Failed(
                        gamingResult.FailureReason,
                        verificationResult,
                        gamingResult.PowerOperation,
                        gamingResult.FanRecovery);
                }

                return ProfileRestoreResult.Successful(
                    verificationResult,
                    gamingResult.PowerOperation,
                    gamingResult.FanRecovery);
            }
            catch (AppleSmcServiceStateException)
            {
                return ProfileRestoreResult.Failed(
                    "Fan control is not available because the AppleSMC service is not running. Enable fan monitoring before restoring original settings.",
                    verificationResult);
            }
        }

        // Other models retain existing power-only Restore
        var powerOperation = await _powerManagementService
            .RestoreOriginalSettingsAsync(cancellationToken)
            .ConfigureAwait(false);

        return ProfileRestoreResult.FromPowerOperation(powerOperation, verificationResult);
    }

    private static bool IsExactVerifiedMacBookPro16_1(ModelVerificationResult verificationResult)
    {
        return string.Equals(
                verificationResult.Model,
                VerifiedHardwareModels.MacBookPro16_1,
                StringComparison.Ordinal)
            && verificationResult.PlatformSupport == PlatformSupportStatus.SupportedIntelMac
            && verificationResult.ValidationLevel == ModelValidationLevel.PerformanceValidated;
    }
}
