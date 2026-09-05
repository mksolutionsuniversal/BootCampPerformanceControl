using System.Buffers.Binary;
using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.Profiles;

namespace BootCampPerformanceControl.Tests.Profiles;

public sealed class FanProfileExecutionResolverTests
{
    private readonly FanProfileExecutionResolver _resolver = new();

    [Fact]
    public void ResolveMaximumSafeRpmPlan_GamingOptimisedOnVerifiedAutoCapability_ReturnsLiveMaximumPlan()
    {
        const float fan0Maximum = 5321.25f;
        const float fan1Maximum = 4789.5f;

        var result = _resolver.ResolveMaximumSafeRpmPlan(
            GamingOptimisedProfile(),
            PerformanceValidatedMacBookPro16_1(),
            SafetyGatedCapability(fan0Maximum, fan1Maximum));

        Assert.True(result.IsExecutable);
        Assert.NotNull(result.Plan);
        Assert.Equal(VerifiedHardwareModels.MacBookPro16_1, result.Plan!.Model);
        Assert.Equal(fan0Maximum, result.Plan.Targets[0].TargetRpm);
        Assert.Equal(fan1Maximum, result.Plan.Targets[1].TargetRpm);
        Assert.Equal(string.Empty, result.FailureReason);
    }

    [Fact]
    public void ResolveMaximumSafeRpmPlan_WrongProfileId_IsNotExecutable()
    {
        var result = _resolver.ResolveMaximumSafeRpmPlan(
            Profile("balanced"),
            PerformanceValidatedMacBookPro16_1(),
            SafetyGatedCapability());

        AssertBlocked(result);
    }

    [Fact]
    public void ResolveMaximumSafeRpmPlan_Restore_IsNotExecutable()
    {
        var result = _resolver.ResolveMaximumSafeRpmPlan(
            Profile("restore"),
            PerformanceValidatedMacBookPro16_1(),
            SafetyGatedCapability());

        AssertBlocked(result);
    }

    [Fact]
    public void ResolveMaximumSafeRpmPlan_DifferentSupportedMacModel_IsExecutable()
    {
        var result = _resolver.ResolveMaximumSafeRpmPlan(
            GamingOptimisedProfile(),
            new ModelVerificationResult(
                "Apple Inc.",
                VerifiedHardwareModels.MacBookPro14_3,
                PlatformSupportStatus.SupportedIntelMac,
                ModelValidationLevel.PerformanceValidated,
                "Supported Intel Mac."),
            SafetyGatedCapability());

        Assert.True(result.IsExecutable);
        Assert.Equal(2, result.Plan?.Targets.Count);
    }

    [Fact]
    public void ResolveMaximumSafeRpmPlan_UnknownModel_IsNotExecutable()
    {
        var result = _resolver.ResolveMaximumSafeRpmPlan(
            GamingOptimisedProfile(),
            ModelVerificationResult.Unknown(),
            SafetyGatedCapability());

        AssertBlocked(result);
    }

    [Fact]
    public void ResolveMaximumSafeRpmPlan_NotIndividuallyTestedSupportedModel_IsExecutable()
    {
        var result = _resolver.ResolveMaximumSafeRpmPlan(
            GamingOptimisedProfile(),
            new ModelVerificationResult(
                "Apple Inc.",
                VerifiedHardwareModels.MacBookPro16_1,
                PlatformSupportStatus.SupportedIntelMac,
                ModelValidationLevel.NotIndividuallyTested,
                "Supported Intel Mac."),
            SafetyGatedCapability());

        Assert.True(result.IsExecutable);
        Assert.Equal(2, result.Plan?.Targets.Count);
    }

