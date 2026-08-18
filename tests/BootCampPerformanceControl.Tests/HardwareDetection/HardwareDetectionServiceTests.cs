using BootCampPerformanceControl.HardwareDetection;

namespace BootCampPerformanceControl.Tests.HardwareDetection;

public sealed class HardwareDetectionServiceTests
{
    [Fact]
    public void VerifyModel_AppleIntelMacBookPro16_1_ReturnsSupportedIntelMacAndPerformanceValidated()
    {
        var service = CreateService();

        var result = service.VerifyModel(Snapshot("Apple Inc.", VerifiedHardwareModels.MacBookPro16_1, IntelProcessor()));

        Assert.Equal("Apple Inc.", result.Manufacturer);
        Assert.Equal(VerifiedHardwareModels.MacBookPro16_1, result.Model);
        Assert.Equal(PlatformSupportStatus.SupportedIntelMac, result.PlatformSupport);
        Assert.Equal(ModelValidationLevel.PerformanceValidated, result.ValidationLevel);
        Assert.True(result.IsApple);
        Assert.True(result.IsIntelProcessor);
        Assert.True(result.IsSupportedIntelMac);
        Assert.Contains("performance-validated", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifyModel_AppleIntelMacBookPro14_3_ReturnsSupportedIntelMacAndNotIndividuallyTested()
    {
        var service = CreateService();

        var result = service.VerifyModel(Snapshot("Apple Inc.", VerifiedHardwareModels.MacBookPro14_3, IntelProcessor()));

        Assert.Equal("Apple Inc.", result.Manufacturer);
        Assert.Equal(VerifiedHardwareModels.MacBookPro14_3, result.Model);
        Assert.Equal(PlatformSupportStatus.SupportedIntelMac, result.PlatformSupport);
        Assert.Equal(ModelValidationLevel.NotIndividuallyTested, result.ValidationLevel);
        Assert.True(result.IsApple);
        Assert.True(result.IsIntelProcessor);
        Assert.True(result.IsSupportedIntelMac);
        Assert.Contains("not individually performance-tested", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifyModel_AppleIntelUnknownModel_ReturnsSupportedIntelMacAndNotIndividuallyTested()
    {
        var service = CreateService();

        var result = service.VerifyModel(Snapshot("Apple Inc.", "MacBookPro15,1", IntelProcessor()));

        Assert.Equal(PlatformSupportStatus.SupportedIntelMac, result.PlatformSupport);
        Assert.Equal(ModelValidationLevel.NotIndividuallyTested, result.ValidationLevel);
        Assert.True(result.IsApple);
        Assert.True(result.IsIntelProcessor);
        Assert.True(result.IsSupportedIntelMac);
        Assert.Contains("not individually performance-tested", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifyModel_AppleNonIntelCpu_ReturnsUnsupportedNonIntel()
    {
        var service = CreateService();

        var result = service.VerifyModel(Snapshot("Apple Inc.", "MacBookPro18,1", NonIntelProcessor()));

        Assert.Equal(PlatformSupportStatus.UnsupportedNonIntel, result.PlatformSupport);
        Assert.Equal(ModelValidationLevel.NotIndividuallyTested, result.ValidationLevel);
        Assert.True(result.IsApple);
        Assert.False(result.IsIntelProcessor);
        Assert.False(result.IsSupportedIntelMac);
        Assert.Contains("requires an Intel processor", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifyModel_NonAppleHardware_ReturnsUnsupportedNonApple()
    {
        var service = CreateService();

        var result = service.VerifyModel(Snapshot("PC Manufacturer", "CustomModel", IntelProcessor()));

        Assert.Equal(PlatformSupportStatus.UnsupportedNonApple, result.PlatformSupport);
        Assert.Equal(ModelValidationLevel.NotIndividuallyTested, result.ValidationLevel);
        Assert.False(result.IsApple);
        Assert.False(result.IsSupportedIntelMac);
        Assert.Contains("requires an Apple Mac", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifyModel_MissingProcessor_ReturnsDetectionIncomplete()
    {
        var service = CreateService();

        var result = service.VerifyModel(Snapshot("Apple Inc.", VerifiedHardwareModels.MacBookPro16_1, processor: null));

        Assert.Equal(PlatformSupportStatus.DetectionIncomplete, result.PlatformSupport);
        Assert.False(result.IsSupportedIntelMac);
        Assert.Contains("Processor information could not be determined", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Unknown")]
    public void VerifyModel_IncompleteManufacturer_ReturnsDetectionIncomplete(string? manufacturer)
    {
        var service = CreateService();

        var result = service.VerifyModel(Snapshot(manufacturer!, "MacBookPro16,1", IntelProcessor()));

        Assert.Equal(PlatformSupportStatus.DetectionIncomplete, result.PlatformSupport);
        Assert.False(result.IsSupportedIntelMac);
        Assert.Contains("Hardware detection was incomplete", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifyModel_CustomValidationRegistry_ReflectsAssignedValidationLevel()
    {
        var customRegistry = new FakeModelSupportRegistry(
            new ModelSupportDefinition("MacBookPro14,3", ModelValidationLevel.FunctionallyValidated));
        var service = new HardwareDetectionService(customRegistry);

        var result = service.VerifyModel(Snapshot("Apple Inc.", "MacBookPro14,3", IntelProcessor()));

        Assert.Equal(PlatformSupportStatus.SupportedIntelMac, result.PlatformSupport);
        Assert.Equal(ModelValidationLevel.FunctionallyValidated, result.ValidationLevel);
        Assert.Contains("functionally validated", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static HardwareDetectionService CreateService()
    {
        return new HardwareDetectionService(new ModelSupportRegistry());
    }

    private static HardwareSnapshot Snapshot(string manufacturer, string model, ProcessorInfo? processor)
    {
        return new HardwareSnapshot(
            new ComputerSystemInfo(manufacturer, model, "x64-based PC"),
            Processor: processor,
            VideoControllers: [],
            OperatingSystem: null,
            DateTimeOffset.Parse("2026-01-01T00:00:00+00:00"));
    }

    private static ProcessorInfo IntelProcessor()
    {
        return new ProcessorInfo(
            "Intel(R) Core(TM) i9-9880H CPU @ 2.30GHz",
            "GenuineIntel",
            NumberOfCores: 8,
            NumberOfLogicalProcessors: 16,
            MaxClockSpeed: 2300);
    }

    private static ProcessorInfo NonIntelProcessor()
    {
        return new ProcessorInfo(
            "VirtualApple @ 2.50GHz",
            "Apple",
            NumberOfCores: 8,
            NumberOfLogicalProcessors: 8,
            MaxClockSpeed: 2500);
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

        public ModelValidationLevel GetValidationLevel(string? modelIdentifier)
        {
            return Find(modelIdentifier)?.ValidationLevel ?? ModelValidationLevel.NotIndividuallyTested;
        }
    }
}
