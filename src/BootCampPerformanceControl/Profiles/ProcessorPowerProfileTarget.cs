namespace BootCampPerformanceControl.Profiles;

public sealed record ProcessorPowerProfileTarget(
    uint? ProcessorMaximumAc,
    uint? ProcessorMaximumDc,
    uint? BoostModeAc,
    uint? BoostModeDc,
    ProfileUnspecifiedValueSource UnspecifiedValueSource);

public enum ProfileUnspecifiedValueSource
{
    None,
    OriginalRestoreSnapshot
}
