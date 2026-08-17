using System.IO;
using System.Text.Json;
using BootCampPerformanceControl.Logging;
using BootCampPerformanceControl.PowerManagement;

namespace BootCampPerformanceControl.SettingsBackup;

public sealed class JsonRestoreSnapshotStore : IRestoreSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly object _syncRoot = new();
    private readonly SemaphoreSlim _fileGate = new(1, 1);
    private readonly IApplicationLogger _logger;
    private readonly string _backupDirectory;
    private readonly string _snapshotFilePath;
    private PowerStateSnapshot? _originalRestoreSnapshot;

    public JsonRestoreSnapshotStore(IApplicationLogger logger)
        : this(GetDefaultBackupDirectory(), logger)
    {
    }

    public JsonRestoreSnapshotStore(string backupDirectory, IApplicationLogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        ArgumentNullException.ThrowIfNull(logger);

        _backupDirectory = backupDirectory;
        _snapshotFilePath = Path.Combine(_backupDirectory, "restore-snapshot.json");
        _logger = logger;

        var loadResult = LoadPersistedSnapshot();
        if (loadResult.Status == SnapshotLoadStatus.Loaded)
        {
            _originalRestoreSnapshot = loadResult.Snapshot;
        }
    }

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

    public async Task<bool> TrySaveOriginalRestoreSnapshotAsync(
        PowerStateSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ValidateSnapshot(snapshot);
        await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var cachedSnapshot = GetSnapshot();
            var loadResult = LoadPersistedSnapshot();

            if (cachedSnapshot is not null)
            {
                if (loadResult.Status == SnapshotLoadStatus.Loaded)
                {
                    if (loadResult.Snapshot is null
                        || !RestoreSnapshotsMatch(cachedSnapshot, loadResult.Snapshot))
                    {
                        throw new InvalidDataException(
                            "The persisted restore snapshot conflicts with the cached original snapshot. "
                            + "Neither snapshot was overwritten.");
                    }

                    return false;
                }

                if (loadResult.Status == SnapshotLoadStatus.Invalid)
                {
                    throw new InvalidDataException(
                        "An existing invalid restore snapshot blocks automatic initialization. "
                        + "The file was preserved and must be replaced explicitly.",
                        loadResult.Exception);
                }

                if (loadResult.Status == SnapshotLoadStatus.Failed)
                {
                    throw new IOException(
                        "The existing restore snapshot could not be checked safely.",
                        loadResult.Exception);
                }

                await WriteSnapshotAtomicallyAsync(
                    cachedSnapshot,
                    replaceExisting: false,
                    cancellationToken).ConfigureAwait(false);
                _logger.Info(
                    "Persistent restore backup reconstructed from the cached original snapshot.");
                return false;
            }

            if (loadResult.Status == SnapshotLoadStatus.Loaded)
            {
                SetSnapshot(loadResult.Snapshot);
                return false;
            }

            if (loadResult.Status == SnapshotLoadStatus.Invalid)
            {
                throw new InvalidDataException(
                    "An existing invalid restore snapshot blocks automatic initialization. "
                    + "The file was preserved and must be replaced explicitly.",
                    loadResult.Exception);
            }

            if (loadResult.Status == SnapshotLoadStatus.Failed)
            {
                throw new IOException(
                    "The existing restore snapshot could not be checked safely.",
                    loadResult.Exception);
            }

            await WriteSnapshotAtomicallyAsync(
                snapshot,
                replaceExisting: false,
                cancellationToken).ConfigureAwait(false);
            SetSnapshot(snapshot);
            _logger.Info("Original restore snapshot saved to the persistent backup file.");
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error("Saving the original restore snapshot failed.", exception);
            throw;
        }
        finally
        {
            _fileGate.Release();
        }
    }

    public Task<PowerStateSnapshot?> GetOriginalRestoreSnapshotAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GetSnapshot());
    }

    public async Task ReplaceOriginalRestoreSnapshotAsync(
        PowerStateSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ValidateSnapshot(snapshot);
        await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await WriteSnapshotAtomicallyAsync(
                snapshot,
                replaceExisting: true,
                cancellationToken).ConfigureAwait(false);
            SetSnapshot(snapshot);
            _logger.Info("Original restore snapshot explicitly replaced.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error("Replacing the original restore snapshot failed.", exception);
            throw;
        }
        finally
        {
            _fileGate.Release();
        }
    }

    public async Task ClearOriginalRestoreSnapshotAsync(CancellationToken cancellationToken)
    {
        await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (File.Exists(_snapshotFilePath))
            {
                File.Delete(_snapshotFilePath);
            }

            SetSnapshot(snapshot: null);
            _logger.Info("Original restore snapshot explicitly cleared.");
        }
        catch (Exception exception)
        {
            _logger.Error("Clearing the original restore snapshot failed.", exception);
            throw;
        }
        finally
        {
            _fileGate.Release();
        }
    }

    private static bool RestoreSnapshotsMatch(
        PowerStateSnapshot cachedSnapshot,
        PowerStateSnapshot persistedSnapshot)
    {
        return cachedSnapshot.SchemeId == persistedSnapshot.SchemeId
            && cachedSnapshot.ProcessorMaximumAc == persistedSnapshot.ProcessorMaximumAc
            && cachedSnapshot.ProcessorMaximumDc == persistedSnapshot.ProcessorMaximumDc
            && cachedSnapshot.BoostModeAc == persistedSnapshot.BoostModeAc
            && cachedSnapshot.BoostModeDc == persistedSnapshot.BoostModeDc
            && cachedSnapshot.CapturedAt.EqualsExact(persistedSnapshot.CapturedAt);
    }

    private static string GetDefaultBackupDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "BootCampPerformanceControl", "Backups");
    }

    private SnapshotLoadResult LoadPersistedSnapshot()
    {
        if (!File.Exists(_snapshotFilePath))
        {
            return new SnapshotLoadResult(SnapshotLoadStatus.Missing, Snapshot: null, Exception: null);
        }

        try
        {
            var json = File.ReadAllText(_snapshotFilePath);
            var document = JsonSerializer.Deserialize<RestoreSnapshotDocument>(json, JsonOptions)
                ?? throw new InvalidDataException("The restore snapshot JSON document is empty.");

            if (document.SchemaVersion != RestoreSnapshotDocument.CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported restore snapshot schema version {document.SchemaVersion}.");
            }

            var snapshot = document.ToSnapshot();
            ValidateSnapshot(snapshot);
            _logger.Info(
                $"Original restore snapshot loaded. SchemeId={snapshot.SchemeId}; "
                + $"CapturedAt={snapshot.CapturedAt:O}.");
            return new SnapshotLoadResult(SnapshotLoadStatus.Loaded, snapshot, Exception: null);
        }
        catch (JsonException exception)
        {
            _logger.Error("The persisted restore snapshot contains malformed JSON.", exception);
            return new SnapshotLoadResult(SnapshotLoadStatus.Invalid, Snapshot: null, exception);
        }
        catch (InvalidDataException exception)
        {
            _logger.Error("The persisted restore snapshot is invalid.", exception);
            return new SnapshotLoadResult(SnapshotLoadStatus.Invalid, Snapshot: null, exception);
        }
        catch (ArgumentException exception)
        {
            _logger.Error("The persisted restore snapshot contains invalid values.", exception);
            return new SnapshotLoadResult(SnapshotLoadStatus.Invalid, Snapshot: null, exception);
        }
        catch (Exception exception)
        {
            _logger.Error("Loading the persisted restore snapshot failed.", exception);
            return new SnapshotLoadResult(SnapshotLoadStatus.Failed, Snapshot: null, exception);
        }
    }

    private async Task WriteSnapshotAtomicallyAsync(
        PowerStateSnapshot snapshot,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_backupDirectory);
        var temporaryFilePath = Path.Combine(
            _backupDirectory,
            $"restore-snapshot.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var document = RestoreSnapshotDocument.FromSnapshot(snapshot);
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_snapshotFilePath))
            {
                if (!replaceExisting)
                {
                    throw new IOException(
                        "A restore snapshot appeared while the original snapshot was being saved; it was not overwritten.");
                }

                File.Replace(
                    temporaryFilePath,
                    _snapshotFilePath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryFilePath, _snapshotFilePath);
            }
        }
        finally
        {
            if (File.Exists(temporaryFilePath))
            {
                try
                {
                    File.Delete(temporaryFilePath);
                }
                catch (Exception exception)
                {
                    _logger.Error("Cleaning up a temporary restore snapshot file failed.", exception);
                }
            }
        }
    }

    private static void ValidateSnapshot(PowerStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.SchemeId == Guid.Empty)
        {
            throw new ArgumentException("The restore snapshot SchemeId cannot be empty.", nameof(snapshot));
        }

        if (snapshot.CapturedAt == default)
        {
            throw new ArgumentException("The restore snapshot capture timestamp is required.", nameof(snapshot));
        }

        var validation = ProcessorPowerSettingsValidator.Validate(
            ProcessorPowerSettings.FromSnapshot(snapshot));
        if (!validation.IsValid)
        {
            throw new ArgumentException(validation.ErrorMessage, nameof(snapshot));
        }
    }

    private PowerStateSnapshot? GetSnapshot()
    {
        lock (_syncRoot)
        {
            return _originalRestoreSnapshot;
        }
    }

    private void SetSnapshot(PowerStateSnapshot? snapshot)
    {
        lock (_syncRoot)
        {
            _originalRestoreSnapshot = snapshot;
        }
    }

    private enum SnapshotLoadStatus
    {
        Missing,
        Loaded,
        Invalid,
        Failed
    }

    private sealed record SnapshotLoadResult(
        SnapshotLoadStatus Status,
        PowerStateSnapshot? Snapshot,
        Exception? Exception);
}
