namespace BootCampPerformanceControl.FanControl;

internal interface IFanExecutionSession : IAsyncDisposable
{
    IFanCapabilityProbe CapabilityProbe { get; }

    IFanOverrideCoordinator OverrideCoordinator { get; }
}
