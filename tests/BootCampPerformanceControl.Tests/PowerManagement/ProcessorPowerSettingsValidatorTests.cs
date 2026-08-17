using BootCampPerformanceControl.PowerManagement;

namespace BootCampPerformanceControl.Tests.PowerManagement;

public sealed class ProcessorPowerSettingsValidatorTests
{
    [Fact]
    public void Validate_AcceptsAllBoundaryValues()
    {
        var lowerBoundary = new ProcessorPowerSettings(0, 0, 0, 0);
        var upperBoundary = new ProcessorPowerSettings(100, 100, 6, 6);

        Assert.True(ProcessorPowerSettingsValidator.Validate(lowerBoundary).IsValid);
        Assert.True(ProcessorPowerSettingsValidator.Validate(upperBoundary).IsValid);
    }

    [Fact]
    public void Validate_ReportsEveryOutOfRangeValue()
    {
        var settings = new ProcessorPowerSettings(101, 200, 7, uint.MaxValue);

        var result = ProcessorPowerSettingsValidator.Validate(settings);

        Assert.False(result.IsValid);
        Assert.Equal(4, result.Errors.Count);
        Assert.Contains(nameof(settings.ProcessorMaximumAc), result.ErrorMessage);
        Assert.Contains(nameof(settings.ProcessorMaximumDc), result.ErrorMessage);
        Assert.Contains(nameof(settings.BoostModeAc), result.ErrorMessage);
        Assert.Contains(nameof(settings.BoostModeDc), result.ErrorMessage);
    }
}
