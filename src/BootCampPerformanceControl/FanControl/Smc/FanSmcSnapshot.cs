namespace BootCampPerformanceControl.FanControl.Smc;

internal sealed record FanSmcSnapshot(
    SmcValue FanCount,
    SmcValue Fan0Maximum,
    SmcValue Fan1Maximum,
    SmcValue Fan0Actual,
    SmcValue Fan1Actual,
    SmcValue Fan0Mode,
    SmcValue Fan1Mode,
    SmcValue Fan0Target,
    SmcValue Fan1Target)
{
    public IReadOnlyList<SmcValue> Values =>
    [
        FanCount,
        Fan0Maximum,
        Fan1Maximum,
        Fan0Actual,
        Fan1Actual,
        Fan0Mode,
        Fan1Mode,
        Fan0Target,
        Fan1Target
    ];
}
