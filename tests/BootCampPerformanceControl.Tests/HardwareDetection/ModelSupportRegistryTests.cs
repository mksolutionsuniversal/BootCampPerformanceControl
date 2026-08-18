using BootCampPerformanceControl.HardwareDetection;

namespace BootCampPerformanceControl.Tests.HardwareDetection;

public sealed class ModelSupportRegistryTests
{
    [Fact]
    public void Find_MacBookPro16_1_ReturnsPerformanceValidatedDefinition()
    {
        var registry = new ModelSupportRegistry();

        var definition = registry.Find(VerifiedHardwareModels.MacBookPro16_1);

        Assert.NotNull(definition);
        Assert.Equal(VerifiedHardwareModels.MacBookPro16_1, definition.ModelIdentifier);
        Assert.Equal(ModelValidationLevel.PerformanceValidated, definition.ValidationLevel);
    }

    [Fact]
    public void Find_MacBookPro14_3_ReturnsNotIndividuallyTestedDefinition()
    {
        var registry = new ModelSupportRegistry();

        var definition = registry.Find(VerifiedHardwareModels.MacBookPro14_3);

        Assert.NotNull(definition);
        Assert.Equal(VerifiedHardwareModels.MacBookPro14_3, definition.ModelIdentifier);
        Assert.Equal(ModelValidationLevel.NotIndividuallyTested, definition.ValidationLevel);
    }

    [Fact]
    public void Find_MatchesCaseInsensitively()
    {
        var registry = new ModelSupportRegistry();

        var definition = registry.Find("macbookpro16,1");

        Assert.NotNull(definition);
        Assert.Equal(VerifiedHardwareModels.MacBookPro16_1, definition.ModelIdentifier);
        Assert.Equal(ModelValidationLevel.PerformanceValidated, definition.ValidationLevel);
    }

    [Fact]
    public void Find_UnknownAppleModel_ReturnsNull()
    {
        var registry = new ModelSupportRegistry();

        var definition = registry.Find("MacBookPro15,1");

        Assert.Null(definition);
    }

    [Theory]
    [InlineData(VerifiedHardwareModels.MacBookPro16_1, ModelValidationLevel.PerformanceValidated)]
    [InlineData("macbookpro16,1", ModelValidationLevel.PerformanceValidated)]
    [InlineData(VerifiedHardwareModels.MacBookPro14_3, ModelValidationLevel.NotIndividuallyTested)]
    [InlineData("MacBookPro15,1", ModelValidationLevel.NotIndividuallyTested)]
    [InlineData(null, ModelValidationLevel.NotIndividuallyTested)]
    [InlineData("", ModelValidationLevel.NotIndividuallyTested)]
    [InlineData(" ", ModelValidationLevel.NotIndividuallyTested)]
    [InlineData("Unknown", ModelValidationLevel.NotIndividuallyTested)]
    public void GetValidationLevel_ReturnsExpectedLevel(string? modelIdentifier, ModelValidationLevel expectedLevel)
    {
        var registry = new ModelSupportRegistry();

        var level = registry.GetValidationLevel(modelIdentifier);

        Assert.Equal(expectedLevel, level);
    }
}
