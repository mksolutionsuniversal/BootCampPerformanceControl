namespace BootCampPerformanceControl.FanControl;

internal interface IFanCapabilityProbe
{
    Task<FanControlCapabilityResult> ProbeAsync(
        string model,
        CancellationToken cancellationToken);
}
