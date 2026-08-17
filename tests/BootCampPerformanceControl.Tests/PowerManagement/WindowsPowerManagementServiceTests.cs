using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.SettingsBackup;
using BootCampPerformanceControl.Tests.TestDoubles;

namespace BootCampPerformanceControl.Tests.PowerManagement;

public sealed class WindowsPowerManagementServiceTests
{
    [Fact]
    public async Task ApplyProcessorSettingsAsync_RejectsInvalidValuesBeforeAnyNativeAccess()
    {
        var schemeId = Guid.NewGuid();
        var powerApi = new FakePowerProfileApi(
            schemeId,
            new ProcessorPowerSettings(100, 100, 2, 2));
        var service = CreateService(powerApi, new InMemoryRestoreSnapshotStore());

        var result = await service.ApplyProcessorSettingsAsync(
            new ProcessorPowerSettings(101, 95, 0, 0),
            CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal(0, powerApi.NativeWriteCount);
        Assert.Equal(0, powerApi.SetActiveSchemeCount);
    }

    [Fact]
    public async Task ApplyProcessorSettingsAsync_SavesOriginalWritesReactivatesAndVerifies()
    {
        var schemeId = Guid.NewGuid();
        var original = new ProcessorPowerSettings(100, 90, 2, 1);
        var requested = new ProcessorPowerSettings(95, 95, 0, 0);
        var powerApi = new FakePowerProfileApi(schemeId, original);
        var store = new InMemoryRestoreSnapshotStore();
        var service = CreateService(powerApi, store);

        var result = await service.ApplyProcessorSettingsAsync(requested, CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.True(result.Verification?.IsSuccessful);
        Assert.Equal(requested, powerApi.GetSettings(schemeId));
        Assert.Equal(4, powerApi.NativeWriteCount);
        Assert.Equal(1, powerApi.SetActiveSchemeCount);
        var saved = await store.GetOriginalRestoreSnapshotAsync(CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal(original, ProcessorPowerSettings.FromSnapshot(saved));
    }

    [Fact]
    public async Task ApplyProcessorSettingsAsync_RollsBackAfterANativeWriteFails()
    {
        var schemeId = Guid.NewGuid();
        var original = new ProcessorPowerSettings(100, 90, 2, 1);
        var powerApi = new FakePowerProfileApi(schemeId, original)
        {
            FailOnNativeWriteNumber = 3
        };
        var service = CreateService(powerApi, new InMemoryRestoreSnapshotStore());

        var result = await service.ApplyProcessorSettingsAsync(
            new ProcessorPowerSettings(95, 95, 0, 0),
            CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.NotNull(result.Rollback);
        Assert.True(result.Rollback.IsSuccessful);
        Assert.Equal(original, powerApi.GetSettings(schemeId));
        Assert.Equal(schemeId, powerApi.ActiveSchemeId);
    }

    [Fact]
    public async Task ApplyProcessorSettingsAsync_AttemptsEveryRollbackValueAfterAnotherRollbackWriteFails()
    {
        var schemeId = Guid.NewGuid();
        var original = new ProcessorPowerSettings(100, 90, 2, 1);
        var powerApi = new FakePowerProfileApi(schemeId, original);
        powerApi.FailOnNativeWriteNumbers.UnionWith([3, 5]);
        var service = CreateService(powerApi, new InMemoryRestoreSnapshotStore());

        var result = await service.ApplyProcessorSettingsAsync(
            new ProcessorPowerSettings(95, 95, 0, 0),
            CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.NotNull(result.Rollback);
        Assert.False(result.Rollback.IsSuccessful);
        Assert.Equal(7, powerApi.NativeWriteCount);

        var readBack = Assert.IsType<PowerStateSnapshot>(
            result.Rollback.ActiveStateAfterRollback);
        Assert.Equal(
            new ProcessorPowerSettings(100, 95, 2, 1),
            ProcessorPowerSettings.FromSnapshot(readBack));
        Assert.True(result.Rollback.ActiveStateVerification?.ProcessorMaximumAcMatches);
        Assert.False(result.Rollback.ActiveStateVerification?.ProcessorMaximumDcMatches);
        Assert.True(result.Rollback.ActiveStateVerification?.BoostModeAcMatches);
        Assert.True(result.Rollback.ActiveStateVerification?.BoostModeDcMatches);
    }

    [Fact]
    public async Task RestoreOriginalSettingsAsync_WithValidPersistentBackup_Proceeds()
    {
        using var directory = new TemporaryDirectory();
        var logger = new TestApplicationLogger();
        var snapshotFile = System.IO.Path.Combine(directory.Path, "restore-snapshot.json");
        var schemeId = Guid.NewGuid();
        var originalSettings = new ProcessorPowerSettings(92, 87, 1, 0);
        var originalSnapshot = CreateSnapshot(schemeId, originalSettings);
        var store = new JsonRestoreSnapshotStore(directory.Path, logger);
        Assert.True(await store.TrySaveOriginalRestoreSnapshotAsync(
            originalSnapshot,
            CancellationToken.None));
        var powerApi = new FakePowerProfileApi(
            schemeId,
            new ProcessorPowerSettings(95, 95, 0, 0));
        var service = new WindowsPowerManagementService(powerApi, store, logger);

        var result = await service.RestoreOriginalSettingsAsync(CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal(4, powerApi.NativeWriteCount);
        Assert.Equal(originalSettings, powerApi.GetSettings(schemeId));
        Assert.False(store.HasOriginalRestoreSnapshot);
        Assert.Null(await store.GetOriginalRestoreSnapshotAsync(CancellationToken.None));
        Assert.False(File.Exists(snapshotFile));
    }

    [Fact]
    public async Task RestoreOriginalSettingsAsync_WithDeletedPersistentBackup_ReconstructsBeforeWriting()
    {
        using var directory = new TemporaryDirectory();
        var logger = new TestApplicationLogger();
        var snapshotFile = System.IO.Path.Combine(directory.Path, "restore-snapshot.json");
        var schemeId = Guid.NewGuid();
        var originalSettings = new ProcessorPowerSettings(92, 87, 1, 0);
        var originalSnapshot = CreateSnapshot(schemeId, originalSettings);
        var store = new JsonRestoreSnapshotStore(directory.Path, logger);
        Assert.True(await store.TrySaveOriginalRestoreSnapshotAsync(
            originalSnapshot,
            CancellationToken.None));
        File.Delete(snapshotFile);
        var powerApi = new FakePowerProfileApi(
            schemeId,
            new ProcessorPowerSettings(95, 95, 0, 0));
        var service = new WindowsPowerManagementService(powerApi, store, logger);

        var result = await service.RestoreOriginalSettingsAsync(CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal(4, powerApi.NativeWriteCount);
        Assert.False(store.HasOriginalRestoreSnapshot);
        Assert.False(File.Exists(snapshotFile));
        Assert.Contains(
            logger.InformationMessages,
            message => message.Contains("reconstructed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestoreOriginalSettingsAsync_WithCorruptedPersistentBackup_RejectsWithoutNativeWrites()
    {
        using var directory = new TemporaryDirectory();
        var logger = new TestApplicationLogger();
        var snapshotFile = System.IO.Path.Combine(directory.Path, "restore-snapshot.json");
        var schemeId = Guid.NewGuid();
        var originalSnapshot = CreateSnapshot(
            schemeId,
            new ProcessorPowerSettings(92, 87, 1, 0));
        var store = new JsonRestoreSnapshotStore(directory.Path, logger);
        Assert.True(await store.TrySaveOriginalRestoreSnapshotAsync(
            originalSnapshot,
            CancellationToken.None));
        const string corruptedContents = "{ corrupted-after-cache";
        await File.WriteAllTextAsync(snapshotFile, corruptedContents);
        var powerApi = new FakePowerProfileApi(
            schemeId,
            new ProcessorPowerSettings(95, 95, 0, 0));
        var service = new WindowsPowerManagementService(powerApi, store, logger);

        var result = await service.RestoreOriginalSettingsAsync(CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Null(result.Rollback);
        Assert.Equal(0, powerApi.NativeWriteCount);
        Assert.Equal(0, powerApi.SetActiveSchemeCount);
        Assert.Equal(corruptedContents, await File.ReadAllTextAsync(snapshotFile));
        Assert.True(store.HasOriginalRestoreSnapshot);
    }

    [Fact]
    public async Task RestoreOriginalSettingsAsync_WithConflictingPersistentBackup_RejectsWithoutNativeWrites()
    {
        using var directory = new TemporaryDirectory();
        var logger = new TestApplicationLogger();
        var snapshotFile = System.IO.Path.Combine(directory.Path, "restore-snapshot.json");
        var schemeId = Guid.NewGuid();
        var originalSnapshot = CreateSnapshot(
            schemeId,
            new ProcessorPowerSettings(92, 87, 1, 0));
        var store = new JsonRestoreSnapshotStore(directory.Path, logger);
        Assert.True(await store.TrySaveOriginalRestoreSnapshotAsync(
            originalSnapshot,
            CancellationToken.None));
        var replacementStore = new JsonRestoreSnapshotStore(directory.Path, logger);
        var conflictingSnapshot = originalSnapshot with
        {
            CapturedAt = originalSnapshot.CapturedAt.AddSeconds(1)
        };
        await replacementStore.ReplaceOriginalRestoreSnapshotAsync(
            conflictingSnapshot,
            CancellationToken.None);
        var conflictingContents = await File.ReadAllBytesAsync(snapshotFile);
        var powerApi = new FakePowerProfileApi(
            schemeId,
            new ProcessorPowerSettings(95, 95, 0, 0));
        var service = new WindowsPowerManagementService(powerApi, store, logger);

        var result = await service.RestoreOriginalSettingsAsync(CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Null(result.Rollback);
        Assert.Equal(0, powerApi.NativeWriteCount);
        Assert.Equal(0, powerApi.SetActiveSchemeCount);
        Assert.Equal(conflictingContents, await File.ReadAllBytesAsync(snapshotFile));
        Assert.True(store.HasOriginalRestoreSnapshot);
    }

    [Fact]
    public async Task RestoreOriginalSettingsAsync_RestoresExactOriginalSchemeAndValues()
    {
        var originalSchemeId = Guid.NewGuid();
        var currentSchemeId = Guid.NewGuid();
        var originalSettings = new ProcessorPowerSettings(92, 87, 1, 0);
        var currentSettings = new ProcessorPowerSettings(95, 95, 0, 0);
        var powerApi = new FakePowerProfileApi(currentSchemeId, currentSettings);
        powerApi.AddScheme(
            originalSchemeId,
            new ProcessorPowerSettings(100, 100, 6, 6));
        var store = new InMemoryRestoreSnapshotStore();
        var originalSnapshot = new PowerStateSnapshot(
            originalSchemeId,
            originalSettings.ProcessorMaximumAc,
            originalSettings.ProcessorMaximumDc,
            originalSettings.BoostModeAc,
            originalSettings.BoostModeDc,
            DateTimeOffset.UtcNow.AddMinutes(-10));
        await store.TrySaveOriginalRestoreSnapshotAsync(originalSnapshot, CancellationToken.None);
        var service = CreateService(powerApi, store);

        var result = await service.RestoreOriginalSettingsAsync(CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.True(result.Verification?.IsSuccessful);
        Assert.Equal(originalSchemeId, powerApi.ActiveSchemeId);
        Assert.Equal(originalSettings, powerApi.GetSettings(originalSchemeId));
        Assert.Null(await store.GetOriginalRestoreSnapshotAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SuccessfulRestore_AllowsLaterApplyToCaptureANewOriginalSnapshot()
    {
        var schemeId = Guid.NewGuid();
        var previousOriginal = new ProcessorPowerSettings(92, 87, 1, 0);
        var store = new InMemoryRestoreSnapshotStore();
        await store.TrySaveOriginalRestoreSnapshotAsync(
            CreateSnapshot(schemeId, previousOriginal),
            CancellationToken.None);
        var powerApi = new FakePowerProfileApi(
            schemeId,
            new ProcessorPowerSettings(95, 95, 0, 0));
        var service = CreateService(powerApi, store);

        var restoreResult = await service.RestoreOriginalSettingsAsync(CancellationToken.None);
        Assert.True(restoreResult.IsSuccessful);
        Assert.False(store.HasOriginalRestoreSnapshot);

        var newBaseline = new ProcessorPowerSettings(88, 84, 3, 2);
        powerApi.AddScheme(schemeId, newBaseline);
        var applyResult = await service.ApplyProcessorSettingsAsync(
            new ProcessorPowerSettings(95, 95, 0, 0),
            CancellationToken.None);

        Assert.True(applyResult.IsSuccessful);
        var newOriginal = await store.GetOriginalRestoreSnapshotAsync(CancellationToken.None);
        Assert.NotNull(newOriginal);
        Assert.Equal(newBaseline, ProcessorPowerSettings.FromSnapshot(newOriginal));
        Assert.NotEqual(previousOriginal, ProcessorPowerSettings.FromSnapshot(newOriginal));
    }

    [Fact]
    public async Task RestoreVerificationFailure_DoesNotClearOriginalSnapshot()
    {
        var schemeId = Guid.NewGuid();
        var originalSettings = new ProcessorPowerSettings(92, 87, 1, 0);
        var originalSnapshot = CreateSnapshot(schemeId, originalSettings);
        var store = new InMemoryRestoreSnapshotStore();
        await store.TrySaveOriginalRestoreSnapshotAsync(originalSnapshot, CancellationToken.None);
        var powerApi = new FakePowerProfileApi(
            schemeId,
            new ProcessorPowerSettings(95, 95, 0, 0));
        powerApi.IgnoreNativeWriteNumbers.Add(1);
        var service = CreateService(powerApi, store);

        var result = await service.RestoreOriginalSettingsAsync(CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.False(result.Verification?.IsSuccessful);
        Assert.NotNull(result.Rollback);
        Assert.Equal(
            originalSnapshot,
            await store.GetOriginalRestoreSnapshotAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RestoreCleanupFailure_ReturnsFailureWithoutRollingBackRestoredState()
    {
        var schemeId = Guid.NewGuid();
        var originalSettings = new ProcessorPowerSettings(92, 87, 1, 0);
        var originalSnapshot = CreateSnapshot(schemeId, originalSettings);
        var store = new FailingClearRestoreSnapshotStore();
        await store.TrySaveOriginalRestoreSnapshotAsync(originalSnapshot, CancellationToken.None);
        var powerApi = new FakePowerProfileApi(
            schemeId,
            new ProcessorPowerSettings(95, 95, 0, 0));
        var service = CreateService(powerApi, store);

        var result = await service.RestoreOriginalSettingsAsync(CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.True(result.Verification?.IsSuccessful);
        Assert.Null(result.Rollback);
        Assert.Equal(originalSettings, powerApi.GetSettings(schemeId));
        Assert.Contains("cleanup failed", result.FailureMessage, StringComparison.Ordinal);
        Assert.True(store.ClearAttempted);
        Assert.Equal(CancellationToken.None, store.ClearCancellationToken);
        Assert.Equal(
            originalSnapshot,
            await store.GetOriginalRestoreSnapshotAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RestoreOriginalSettingsAsync_RollsBackBothSchemesAfterAWriteFailure()
    {
        var originalSchemeId = Guid.NewGuid();
        var currentSchemeId = Guid.NewGuid();
        var originalRestoreSettings = new ProcessorPowerSettings(92, 87, 1, 0);
        var targetStateBeforeRestore = new ProcessorPowerSettings(100, 100, 6, 6);
        var activeStateBeforeRestore = new ProcessorPowerSettings(95, 95, 0, 0);
        var powerApi = new FakePowerProfileApi(currentSchemeId, activeStateBeforeRestore)
        {
            FailOnNativeWriteNumber = 3
        };
        powerApi.AddScheme(originalSchemeId, targetStateBeforeRestore);
        var store = new InMemoryRestoreSnapshotStore();
        var originalSnapshot = new PowerStateSnapshot(
            originalSchemeId,
            originalRestoreSettings.ProcessorMaximumAc,
            originalRestoreSettings.ProcessorMaximumDc,
            originalRestoreSettings.BoostModeAc,
            originalRestoreSettings.BoostModeDc,
            DateTimeOffset.UtcNow.AddMinutes(-10));
        await store.TrySaveOriginalRestoreSnapshotAsync(originalSnapshot, CancellationToken.None);
        var service = CreateService(powerApi, store);

        var result = await service.RestoreOriginalSettingsAsync(CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.NotNull(result.Rollback);
        Assert.True(result.Rollback.IsSuccessful);
        Assert.Equal(currentSchemeId, powerApi.ActiveSchemeId);
        Assert.Equal(activeStateBeforeRestore, powerApi.GetSettings(currentSchemeId));
        Assert.Equal(targetStateBeforeRestore, powerApi.GetSettings(originalSchemeId));
        Assert.Equal(
            originalSnapshot,
            await store.GetOriginalRestoreSnapshotAsync(CancellationToken.None));
    }

    private static WindowsPowerManagementService CreateService(
        IPowerProfileApi powerApi,
        IRestoreSnapshotStore store)
    {
        return new WindowsPowerManagementService(
            powerApi,
            store,
            new TestApplicationLogger());
    }

    private static PowerStateSnapshot CreateSnapshot(
        Guid schemeId,
        ProcessorPowerSettings settings)
    {
        return new PowerStateSnapshot(
            schemeId,
            settings.ProcessorMaximumAc,
            settings.ProcessorMaximumDc,
            settings.BoostModeAc,
            settings.BoostModeDc,
            DateTimeOffset.UtcNow.AddMinutes(-10));
    }

    private sealed class FailingClearRestoreSnapshotStore : IRestoreSnapshotStore
    {
        private readonly InMemoryRestoreSnapshotStore _innerStore = new();

        public bool HasOriginalRestoreSnapshot => _innerStore.HasOriginalRestoreSnapshot;

        public bool ClearAttempted { get; private set; }

        public CancellationToken ClearCancellationToken { get; private set; }

        public Task<bool> TrySaveOriginalRestoreSnapshotAsync(
            PowerStateSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            return _innerStore.TrySaveOriginalRestoreSnapshotAsync(snapshot, cancellationToken);
        }

        public Task<PowerStateSnapshot?> GetOriginalRestoreSnapshotAsync(
            CancellationToken cancellationToken)
        {
            return _innerStore.GetOriginalRestoreSnapshotAsync(cancellationToken);
        }

        public Task ReplaceOriginalRestoreSnapshotAsync(
            PowerStateSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            return _innerStore.ReplaceOriginalRestoreSnapshotAsync(snapshot, cancellationToken);
        }

        public Task ClearOriginalRestoreSnapshotAsync(CancellationToken cancellationToken)
        {
            ClearAttempted = true;
            ClearCancellationToken = cancellationToken;
            throw new IOException("Configured restore-session cleanup failure.");
        }
    }
}
