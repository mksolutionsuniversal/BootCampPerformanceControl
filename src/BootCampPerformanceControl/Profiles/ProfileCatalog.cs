using BootCampPerformanceControl.HardwareDetection;

namespace BootCampPerformanceControl.Profiles;

public sealed class ProfileCatalog : IProfileCatalog
{
    public IReadOnlyList<PerformanceProfile> GetProfiles(ModelVerificationResult verificationResult)
    {
        ArgumentNullException.ThrowIfNull(verificationResult);

        var isSupported = verificationResult.PlatformSupport == PlatformSupportStatus.SupportedIntelMac;
        var isVerifiedMbp16_1 = isSupported
            && string.Equals(verificationResult.Model, VerifiedHardwareModels.MacBookPro16_1, StringComparison.Ordinal)
            && verificationResult.ValidationLevel == ModelValidationLevel.PerformanceValidated;

        return
        [
            CreateGamingOptimisedProfile(isSupported, isVerifiedMbp16_1),
            CreateRestoreProfile(isSupported, isVerifiedMbp16_1)
        ];
    }

    private static PerformanceProfile CreateGamingOptimisedProfile(bool isSupported, bool isVerifiedMbp16_1)
    {
        var powerTarget = new ProcessorPowerProfileTarget(
            ProcessorMaximumAc: 95,
            ProcessorMaximumDc: 95,
            BoostModeAc: 0,
            BoostModeDc: 0,
            ProfileUnspecifiedValueSource.None);

        if (isVerifiedMbp16_1)
        {
            return new PerformanceProfile(
                "gaming-optimised",
                "Gaming Optimised",
                IsAvailableForDetectedModel: true,
                powerTarget,
                [
                    new ProfileSettingMetadata("CPU Maximum AC", "95%"),
                    new ProfileSettingMetadata("CPU Maximum DC", "95%"),
                    new ProfileSettingMetadata("Turbo/Boost AC", "Disabled"),
                    new ProfileSettingMetadata("Turbo/Boost DC", "Disabled"),
                    new ProfileSettingMetadata("Fans", "Maximum Safe RPM"),
                    new ProfileSettingMetadata("Display", "Unchanged")
                ],
                "Optimises Windows processor power settings and fan control for gaming: caps maximum processor state to 95%, disables Turbo/Boost, sets fans to Maximum Safe RPM (dynamically derived from live F0Mx/F1Mx limits), and leaves display unchanged.");
        }

        if (isSupported)
        {
            return new PerformanceProfile(
                "gaming-optimised",
                "Gaming Optimised",
                IsAvailableForDetectedModel: true,
                powerTarget,
                [
                    new ProfileSettingMetadata("CPU Maximum AC", "95%"),
                    new ProfileSettingMetadata("CPU Maximum DC", "95%"),
                    new ProfileSettingMetadata("Boost Mode AC", "0 (Disabled)"),
                    new ProfileSettingMetadata("Boost Mode DC", "0 (Disabled)")
                ],
                "Optimises Windows processor power settings for gaming by capping maximum processor state to 95% and disabling CPU Boost.");
        }

        return new PerformanceProfile(
            "gaming-optimised",
            "Gaming Optimised",
            IsAvailableForDetectedModel: false,
            powerTarget,
            [],
            "Gaming Optimised is available for supported Intel Mac models.");
    }

    private static PerformanceProfile CreateRestoreProfile(bool isAvailableForDetectedModel, bool isVerifiedMbp16_1)
    {
        if (isVerifiedMbp16_1)
        {
            return new PerformanceProfile(
                "restore",
                "Restore Original Settings",
                isAvailableForDetectedModel,
                new ProcessorPowerProfileTarget(
                    ProcessorMaximumAc: null,
                    ProcessorMaximumDc: null,
                    BoostModeAc: null,
                    BoostModeDc: null,
                    ProfileUnspecifiedValueSource.OriginalRestoreSnapshot),
                [
                    new ProfileSettingMetadata("Power settings", "Exact original processor power snapshot"),
                    new ProfileSettingMetadata("Fans", "Apple Auto fans")
                ],
                "Restore resolves the original processor power snapshot and restores fans to Apple Auto control.");
        }

        return new PerformanceProfile(
            "restore",
            "Restore Original Settings",
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
