namespace BootCampPerformanceControl.FanControl;

internal interface IFanOverrideWriter
{
    Task ApplyMaximumSafeRpmAsync(
        FanMaximumSafeRpmPlan plan,
        CancellationToken cancellationToken);

    Task RestoreAppleAutoAsync(CancellationToken cancellationToken);
}
