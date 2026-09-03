using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.SettingsBackup;

namespace BootCampPerformanceControl.Tests.TestDoubles;

internal sealed class FakeRestoreSnapshotStore : IRestoreSnapshotStore
{
    public bool HasOriginalRestoreSnapshot { get; set; }

    public PowerStateSnapshot? Snapshot { get; set; }

    public Task<bool> TrySaveOriginalRestoreSnapshotAsync(
        PowerStateSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        HasOriginalRestoreSnapshot = true;
        Snapshot = snapshot;
        return Task.FromResult(true);
    }

    public Task<PowerStateSnapshot?> GetOriginalRestoreSnapshotAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Snapshot);
    }

    public Task ReplaceOriginalRestoreSnapshotAsync(
        PowerStateSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        HasOriginalRestoreSnapshot = true;
        Snapshot = snapshot;
        return Task.CompletedTask;
    }

    public Task ClearOriginalRestoreSnapshotAsync(CancellationToken cancellationToken)
    {
        HasOriginalRestoreSnapshot = false;
        Snapshot = null;
        return Task.CompletedTask;
    }
}