    [Fact]
    public void ResolveMaximumSafeRpmPlan_OneFanUsesFreshLiveMaximum()
    {
        const float maximum = 2900f;
        var snapshot = new FanSmcSnapshot(
            UInt8("FNum", 1, 0x80),
            [
                new FanSmcChannelSnapshot(
                    new FanIndex(0),
                    Float32("F0Mx", maximum, 0x85),
                    Float32("F0Ac", 1200f, 0x84),
                    UInt8("F0Md", 0, 0xD0),
                    Float32("F0Tg", 1200f, 0xD4))
            ]);
        var capability = new FanSafetyPolicy().Evaluate(
            "Macmini8,1",
            SmcTransportProtocol.Mmio,
            snapshot);

        var result = _resolver.ResolveMaximumSafeRpmPlan(
            GamingOptimisedProfile(),
            new ModelVerificationResult(
                "Apple Inc.",
                "Macmini8,1",
                PlatformSupportStatus.SupportedIntelMac,
                ModelValidationLevel.NotIndividuallyTested,
                "Supported Intel Mac."),
            capability);

        Assert.True(result.IsExecutable);
        var target = Assert.Single(result.Plan!.Targets);
        Assert.Equal(0, target.Index.Value);
        Assert.Equal(maximum, target.TargetRpm);
    }

    [Fact]
    public void ResolveMaximumSafeRpmPlan_ProfileNotAvailable_IsNotExecutable()
    {
        var result = _resolver.ResolveMaximumSafeRpmPlan(
            GamingOptimisedProfile(isAvailableForDetectedModel: false),
            PerformanceValidatedMacBookPro16_1(),
            SafetyGatedCapability());

        AssertBlocked(result);
    }

