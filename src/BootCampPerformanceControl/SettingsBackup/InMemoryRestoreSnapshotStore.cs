using BootCampPerformanceControl.PowerManagement;

namespace BootCampPerformanceControl.SettingsBackup;

public sealed class InMemoryRestoreSnapshotStore : IRestoreSnapshotStore
{
    private readonly object _syncRoot = new();
    private PowerStateSnapshot? _originalRestoreSnapshot;

    public bool HasOriginalRestoreSnapshot
    {
        get
        {
            lock (_syncRoot)
            {
                return _originalRestoreSnapshot is not null;
            }
        }
    }

    public Task<bool> TrySaveOriginalRestoreSnapshotAsync(
        PowerStateSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (_originalRestoreSnapshot is not null)
            {
                return Task.FromResult(false);
            }

            _originalRestoreSnapshot = snapshot;
            return Task.FromResult(true);
        }
    }

    public Task<PowerStateSnapshot?> GetOriginalRestoreSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            return Task.FromResult(_originalRestoreSnapshot);
        }
    }

    public Task ReplaceOriginalRestoreSnapshotAsync(
        PowerStateSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _originalRestoreSnapshot = snapshot;
        }

        return Task.CompletedTask;
    }

    public Task ClearOriginalRestoreSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _originalRestoreSnapshot = null;
        }

        return Task.CompletedTask;
    }
}
