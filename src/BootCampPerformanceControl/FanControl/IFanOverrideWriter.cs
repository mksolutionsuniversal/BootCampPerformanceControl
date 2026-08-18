namespace BootCampPerformanceControl.FanControl;

internal interface IFanOverrideWriter
{
    // Implementations must return only after the requested fan state has been
    // re-read and verified. Throw or cancel on any partial or unverifiable state.
    Task ApplyMaximumSafeRpmAsync(
        FanMaximumSafeRpmPlan plan,
        CancellationToken cancellationToken);

    // Implementations must return only after both fan modes have been re-read
    // and verified as Apple Auto. Throw or cancel if verification is incomplete.
    Task RestoreAppleAutoAsync(CancellationToken cancellationToken);
}
