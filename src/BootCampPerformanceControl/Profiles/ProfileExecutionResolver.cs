using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.PowerManagement;

namespace BootCampPerformanceControl.Profiles;

public sealed class ProfileExecutionResolver
{
    private const string AppleIncManufacturer = "Apple Inc.";
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
        if (profile.Scope != ProfileScope.VerifiedModelSpecific)
        {
            return ProfileExecutionResolution.NotExecutable(
                "Gaming Optimised requires verified model-specific profile metadata.");
        }

        if (!string.Equals(
                profile.TargetModel,
                VerifiedHardwareModels.MacBookPro16_1,
                StringComparison.OrdinalIgnoreCase))
        {
            return ProfileExecutionResolution.NotExecutable(
                $"Gaming Optimised targets only {VerifiedHardwareModels.MacBookPro16_1}.");
        }

        if (!profile.IsAvailableForDetectedModel)
        {
            return ProfileExecutionResolution.NotExecutable(
                "Gaming Optimised is not marked available for the detected verified model.");
        }

        if (!string.Equals(
                verificationResult.Manufacturer,
                AppleIncManufacturer,
                StringComparison.OrdinalIgnoreCase))
        {
            return ProfileExecutionResolution.NotExecutable(
                "Gaming Optimised requires manufacturer Apple Inc.");
        }

        if (!verificationResult.IsApple)
        {
            return ProfileExecutionResolution.NotExecutable(
                "Gaming Optimised requires verified Apple hardware.");
        }

        if (!string.Equals(
                verificationResult.Model,
                VerifiedHardwareModels.MacBookPro16_1,
                StringComparison.OrdinalIgnoreCase))
        {
            return ProfileExecutionResolution.NotExecutable(
                $"Gaming Optimised requires model {VerifiedHardwareModels.MacBookPro16_1}.");
        }

        if (verificationResult.Status != HardwareVerificationStatus.Verified
            || !verificationResult.IsVerified)
        {
            return ProfileExecutionResolution.NotExecutable(
                "Gaming Optimised requires hardware verification status Verified.");
        }

        var settings = new ProcessorPowerSettings(
            profile.PowerTarget.ProcessorMaximumAc!.Value,
            profile.PowerTarget.ProcessorMaximumDc!.Value,
            profile.PowerTarget.BoostModeAc!.Value,
            profile.PowerTarget.BoostModeDc!.Value);

        if (settings != GamingOptimisedSettings)
        {
            return ProfileExecutionResolution.NotExecutable(
                "Gaming Optimised metadata does not match the verified 95/95/0/0 power target.");
        }

        return ProfileExecutionResolution.Executable(settings);
    }

    private static string? ValidateNormalApplyMetadata(PerformanceProfile profile)
    {
        return profile.PowerTarget.UnspecifiedValueSource switch
        {
            ProfileUnspecifiedValueSource.None => ValidateCompleteProcessorTarget(profile),
            ProfileUnspecifiedValueSource.ConfigurablePlaceholder =>
                "Profile execution is not available for configurable placeholder processor targets.",
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
