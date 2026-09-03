namespace BootCampPerformanceControl.FanControl;

internal interface IFanOverrideOwnershipReader
{
    Task<FanOverrideOwnershipMarker?> LoadAsync(CancellationToken cancellationToken);
}
