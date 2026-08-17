using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.SettingsBackup;

namespace BootCampPerformanceControl.Tests.SettingsBackup;

public sealed class InMemoryRestoreSnapshotStoreTests
{
    [Fact]
    public async Task TrySaveOriginalRestoreSnapshotAsync_SavesOnlyTheFirstSnapshot()
    {
        var store = new InMemoryRestoreSnapshotStore();
        var original = Snapshot(Guid.NewGuid(), 95);
        var laterReading = Snapshot(Guid.NewGuid(), 100);

        var firstSave = await store.TrySaveOriginalRestoreSnapshotAsync(original, CancellationToken.None);
        var secondSave = await store.TrySaveOriginalRestoreSnapshotAsync(laterReading, CancellationToken.None);
        var saved = await store.GetOriginalRestoreSnapshotAsync(CancellationToken.None);

        Assert.True(firstSave);
        Assert.False(secondSave);
        Assert.Equal(original, saved);
    }

    private static PowerStateSnapshot Snapshot(Guid schemeId, uint processorMaximum)
    {
        return new PowerStateSnapshot(
            schemeId,
            processorMaximum,
            processorMaximum,
            2,
            2,
            DateTimeOffset.UtcNow);
    }
}
