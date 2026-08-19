namespace BootCampPerformanceControl.FanControl;

public interface IFanControlService
{
    Task<FanControlStatus> ReadStatusAsync(
        string model,
        CancellationToken cancellationToken);
}
