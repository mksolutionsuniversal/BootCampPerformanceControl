namespace BootCampPerformanceControl.FanControl;

internal sealed record FanOverrideOwnershipTargetDocument(
    int Index,
    float ExpectedTargetRpm);

internal sealed record FanOverrideOwnershipDocument(
    int SchemaVersion,
    string Model,
    IReadOnlyList<FanOverrideOwnershipTargetDocument> Targets,
    DateTimeOffset CreatedAtUtc)
{
    public const int CurrentSchemaVersion = 2;

    public static FanOverrideOwnershipDocument FromMarker(FanOverrideOwnershipMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);

        return new FanOverrideOwnershipDocument(
            CurrentSchemaVersion,
            marker.Model,
            marker.Targets.Select(target => new FanOverrideOwnershipTargetDocument(
                target.Index.Value,
                target.ExpectedTargetRpm)).ToArray(),
            marker.CreatedAtUtc);
    }

    public FanOverrideOwnershipMarker ToMarker()
    {
        return new FanOverrideOwnershipMarker(
            Model,
            Targets.Select(target => new FanOverrideOwnershipTarget(
                new FanIndex(target.Index),
                target.ExpectedTargetRpm)),
            CreatedAtUtc);
    }
}

internal sealed record LegacyFanOverrideOwnershipDocument(
    int SchemaVersion,
    string Model,
    float Fan0ExpectedTargetRpm,
    float Fan1ExpectedTargetRpm,
    DateTimeOffset CreatedAtUtc)
{
    public const int SchemaVersionValue = 1;

    public static LegacyFanOverrideOwnershipDocument FromMarker(
        FanOverrideOwnershipMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);

        return new LegacyFanOverrideOwnershipDocument(
            SchemaVersionValue,
            marker.Model,
            marker.Targets[0].ExpectedTargetRpm,
            marker.Targets[1].ExpectedTargetRpm,
            marker.CreatedAtUtc);
    }

    public FanOverrideOwnershipMarker ToMarker()
    {
        return new FanOverrideOwnershipMarker(
            Model,
            [
                new FanOverrideOwnershipTarget(new FanIndex(0), Fan0ExpectedTargetRpm),
                new FanOverrideOwnershipTarget(new FanIndex(1), Fan1ExpectedTargetRpm)
            ],
            CreatedAtUtc);
    }
}
