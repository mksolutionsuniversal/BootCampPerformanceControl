using BootCampPerformanceControl.HardwareDetection;

namespace BootCampPerformanceControl.Profiles;

public sealed class ProfileCatalog : IProfileCatalog
{
    public IReadOnlyList<PerformanceProfile> GetProfiles(ModelVerificationResult verificationResult)
    {
        var genericProfilesAvailable = verificationResult.IsApple
            && verificationResult.IsVerified
            && verificationResult.Status == HardwareVerificationStatus.Verified;

        return
        [
            CreateGamingOptimisedProfile(verificationResult),
            CreateBalancedProfile(genericProfilesAvailable),
            CreateFullPerformanceProfile(genericProfilesAvailable),
            CreateRestoreProfile(genericProfilesAvailable)
        ];
    }

    private static PerformanceProfile CreateGamingOptimisedProfile(ModelVerificationResult verificationResult)
    {
        var powerTarget = new ProcessorPowerProfileTarget(
            ProcessorMaximumAc: 95,
            ProcessorMaximumDc: 95,
            BoostModeAc: 0,
            BoostModeDc: 0,
            ProfileUnspecifiedValueSource.None);

        if (verificationResult.IsApple
            && verificationResult.IsVerified
            && verificationResult.Status == HardwareVerificationStatus.Verified
            && string.Equals(verificationResult.Model, VerifiedHardwareModels.MacBookPro16_1, StringComparison.OrdinalIgnoreCase))
        {
            return new PerformanceProfile(
                "gaming-optimised",
                "Gaming Optimised",
                ProfileScope.VerifiedModelSpecific,
                VerifiedHardwareModels.MacBookPro16_1,
                IsAvailableForDetectedModel: true,
                powerTarget,
                [
                    new ProfileSettingMetadata("CPU Maximum AC", "95%"),
                    new ProfileSettingMetadata("CPU Maximum DC", "95%"),
                    new ProfileSettingMetadata("Boost Mode AC", "0 (Disabled)"),
                    new ProfileSettingMetadata("Boost Mode DC", "0 (Disabled)")
                ],
                $"Verified {VerifiedHardwareModels.MacBookPro16_1} gaming power target. It is not connected to the UI.");
        }

        return new PerformanceProfile(
            "gaming-optimised",
            "Gaming Optimised",
            ProfileScope.VerifiedModelSpecific,
            VerifiedHardwareModels.MacBookPro16_1,
            IsAvailableForDetectedModel: false,
            powerTarget,
            [],
            "Model-specific gaming metadata is unavailable until the detected model is verified.");
    }

    private static PerformanceProfile CreateBalancedProfile(bool isAvailableForDetectedModel)
    {
        return new PerformanceProfile(
            "balanced",
            "Balanced",
            ProfileScope.Generic,
            TargetModel: null,
            isAvailableForDetectedModel,
            new ProcessorPowerProfileTarget(
                ProcessorMaximumAc: null,
                ProcessorMaximumDc: null,
                BoostModeAc: null,
                BoostModeDc: null,
                ProfileUnspecifiedValueSource.ConfigurablePlaceholder),
            [new ProfileSettingMetadata("Processor settings", "Configurable placeholder")],
            "Balanced remains an intentionally unconfigured metadata placeholder.");
    }

    private static PerformanceProfile CreateFullPerformanceProfile(bool isAvailableForDetectedModel)
    {
        return new PerformanceProfile(
            "full-performance",
            "Full Performance",
            ProfileScope.Generic,
            TargetModel: null,
            isAvailableForDetectedModel,
            new ProcessorPowerProfileTarget(
                ProcessorMaximumAc: 100,
                ProcessorMaximumDc: 100,
                BoostModeAc: null,
                BoostModeDc: null,
                ProfileUnspecifiedValueSource.OriginalRestoreSnapshot),
            [
                new ProfileSettingMetadata("CPU Maximum AC", "100%"),
                new ProfileSettingMetadata("CPU Maximum DC", "100%"),
                new ProfileSettingMetadata("Boost Mode AC", "Original snapshot value"),
                new ProfileSettingMetadata("Boost Mode DC", "Original snapshot value")
            ],
            "Full Performance keeps boost behaviour derived from the saved original state; no boost value is invented.");
    }

    private static PerformanceProfile CreateRestoreProfile(bool isAvailableForDetectedModel)
    {
        return new PerformanceProfile(
            "restore",
            "Restore",
            ProfileScope.Generic,
            TargetModel: null,
            isAvailableForDetectedModel,
            new ProcessorPowerProfileTarget(
                ProcessorMaximumAc: null,
                ProcessorMaximumDc: null,
                BoostModeAc: null,
                BoostModeDc: null,
                ProfileUnspecifiedValueSource.OriginalRestoreSnapshot),
            [new ProfileSettingMetadata("Power settings", "Exact original restore snapshot")],
            "Restore resolves the original scheme and all processor values from the saved snapshot.");
    }
}
