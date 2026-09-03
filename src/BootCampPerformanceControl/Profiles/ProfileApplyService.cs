using BootCampPerformanceControl.FanControl.Smc.CrystalIdea;
using BootCampPerformanceControl.FanControl.Smc.Windows;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.PowerManagement;

namespace BootCampPerformanceControl.Profiles;

public sealed class ProfileApplyService
{
    private readonly IHardwareDetectionService _hardwareDetectionService;
    private readonly IProfileCatalog _profileCatalog;
    private readonly ProfileExecutionResolver _profileExecutionResolver;
    private readonly IPowerManagementService _powerManagementService;
    private readonly GamingOptimisedApplyCoordinator? _gamingOptimisedApplyCoordinator;

    internal ProfileApplyService(
        IHardwareDetectionService hardwareDetectionService,
        IProfileCatalog profileCatalog,
        ProfileExecutionResolver profileExecutionResolver,
        IPowerManagementService powerManagementService,
        GamingOptimisedApplyCoordinator gamingOptimisedApplyCoordinator)
    {
        _hardwareDetectionService = hardwareDetectionService ?? throw new ArgumentNullException(nameof(hardwareDetectionService));
        _profileCatalog = profileCatalog ?? throw new ArgumentNullException(nameof(profileCatalog));
        _profileExecutionResolver = profileExecutionResolver ?? throw new ArgumentNullException(nameof(profileExecutionResolver));
        _powerManagementService = powerManagementService ?? throw new ArgumentNullException(nameof(powerManagementService));
        _gamingOptimisedApplyCoordinator = gamingOptimisedApplyCoordinator ?? throw new ArgumentNullException(nameof(gamingOptimisedApplyCoordinator));
    }

    public ProfileApplyService(
        IHardwareDetectionService hardwareDetectionService,
        IProfileCatalog profileCatalog,
        ProfileExecutionResolver profileExecutionResolver,
        IPowerManagementService powerManagementService)
    {
        _hardwareDetectionService = hardwareDetectionService ?? throw new ArgumentNullException(nameof(hardwareDetectionService));
        _profileCatalog = profileCatalog ?? throw new ArgumentNullException(nameof(profileCatalog));
        _profileExecutionResolver = profileExecutionResolver ?? throw new ArgumentNullException(nameof(profileExecutionResolver));
        _powerManagementService = powerManagementService ?? throw new ArgumentNullException(nameof(powerManagementService));
        _gamingOptimisedApplyCoordinator = null;
    }

    public async Task<ProfileApplyResult> ApplyProfileAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return ProfileApplyResult.Failed(
                profileId ?? string.Empty,
                "Profile ID is required.",
                ModelVerificationResult.Unknown());
        }

        var hardwareSnapshot = await _hardwareDetectionService
            .DetectAsync(cancellationToken)
            .ConfigureAwait(false);
        var verificationResult = _hardwareDetectionService.VerifyModel(hardwareSnapshot);
        var profiles = _profileCatalog.GetProfiles(verificationResult);
        var matches = profiles
            .Where(profile => string.Equals(
                profile.Id,
                profileId,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count != 1)
        {
            return ProfileApplyResult.Failed(
                profileId,
                $"Profile '{profileId}' was not found.",
                verificationResult);
        }

        var profile = matches[0];

        if (IsExactVerifiedMacBookPro16_1GamingOptimised(profile, verificationResult))
        {
            if (_gamingOptimisedApplyCoordinator is null)
            {
                // CRITICAL: MacBookPro16,1 must NEVER fall back to CPU-only.
                // Fail closed; CPU Apply must not run.
                return ProfileApplyResult.Failed(
                    profileId,
                    $"Transactional fan coordinator is required for verified {VerifiedHardwareModels.MacBookPro16_1} Gaming Optimised apply.",
                    verificationResult);
            }

            try
            {
                var gamingResult = await _gamingOptimisedApplyCoordinator
                    .ApplyAsync(profile, verificationResult, cancellationToken)
                    .ConfigureAwait(false);

                return ProfileApplyResult.FromGamingOptimisedResult(gamingResult, verificationResult);
            }
            catch (AppleSmcServiceStateException)
            {
                return ProfileApplyResult.Failed(
                    profileId,
                    "Fan control is not available because the AppleSMC service is not running. Enable fan monitoring before applying Gaming Optimised.",
                    verificationResult,
                    _profileExecutionResolver.ResolveProcessorSettings(profile, verificationResult));
            }
        }

        var resolution = _profileExecutionResolver.ResolveProcessorSettings(
            profile,
            verificationResult);

        if (!resolution.IsExecutable || resolution.Settings is null)
        {
            return ProfileApplyResult.Failed(
                profileId,
                resolution.FailureReason,
                verificationResult,
                resolution);
        }

        var expectedStateBefore = await _powerManagementService
            .ReadCurrentStateAsync(cancellationToken)
            .ConfigureAwait(false);
        var powerOperation = await _powerManagementService
            .ApplyProcessorSettingsAsync(
                resolution.Settings,
                expectedStateBefore,
                cancellationToken)
            .ConfigureAwait(false);

        return ProfileApplyResult.FromPowerOperation(
            profileId,
            verificationResult,
            resolution,
            powerOperation);
    }

    private static bool IsExactVerifiedMacBookPro16_1GamingOptimised(
        PerformanceProfile profile,
        ModelVerificationResult verificationResult)
    {
        return string.Equals(profile.Id, "gaming-optimised", StringComparison.OrdinalIgnoreCase)
            && string.Equals(verificationResult.Model, VerifiedHardwareModels.MacBookPro16_1, StringComparison.Ordinal)
            && verificationResult.PlatformSupport == PlatformSupportStatus.SupportedIntelMac
            && verificationResult.ValidationLevel == ModelValidationLevel.PerformanceValidated;
    }
}
