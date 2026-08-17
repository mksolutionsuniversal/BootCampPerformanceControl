using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.PowerManagement;

namespace BootCampPerformanceControl.Profiles;

public sealed class ProfileApplyService
{
    private readonly IHardwareDetectionService _hardwareDetectionService;
    private readonly IProfileCatalog _profileCatalog;
    private readonly ProfileExecutionResolver _profileExecutionResolver;
    private readonly IPowerManagementService _powerManagementService;

    public ProfileApplyService(
        IHardwareDetectionService hardwareDetectionService,
        IProfileCatalog profileCatalog,
        ProfileExecutionResolver profileExecutionResolver,
        IPowerManagementService powerManagementService)
    {
        _hardwareDetectionService = hardwareDetectionService;
        _profileCatalog = profileCatalog;
        _profileExecutionResolver = profileExecutionResolver;
        _powerManagementService = powerManagementService;
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
}
