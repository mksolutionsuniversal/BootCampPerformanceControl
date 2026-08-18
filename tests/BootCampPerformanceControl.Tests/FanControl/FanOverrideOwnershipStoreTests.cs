using System.IO;
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

        Assert.Equal(marker, loaded);
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
        var replacement = original with { Fan0ExpectedTargetRpm = 5500f };

        await store.SaveNewAsync(original, CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(
            () => store.SaveNewAsync(replacement, CancellationToken.None));

        Assert.Equal(original, await store.LoadAsync(CancellationToken.None));
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
