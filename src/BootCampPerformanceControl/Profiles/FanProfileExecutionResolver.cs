using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.HardwareDetection;

namespace BootCampPerformanceControl.Profiles;

internal sealed class FanProfileExecutionResolver
{
    private const string GamingOptimisedProfileId = "gaming-optimised";
    private const string RestoreProfileId = "restore";

    private readonly FanOverridePreflightPolicy _preflightPolicy;
    private readonly ProfileExecutionResolver _profileExecutionResolver;

    public FanProfileExecutionResolver(
        FanOverridePreflightPolicy? preflightPolicy = null,
        ProfileExecutionResolver? profileExecutionResolver = null)
    {
        _preflightPolicy = preflightPolicy ?? new FanOverridePreflightPolicy();
        _profileExecutionResolver = profileExecutionResolver ?? new ProfileExecutionResolver();
    }

    public FanProfileExecutionResolution ResolveMaximumSafeRpmPlan(
        PerformanceProfile profile,
        ModelVerificationResult verificationResult,
        FanControlCapabilityResult fanCapability)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(verificationResult);
        ArgumentNullException.ThrowIfNull(fanCapability);

        if (string.Equals(profile.Id, RestoreProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return FanProfileExecutionResolution.NotExecutable(
                "Restore does not request fan execution.");
        }

        if (!string.Equals(profile.Id, GamingOptimisedProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return FanProfileExecutionResolution.NotExecutable(
                $"Profile '{profile.Id}' does not request fan execution.");
        }

        if (verificationResult.PlatformSupport != PlatformSupportStatus.SupportedIntelMac)
        {
            return FanProfileExecutionResolution.NotExecutable(
                "Gaming Optimised fan execution requires a supported Intel Mac platform.");
        }

        if (!profile.IsAvailableForDetectedModel)
        {
            return FanProfileExecutionResolution.NotExecutable(
                "Gaming Optimised is not marked available for the detected platform.");
        }

        var processorResolution = _profileExecutionResolver.ResolveProcessorSettings(
            profile,
            verificationResult);

        if (!processorResolution.IsExecutable)
        {
            return FanProfileExecutionResolution.NotExecutable(
                "Gaming Optimised fan execution requires an executable processor profile. "
                + processorResolution.FailureReason);
        }

        if (!fanCapability.IsReadSupported)
        {
            return FanProfileExecutionResolution.NotExecutable(
                "Fan execution requires read-supported SMC fan capability.");
        }

        if (!fanCapability.IsHardwareSafetyGateSatisfied)
        {
            return FanProfileExecutionResolution.NotExecutable(
                "Fan execution requires a satisfied hardware safety gate.");
        }

        if (fanCapability.Snapshot is null)
        {
            return FanProfileExecutionResolution.NotExecutable(
                "Fan execution requires a live SMC fan snapshot.");
        }

        var preparation = _preflightPolicy.PrepareMaximumSafeRpm(
            verificationResult.Model,
            fanCapability);

        if (!preparation.IsAllowed || preparation.Plan is null)
        {
            return FanProfileExecutionResolution.NotExecutable(
                preparation.FailureReason ?? "Fan override preflight was blocked.");
        }

        return FanProfileExecutionResolution.Executable(preparation.Plan);
    }
}
