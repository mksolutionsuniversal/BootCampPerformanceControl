namespace BootCampPerformanceControl.FanControl;

public sealed class UnavailableFanControlService : IFanControlService
{
    public FanControlStatus GetStatus()
    {
        return new FanControlStatus(
            IsAvailable: false,
            DisplayText: "Fan Control: Not available");
    }
}
