using BootCampPerformanceControl.PowerManagement;

namespace BootCampPerformanceControl.SettingsBackup;

public interface IRestoreSnapshotStore
{
    bool HasOriginalRestoreSnapshot { get; }

    Task<bool> TrySaveOriginalRestoreSnapshotAsync(
        PowerStateSnapshot snapshot,
        CancellationToken cancellationToken);

    Task<PowerStateSnapshot?> GetOriginalRestoreSnapshotAsync(CancellationToken cancellationToken);

    Task ReplaceOriginalRestoreSnapshotAsync(
        PowerStateSnapshot snapshot,
        CancellationToken cancellationToken);

    Task ClearOriginalRestoreSnapshotAsync(CancellationToken cancellationToken);
}
