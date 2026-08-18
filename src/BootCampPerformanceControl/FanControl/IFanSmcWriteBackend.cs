namespace BootCampPerformanceControl.FanControl;

internal interface IFanSmcWriteBackend
{
    Task SetManualModeAsync(
        FanIndex fan,
        CancellationToken cancellationToken);

    Task SetTargetRpmAsync(
        FanIndex fan,
        float targetRpm,
        CancellationToken cancellationToken);

    Task SetAppleAutoAsync(
        FanIndex fan,
        CancellationToken cancellationToken);
}
