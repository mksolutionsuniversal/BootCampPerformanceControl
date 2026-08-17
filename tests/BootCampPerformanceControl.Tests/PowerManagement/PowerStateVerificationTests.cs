using BootCampPerformanceControl.PowerManagement;

namespace BootCampPerformanceControl.Tests.PowerManagement;

public sealed class PowerStateVerificationTests
{
    [Fact]
    public void Compare_ReportsSuccessWhenSchemeAndEveryValueMatch()
    {
        var schemeId = Guid.NewGuid();
        var expected = new ProcessorPowerSettings(95, 90, 0, 1);
        var actual = Snapshot(schemeId, expected);

        var result = PowerStateVerification.Compare(schemeId, expected, actual);

        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public void Compare_IdentifiesIndividualMismatches()
    {
        var expectedSchemeId = Guid.NewGuid();
        var expected = new ProcessorPowerSettings(95, 95, 0, 0);
        var actual = Snapshot(
            Guid.NewGuid(),
            new ProcessorPowerSettings(100, 95, 2, 0));

        var result = PowerStateVerification.Compare(expectedSchemeId, expected, actual);

        Assert.False(result.IsSuccessful);
        Assert.False(result.SchemeIdMatches);
        Assert.False(result.ProcessorMaximumAcMatches);
        Assert.True(result.ProcessorMaximumDcMatches);
        Assert.False(result.BoostModeAcMatches);
        Assert.True(result.BoostModeDcMatches);
    }

    private static PowerStateSnapshot Snapshot(Guid schemeId, ProcessorPowerSettings settings)
    {
        return new PowerStateSnapshot(
            schemeId,
            settings.ProcessorMaximumAc,
            settings.ProcessorMaximumDc,
            settings.BoostModeAc,
            settings.BoostModeDc,
            DateTimeOffset.UtcNow);
    }
}
