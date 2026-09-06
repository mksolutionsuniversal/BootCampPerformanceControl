namespace BootCampPerformanceControl.FanControl;

internal sealed record FanOverrideOwnershipTarget(
    FanIndex Index,
    float ExpectedTargetRpm)
{
    public string? ExpectedTargetRawHex { get; init; }
}

internal sealed record FanOverrideOwnershipMarker
{
    public FanOverrideOwnershipMarker(
        string model,
        IEnumerable<FanOverrideOwnershipTarget> targets,
        DateTimeOffset createdAtUtc)
        : this(
            model,
            FanCapabilityFamily.PerFanModeFloat32,
            targets,
            createdAtUtc,
            expectedGlobalModeMask: null)
    {
    }

    public FanOverrideOwnershipMarker(
        string model,
        FanCapabilityFamily family,
        IEnumerable<FanOverrideOwnershipTarget> targets,
        DateTimeOffset createdAtUtc,
        ushort? expectedGlobalModeMask)
    {
        Model = model;
        Family = family;
        ArgumentNullException.ThrowIfNull(targets);
        Targets = targets.ToArray();
        CreatedAtUtc = createdAtUtc;
        ExpectedGlobalModeMask = expectedGlobalModeMask;
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

    public FanCapabilityFamily Family { get; init; }

    public IReadOnlyList<FanOverrideOwnershipTarget> Targets { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public ushort? ExpectedGlobalModeMask { get; }

    public static FanOverrideOwnershipMarker FromPlan(
        FanMaximumSafeRpmPlan plan,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new FanOverrideOwnershipMarker(
            plan.Model,
            plan.Family,
            plan.Targets.Select(target =>
                new FanOverrideOwnershipTarget(
                    target.Index,
                    target.TargetRpm)
                {
                    ExpectedTargetRawHex = target.ExactTargetPayload.IsEmpty
                        ? null
                        : Convert.ToHexString(target.ExactTargetPayload.Span)
                }),
            createdAtUtc,
            plan.Family == FanCapabilityFamily.GlobalMaskFpe2
                ? GlobalMaskFpe2Strategy.GetManualMask(plan.Targets.Count)
                : null);
    }
}
