using System.Text;
using System.Text.Json;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.SettingsBackup;
using BootCampPerformanceControl.Tests.TestDoubles;

namespace BootCampPerformanceControl.Tests.SettingsBackup;

public sealed class JsonRestoreSnapshotStoreTests
{
    [Fact]
    public async Task MissingFile_TrySaveCreatesSnapshotSuccessfully()
    {
        using var directory = new TemporaryDirectory();
        var logger = new TestApplicationLogger();
        var capturedAt = DateTimeOffset.Parse("2026-08-17T12:34:56+00:00");
        var original = new PowerStateSnapshot(
            Guid.NewGuid(),
            95,
            90,
            0,
            2,
            capturedAt);
        var store = new JsonRestoreSnapshotStore(directory.Path, logger);

        var saved = await store.TrySaveOriginalRestoreSnapshotAsync(original, CancellationToken.None);

        var snapshotFile = System.IO.Path.Combine(directory.Path, "restore-snapshot.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(snapshotFile));
        var root = document.RootElement;
        Assert.True(saved);
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(original.SchemeId, root.GetProperty("schemeId").GetGuid());
        Assert.Equal(95U, root.GetProperty("processorMaximumAc").GetUInt32());
        Assert.Equal(90U, root.GetProperty("processorMaximumDc").GetUInt32());
        Assert.Equal(0U, root.GetProperty("boostModeAc").GetUInt32());
        Assert.Equal(2U, root.GetProperty("boostModeDc").GetUInt32());
        Assert.Equal(capturedAt, root.GetProperty("capturedAt").GetDateTimeOffset());
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));

