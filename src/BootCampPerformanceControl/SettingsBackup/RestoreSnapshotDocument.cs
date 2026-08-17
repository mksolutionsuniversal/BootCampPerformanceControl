using BootCampPerformanceControl.PowerManagement;

namespace BootCampPerformanceControl.SettingsBackup;

internal sealed record RestoreSnapshotDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; }

    public DateTimeOffset CapturedAt { get; init; }

    public Guid SchemeId { get; init; }

    public uint ProcessorMaximumAc { get; init; }

    public uint ProcessorMaximumDc { get; init; }

    public uint BoostModeAc { get; init; }

    public uint BoostModeDc { get; init; }

    public static RestoreSnapshotDocument FromSnapshot(PowerStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new RestoreSnapshotDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            CapturedAt = snapshot.CapturedAt,
            SchemeId = snapshot.SchemeId,
            ProcessorMaximumAc = snapshot.ProcessorMaximumAc,
            ProcessorMaximumDc = snapshot.ProcessorMaximumDc,
            BoostModeAc = snapshot.BoostModeAc,
            BoostModeDc = snapshot.BoostModeDc
        };
    }

    public PowerStateSnapshot ToSnapshot()
    {
        return new PowerStateSnapshot(
            SchemeId,
            ProcessorMaximumAc,
            ProcessorMaximumDc,
            BoostModeAc,
            BoostModeDc,
            CapturedAt);
    }
}
