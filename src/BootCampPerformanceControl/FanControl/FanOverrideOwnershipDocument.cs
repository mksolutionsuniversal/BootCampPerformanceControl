using System.IO;

namespace BootCampPerformanceControl.FanControl;

internal sealed record FanOverrideOwnershipTargetDocument(
    int Index,
    float ExpectedTargetRpm,
    string? ExpectedTargetRawHex = null);

internal sealed record FanOverrideBaselineTargetDocument(
    int Index,
    string TargetRawHex,
    byte? Mode);

internal sealed record FanOverrideTransactionJournalDocument(
    int SchemaVersion,
    string Model,
    string CapabilityFamily,
    int ReportedFanCount,
    IReadOnlyList<FanOverrideOwnershipTargetDocument> Targets,
    ushort? ExpectedGlobalModeMask,
    IReadOnlyList<FanOverrideBaselineTargetDocument> BaselineTargets,
    ushort? BaselineGlobalModeMask,
    DateTimeOffset CreatedAtUtc)
{
    public const int SchemaVersionValue = 4;

    public static FanOverrideTransactionJournalDocument FromMarker(
        FanOverrideOwnershipMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);

        return new FanOverrideTransactionJournalDocument(
            SchemaVersionValue,
            marker.Model,
            marker.Family.ToString(),
            marker.Targets.Count,
            marker.Targets.Select(target => new FanOverrideOwnershipTargetDocument(
                target.Index.Value,
                target.ExpectedTargetRpm,
                target.ExpectedTargetRawHex)).ToArray(),
            marker.ExpectedGlobalModeMask,
            marker.BaselineTargets.Select(target => new FanOverrideBaselineTargetDocument(
                target.Index.Value,
                target.TargetRawHex,
                target.Mode)).ToArray(),
            marker.BaselineGlobalModeMask,
            marker.CreatedAtUtc);
    }

    public FanOverrideOwnershipMarker ToMarker()
    {
        if (!Enum.TryParse<FanCapabilityFamily>(CapabilityFamily, out var family) ||
            family is FanCapabilityFamily.Unknown or FanCapabilityFamily.Passive)
        {
            throw new InvalidDataException(
                $"Transaction journal capability family '{CapabilityFamily}' is not writable.");
        }

        if (ReportedFanCount != Targets.Count || BaselineTargets.Count != Targets.Count)
        {
            throw new InvalidDataException(
                "Transaction journal topology does not match its expected and baseline target counts.");
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
            ExpectedGlobalModeMask)
        {
            BaselineTargets = BaselineTargets.Select(target =>
                new FanOverrideBaselineTarget(
                    new FanIndex(target.Index),
                    target.TargetRawHex,
                    target.Mode)).ToArray(),
            BaselineGlobalModeMask = BaselineGlobalModeMask
        };
    }
}

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
            FanCapabilityFamily.PerFanModeFloat32,
            [
                new FanOverrideOwnershipTarget(new FanIndex(0), Fan0ExpectedTargetRpm),
                new FanOverrideOwnershipTarget(new FanIndex(1), Fan1ExpectedTargetRpm)
            ],
            CreatedAtUtc,
            expectedGlobalModeMask: null);
    }
}
