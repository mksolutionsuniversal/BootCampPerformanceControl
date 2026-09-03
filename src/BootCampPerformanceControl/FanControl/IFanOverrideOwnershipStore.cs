namespace BootCampPerformanceControl.FanControl;

internal interface IFanOverrideOwnershipStore : IFanOverrideOwnershipReader
{
    Task SaveNewAsync(
        FanOverrideOwnershipMarker marker,
        CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}
