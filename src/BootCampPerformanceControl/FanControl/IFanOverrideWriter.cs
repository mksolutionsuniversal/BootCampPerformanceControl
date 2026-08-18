namespace BootCampPerformanceControl.FanControl;

internal interface IFanOverrideWriter
{
    // Implementations must return only after the requested fan state has been
    // re-read and verified. Throw or cancel on any partial or unverifiable state.
    Task ApplyMaximumSafeRpmAsync(
        FanMaximumSafeRpmPlan plan,
        CancellationToken cancellationToken);

    // Implementations must perform a fresh ownership/readback check immediately
    // before restoring Apple Auto, then return only after both fan modes have been
    // re-read and verified as Apple Auto.
    Task RestoreAppleAutoAsync(
        FanOverrideOwnershipMarker ownershipMarker,
        CancellationToken cancellationToken);
}
