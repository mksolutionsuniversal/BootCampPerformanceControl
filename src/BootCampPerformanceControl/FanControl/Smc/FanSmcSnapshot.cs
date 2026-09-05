namespace BootCampPerformanceControl.FanControl.Smc;

internal sealed record FanSmcChannelSnapshot(
    FanIndex Index,
    SmcValue Maximum,
    SmcValue Actual,
    SmcValue Mode,
    SmcValue Target);

internal sealed record FanSmcSnapshot
{
    public FanSmcSnapshot(
        SmcValue fanCount,
        IEnumerable<FanSmcChannelSnapshot> fans)
    {
        FanCount = fanCount ?? throw new ArgumentNullException(nameof(fanCount));
        ArgumentNullException.ThrowIfNull(fans);
        Fans = fans.ToArray();
    }

    public SmcValue FanCount { get; }

    public IReadOnlyList<FanSmcChannelSnapshot> Fans { get; }

    public IReadOnlyList<SmcValue> Values =>
        [FanCount, .. Fans.SelectMany(fan => new[]
        {
            fan.Maximum,
            fan.Actual,
            fan.Mode,
            fan.Target
        })];
}
