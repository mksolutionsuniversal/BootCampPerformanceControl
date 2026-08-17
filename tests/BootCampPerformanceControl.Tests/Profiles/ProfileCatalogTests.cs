using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.Profiles;
using BootCampPerformanceControl.UI;

namespace BootCampPerformanceControl.Tests.Profiles;

public sealed class ProfileCatalogTests
{
    [Fact]
    public void VerifiedModel_HasConservativeTypedTargetsAndExpectedButtonAvailability()
    {
        var verification = new ModelVerificationResult(
            "Apple Inc.",
            VerifiedHardwareModels.MacBookPro16_1,
            IsApple: true,
            IsVerified: true,
            HardwareVerificationStatus.Verified,
            "Verified.");

        var profiles = new ProfileCatalog().GetProfiles(verification);

        var gaming = Assert.Single(profiles, profile => profile.Id == "gaming-optimised");
        Assert.Equal(95U, gaming.PowerTarget.ProcessorMaximumAc);
        Assert.Equal(95U, gaming.PowerTarget.ProcessorMaximumDc);
        Assert.Equal(0U, gaming.PowerTarget.BoostModeAc);
        Assert.Equal(0U, gaming.PowerTarget.BoostModeDc);

        var balanced = Assert.Single(profiles, profile => profile.Id == "balanced");
        Assert.Equal(
            ProfileUnspecifiedValueSource.ConfigurablePlaceholder,
            balanced.PowerTarget.UnspecifiedValueSource);

        var fullPerformance = Assert.Single(profiles, profile => profile.Id == "full-performance");
        Assert.Equal(100U, fullPerformance.PowerTarget.ProcessorMaximumAc);
        Assert.Equal(100U, fullPerformance.PowerTarget.ProcessorMaximumDc);
        Assert.Null(fullPerformance.PowerTarget.BoostModeAc);
        Assert.Null(fullPerformance.PowerTarget.BoostModeDc);
        Assert.Equal(
            ProfileUnspecifiedValueSource.OriginalRestoreSnapshot,
            fullPerformance.PowerTarget.UnspecifiedValueSource);

        var buttons = profiles
            .Select(profile => new ProfileButtonViewModel(
                profile,
                new AsyncCommand(_ => Task.CompletedTask),
                isRestoreSnapshotAvailable: false))
            .ToList();

        Assert.True(Assert.Single(buttons, button => button.ProfileId == "gaming-optimised").IsEnabled);
        Assert.All(
            buttons.Where(button => button.ProfileId != "gaming-optimised"),
            button => Assert.False(button.IsEnabled));
    }

    [Fact]
    public void NonAppleHardware_DoesNotClaimAnyProfileAvailability()
    {
        var verification = new ModelVerificationResult(
            "PC Manufacturer",
            "PC Model",
            IsApple: false,
            IsVerified: false,
            HardwareVerificationStatus.NonAppleHardware,
            "Not Apple hardware.");

        var profiles = new ProfileCatalog().GetProfiles(verification);

        Assert.All(profiles, profile => Assert.False(profile.IsAvailableForDetectedModel));
    }
}
