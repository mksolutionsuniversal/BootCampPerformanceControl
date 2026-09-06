using System.IO;
using System.Buffers.Binary;
using System.Text.Json;
using BootCampPerformanceControl.HardwareDetection;
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
                LegacyDynamicFanOverrideOwnershipDocument.SchemaVersionValue =>
                    (root.Deserialize<LegacyDynamicFanOverrideOwnershipDocument>(JsonOptions)
                        ?? throw new InvalidDataException("The legacy dynamic fan ownership marker JSON document is empty."))
                    .ToMarker(),
                FanOverrideTransactionJournalDocument.SchemaVersionValue =>
                    (root.Deserialize<FanOverrideTransactionJournalDocument>(JsonOptions)
                        ?? throw new InvalidDataException("The fan transaction journal JSON document is empty."))
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
                    await SerializeMarkerAsync(stream, marker, cancellationToken)
                        .ConfigureAwait(false);
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

    public async Task ReplaceAsync(
        FanOverrideOwnershipMarker marker,
        CancellationToken cancellationToken)
    {
        ValidateMarker(marker);
        if (marker.IsTransactionJournal)
        {
            throw new ArgumentException(
                "Completing fan ownership requires a final marker, not another transaction journal.",
                nameof(marker));
        }

        await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_markerFilePath))
            {
                throw new IOException(
                    "The in-progress fan transaction journal is missing and cannot be completed safely.");
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
                    await SerializeMarkerAsync(stream, marker, cancellationToken)
                        .ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporaryFilePath, _markerFilePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryFilePath))
                {
                    File.Delete(temporaryFilePath);
                }
            }

            _logger.Info(
                $"Fan transaction journal replaced by final ownership marker. Model={marker.Model}; CreatedAtUtc={marker.CreatedAtUtc:O}.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error("Completing the fan transaction journal failed.", exception);
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

    private static Task SerializeMarkerAsync(
        Stream stream,
        FanOverrideOwnershipMarker marker,
        CancellationToken cancellationToken)
    {
        if (marker.IsTransactionJournal)
        {
            return JsonSerializer.SerializeAsync(
                stream,
                FanOverrideTransactionJournalDocument.FromMarker(marker),
                JsonOptions,
                cancellationToken);
        }

        if (ShouldWriteLegacySchema(marker))
        {
            return JsonSerializer.SerializeAsync(
                stream,
                LegacyFanOverrideOwnershipDocument.FromMarker(marker),
                JsonOptions,
                cancellationToken);
        }

        return JsonSerializer.SerializeAsync(
            stream,
            FanOverrideOwnershipDocument.FromMarker(marker),
            JsonOptions,
            cancellationToken);
    }

    private static bool ShouldWriteLegacySchema(FanOverrideOwnershipMarker marker)
    {
        return marker.Family == FanCapabilityFamily.PerFanModeFloat32 &&
            string.Equals(
                marker.Model,
                VerifiedHardwareModels.MacBookPro16_1,
                StringComparison.Ordinal) &&
            marker.Targets.Count == 2 &&
            marker.Targets[0].Index == new FanIndex(0) &&
            marker.Targets[1].Index == new FanIndex(1);
    }

    private static void ValidateMarker(FanOverrideOwnershipMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        ArgumentException.ThrowIfNullOrWhiteSpace(marker.Model);

        if (marker.Targets.Count == 0)
        {
            throw new ArgumentException("A fan ownership marker must contain at least one target.", nameof(marker));
        }

        if (marker.Family is FanCapabilityFamily.Unknown or FanCapabilityFamily.Passive)
        {
            throw new ArgumentException(
                "A fan ownership marker must identify a bounded writable capability family.",
                nameof(marker));
        }

        if (marker.IsTransactionJournal)
        {
            ValidateTransactionJournal(marker);
        }
        else if (marker.Family == FanCapabilityFamily.GlobalMaskFpe2)
        {
            var expectedMask = GlobalMaskFpe2Strategy.GetManualMask(marker.Targets.Count);
            if (marker.ExpectedGlobalModeMask != expectedMask ||
                marker.Targets.Any(target => !HasValidExactFpe2Target(target)))
            {
                throw new ArgumentException(
                    "A GlobalMaskFpe2 ownership marker requires the proven global mask and exact two-byte targets.",
                    nameof(marker));
            }
        }
        else if (marker.ExpectedGlobalModeMask is not null)
        {
            throw new ArgumentException(
                "A per-fan ownership marker cannot contain global mode state.",
                nameof(marker));
        }
        else if (marker.Targets.Any(target => target.ExpectedTargetRawHex is not null))
        {
            throw new ArgumentException(
                "A PerFanModeFloat32 ownership marker cannot contain fpe2 raw targets.",
                nameof(marker));
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

    private static bool HasValidExactFpe2Target(FanOverrideOwnershipTarget target)
    {
        if (target.ExpectedTargetRawHex is null ||
            target.ExpectedTargetRawHex.Length != 4)
        {
            return false;
        }

        try
        {
            var raw = Convert.FromHexString(target.ExpectedTargetRawHex);
            return raw.Length == 2 &&
                BinaryPrimitives.ReadUInt16BigEndian(raw) / 4f == target.ExpectedTargetRpm;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void ValidateTransactionJournal(
        FanOverrideOwnershipMarker marker)
    {
        if (marker.BaselineTargets.Count != marker.Targets.Count ||
            !marker.BaselineTargets.Select(target => target.Index)
                .SequenceEqual(marker.Targets.Select(target => target.Index)))
        {
            throw new ArgumentException(
                "A fan transaction journal requires one ordered baseline for every target.",
                nameof(marker));
        }

        var expectedPayloadLength = marker.Family == FanCapabilityFamily.GlobalMaskFpe2
            ? 2
            : 4;
        if (marker.Targets.Any(target =>
                !HasValidExactTarget(target, marker.Family, expectedPayloadLength)) ||
            marker.BaselineTargets.Any(target =>
                !HasValidRawPayload(target.TargetRawHex, expectedPayloadLength)))
        {
            throw new ArgumentException(
                "A fan transaction journal contains invalid expected or baseline target bytes.",
                nameof(marker));
        }

        if (marker.Family == FanCapabilityFamily.PerFanModeFloat32)
        {
            if (marker.ExpectedGlobalModeMask is not null ||
                marker.BaselineGlobalModeMask is not null ||
                marker.BaselineTargets.Any(target => target.Mode != 0))
            {
                throw new ArgumentException(
                    "A PerFanModeFloat32 transaction journal requires an all-Apple-Auto per-fan baseline.",
                    nameof(marker));
            }

            return;
        }

        var expectedMask = GlobalMaskFpe2Strategy.GetManualMask(marker.Targets.Count);
        if (marker.ExpectedGlobalModeMask != expectedMask ||
            marker.BaselineGlobalModeMask != 0 ||
            marker.BaselineTargets.Any(target => target.Mode is not null))
        {
            throw new ArgumentException(
                "A GlobalMaskFpe2 transaction journal requires the proven takeover mask and global Apple Auto baseline.",
                nameof(marker));
        }
    }

    private static bool HasValidExactTarget(
        FanOverrideOwnershipTarget target,
        FanCapabilityFamily family,
        int expectedLength)
    {
        if (!TryGetRawPayload(target.ExpectedTargetRawHex, expectedLength, out var raw))
        {
            return false;
        }

        var decoded = family == FanCapabilityFamily.GlobalMaskFpe2
            ? BinaryPrimitives.ReadUInt16BigEndian(raw) / 4f
            : BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(raw));
        return float.IsFinite(decoded) && decoded == target.ExpectedTargetRpm;
    }

    private static bool HasValidRawPayload(string rawHex, int expectedLength)
    {
        return TryGetRawPayload(rawHex, expectedLength, out _);
    }

    private static bool TryGetRawPayload(
        string? rawHex,
        int expectedLength,
        out byte[] raw)
    {
        raw = Array.Empty<byte>();
        if (rawHex is null || rawHex.Length != expectedLength * 2)
        {
            return false;
        }

        try
        {
            raw = Convert.FromHexString(rawHex);
            return raw.Length == expectedLength;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
