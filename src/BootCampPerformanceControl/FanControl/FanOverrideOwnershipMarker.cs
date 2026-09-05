namespace BootCampPerformanceControl.FanControl;

internal sealed record FanOverrideOwnershipTarget(
    FanIndex Index,
    float ExpectedTargetRpm);

internal sealed record FanOverrideOwnershipMarker
{
    public FanOverrideOwnershipMarker(
        string model,
        IEnumerable<FanOverrideOwnershipTarget> targets,
        DateTimeOffset createdAtUtc)
    {
        Model = model;
        ArgumentNullException.ThrowIfNull(targets);
        Targets = targets.ToArray();
        CreatedAtUtc = createdAtUtc;
    }

    public FanOverrideOwnershipMarker(
        string model,
        float fan0ExpectedTargetRpm,
        float fan1ExpectedTargetRpm,
        DateTimeOffset createdAtUtc)
        : this(
            model,
            [
                new FanOverrideOwnershipTarget(new FanIndex(0), fan0ExpectedTargetRpm),
                new FanOverrideOwnershipTarget(new FanIndex(1), fan1ExpectedTargetRpm)
            ],
            createdAtUtc)
    {
    }

    public string Model { get; init; }

    public IReadOnlyList<FanOverrideOwnershipTarget> Targets { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public static FanOverrideOwnershipMarker FromPlan(
        FanMaximumSafeRpmPlan plan,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new FanOverrideOwnershipMarker(
            plan.Model,
            plan.Targets.Select(target => new FanOverrideOwnershipTarget(
                target.Index,
                target.TargetRpm)),
            createdAtUtc);
    }
}
