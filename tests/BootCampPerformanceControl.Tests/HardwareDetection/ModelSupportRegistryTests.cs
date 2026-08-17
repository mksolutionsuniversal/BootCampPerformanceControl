using BootCampPerformanceControl.HardwareDetection;

namespace BootCampPerformanceControl.Tests.HardwareDetection;

public sealed class ModelSupportRegistryTests
{
    [Fact]
    public void Find_MacBookPro16_1_ReturnsProcessorPowerControlVerifiedDefinition()
    {
        var registry = new ModelSupportRegistry();

        var definition = registry.Find(VerifiedHardwareModels.MacBookPro16_1);

        Assert.NotNull(definition);
        Assert.Equal(VerifiedHardwareModels.MacBookPro16_1, definition.ModelIdentifier);
        Assert.True(definition.ProcessorPowerControlVerified);
    }

    [Fact]
    public void Find_MatchesCaseInsensitively()
    {
        var registry = new ModelSupportRegistry();

        var definition = registry.Find("macbookpro16,1");

        Assert.NotNull(definition);
        Assert.Equal(VerifiedHardwareModels.MacBookPro16_1, definition.ModelIdentifier);
        Assert.True(definition.ProcessorPowerControlVerified);
    }

    [Fact]
    public void Find_UnknownAppleModel_ReturnsNoVerifiedDefinition()
    {
        var registry = new ModelSupportRegistry();

        var definition = registry.Find("MacBookPro15,1");

        Assert.Null(definition);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Unknown")]
    public void Find_BlankOrUnknownModel_CannotBecomeVerified(string? modelIdentifier)
    {
        var registry = new ModelSupportRegistry();

        var definition = registry.Find(modelIdentifier);

        Assert.True(definition?.ProcessorPowerControlVerified != true);
    }
}