        var reloadedStore = new JsonRestoreSnapshotStore(directory.Path, logger);
        var reloaded = await reloadedStore.GetOriginalRestoreSnapshotAsync(CancellationToken.None);
        Assert.Equal(original, reloaded);
    }

    [Fact]
    public async Task CachedSnapshotWithMatchingFile_IsAcceptedAndUnchanged()
    {
        using var directory = new TemporaryDirectory();
        var logger = new TestApplicationLogger();
        var snapshotFile = System.IO.Path.Combine(directory.Path, "restore-snapshot.json");
        var original = CreateSnapshot(95, 90);
        var initialStore = new JsonRestoreSnapshotStore(directory.Path, logger);
        Assert.True(await initialStore.TrySaveOriginalRestoreSnapshotAsync(
            original,
            CancellationToken.None));
        var originalContents = await File.ReadAllBytesAsync(snapshotFile);

        var reloadedStore = new JsonRestoreSnapshotStore(directory.Path, logger);
        var laterReading = CreateSnapshot(100, 100);

        var saved = await reloadedStore.TrySaveOriginalRestoreSnapshotAsync(
            laterReading,
            CancellationToken.None);

        Assert.False(saved);
        Assert.Equal(originalContents, await File.ReadAllBytesAsync(snapshotFile));
        Assert.Equal(
            original,
            await reloadedStore.GetOriginalRestoreSnapshotAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CachedSnapshotWithDeletedFile_RecreatesFileFromCachedOriginal()
    {
        using var directory = new TemporaryDirectory();
        var logger = new TestApplicationLogger();
        var snapshotFile = System.IO.Path.Combine(directory.Path, "restore-snapshot.json");
        var original = CreateSnapshot(100, 100);
        var initialStore = new JsonRestoreSnapshotStore(directory.Path, logger);
        Assert.True(await initialStore.TrySaveOriginalRestoreSnapshotAsync(
            original,
            CancellationToken.None));
        var cachedStore = new JsonRestoreSnapshotStore(directory.Path, logger);
        File.Delete(snapshotFile);
        var differentCurrentState = CreateSnapshot(95, 95);

        var saved = await cachedStore.TrySaveOriginalRestoreSnapshotAsync(
            differentCurrentState,
            CancellationToken.None);

        Assert.False(saved);
        Assert.True(File.Exists(snapshotFile));
        var reloadedStore = new JsonRestoreSnapshotStore(directory.Path, logger);
        var reconstructed = await reloadedStore.GetOriginalRestoreSnapshotAsync(
            CancellationToken.None);
        Assert.Equal(original, reconstructed);
        Assert.NotEqual(differentCurrentState, reconstructed);
        Assert.Empty(Directory.GetFiles(directory.Path, "restore-snapshot.*.tmp"));
        Assert.Contains(
            logger.InformationMessages,
            message => message.Contains("reconstructed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CachedSnapshotWithMalformedFile_FailsAndPreservesExactContents()
    {
        using var directory = new TemporaryDirectory();
        var logger = new TestApplicationLogger();
        var snapshotFile = System.IO.Path.Combine(directory.Path, "restore-snapshot.json");
        var original = CreateSnapshot(100, 100);
        var initialStore = new JsonRestoreSnapshotStore(directory.Path, logger);
        Assert.True(await initialStore.TrySaveOriginalRestoreSnapshotAsync(
            original,
            CancellationToken.None));
        var cachedStore = new JsonRestoreSnapshotStore(directory.Path, logger);
        var malformedContents = Encoding.UTF8.GetBytes("{ broken-after-cache\r\noriginal-backup");
        await File.WriteAllBytesAsync(snapshotFile, malformedContents);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => cachedStore.TrySaveOriginalRestoreSnapshotAsync(
                CreateSnapshot(95, 95),
                CancellationToken.None));

        Assert.Equal(malformedContents, await File.ReadAllBytesAsync(snapshotFile));
        Assert.Empty(Directory.GetFiles(directory.Path, "restore-snapshot.*.tmp"));
    }

    [Fact]
    public async Task CachedSnapshotWithDifferentValidFile_FailsAndPreservesExactContents()
    {
        using var directory = new TemporaryDirectory();
        var logger = new TestApplicationLogger();
        var snapshotFile = System.IO.Path.Combine(directory.Path, "restore-snapshot.json");
        var original = CreateSnapshot(100, 100);
        var initialStore = new JsonRestoreSnapshotStore(directory.Path, logger);
        Assert.True(await initialStore.TrySaveOriginalRestoreSnapshotAsync(
            original,
            CancellationToken.None));
        var cachedStore = new JsonRestoreSnapshotStore(directory.Path, logger);
        var replacementStore = new JsonRestoreSnapshotStore(directory.Path, logger);
        var conflictingSnapshot = original with { CapturedAt = original.CapturedAt.AddSeconds(1) };
        await replacementStore.ReplaceOriginalRestoreSnapshotAsync(
            conflictingSnapshot,
            CancellationToken.None);
        var conflictingContents = await File.ReadAllBytesAsync(snapshotFile);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => cachedStore.TrySaveOriginalRestoreSnapshotAsync(
                CreateSnapshot(95, 95),
                CancellationToken.None));

        Assert.Contains("conflicts", exception.Message, StringComparison.Ordinal);
        Assert.Equal(conflictingContents, await File.ReadAllBytesAsync(snapshotFile));
        Assert.Empty(Directory.GetFiles(directory.Path, "restore-snapshot.*.tmp"));
    }

    [Fact]
    public async Task ExistingMalformedJson_TrySaveFailsAndPreservesExactContents()
    {
        using var directory = new TemporaryDirectory();
        var snapshotFile = System.IO.Path.Combine(directory.Path, "restore-snapshot.json");
        var originalContents = Encoding.UTF8.GetBytes("{ definitely-not-json\r\noriginal-backup");
        await File.WriteAllBytesAsync(snapshotFile, originalContents);
        var logger = new TestApplicationLogger();
        var store = new JsonRestoreSnapshotStore(directory.Path, logger);
        var currentState = CreateSnapshot(95, 95);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => store.TrySaveOriginalRestoreSnapshotAsync(currentState, CancellationToken.None));

        Assert.Contains("blocks automatic initialization", exception.Message, StringComparison.Ordinal);
        Assert.Equal(originalContents, await File.ReadAllBytesAsync(snapshotFile));
        Assert.Empty(Directory.GetFiles(directory.Path, "restore-snapshot.*.tmp"));
    }

    [Fact]
    public async Task ExistingUnsupportedSchema_TrySaveFailsAndPreservesExactContents()
    {
        using var directory = new TemporaryDirectory();
        var snapshotFile = System.IO.Path.Combine(directory.Path, "restore-snapshot.json");
        var originalContents = Encoding.UTF8.GetBytes(
            $$"""
            {
              "schemaVersion": 999,
              "capturedAt": "2026-08-17T12:34:56+00:00",
              "schemeId": "{{Guid.NewGuid()}}",
              "processorMaximumAc": 100,
              "processorMaximumDc": 100,
              "boostModeAc": 2,
              "boostModeDc": 2
            }
            """);
        await File.WriteAllBytesAsync(snapshotFile, originalContents);
        var logger = new TestApplicationLogger();
        var store = new JsonRestoreSnapshotStore(directory.Path, logger);
        var currentState = CreateSnapshot(95, 95);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => store.TrySaveOriginalRestoreSnapshotAsync(currentState, CancellationToken.None));

        Assert.Contains("blocks automatic initialization", exception.Message, StringComparison.Ordinal);
        Assert.Equal(originalContents, await File.ReadAllBytesAsync(snapshotFile));
        Assert.Empty(Directory.GetFiles(directory.Path, "restore-snapshot.*.tmp"));
    }

    [Fact]
    public void JsonWithoutSchemaVersion_IsRejected()
    {
        using var directory = new TemporaryDirectory();
        var snapshotFile = System.IO.Path.Combine(directory.Path, "restore-snapshot.json");
        File.WriteAllText(
            snapshotFile,
            $$"""
            {
              "capturedAt": "2026-08-17T12:34:56+00:00",
              "schemeId": "{{Guid.NewGuid()}}",
              "processorMaximumAc": 95,
              "processorMaximumDc": 95,
              "boostModeAc": 0,
              "boostModeDc": 0
            }
            """);
        var logger = new TestApplicationLogger();

        var store = new JsonRestoreSnapshotStore(directory.Path, logger);

        Assert.False(store.HasOriginalRestoreSnapshot);
        Assert.Contains(
            logger.Errors,
            error => error.Message.Contains("invalid", StringComparison.OrdinalIgnoreCase));
    }

    private static PowerStateSnapshot CreateSnapshot(uint processorMaximumAc, uint processorMaximumDc)
    {
        return new PowerStateSnapshot(
            Guid.NewGuid(),
            processorMaximumAc,
            processorMaximumDc,
            0,
            0,
            DateTimeOffset.UtcNow);
    }
}
