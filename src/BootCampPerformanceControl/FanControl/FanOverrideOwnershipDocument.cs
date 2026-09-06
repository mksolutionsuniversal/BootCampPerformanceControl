using System.IO;

namespace BootCampPerformanceControl.FanControl;

internal sealed record FanOverrideOwnershipTargetDocument(
    int Index,
    float ExpectedTargetRpm,
    string? ExpectedTargetRawHex = null);

internal sealed record FanOverrideOwnershipDocument(
    int SchemaVersion,
    string Model,
    string CapabilityFamily,
    int ReportedFanCount,
    IReadOnlyList<FanOverrideOwnershipTargetDocument> Targets,
    ushort? ExpectedGlobalModeMask,
    DateTimeOffset CreatedAtUtc)
{
    public const int CurrentSchemaVersion = 3;

    public static FanOverrideOwnershipDocument FromMarker(FanOverrideOwnershipMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);

        return new FanOverrideOwnershipDocument(
            CurrentSchemaVersion,
            marker.Model,
            marker.Family.ToString(),
            marker.Targets.Count,
            marker.Targets.Select(target => new FanOverrideOwnershipTargetDocument(
                target.Index.Value,
                target.ExpectedTargetRpm,
                target.ExpectedTargetRawHex)).ToArray(),
            marker.ExpectedGlobalModeMask,
            marker.CreatedAtUtc);
    }

    public FanOverrideOwnershipMarker ToMarker()
    {
        if (!Enum.TryParse<FanCapabilityFamily>(CapabilityFamily, out var family) ||
            family is FanCapabilityFamily.Unknown or FanCapabilityFamily.Passive)
        {
            throw new InvalidDataException(
                $"Ownership marker capability family '{CapabilityFamily}' is not a writable family.");
        }

        if (ReportedFanCount != Targets.Count)
        {
            throw new InvalidDataException(
                "Ownership marker reported fan count does not match its target topology.");
        }

        return new FanOverrideOwnershipMarker(
            Model,
            family,
            Targets.Select(target =>
                new FanOverrideOwnershipTarget(
                    new FanIndex(target.Index),
                    target.ExpectedTargetRpm)
                {
                    ExpectedTargetRawHex = target.ExpectedTargetRawHex
                }),
            CreatedAtUtc,
            ExpectedGlobalModeMask);
    }
}

internal sealed record LegacyDynamicFanOverrideOwnershipDocument(
    int SchemaVersion,
    string Model,
    IReadOnlyList<FanOverrideOwnershipTargetDocument> Targets,
    DateTimeOffset CreatedAtUtc)
{
    public const int SchemaVersionValue = 2;

    public FanOverrideOwnershipMarker ToMarker()
    {
        return new FanOverrideOwnershipMarker(
            Model,
            FanCapabilityFamily.PerFanModeFloat32,
            Targets.Select(target => new FanOverrideOwnershipTarget(
                new FanIndex(target.Index),
                target.ExpectedTargetRpm)),
            CreatedAtUtc,
            expectedGlobalModeMask: null);
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

    public FanOverrideOwnershipMarker ToMarker()
    {
        return new FanOverrideOwnershipMarker(
            Model,
            FanCapabilityFamily.PerFanModeFloat32,
            [
                new FanOverrideOwnershipTarget(new FanIndex(0), Fan0ExpectedTargetRpm),
                new FanOverrideOwnershipTarget(new FanIndex(1), Fan1ExpectedTargetRpm)
            ],
            CreatedAtUtc,
            expectedGlobalModeMask: null);
    }
}
