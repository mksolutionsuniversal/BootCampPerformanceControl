using System.IO;
using System.Text.Json;
using BootCampPerformanceControl.Logging;

namespace BootCampPerformanceControl.FanControl;

internal sealed class JsonFanOverrideOwnershipStore : IFanOverrideOwnershipStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _fileGate = new(1, 1);
    private readonly string _backupDirectory;
    private readonly string _markerFilePath;
    private readonly IApplicationLogger _logger;

    public JsonFanOverrideOwnershipStore(IApplicationLogger logger)
        : this(GetDefaultBackupDirectory(), logger)
    {
    }

    internal JsonFanOverrideOwnershipStore(
        string backupDirectory,
        IApplicationLogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        ArgumentNullException.ThrowIfNull(logger);

        _backupDirectory = backupDirectory;
        _markerFilePath = Path.Combine(_backupDirectory, "fan-override-ownership.json");
        _logger = logger;
    }

    public async Task<FanOverrideOwnershipMarker?> LoadAsync(
        CancellationToken cancellationToken)
    {
        await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!File.Exists(_markerFilePath))
            {
                return null;
            }

            await using var stream = new FileStream(
                _markerFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var root = await JsonSerializer.DeserializeAsync<JsonElement>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schemaVersion", out var schemaVersionElement) ||
                !schemaVersionElement.TryGetInt32(out var schemaVersion))
            {
                throw new InvalidDataException(
                    "The fan ownership marker does not contain a valid schema version.");
            }

            var marker = schemaVersion switch
            {
                LegacyFanOverrideOwnershipDocument.SchemaVersionValue =>
                    (root.Deserialize<LegacyFanOverrideOwnershipDocument>(JsonOptions)
                        ?? throw new InvalidDataException("The legacy fan ownership marker JSON document is empty."))
                    .ToMarker(),
                FanOverrideOwnershipDocument.CurrentSchemaVersion =>
                    (root.Deserialize<FanOverrideOwnershipDocument>(JsonOptions)
                        ?? throw new InvalidDataException("The fan ownership marker JSON document is empty."))
                    .ToMarker(),
                _ => throw new InvalidDataException(
                    $"Unsupported fan ownership marker schema version {schemaVersion}.")
            };
            ValidateMarker(marker);
            _logger.Info(
                $"Fan override ownership marker loaded. Model={marker.Model}; CreatedAtUtc={marker.CreatedAtUtc:O}.");
            return marker;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error("Loading the fan override ownership marker failed.", exception);
            throw;
        }
        finally
        {
            _fileGate.Release();
        }
    }

    public async Task SaveNewAsync(
        FanOverrideOwnershipMarker marker,
        CancellationToken cancellationToken)
    {
        ValidateMarker(marker);
        await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Directory.CreateDirectory(_backupDirectory);

            if (File.Exists(_markerFilePath))
            {
                throw new IOException(
                    "A fan override ownership marker already exists. New ownership cannot be taken until it is recovered or explicitly cleared.");
            }

            var temporaryFilePath = Path.Combine(
                _backupDirectory,
                $"fan-override-ownership.{Guid.NewGuid():N}.tmp");

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
                    var document = FanOverrideOwnershipDocument.FromMarker(marker);
                    await JsonSerializer.SerializeAsync(
                        stream,
                        document,
                        JsonOptions,
                        cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporaryFilePath, _markerFilePath);
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
                        _logger.Error(
                            "Cleaning up a temporary fan ownership marker file failed.",
                            exception);
                    }
                }
            }

            _logger.Info(
                $"Fan override ownership marker persisted before hardware override. Model={marker.Model}; CreatedAtUtc={marker.CreatedAtUtc:O}.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error("Saving the fan override ownership marker failed.", exception);
            throw;
        }
        finally
        {
            _fileGate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (File.Exists(_markerFilePath))
            {
                File.Delete(_markerFilePath);
            }

            _logger.Info("Fan override ownership marker cleared.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error("Clearing the fan override ownership marker failed.", exception);
            throw;
        }
        finally
        {
            _fileGate.Release();
        }
    }

    private static string GetDefaultBackupDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "BootCampPerformanceControl", "Backups");
    }

    private static void ValidateMarker(FanOverrideOwnershipMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        ArgumentException.ThrowIfNullOrWhiteSpace(marker.Model);

        if (marker.Targets.Count == 0)
        {
            throw new ArgumentException("A fan ownership marker must contain at least one target.", nameof(marker));
        }

        for (var position = 0; position < marker.Targets.Count; position++)
        {
            var target = marker.Targets[position];
            if (target.Index.Value != position)
            {
                throw new ArgumentException(
                    "Fan ownership target indexes must be unique, contiguous, and ordered from zero.",
                    nameof(marker));
            }

            if (!float.IsFinite(target.ExpectedTargetRpm) || target.ExpectedTargetRpm <= 0f)
            {
                throw new ArgumentException(
                    $"Fan {target.Index.Value} ownership target RPM must be finite and positive.",
                    nameof(marker));
            }
        }

        if (marker.CreatedAtUtc == default || marker.CreatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Fan ownership marker timestamp must be a valid UTC timestamp.", nameof(marker));
        }
    }
}
