using BootCampPerformanceControl.HardwareDetection;

namespace BootCampPerformanceControl.Tests.HardwareDetection;

public sealed class HardwareDetectionServiceTests
{
    [Fact]
    public void VerifyModel_AppleMacBookPro16_1_ReturnsVerified()
    {
        var service = CreateService();

        var result = service.VerifyModel(Snapshot("Apple Inc.", VerifiedHardwareModels.MacBookPro16_1));

        Assert.Equal("Apple Inc.", result.Manufacturer);
        Assert.Equal(VerifiedHardwareModels.MacBookPro16_1, result.Model);
        Assert.True(result.IsApple);
        Assert.True(result.IsVerified);
        Assert.Equal(HardwareVerificationStatus.Verified, result.Status);
        Assert.Equal("This Mac model is verified for this milestone.", result.Message);
    }

    [Fact]
    public void VerifyModel_AppleUnknownModel_ReturnsUnverifiedAppleModel()
    {
        var service = CreateService();

        var result = service.VerifyModel(Snapshot("Apple Inc.", "MacBookPro15,1"));

        Assert.True(result.IsApple);
        Assert.False(result.IsVerified);
        Assert.Equal(HardwareVerificationStatus.UnverifiedAppleModel, result.Status);
        Assert.Equal("This Apple model is not verified. Model-specific settings are unavailable.", result.Message);
    }

    [Fact]
    public void VerifyModel_NonAppleHardware_ReturnsNonAppleHardware()
    {
        var service = CreateService();

        var result = service.VerifyModel(Snapshot("PC Manufacturer", VerifiedHardwareModels.MacBookPro16_1));

        Assert.False(result.IsApple);
        Assert.False(result.IsVerified);
        Assert.Equal(HardwareVerificationStatus.NonAppleHardware, result.Status);
        Assert.Equal(
            "This milestone is intended for Intel Macs running Windows through Boot Camp.",
            result.Message);
    }

    [Fact]
    public void VerifyModel_DefinitionWithoutProcessorPowerVerification_DoesNotReturnVerified()
    {
        var service = new HardwareDetectionService(
            new FakeModelSupportRegistry(
                new ModelSupportDefinition(
                    VerifiedHardwareModels.MacBookPro16_1,
                    ProcessorPowerControlVerified: false)));

        var result = service.VerifyModel(Snapshot("Apple Inc.", VerifiedHardwareModels.MacBookPro16_1));

        Assert.True(result.IsApple);
        Assert.False(result.IsVerified);
        Assert.Equal(HardwareVerificationStatus.UnverifiedAppleModel, result.Status);
    }

    private static HardwareDetectionService CreateService()
    {
        return new HardwareDetectionService(new ModelSupportRegistry());
    }

    private static HardwareSnapshot Snapshot(string manufacturer, string model)
    {
        return new HardwareSnapshot(
            new ComputerSystemInfo(manufacturer, model, "x64-based PC"),
            Processor: null,
            VideoControllers: [],
            OperatingSystem: null,
            DateTimeOffset.Parse("2026-01-01T00:00:00+00:00"));
    }

    private sealed class FakeModelSupportRegistry : IModelSupportRegistry
    {
        private readonly ModelSupportDefinition _definition;

        public FakeModelSupportRegistry(ModelSupportDefinition definition)
        {
            _definition = definition;
        }

        public ModelSupportDefinition? Find(string? modelIdentifier)
        {
            return string.Equals(
                modelIdentifier,
                _definition.ModelIdentifier,
                StringComparison.OrdinalIgnoreCase)
                    ? _definition
                    : null;
        }
    }
}
