using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.Profiles;
using BootCampPerformanceControl.UI;

namespace BootCampPerformanceControl.Tests.Profiles;

public sealed class ProfileCatalogTests
{
    [Fact]
    public void SupportedIntelMac_ReturnsExactlyGamingOptimisedAndRestoreProfiles()
    {
        var verification = new ModelVerificationResult(
            "Apple Inc.",
            VerifiedHardwareModels.MacBookPro16_1,
            PlatformSupportStatus.SupportedIntelMac,
            ModelValidationLevel.PerformanceValidated,
            "Verified.");

        var profiles = new ProfileCatalog().GetProfiles(verification);

        Assert.Equal(2, profiles.Count);

        var gaming = Assert.Single(profiles, profile => profile.Id == "gaming-optimised");
        Assert.Equal("Gaming Optimised", gaming.DisplayName);
        Assert.True(gaming.IsAvailableForDetectedModel);
        Assert.Equal(95U, gaming.PowerTarget.ProcessorMaximumAc);
        Assert.Equal(95U, gaming.PowerTarget.ProcessorMaximumDc);
        Assert.Equal(0U, gaming.PowerTarget.BoostModeAc);
        Assert.Equal(0U, gaming.PowerTarget.BoostModeDc);
        Assert.Equal(ProfileUnspecifiedValueSource.None, gaming.PowerTarget.UnspecifiedValueSource);

        var restore = Assert.Single(profiles, profile => profile.Id == "restore");
        Assert.Equal("Restore Original Settings", restore.DisplayName);
        Assert.True(restore.IsAvailableForDetectedModel);
        Assert.Null(restore.PowerTarget.ProcessorMaximumAc);
        Assert.Null(restore.PowerTarget.ProcessorMaximumDc);
        Assert.Null(restore.PowerTarget.BoostModeAc);
        Assert.Null(restore.PowerTarget.BoostModeDc);
        Assert.Equal(
            ProfileUnspecifiedValueSource.OriginalRestoreSnapshot,
            restore.PowerTarget.UnspecifiedValueSource);

        Assert.DoesNotContain(profiles, profile => profile.Id == "balanced");
        Assert.DoesNotContain(profiles, profile => profile.Id == "full-performance");
    }

    [Fact]
    public void SupportedIntelMacNotIndividuallyTested_ExposesGamingOptimisedAndRestoreProfiles()
    {
        var verification = new ModelVerificationResult(
            "Apple Inc.",
            VerifiedHardwareModels.MacBookPro14_3,
            PlatformSupportStatus.SupportedIntelMac,
            ModelValidationLevel.NotIndividuallyTested,
            "Supported Intel Mac.");

        var profiles = new ProfileCatalog().GetProfiles(verification);

        Assert.Equal(2, profiles.Count);
        Assert.All(profiles, profile => Assert.True(profile.IsAvailableForDetectedModel));
    }

    [Theory]
    [InlineData(PlatformSupportStatus.UnsupportedNonApple)]
    [InlineData(PlatformSupportStatus.UnsupportedNonIntel)]
    [InlineData(PlatformSupportStatus.DetectionIncomplete)]
    public void UnsupportedPlatforms_DoNotClaimProfileAvailability(PlatformSupportStatus platformSupport)
    {
        var verification = new ModelVerificationResult(
            "Manufacturer",
            "Model",
            platformSupport,
            ModelValidationLevel.NotIndividuallyTested,
            "Unsupported platform.");

        var profiles = new ProfileCatalog().GetProfiles(verification);

        Assert.Equal(2, profiles.Count);
        Assert.All(profiles, profile => Assert.False(profile.IsAvailableForDetectedModel));
    }

    [Fact]
    public void GetProfiles_VerifiedMacBookPro16_1_ReportsMaximumSafeRpmWithoutHardcodedValues()
    {
        var verification = new ModelVerificationResult(
            "Apple Inc.",
            VerifiedHardwareModels.MacBookPro16_1,
            PlatformSupportStatus.SupportedIntelMac,
            ModelValidationLevel.PerformanceValidated,
            "Verified.");

        var profiles = new ProfileCatalog().GetProfiles(verification);
        var gaming = Assert.Single(profiles, profile => profile.Id == "gaming-optimised");

        var fanSetting = Assert.Single(gaming.Settings, s => s.Name == "Fans");
        Assert.Equal("Maximum Safe RPM", fanSetting.Value);
        Assert.DoesNotContain("5616", fanSetting.Value);
        Assert.DoesNotContain("5200", fanSetting.Value);

        var displaySetting = Assert.Single(gaming.Settings, s => s.Name == "Display");
        Assert.Equal("Unchanged", displaySetting.Value);
    }

    [Fact]
    public void GetProfiles_VerifiedMacBookPro16_1_RestoreReportsAppleAutoFans()
    {
        var verification = new ModelVerificationResult(
            "Apple Inc.",
            VerifiedHardwareModels.MacBookPro16_1,
            PlatformSupportStatus.SupportedIntelMac,
            ModelValidationLevel.PerformanceValidated,
            "Verified.");

        var profiles = new ProfileCatalog().GetProfiles(verification);
        var restore = Assert.Single(profiles, profile => profile.Id == "restore");

        var fanSetting = Assert.Single(restore.Settings, s => s.Name == "Fans");
        Assert.Equal("Apple Auto fans", fanSetting.Value);

        var powerSetting = Assert.Single(restore.Settings, s => s.Name == "Power settings");
        Assert.Equal("Exact original processor power snapshot", powerSetting.Value);
    }

    [Fact]
    public void GetProfiles_OtherSupportedModel_DoesNotReportFanMaximumSafeRpm()
    {
        var verification = new ModelVerificationResult(
            "Apple Inc.",
            VerifiedHardwareModels.MacBookPro14_3,
            PlatformSupportStatus.SupportedIntelMac,
            ModelValidationLevel.NotIndividuallyTested,
            "Supported 14,3.");

        var profiles = new ProfileCatalog().GetProfiles(verification);
        var gaming = Assert.Single(profiles, profile => profile.Id == "gaming-optimised");

        Assert.DoesNotContain(gaming.Settings, s => s.Name == "Fans");
        Assert.DoesNotContain(gaming.Settings, s => s.Value.Contains("Maximum Safe RPM"));

        var restore = Assert.Single(profiles, profile => profile.Id == "restore");
        Assert.DoesNotContain(restore.Settings, s => s.Name == "Fans");
    }
}
