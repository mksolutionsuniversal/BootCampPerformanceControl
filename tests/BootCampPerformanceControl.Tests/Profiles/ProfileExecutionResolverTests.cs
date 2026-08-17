using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.Profiles;

namespace BootCampPerformanceControl.Tests.Profiles;

public sealed class ProfileExecutionResolverTests
{
    private readonly ProfileExecutionResolver _resolver = new();

    [Fact]
    public void ResolveProcessorSettings_GamingOptimisedWithVerifiedAppleMacBookPro16_1_IsExecutable()
    {
        var verification = VerifiedMacBookPro16_1();
        var profile = GetCatalogProfile("gaming-optimised", verification);

        var result = _resolver.ResolveProcessorSettings(profile, verification);

        Assert.True(result.IsExecutable);
        Assert.Equal(
            new ProcessorPowerSettings(
                ProcessorMaximumAc: 95,
                ProcessorMaximumDc: 95,
                BoostModeAc: 0,
                BoostModeDc: 0),
            result.Settings);
        Assert.Equal(string.Empty, result.FailureReason);
    }

    [Fact]
    public void ResolveProcessorSettings_GamingOptimisedWithDifferentMacModel_IsNotExecutable()
    {
        var verification = new ModelVerificationResult(
            "Apple Inc.",
            "MacBookPro15,1",
            IsApple: true,
            IsVerified: false,
            HardwareVerificationStatus.UnverifiedAppleModel,
            "Different Apple model.");
        var profile = GetCatalogProfile("gaming-optimised", verification);

        var result = _resolver.ResolveProcessorSettings(profile, verification);

        Assert.False(result.IsExecutable);
        Assert.Null(result.Settings);
        Assert.False(string.IsNullOrWhiteSpace(result.FailureReason));
    }

    [Fact]
    public void ResolveProcessorSettings_GamingOptimisedWithUnverifiedMatchingModel_IsNotExecutable()
    {
        var verification = new ModelVerificationResult(
            "Apple Inc.",
            VerifiedHardwareModels.MacBookPro16_1,
            IsApple: true,
            IsVerified: false,
            HardwareVerificationStatus.UnverifiedAppleModel,
            "Matching model string without verification.");
        var profile = GetCatalogProfile("gaming-optimised", verification);

        var result = _resolver.ResolveProcessorSettings(profile, verification);

        Assert.False(result.IsExecutable);
        Assert.Null(result.Settings);
        Assert.Contains("verified", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveProcessorSettings_GamingOptimisedWithExecutableMetadataButUnverifiedHardware_IsNotExecutable()
    {
        var verification = new ModelVerificationResult(
            "Apple Inc.",
            VerifiedHardwareModels.MacBookPro16_1,
            IsApple: true,
            IsVerified: false,
            HardwareVerificationStatus.UnverifiedAppleModel,
            "Matching model string without verification.");
        var profile = new PerformanceProfile(
            "gaming-optimised",
            "Gaming Optimised",
            ProfileScope.VerifiedModelSpecific,
            VerifiedHardwareModels.MacBookPro16_1,
            IsAvailableForDetectedModel: true,
            new ProcessorPowerProfileTarget(
                ProcessorMaximumAc: 95,
                ProcessorMaximumDc: 95,
                BoostModeAc: 0,
                BoostModeDc: 0,
                ProfileUnspecifiedValueSource.None),
            [],
            "Manually constructed executable-looking profile metadata.");

        var result = _resolver.ResolveProcessorSettings(profile, verification);

        Assert.False(result.IsExecutable);
        Assert.Null(result.Settings);
        Assert.Contains("verification", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveProcessorSettings_GamingOptimisedWithNonAppleManufacturer_IsNotExecutable()
    {
        var verification = new ModelVerificationResult(
            "PC Manufacturer",
            VerifiedHardwareModels.MacBookPro16_1,
            IsApple: false,
            IsVerified: false,
            HardwareVerificationStatus.NonAppleHardware,
            "Not Apple hardware.");
        var profile = GetCatalogProfile("gaming-optimised", verification);

        var result = _resolver.ResolveProcessorSettings(profile, verification);

        Assert.False(result.IsExecutable);
        Assert.Null(result.Settings);
        Assert.False(string.IsNullOrWhiteSpace(result.FailureReason));
    }

    [Fact]
    public void ResolveProcessorSettings_Balanced_IsNotExecutableYet()
    {
        var verification = VerifiedMacBookPro16_1();
        var profile = GetCatalogProfile("balanced", verification);

        var result = _resolver.ResolveProcessorSettings(profile, verification);

        Assert.False(result.IsExecutable);
        Assert.Null(result.Settings);
        Assert.Contains("configurable placeholder", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveProcessorSettings_FullPerformance_IsNotExecutableYetAndDoesNotInventBoostValues()
    {
        var verification = VerifiedMacBookPro16_1();
        var profile = GetCatalogProfile("full-performance", verification);

        var result = _resolver.ResolveProcessorSettings(profile, verification);

        Assert.False(result.IsExecutable);
        Assert.Null(profile.PowerTarget.BoostModeAc);
        Assert.Null(profile.PowerTarget.BoostModeDc);
        Assert.Null(result.Settings);
        Assert.Contains("restore snapshot", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveProcessorSettings_Restore_IsNotExecutableThroughProfileResolver()
    {
        var verification = VerifiedMacBookPro16_1();
        var profile = GetCatalogProfile("restore", verification);

        var result = _resolver.ResolveProcessorSettings(profile, verification);

        Assert.False(result.IsExecutable);
        Assert.Null(result.Settings);
        Assert.Contains("Restore is not resolved", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveProcessorSettings_InvalidIncompleteProcessorTarget_IsRejectedFailClosed()
    {
        var verification = VerifiedMacBookPro16_1();
        var profile = new PerformanceProfile(
            "gaming-optimised",
            "Gaming Optimised",
            ProfileScope.VerifiedModelSpecific,
            VerifiedHardwareModels.MacBookPro16_1,
            IsAvailableForDetectedModel: true,
            new ProcessorPowerProfileTarget(
                ProcessorMaximumAc: null,
                ProcessorMaximumDc: 95,
                BoostModeAc: 0,
                BoostModeDc: 0,
                ProfileUnspecifiedValueSource.None),
            [],
            "Invalid incomplete profile metadata.");

        var result = _resolver.ResolveProcessorSettings(profile, verification);

        Assert.False(result.IsExecutable);
        Assert.Null(result.Settings);
        Assert.Contains("incomplete", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    private static PerformanceProfile GetCatalogProfile(
        string profileId,
        ModelVerificationResult verification)
    {
        return Assert.Single(
            new ProfileCatalog().GetProfiles(verification),
            profile => string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase));
    }

    private static ModelVerificationResult VerifiedMacBookPro16_1()
    {
        return new ModelVerificationResult(
            "Apple Inc.",
            VerifiedHardwareModels.MacBookPro16_1,
            IsApple: true,
            IsVerified: true,
            HardwareVerificationStatus.Verified,
            "Verified.");
    }
}
