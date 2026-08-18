using BootCampPerformanceControl.HardwareDetection;

namespace BootCampPerformanceControl.Profiles;

public sealed class ProfileCatalog : IProfileCatalog
{
    public IReadOnlyList<PerformanceProfile> GetProfiles(ModelVerificationResult verificationResult)
    {
        ArgumentNullException.ThrowIfNull(verificationResult);

        var isSupported = verificationResult.PlatformSupport == PlatformSupportStatus.SupportedIntelMac;

        return
        [
            CreateGamingOptimisedProfile(isSupported),
            CreateRestoreProfile(isSupported)
        ];
    }

    private static PerformanceProfile CreateGamingOptimisedProfile(bool isSupported)
    {
        var powerTarget = new ProcessorPowerProfileTarget(
            ProcessorMaximumAc: 95,
            ProcessorMaximumDc: 95,
            BoostModeAc: 0,
            BoostModeDc: 0,
            ProfileUnspecifiedValueSource.None);

        if (isSupported)
        {
            return new PerformanceProfile(
                "gaming-optimised",
                "Gaming Optimised",
                ProfileScope.Generic,
                TargetModel: null,
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
            ProfileScope.Generic,
            TargetModel: null,
            IsAvailableForDetectedModel: false,
            powerTarget,
            [],
            "Gaming Optimised is available for supported Intel Mac models.");
    }

    private static PerformanceProfile CreateRestoreProfile(bool isAvailableForDetectedModel)
    {
        return new PerformanceProfile(
            "restore",
            "Restore Original Settings",
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
