using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.Profiles;

namespace BootCampPerformanceControl.Tests.Profiles;

public sealed class ProcessorProfileStateEvaluatorTests
{
    [Theory]
    [InlineData(VerifiedHardwareModels.MacBookPro16_1)]
    [InlineData(VerifiedHardwareModels.MacBookPro14_3)]
    [InlineData("MacBookPro99,1")]
    public void Evaluate_SupportedIntelMacWithExactGamingValues_ReturnsGamingOptimisedDetected(string model)
    {
        var evaluator = CreateEvaluator(new ProfileCatalog());
        var verification = new ModelVerificationResult(
            "Apple Inc.",
            model,
            PlatformSupportStatus.SupportedIntelMac,
            "Supported.");

        var result = evaluator.Evaluate(GamingPowerState(), verification);

        Assert.Equal(ProcessorProfileState.GamingOptimisedDetected, result);
    }

    [Fact]
    public void Evaluate_SupportedIntelMacWithDifferingValue_ReturnsOther()
    {
        var evaluator = CreateEvaluator(new ProfileCatalog());
        var verification = SupportedMacBookPro16_1();

        var result = evaluator.Evaluate(
            GamingPowerState() with { ProcessorMaximumDc = 94 },
            verification);

        Assert.Equal(ProcessorProfileState.Other, result);
    }

    [Fact]
    public void Evaluate_UnsupportedNonAppleWithMatchingValues_ReturnsOther()
    {
        var evaluator = CreateEvaluator(new ProfileCatalog());
        var verification = new ModelVerificationResult(
            "PC Manufacturer",
            "PC Model",
            PlatformSupportStatus.UnsupportedNonApple,
            "Not Apple.");

        var result = evaluator.Evaluate(GamingPowerState(), verification);

        Assert.Equal(ProcessorProfileState.Other, result);
    }

    [Fact]
    public void Evaluate_UnsupportedNonIntelWithMatchingValues_ReturnsOther()
    {
        var evaluator = CreateEvaluator(new ProfileCatalog());
        var verification = new ModelVerificationResult(
            "Apple Inc.",
            "MacBookPro18,1",
            PlatformSupportStatus.UnsupportedNonIntel,
            "Non-Intel.");

        var result = evaluator.Evaluate(GamingPowerState(), verification);

        Assert.Equal(ProcessorProfileState.Other, result);
    }

    [Fact]
    public void Evaluate_DetectionIncompleteWithMatchingValues_ReturnsOther()
    {
        var evaluator = CreateEvaluator(new ProfileCatalog());
        var verification = new ModelVerificationResult(
            "Unknown",
            "Unknown",
            PlatformSupportStatus.DetectionIncomplete,
            "Incomplete.");

        var result = evaluator.Evaluate(GamingPowerState(), verification);

        Assert.Equal(ProcessorProfileState.Other, result);
    }

    [Fact]
    public void Evaluate_NullPowerState_ReturnsUnknown()
    {
        var evaluator = CreateEvaluator(new ProfileCatalog());

        var result = evaluator.Evaluate(powerState: null, SupportedMacBookPro16_1());

        Assert.Equal(ProcessorProfileState.Unknown, result);
    }

    [Fact]
    public void Evaluate_InvalidGamingTargetWithMissingValue_ReturnsOther()
    {
        var evaluator = CreateEvaluator(
            new SingleProfileCatalog(
                CreateGamingProfile(
                    isAvailableForDetectedModel: true,
                    new ProcessorPowerProfileTarget(
                        ProcessorMaximumAc: 95,
                        ProcessorMaximumDc: null,
                        BoostModeAc: 0,
                        BoostModeDc: 0,
                        ProfileUnspecifiedValueSource.None))));

        var result = evaluator.Evaluate(GamingPowerState(), SupportedMacBookPro16_1());

        Assert.Equal(ProcessorProfileState.Other, result);
    }

    [Fact]
    public void Evaluate_UnavailableGamingProfileWithMatchingValues_ReturnsOther()
    {
        var evaluator = CreateEvaluator(
            new SingleProfileCatalog(
                CreateGamingProfile(
                    isAvailableForDetectedModel: false,
                    new ProcessorPowerProfileTarget(
                        ProcessorMaximumAc: 95,
                        ProcessorMaximumDc: 95,
                        BoostModeAc: 0,
                        BoostModeDc: 0,
                        ProfileUnspecifiedValueSource.None))));

        var result = evaluator.Evaluate(GamingPowerState(), SupportedMacBookPro16_1());

        Assert.Equal(ProcessorProfileState.Other, result);
    }

    private static ProcessorProfileStateEvaluator CreateEvaluator(IProfileCatalog profileCatalog)
    {
        return new ProcessorProfileStateEvaluator(
            profileCatalog,
            new ProfileExecutionResolver());
    }

    private static PerformanceProfile CreateGamingProfile(
        bool isAvailableForDetectedModel,
        ProcessorPowerProfileTarget target)
    {
        return new PerformanceProfile(
            "gaming-optimised",
            "Gaming Optimised",
            isAvailableForDetectedModel,
            target,
            [],
            "Test profile.");
    }

    private static PowerStateSnapshot GamingPowerState()
    {
        return new PowerStateSnapshot(
            Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"),
            ProcessorMaximumAc: 95,
            ProcessorMaximumDc: 95,
            BoostModeAc: 0,
            BoostModeDc: 0,
            DateTimeOffset.Parse("2026-01-01T00:00:00+00:00"));
    }

    private static ModelVerificationResult SupportedMacBookPro16_1()
    {
        return new ModelVerificationResult(
            "Apple Inc.",
            VerifiedHardwareModels.MacBookPro16_1,
            PlatformSupportStatus.SupportedIntelMac,
            string.Empty);
    }

    private sealed class SingleProfileCatalog : IProfileCatalog
    {
        private readonly PerformanceProfile _profile;

        public SingleProfileCatalog(PerformanceProfile profile)
        {
            _profile = profile;
        }

        public IReadOnlyList<PerformanceProfile> GetProfiles(ModelVerificationResult verificationResult)
        {
            return [_profile];
        }
    }
}
