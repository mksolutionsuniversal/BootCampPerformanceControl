namespace BootCampPerformanceControl.FanControl;

internal sealed record FanMaximumSafeRpmTarget(
    FanIndex Index,
    float TargetRpm)
{
    public ReadOnlyMemory<byte> ExactTargetPayload { get; init; }
}

internal sealed record FanMaximumSafeRpmPlan
{
    public FanMaximumSafeRpmPlan(
        string model,
        IEnumerable<FanMaximumSafeRpmTarget> targets)
        : this(model, FanCapabilityFamily.PerFanModeFloat32, targets)
    {
    }

    public FanMaximumSafeRpmPlan(
        string model,
        FanCapabilityFamily family,
        IEnumerable<FanMaximumSafeRpmTarget> targets)
    {
        Model = model;
        Family = family;
        ArgumentNullException.ThrowIfNull(targets);
        Targets = targets.ToArray();
    }

    public string Model { get; init; }

    public FanCapabilityFamily Family { get; init; }

    public IReadOnlyList<FanMaximumSafeRpmTarget> Targets { get; }
}
