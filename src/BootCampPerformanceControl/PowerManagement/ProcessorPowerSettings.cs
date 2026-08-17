namespace BootCampPerformanceControl.PowerManagement;

public sealed record ProcessorPowerSettings(
    uint ProcessorMaximumAc,
    uint ProcessorMaximumDc,
    uint BoostModeAc,
    uint BoostModeDc)
{
    public static ProcessorPowerSettings FromSnapshot(PowerStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new ProcessorPowerSettings(
            snapshot.ProcessorMaximumAc,
            snapshot.ProcessorMaximumDc,
            snapshot.BoostModeAc,
            snapshot.BoostModeDc);
    }
}
