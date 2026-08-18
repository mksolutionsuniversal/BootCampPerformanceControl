namespace BootCampPerformanceControl.FanControl;

internal interface IFanOverrideOwnershipStore
{
    Task<FanOverrideOwnershipMarker?> LoadAsync(CancellationToken cancellationToken);

    Task SaveNewAsync(
        FanOverrideOwnershipMarker marker,
        CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}
