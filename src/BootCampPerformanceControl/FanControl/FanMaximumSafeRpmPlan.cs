namespace BootCampPerformanceControl.FanControl;

internal sealed record FanMaximumSafeRpmTarget(
    FanIndex Index,
    float TargetRpm);

internal sealed record FanMaximumSafeRpmPlan
{
    public FanMaximumSafeRpmPlan(
        string model,
        IEnumerable<FanMaximumSafeRpmTarget> targets)
    {
        Model = model;
        ArgumentNullException.ThrowIfNull(targets);
        Targets = targets.ToArray();
    }

    public string Model { get; init; }

    public IReadOnlyList<FanMaximumSafeRpmTarget> Targets { get; }
}
