using BootCampPerformanceControl.HardwareDetection;

namespace BootCampPerformanceControl.Profiles;

public sealed class ProfileCatalog : IProfileCatalog
{
    public IReadOnlyList<PerformanceProfile> GetProfiles(ModelVerificationResult verificationResult)
    {
        var genericProfilesAvailable = verificationResult.Status == HardwareVerificationStatus.Verified;

        return
        [
            CreateGamingOptimisedProfile(verificationResult),
            CreateGenericProfile(
                "balanced",
                "Balanced",
                genericProfilesAvailable,
                "Generic Boot Camp balanced profile metadata. It is not applied in this milestone."),
            CreateGenericProfile(
                "full-performance",
                "Full Performance",
                genericProfilesAvailable,
                "Generic Boot Camp full-performance profile metadata. It is not applied in this milestone."),
            CreateGenericProfile(
                "restore",
                "Restore",
                genericProfilesAvailable,
                "Restore metadata for a future saved snapshot. It is not applied in this milestone.")
        ];
    }

    private static PerformanceProfile CreateGamingOptimisedProfile(ModelVerificationResult verificationResult)
    {
        if (verificationResult.IsVerified
            && string.Equals(verificationResult.Model, VerifiedHardwareModels.MacBookPro16_1, StringComparison.OrdinalIgnoreCase))
        {
            return new PerformanceProfile(
                "gaming-optimised",
                "Gaming Optimised",
                ProfileScope.VerifiedModelSpecific,
                VerifiedHardwareModels.MacBookPro16_1,
                IsAvailableForDetectedModel: true,
                [
                    new ProfileSettingMetadata("CPU Maximum State", "95%"),
                    new ProfileSettingMetadata("Turbo Boost", "Disabled"),
                    new ProfileSettingMetadata("Fans", "Maximum"),
                    new ProfileSettingMetadata("Display", "unchanged")
                ],
                $"Verified {VerifiedHardwareModels.MacBookPro16_1} gaming metadata. It is not applied in this milestone.");
        }

        return new PerformanceProfile(
            "gaming-optimised",
            "Gaming Optimised",
            ProfileScope.VerifiedModelSpecific,
            VerifiedHardwareModels.MacBookPro16_1,
            IsAvailableForDetectedModel: false,
            [],
            "Model-specific gaming metadata is unavailable until the detected model is verified.");
    }

    private static PerformanceProfile CreateGenericProfile(
        string id,
        string displayName,
        bool isAvailableForDetectedModel,
        string description)
    {
        return new PerformanceProfile(
            id,
            displayName,
            ProfileScope.Generic,
            TargetModel: null,
            isAvailableForDetectedModel,
            [],
            description);
    }
}
