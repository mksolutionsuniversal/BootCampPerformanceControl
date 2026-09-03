using BootCampPerformanceControl.FanControl;

namespace BootCampPerformanceControl.Tests.TestDoubles;

internal sealed class TestFanOverrideOwnershipStore : IFanOverrideOwnershipStore
{
    public FanOverrideOwnershipMarker? Marker { get; set; }

    public Exception? LoadException { get; set; }

    public int LoadCallCount { get; private set; }

    public int SaveCallCount { get; private set; }

    public int ClearCallCount { get; private set; }

    public List<CancellationToken> LoadTokens { get; } = [];

    public Func<CancellationToken, Task<FanOverrideOwnershipMarker?>>? LoadHandler { get; set; }

    public Func<FanOverrideOwnershipMarker, CancellationToken, Task>? SaveHandler { get; set; }

    public Func<CancellationToken, Task>? ClearHandler { get; set; }

    public Task<FanOverrideOwnershipMarker?> LoadAsync(CancellationToken cancellationToken)
    {
        LoadCallCount++;
        LoadTokens.Add(cancellationToken);

        if (LoadException is not null)
        {
            throw LoadException;
        }

        if (LoadHandler is not null)
        {
            return LoadHandler(cancellationToken);
        }

        return Task.FromResult(Marker);
    }

    public Task SaveNewAsync(
        FanOverrideOwnershipMarker marker,
        CancellationToken cancellationToken)
    {
        SaveCallCount++;
        Marker = marker;

        if (SaveHandler is not null)
        {
            return SaveHandler(marker, cancellationToken);
        }

        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        ClearCallCount++;
        Marker = null;

        if (ClearHandler is not null)
        {
            return ClearHandler(cancellationToken);
        }

        return Task.CompletedTask;
    }
}
