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
}
