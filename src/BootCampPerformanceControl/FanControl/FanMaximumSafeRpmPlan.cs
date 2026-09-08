namespace BootCampPerformanceControl.FanControl;

internal sealed record FanMaximumSafeRpmTarget(
    FanIndex Index,
    float TargetRpm)
{
    public ReadOnlyMemory<byte> ExactTargetPayload { get; init; }

    public ReadOnlyMemory<byte> BaselineTargetPayload { get; init; }

    public byte? BaselineMode { get; init; }
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
        IEnumerable<FanMaximumSafeRpmTarget> targets,
        ushort? baselineGlobalModeMask = null)
    {
        Model = model;
        Family = family;
        ArgumentNullException.ThrowIfNull(targets);
        Targets = targets.ToArray();
        BaselineGlobalModeMask = baselineGlobalModeMask;
    }

    public string Model { get; init; }

    public FanCapabilityFamily Family { get; init; }

    public IReadOnlyList<FanMaximumSafeRpmTarget> Targets { get; }

    public ushort? BaselineGlobalModeMask { get; }
}
