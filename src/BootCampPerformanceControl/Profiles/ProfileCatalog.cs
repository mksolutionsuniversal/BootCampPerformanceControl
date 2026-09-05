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
                IsAvailableForDetectedModel: true,
                powerTarget,
                [
                    new ProfileSettingMetadata("CPU Maximum AC", "95%"),
                    new ProfileSettingMetadata("CPU Maximum DC", "95%"),
                    new ProfileSettingMetadata("Turbo/Boost AC", "Disabled"),
                    new ProfileSettingMetadata("Turbo/Boost DC", "Disabled"),
                    new ProfileSettingMetadata("Fans", "Maximum Safe RPM when verified T2 SMC family is available; otherwise unchanged"),
                    new ProfileSettingMetadata("Display", "Unchanged")
                ],
                "Applies the global Gaming Optimised processor target (95% maximum state and disabled Turbo/Boost). When the verified T2 SMC fan family is available, Maximum Safe RPM is added using fresh live fan maxima.");
        }

        return new PerformanceProfile(
            "gaming-optimised",
            "Gaming Optimised",
            IsAvailableForDetectedModel: false,
            powerTarget,
            [],
            "Gaming Optimised is available for supported Intel Mac models.");
    }

    private static PerformanceProfile CreateRestoreProfile(bool isAvailableForDetectedModel)
    {
        if (isAvailableForDetectedModel)
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
                    new ProfileSettingMetadata("Fans", "Apple Auto when BCPC fan ownership exists")
                ],
                "Restore resolves the original processor power snapshot and first restores BCPC-owned fans to Apple Auto when fan recovery context exists.");
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