    [Fact]
    public void ResolveMaximumSafeRpmPlan_GamingOptimisedWithInvalidProcessorMetadata_IsNotExecutable()
    {
        var profile = new PerformanceProfile(
            "gaming-optimised",
            "Gaming Optimised",
            IsAvailableForDetectedModel: true,
            new ProcessorPowerProfileTarget(
                ProcessorMaximumAc: 100,
                ProcessorMaximumDc: 100,
                BoostModeAc: 2,
                BoostModeDc: 2,
                ProfileUnspecifiedValueSource.None),
            [],
            "Corrupted Gaming Optimised metadata.");

        var result = _resolver.ResolveMaximumSafeRpmPlan(
            profile,
            PerformanceValidatedMacBookPro16_1(),
            SafetyGatedCapability());

        AssertBlocked(result);
        Assert.Contains("processor profile", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveMaximumSafeRpmPlan_ReadUnsupportedCapability_IsNotExecutable()
    {
        var result = _resolver.ResolveMaximumSafeRpmPlan(
            GamingOptimisedProfile(),
            PerformanceValidatedMacBookPro16_1(),
            Capability(
                isReadSupported: false,
                isHardwareSafetyGateSatisfied: true,
                snapshot: ValidSnapshot()));

        AssertBlocked(result);
    }

    [Fact]
    public void ResolveMaximumSafeRpmPlan_UnsatisfiedHardwareSafetyGate_IsNotExecutable()
    {
        var result = _resolver.ResolveMaximumSafeRpmPlan(
            GamingOptimisedProfile(),
            PerformanceValidatedMacBookPro16_1(),
            Capability(
                isReadSupported: true,
                isHardwareSafetyGateSatisfied: false,
                snapshot: ValidSnapshot()));

        AssertBlocked(result);
    }

    [Fact]
    public void ResolveMaximumSafeRpmPlan_MissingSnapshot_IsNotExecutable()
    {
        var result = _resolver.ResolveMaximumSafeRpmPlan(
            GamingOptimisedProfile(),
            PerformanceValidatedMacBookPro16_1(),
            Capability(
                isReadSupported: true,
                isHardwareSafetyGateSatisfied: true,
                snapshot: null));

        AssertBlocked(result);
    }

    [Fact]
    public void ResolveMaximumSafeRpmPlan_Fan0AlreadyManual_IsNotExecutable()
    {
        var result = _resolver.ResolveMaximumSafeRpmPlan(
            GamingOptimisedProfile(),
            PerformanceValidatedMacBookPro16_1(),
            SafetyGatedCapability(fan0Mode: 1));

        AssertBlocked(result);
    }

    [Fact]
    public void ResolveMaximumSafeRpmPlan_Fan1AlreadyManual_IsNotExecutable()
    {
        var result = _resolver.ResolveMaximumSafeRpmPlan(
            GamingOptimisedProfile(),
            PerformanceValidatedMacBookPro16_1(),
            SafetyGatedCapability(fan1Mode: 1));

        AssertBlocked(result);
    }

    private static void AssertBlocked(FanProfileExecutionResolution result)
    {
        Assert.False(result.IsExecutable);
        Assert.Null(result.Plan);
        Assert.False(string.IsNullOrWhiteSpace(result.FailureReason));
    }

    private static FanControlCapabilityResult SafetyGatedCapability(
        float fan0Maximum = 5321.25f,
        float fan1Maximum = 4789.5f,
        byte fan0Mode = 0,
        byte fan1Mode = 0)
    {
        return new FanSafetyPolicy().Evaluate(
            VerifiedHardwareModels.MacBookPro16_1,
            SmcTransportProtocol.Mmio,
            ValidSnapshot(fan0Maximum, fan1Maximum, fan0Mode, fan1Mode));
    }

    private static FanControlCapabilityResult Capability(
        bool isReadSupported,
        bool isHardwareSafetyGateSatisfied,
        FanSmcSnapshot? snapshot)
    {
        return new FanControlCapabilityResult(
            isReadSupported,
            isHardwareSafetyGateSatisfied,
            [],
            SmcTransportProtocol.Mmio,
            snapshot);
    }

    private static FanSmcSnapshot ValidSnapshot(
        float fan0Maximum = 5321.25f,
        float fan1Maximum = 4789.5f,
        byte fan0Mode = 0,
        byte fan1Mode = 0)
    {
        return new FanSmcSnapshot(
            UInt8("FNum", 2, 0x80),
            [
                new FanSmcChannelSnapshot(
                    new FanIndex(0),
                    Float32("F0Mx", fan0Maximum, 0x85),
                    Float32("F0Ac", 1800f, 0x84),
                    UInt8("F0Md", fan0Mode, 0xD0),
                    Float32("F0Tg", 1800f, 0xD4)),
                new FanSmcChannelSnapshot(
                    new FanIndex(1),
                    Float32("F1Mx", fan1Maximum, 0x85),
                    Float32("F1Ac", 1700f, 0x84),
                    UInt8("F1Md", fan1Mode, 0xD0),
                    Float32("F1Tg", 1700f, 0xD4))
            ]);
    }

    private static SmcValue Float32(
        string key,
        float value,
        byte attributes)
    {
        Span<byte> rawData = stackalloc byte[sizeof(float)];
        BinaryPrimitives.WriteInt32LittleEndian(
            rawData,
            BitConverter.SingleToInt32Bits(value));

        return new SmcValue(
            new SmcKeyInfo(key, 4, "flt ", attributes),
            rawData);
    }

    private static SmcValue UInt8(
        string key,
        byte value,
        byte attributes)
    {
        return new SmcValue(
            new SmcKeyInfo(key, 1, "ui8 ", attributes),
            [value]);
    }

    private static PerformanceProfile GamingOptimisedProfile(
        bool isAvailableForDetectedModel = true)
    {
        return Profile("gaming-optimised", isAvailableForDetectedModel);
    }

    private static PerformanceProfile Profile(
        string id,
        bool isAvailableForDetectedModel = true)
    {
        return new PerformanceProfile(
            id,
            id,
            isAvailableForDetectedModel,
            new ProcessorPowerProfileTarget(95, 95, 0, 0, ProfileUnspecifiedValueSource.None),
            [],
            "Test profile.");
    }

    private static ModelVerificationResult PerformanceValidatedMacBookPro16_1()
    {
        return new ModelVerificationResult(
            "Apple Inc.",
            VerifiedHardwareModels.MacBookPro16_1,
            PlatformSupportStatus.SupportedIntelMac,
            ModelValidationLevel.PerformanceValidated,
            "Performance validated.");
    }
}
