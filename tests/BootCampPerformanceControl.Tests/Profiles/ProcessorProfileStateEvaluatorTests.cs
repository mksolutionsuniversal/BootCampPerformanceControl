using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.Profiles;

namespace BootCampPerformanceControl.Tests.Profiles;

public sealed class ProcessorProfileStateEvaluatorTests
{
    [Fact]
    public void Evaluate_VerifiedMacBookPro16_1WithExactGamingValues_ReturnsGamingOptimisedDetected()
    {
        var evaluator = CreateEvaluator(new ProfileCatalog());

        var result = evaluator.Evaluate(GamingPowerState(), VerifiedMacBookPro16_1());

        Assert.Equal(ProcessorProfileState.GamingOptimisedDetected, result);
    }

    [Fact]
    public void Evaluate_VerifiedMacBookPro16_1WithDifferingValue_ReturnsOther()
    {
        var evaluator = CreateEvaluator(new ProfileCatalog());

        var result = evaluator.Evaluate(
            GamingPowerState() with { ProcessorMaximumDc = 94 },
            VerifiedMacBookPro16_1());

        Assert.Equal(ProcessorProfileState.Other, result);
    }

    [Fact]
    public void Evaluate_UnverifiedAppleModelWithMatchingValues_ReturnsOther()
    {
        var evaluator = CreateEvaluator(new ProfileCatalog());

        var result = evaluator.Evaluate(GamingPowerState(), UnverifiedAppleModel());

        Assert.Equal(ProcessorProfileState.Other, result);
    }

    [Fact]
    public void Evaluate_NonAppleHardwareWithMatchingValues_ReturnsOther()
    {
        var evaluator = CreateEvaluator(new ProfileCatalog());

        var result = evaluator.Evaluate(GamingPowerState(), NonAppleHardware());

        Assert.Equal(ProcessorProfileState.Other, result);
    }

    [Fact]
    public void Evaluate_NullPowerState_ReturnsUnknown()
    {
        var evaluator = CreateEvaluator(new ProfileCatalog());

        var result = evaluator.Evaluate(powerState: null, VerifiedMacBookPro16_1());

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

        var result = evaluator.Evaluate(GamingPowerState(), VerifiedMacBookPro16_1());

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

        var result = evaluator.Evaluate(GamingPowerState(), VerifiedMacBookPro16_1());

        Assert.Equal(ProcessorProfileState.Other, result);
    }

    [Fact]
    public void Evaluate_AvailableProfileWithWrongTargetModel_ReturnsOther()
    {
        var evaluator = CreateEvaluator(
            new SingleProfileCatalog(
                new PerformanceProfile(
                    "gaming-optimised",
                    "Gaming Optimised",
                    ProfileScope.VerifiedModelSpecific,
                    "MacBookPro15,1",
                    IsAvailableForDetectedModel: true,
                    new ProcessorPowerProfileTarget(
                        ProcessorMaximumAc: 95,
                        ProcessorMaximumDc: 95,
                        BoostModeAc: 0,
                        BoostModeDc: 0,
                        ProfileUnspecifiedValueSource.None),
                    [],
                    "Wrong target model.")));

        var result = evaluator.Evaluate(GamingPowerState(), VerifiedMacBookPro16_1());

        Assert.Equal(ProcessorProfileState.Other, result);
    }

    [Fact]
    public void Evaluate_ApparentlyVerifiedResultWithInvalidManufacturer_ReturnsOther()
    {
        var evaluator = CreateEvaluator(
            new SingleProfileCatalog(
                CreateGamingProfile(
                    isAvailableForDetectedModel: true,
                    new ProcessorPowerProfileTarget(
                        ProcessorMaximumAc: 95,
                        ProcessorMaximumDc: 95,
                        BoostModeAc: 0,
                        BoostModeDc: 0,
                        ProfileUnspecifiedValueSource.None))));

        var result = evaluator.Evaluate(GamingPowerState(), InvalidManufacturerVerifiedResult());

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
            ProfileScope.VerifiedModelSpecific,
            VerifiedHardwareModels.MacBookPro16_1,
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

    private static ModelVerificationResult UnverifiedAppleModel()
    {
        return new ModelVerificationResult(
            "Apple Inc.",
            "MacBookPro15,1",
            IsApple: true,
            IsVerified: false,
            HardwareVerificationStatus.UnverifiedAppleModel,
            "Unverified.");
    }

    private static ModelVerificationResult NonAppleHardware()
    {
        return new ModelVerificationResult(
            "PC Manufacturer",
            "PC Model",
            IsApple: false,
            IsVerified: false,
            HardwareVerificationStatus.NonAppleHardware,
            "Not Apple.");
    }

    private static ModelVerificationResult InvalidManufacturerVerifiedResult()
    {
        return new ModelVerificationResult(
            "Apple Computer, Inc.",
            VerifiedHardwareModels.MacBookPro16_1,
            IsApple: true,
            IsVerified: true,
            HardwareVerificationStatus.Verified,
            "Apparently verified but invalid manufacturer.");
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
