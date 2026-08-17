using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.PowerManagement;

namespace BootCampPerformanceControl.Profiles;

public sealed class ProcessorProfileStateEvaluator
{
    private const string GamingOptimisedProfileId = "gaming-optimised";
    private readonly IProfileCatalog _profileCatalog;
    private readonly ProfileExecutionResolver _profileExecutionResolver;

    public ProcessorProfileStateEvaluator(
        IProfileCatalog profileCatalog,
        ProfileExecutionResolver profileExecutionResolver)
    {
        ArgumentNullException.ThrowIfNull(profileCatalog);
        ArgumentNullException.ThrowIfNull(profileExecutionResolver);

        _profileCatalog = profileCatalog;
        _profileExecutionResolver = profileExecutionResolver;
    }

    public ProcessorProfileState Evaluate(
        PowerStateSnapshot? powerState,
        ModelVerificationResult verificationResult)
    {
        if (powerState is null)
        {
            return ProcessorProfileState.Unknown;
        }

        ArgumentNullException.ThrowIfNull(verificationResult);

        var profile = _profileCatalog
            .GetProfiles(verificationResult)
            .FirstOrDefault(profile => string.Equals(
                profile.Id,
                GamingOptimisedProfileId,
                StringComparison.OrdinalIgnoreCase));

        if (profile is null)
        {
            return ProcessorProfileState.Other;
        }

        var resolution = _profileExecutionResolver.ResolveProcessorSettings(
            profile,
            verificationResult);

        if (!resolution.IsExecutable || resolution.Settings is null)
        {
            return ProcessorProfileState.Other;
        }

        return powerState.ProcessorMaximumAc == resolution.Settings.ProcessorMaximumAc
            && powerState.ProcessorMaximumDc == resolution.Settings.ProcessorMaximumDc
            && powerState.BoostModeAc == resolution.Settings.BoostModeAc
            && powerState.BoostModeDc == resolution.Settings.BoostModeDc
                ? ProcessorProfileState.GamingOptimisedDetected
                : ProcessorProfileState.Other;
    }
}
