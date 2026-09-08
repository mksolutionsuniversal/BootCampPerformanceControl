using System.IO;
using System.Text.Json;
using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.Logging;

namespace BootCampPerformanceControl.Tests.FanControl;

public sealed class FanOverrideOwnershipStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"BootCampPerformanceControl.Tests.{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveLoadClear_RoundTripsMarker()
    {
        var store = new JsonFanOverrideOwnershipStore(_directory, new TestLogger());
        var marker = CreateMarker();

        await store.SaveNewAsync(marker, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(marker.Model, loaded.Model);
        Assert.Equal(marker.CreatedAtUtc, loaded.CreatedAtUtc);
        Assert.Equal(marker.Family, loaded.Family);
        Assert.Equal(marker.Targets, loaded.Targets);
        Assert.True(File.Exists(GetMarkerPath()));

        await store.ClearAsync(CancellationToken.None);

        Assert.Null(await store.LoadAsync(CancellationToken.None));
        Assert.False(File.Exists(GetMarkerPath()));
    }

    [Fact]
    public async Task SaveNewAsync_RefusesToOverwriteExistingMarker()
    {
        var store = new JsonFanOverrideOwnershipStore(_directory, new TestLogger());
        var original = CreateMarker();
        var replacement = new FanOverrideOwnershipMarker(
            original.Model,
            [
                new FanOverrideOwnershipTarget(new FanIndex(0), 5500f),
                original.Targets[1]
            ],
            original.CreatedAtUtc);

        await store.SaveNewAsync(original, CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(
            () => store.SaveNewAsync(replacement, CancellationToken.None));

        var loaded = await store.LoadAsync(CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(original.Model, loaded.Model);
        Assert.Equal(original.CreatedAtUtc, loaded.CreatedAtUtc);
        Assert.Equal(original.Targets, loaded.Targets);
    }

    [Fact]
    public async Task SaveLoad_SchemaVersion4TransactionJournalPreservesExactBaseline()
    {
        var store = new JsonFanOverrideOwnershipStore(_directory, new TestLogger());
        var journal = CreatePerFanJournal();

        await store.SaveNewAsync(journal, CancellationToken.None);

        using (var document = JsonDocument.Parse(await File.ReadAllTextAsync(GetMarkerPath())))
        {
            Assert.Equal(4, document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(2, document.RootElement.GetProperty("baselineTargets").GetArrayLength());
        }

        var loaded = await store.LoadAsync(CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.True(loaded.IsTransactionJournal);
        Assert.Equal(journal.Targets, loaded.Targets);
        Assert.Equal(journal.BaselineTargets, loaded.BaselineTargets);
        Assert.Null(loaded.BaselineGlobalModeMask);
    }

    [Fact]
    public async Task ReplaceAsync_CompletedValidatedPerFanTransactionWritesLegacySchemaVersion1()
    {
        var store = new JsonFanOverrideOwnershipStore(_directory, new TestLogger());
        var journal = CreatePerFanJournal();
        await store.SaveNewAsync(journal, CancellationToken.None);

        await store.ReplaceAsync(
            journal.ToFinalOwnershipMarker(),
            CancellationToken.None);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(GetMarkerPath()));
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.False(document.RootElement.TryGetProperty("baselineTargets", out _));
        var loaded = await store.LoadAsync(CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.False(loaded.IsTransactionJournal);
    }

    [Fact]
    public async Task LoadAsync_PreservesMalformedMarkerAndThrows()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(GetMarkerPath(), "{ definitely-not-json ");
        var store = new JsonFanOverrideOwnershipStore(_directory, new TestLogger());

        await Assert.ThrowsAnyAsync<Exception>(
            () => store.LoadAsync(CancellationToken.None));

        Assert.True(File.Exists(GetMarkerPath()));
        Assert.Equal(
            "{ definitely-not-json ",
            await File.ReadAllTextAsync(GetMarkerPath()));
    }

    [Fact]
    public async Task LoadAsync_PreservesUnsupportedSchemaAndThrows()
    {
        Directory.CreateDirectory(_directory);
        var json = """
            {
              "schemaVersion": 999,
              "model": "MacBookPro16,1",
              "fan0ExpectedTargetRpm": 5616,
              "fan1ExpectedTargetRpm": 5200,
              "createdAtUtc": "2026-08-18T19:00:00+00:00"
            }
            """;
        await File.WriteAllTextAsync(GetMarkerPath(), json);
        var store = new JsonFanOverrideOwnershipStore(_directory, new TestLogger());

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => store.LoadAsync(CancellationToken.None));

        Assert.Contains("schema version", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(GetMarkerPath()));
    }

    [Fact]
    public async Task LoadAsync_SchemaVersion1MigratesLegacyTwoFanMarker()
    {
        Directory.CreateDirectory(_directory);
        var json = """
            {
              "schemaVersion": 1,
              "model": "MacBookPro16,1",
              "fan0ExpectedTargetRpm": 5616,
              "fan1ExpectedTargetRpm": 5200,
              "createdAtUtc": "2026-08-18T19:00:00+00:00"
            }
            """;
        await File.WriteAllTextAsync(GetMarkerPath(), json);
        var store = new JsonFanOverrideOwnershipStore(_directory, new TestLogger());

        var marker = await store.LoadAsync(CancellationToken.None);

        Assert.NotNull(marker);
        Assert.Equal("MacBookPro16,1", marker.Model);
        Assert.Equal(FanCapabilityFamily.PerFanModeFloat32, marker.Family);
        Assert.Equal(new[] { 0, 1 }, marker.Targets.Select(target => target.Index.Value));
        Assert.Equal(new[] { 5616f, 5200f }, marker.Targets.Select(target => target.ExpectedTargetRpm));
        Assert.Equal(
            new DateTimeOffset(2026, 8, 18, 19, 0, 0, TimeSpan.Zero),
            marker.CreatedAtUtc);
        Assert.True(File.Exists(GetMarkerPath()));
    }

    [Fact]
    public async Task LoadAsync_SchemaVersion2IsInterpretedOnlyAsPerFanFamily()
    {
        Directory.CreateDirectory(_directory);
        var json = """
            {
              "schemaVersion": 2,
              "model": "Macmini8,1",
              "targets": [
                { "index": 0, "expectedTargetRpm": 2900 }
              ],
              "createdAtUtc": "2026-08-18T19:00:00+00:00"
            }
            """;
        await File.WriteAllTextAsync(GetMarkerPath(), json);
        var store = new JsonFanOverrideOwnershipStore(_directory, new TestLogger());

        var marker = await store.LoadAsync(CancellationToken.None);

        Assert.NotNull(marker);
        Assert.Equal(FanCapabilityFamily.PerFanModeFloat32, marker.Family);
        Assert.Null(marker.ExpectedGlobalModeMask);
    }

    [Fact]
    public async Task SaveNewAsync_WritesFamilyAwareSchemaVersion3()
    {
        var store = new JsonFanOverrideOwnershipStore(_directory, new TestLogger());
        var marker = new FanOverrideOwnershipMarker(
            "MacBookPro16,1",
            [
                new FanOverrideOwnershipTarget(new FanIndex(0), 5616f),
                new FanOverrideOwnershipTarget(new FanIndex(1), 5200f),
                new FanOverrideOwnershipTarget(new FanIndex(2), 4800f)
            ],
            new DateTimeOffset(2026, 8, 18, 19, 0, 0, TimeSpan.Zero));

        await store.SaveNewAsync(marker, CancellationToken.None);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(GetMarkerPath()));
        Assert.Equal(3, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("PerFanModeFloat32", document.RootElement.GetProperty("capabilityFamily").GetString());
        Assert.Equal(3, document.RootElement.GetProperty("reportedFanCount").GetInt32());
        var targets = document.RootElement.GetProperty("targets");
        Assert.Equal(3, targets.GetArrayLength());
        Assert.Equal(2, targets[2].GetProperty("index").GetInt32());
        Assert.Equal(4800f, targets[2].GetProperty("expectedTargetRpm").GetSingle());
        Assert.False(document.RootElement.TryGetProperty("fan0ExpectedTargetRpm", out _));
    }

    [Fact]
    public async Task SaveNewAsync_ExactLegacyModelRetainsDowngradeCompatibleSchemaVersion1()
    {
        var store = new JsonFanOverrideOwnershipStore(_directory, new TestLogger());

        await store.SaveNewAsync(CreateMarker(), CancellationToken.None);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(GetMarkerPath()));
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(5616f, document.RootElement.GetProperty("fan0ExpectedTargetRpm").GetSingle());
        Assert.Equal(5200f, document.RootElement.GetProperty("fan1ExpectedTargetRpm").GetSingle());
        Assert.False(document.RootElement.TryGetProperty("capabilityFamily", out _));
    }

    [Fact]
    public async Task SaveNewAsync_OtherModelWithTwoFansWritesSchemaVersion3()
    {
        var store = new JsonFanOverrideOwnershipStore(_directory, new TestLogger());
        var marker = new FanOverrideOwnershipMarker(
            "Macmini8,1",
            [
                new FanOverrideOwnershipTarget(new FanIndex(0), 2900f),
                new FanOverrideOwnershipTarget(new FanIndex(1), 3100f)
            ],
            new DateTimeOffset(2026, 8, 18, 19, 0, 0, TimeSpan.Zero));

        await store.SaveNewAsync(marker, CancellationToken.None);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(GetMarkerPath()));
        Assert.Equal(3, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("targets").GetArrayLength());
        Assert.False(document.RootElement.TryGetProperty("fan0ExpectedTargetRpm", out _));
    }

    [Fact]
    public async Task SaveLoad_GlobalMaskMarkerPreservesFamilyMaskTopologyAndExactTargets()
    {
        var store = new JsonFanOverrideOwnershipStore(_directory, new TestLogger());
        var marker = new FanOverrideOwnershipMarker(
            "MacBookPro12,1",
            FanCapabilityFamily.GlobalMaskFpe2,
            [
                new FanOverrideOwnershipTarget(new FanIndex(0), 6199f)
                {
                    ExpectedTargetRawHex = "60DC"
                }
            ],
            new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero),
            expectedGlobalModeMask: 0x0001);

        await store.SaveNewAsync(marker, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(FanCapabilityFamily.GlobalMaskFpe2, loaded.Family);
        Assert.Equal((ushort)0x0001, loaded.ExpectedGlobalModeMask);
        Assert.Equal("60DC", loaded.Targets[0].ExpectedTargetRawHex);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string GetMarkerPath()
    {
        return Path.Combine(_directory, "fan-override-ownership.json");
    }

    private static FanOverrideOwnershipMarker CreateMarker()
    {
        return new FanOverrideOwnershipMarker(
            "MacBookPro16,1",
            5616f,
            5200f,
            new DateTimeOffset(2026, 8, 18, 19, 0, 0, TimeSpan.Zero));
    }

    private static FanOverrideOwnershipMarker CreatePerFanJournal()
    {
        return new FanOverrideOwnershipMarker(
            "MacBookPro16,1",
            FanCapabilityFamily.PerFanModeFloat32,
            [
                new FanOverrideOwnershipTarget(new FanIndex(0), 5616f)
                {
                    ExpectedTargetRawHex = Convert.ToHexString(BitConverter.GetBytes(5616f))
                },
                new FanOverrideOwnershipTarget(new FanIndex(1), 5200f)
                {
                    ExpectedTargetRawHex = Convert.ToHexString(BitConverter.GetBytes(5200f))
                }
            ],
            new DateTimeOffset(2026, 8, 18, 19, 0, 0, TimeSpan.Zero),
            expectedGlobalModeMask: null)
        {
            BaselineTargets =
            [
                new FanOverrideBaselineTarget(
                    new FanIndex(0),
                    Convert.ToHexString(BitConverter.GetBytes(1836f)),
                    Mode: 0),
                new FanOverrideBaselineTarget(
                    new FanIndex(1),
                    Convert.ToHexString(BitConverter.GetBytes(1700f)),
                    Mode: 0)
            ]
        };
    }

    private sealed class TestLogger : IApplicationLogger
    {
        public void Info(string message)
        {
        }

        public void Error(string message, Exception exception)
        {
        }
    }
}
