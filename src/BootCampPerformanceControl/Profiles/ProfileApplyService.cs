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

        if (IsSupportedIntelMacGamingOptimised(profile, verificationResult)
            && _gamingOptimisedApplyCoordinator is not null)
        {
            try
            {
                var gamingResult = await _gamingOptimisedApplyCoordinator
                    .ApplyAsync(profile, verificationResult, cancellationToken)
                    .ConfigureAwait(false);

                return ProfileApplyResult.FromGamingOptimisedResult(gamingResult, verificationResult);
            }
            catch (AppleSmcServiceStateException)
            {
                // Fan control is additive. A stopped/unavailable backend must not
                // prevent the global processor target from being applied below.
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

    private static bool IsSupportedIntelMacGamingOptimised(
        PerformanceProfile profile,
        ModelVerificationResult verificationResult)
    {
        return string.Equals(profile.Id, "gaming-optimised", StringComparison.OrdinalIgnoreCase)
            && verificationResult.PlatformSupport == PlatformSupportStatus.SupportedIntelMac;
    }
}
