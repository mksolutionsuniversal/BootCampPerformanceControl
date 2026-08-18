namespace BootCampPerformanceControl.FanControl;

internal sealed record FanOverrideOwnershipDocument(
    int SchemaVersion,
    string Model,
    float Fan0ExpectedTargetRpm,
    float Fan1ExpectedTargetRpm,
    DateTimeOffset CreatedAtUtc)
{
    public const int CurrentSchemaVersion = 1;

    public static FanOverrideOwnershipDocument FromMarker(FanOverrideOwnershipMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);

        return new FanOverrideOwnershipDocument(
            CurrentSchemaVersion,
            marker.Model,
            marker.Fan0ExpectedTargetRpm,
            marker.Fan1ExpectedTargetRpm,
            marker.CreatedAtUtc);
    }

    public FanOverrideOwnershipMarker ToMarker()
    {
        return new FanOverrideOwnershipMarker(
            Model,
            Fan0ExpectedTargetRpm,
            Fan1ExpectedTargetRpm,
            CreatedAtUtc);
    }
}
