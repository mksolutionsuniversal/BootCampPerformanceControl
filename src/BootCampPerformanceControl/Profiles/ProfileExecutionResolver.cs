using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.PowerManagement;

namespace BootCampPerformanceControl.Profiles;

public sealed class ProfileExecutionResolver
{
    private static readonly ProcessorPowerSettings GamingOptimisedSettings = new(
        ProcessorMaximumAc: 95,
        ProcessorMaximumDc: 95,
        BoostModeAc: 0,
        BoostModeDc: 0);

    public ProfileExecutionResolution ResolveProcessorSettings(
        PerformanceProfile profile,
        ModelVerificationResult verificationResult)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(verificationResult);

        if (string.Equals(profile.Id, "restore", StringComparison.OrdinalIgnoreCase))
        {
            return ProfileExecutionResolution.NotExecutable(
                "Restore is not resolved through profile execution; it uses the saved original restore snapshot.");
        }

        var metadataFailure = ValidateNormalApplyMetadata(profile);
        if (metadataFailure is not null)
        {
            return ProfileExecutionResolution.NotExecutable(metadataFailure);
        }

        if (string.Equals(profile.Id, "gaming-optimised", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveGamingOptimised(profile, verificationResult);
        }

        return ProfileExecutionResolution.NotExecutable(
            $"Profile '{profile.Id}' is not supported for processor power execution.");
    }

    private static ProfileExecutionResolution ResolveGamingOptimised(
        PerformanceProfile profile,
        ModelVerificationResult verificationResult)
    {
        if (verificationResult.PlatformSupport != PlatformSupportStatus.SupportedIntelMac)
        {
            return ProfileExecutionResolution.NotExecutable(
                verificationResult.PlatformSupport switch
                {
                    PlatformSupportStatus.UnsupportedNonApple =>
                        "Gaming Optimised requires Apple hardware.",
                    PlatformSupportStatus.UnsupportedNonIntel =>
                        "Gaming Optimised requires an Intel processor on Apple hardware.",
                    PlatformSupportStatus.DetectionIncomplete =>
                        "Gaming Optimised requires complete hardware detection.",
                    _ =>
                        "Gaming Optimised requires a supported Intel Mac platform."
                });
        }

        if (!profile.IsAvailableForDetectedModel)
        {
            return ProfileExecutionResolution.NotExecutable(
                "Gaming Optimised is not marked available for the detected platform.");
        }

        var settings = new ProcessorPowerSettings(
            profile.PowerTarget.ProcessorMaximumAc!.Value,
            profile.PowerTarget.ProcessorMaximumDc!.Value,
            profile.PowerTarget.BoostModeAc!.Value,
            profile.PowerTarget.BoostModeDc!.Value);

        if (settings != GamingOptimisedSettings)
        {
            return ProfileExecutionResolution.NotExecutable(
                "Gaming Optimised metadata does not match the 95/95/0/0 power target.");
        }

        return ProfileExecutionResolution.Executable(settings);
    }

    private static string? ValidateNormalApplyMetadata(PerformanceProfile profile)
    {
        return profile.PowerTarget.UnspecifiedValueSource switch
        {
            ProfileUnspecifiedValueSource.None => ValidateCompleteProcessorTarget(profile),
            ProfileUnspecifiedValueSource.OriginalRestoreSnapshot =>
                "Profile execution is not available for unresolved original restore snapshot values.",
            _ => "Profile execution is not available for an unsupported processor value source."
        };
    }

    private static string? ValidateCompleteProcessorTarget(PerformanceProfile profile)
    {
        if (profile.PowerTarget.ProcessorMaximumAc is null
            || profile.PowerTarget.ProcessorMaximumDc is null
            || profile.PowerTarget.BoostModeAc is null
            || profile.PowerTarget.BoostModeDc is null)
        {
            return "Profile processor target metadata is incomplete.";
        }

        return null;
    }
}
