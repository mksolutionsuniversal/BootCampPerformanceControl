using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.Profiles;

namespace BootCampPerformanceControl.Tests.Profiles;

public sealed class ProfileExecutionResolverTests
{
    private readonly ProfileExecutionResolver _resolver = new();

    [Theory]
    [InlineData(VerifiedHardwareModels.MacBookPro16_1, ModelValidationLevel.PerformanceValidated)]
    [InlineData(VerifiedHardwareModels.MacBookPro14_3, ModelValidationLevel.NotIndividuallyTested)]
    [InlineData("MacBookPro15,1", ModelValidationLevel.NotIndividuallyTested)]
    [InlineData("MacBookPro11,5", ModelValidationLevel.CommunityTested)]
    [InlineData("MacBookPro15,2", ModelValidationLevel.FunctionallyValidated)]
    public void ResolveProcessorSettings_GamingOptimised_IsExecutableOnAnySupportedIntelMacRegardlessOfValidationLevel(
        string model,
        ModelValidationLevel validationLevel)
    {
        var verification = new ModelVerificationResult(
            "Apple Inc.",
            model,
            PlatformSupportStatus.SupportedIntelMac,
            validationLevel,
            "Supported.");
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
    public void ResolveProcessorSettings_GamingOptimisedWithUnsupportedNonApple_IsNotExecutable()
    {
        var verification = new ModelVerificationResult(
            "PC Manufacturer",
            "PC Model",
            PlatformSupportStatus.UnsupportedNonApple,
            ModelValidationLevel.NotIndividuallyTested,
            "Not Apple hardware.");
        var profile = GetCatalogProfile("gaming-optimised", verification);

        var result = _resolver.ResolveProcessorSettings(profile, verification);

        Assert.False(result.IsExecutable);
        Assert.Null(result.Settings);
        Assert.Contains("Apple hardware", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveProcessorSettings_GamingOptimisedWithUnsupportedNonIntel_IsNotExecutable()
    {
        var verification = new ModelVerificationResult(
            "Apple Inc.",
            "MacBookPro18,1",
            PlatformSupportStatus.UnsupportedNonIntel,
            ModelValidationLevel.NotIndividuallyTested,
            "Apple Silicon.");
        var profile = GetCatalogProfile("gaming-optimised", verification);

        var result = _resolver.ResolveProcessorSettings(profile, verification);

        Assert.False(result.IsExecutable);
        Assert.Null(result.Settings);
        Assert.Contains("Intel processor", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveProcessorSettings_GamingOptimisedWithDetectionIncomplete_IsNotExecutable()
    {
        var verification = new ModelVerificationResult(
            "Unknown",
            "Unknown",
            PlatformSupportStatus.DetectionIncomplete,
            ModelValidationLevel.NotIndividuallyTested,
            "Detection incomplete.");
        var profile = GetCatalogProfile("gaming-optimised", verification);

        var result = _resolver.ResolveProcessorSettings(profile, verification);

        Assert.False(result.IsExecutable);
        Assert.Null(result.Settings);
        Assert.Contains("detection", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveProcessorSettings_Restore_IsNotExecutableThroughProfileResolver()
    {
        var verification = SupportedMacBookPro16_1();
        var profile = GetCatalogProfile("restore", verification);

        var result = _resolver.ResolveProcessorSettings(profile, verification);

        Assert.False(result.IsExecutable);
        Assert.Null(result.Settings);
        Assert.Contains("Restore is not resolved", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("balanced")]
    [InlineData("full-performance")]
    [InlineData("custom-profile")]
    public void ResolveProcessorSettings_RemovedOrUnknownProfiles_AreNotExecutable(string profileId)
    {
        var verification = SupportedMacBookPro16_1();
        var profile = new PerformanceProfile(
            profileId,
            profileId,
            ProfileScope.Generic,
            TargetModel: null,
            IsAvailableForDetectedModel: true,
            new ProcessorPowerProfileTarget(95, 95, 0, 0, ProfileUnspecifiedValueSource.None),
            [],
            "Removed profile.");

        var result = _resolver.ResolveProcessorSettings(profile, verification);

        Assert.False(result.IsExecutable);
        Assert.Null(result.Settings);
        Assert.Contains("not supported", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveProcessorSettings_MalformedTargetValues_IsRejectedFailClosed()
    {
        var verification = SupportedMacBookPro16_1();
        var profile = new PerformanceProfile(
            "gaming-optimised",
            "Gaming Optimised",
            ProfileScope.Generic,
            TargetModel: null,
            IsAvailableForDetectedModel: true,
            new ProcessorPowerProfileTarget(
                ProcessorMaximumAc: 99,
                ProcessorMaximumDc: 95,
                BoostModeAc: 0,
                BoostModeDc: 0,
                ProfileUnspecifiedValueSource.None),
            [],
            "Malformed profile metadata.");

        var result = _resolver.ResolveProcessorSettings(profile, verification);

        Assert.False(result.IsExecutable);
        Assert.Null(result.Settings);
        Assert.Contains("does not match", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveProcessorSettings_InvalidIncompleteProcessorTarget_IsRejectedFailClosed()
    {
        var verification = SupportedMacBookPro16_1();
        var profile = new PerformanceProfile(
            "gaming-optimised",
            "Gaming Optimised",
            ProfileScope.Generic,
            TargetModel: null,
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

    private static ModelVerificationResult SupportedMacBookPro16_1()
    {
        return new ModelVerificationResult(
            "Apple Inc.",
            VerifiedHardwareModels.MacBookPro16_1,
            PlatformSupportStatus.SupportedIntelMac,
            ModelValidationLevel.PerformanceValidated,
            "Verified.");
    }
}
