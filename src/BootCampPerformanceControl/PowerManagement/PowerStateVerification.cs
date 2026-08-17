namespace BootCampPerformanceControl.PowerManagement;

public sealed record PowerStateVerification(
    Guid ExpectedSchemeId,
    Guid? ActualSchemeId,
    ProcessorPowerSettings ExpectedSettings,
    ProcessorPowerSettings? ActualSettings,
    bool SchemeIdMatches,
    bool ProcessorMaximumAcMatches,
    bool ProcessorMaximumDcMatches,
    bool BoostModeAcMatches,
    bool BoostModeDcMatches)
{
    public bool IsSuccessful =>
        SchemeIdMatches
        && ProcessorMaximumAcMatches
        && ProcessorMaximumDcMatches
        && BoostModeAcMatches
        && BoostModeDcMatches;

    public static PowerStateVerification Compare(
        Guid expectedSchemeId,
        ProcessorPowerSettings expectedSettings,
        PowerStateSnapshot? actualState)
    {
        ArgumentNullException.ThrowIfNull(expectedSettings);

        var actualSettings = actualState is null
            ? null
            : ProcessorPowerSettings.FromSnapshot(actualState);

        return new PowerStateVerification(
            expectedSchemeId,
            actualState?.SchemeId,
            expectedSettings,
            actualSettings,
            actualState?.SchemeId == expectedSchemeId,
            actualSettings?.ProcessorMaximumAc == expectedSettings.ProcessorMaximumAc,
            actualSettings?.ProcessorMaximumDc == expectedSettings.ProcessorMaximumDc,
            actualSettings?.BoostModeAc == expectedSettings.BoostModeAc,
            actualSettings?.BoostModeDc == expectedSettings.BoostModeDc);
    }
}
