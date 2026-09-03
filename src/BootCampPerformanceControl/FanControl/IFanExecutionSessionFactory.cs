namespace BootCampPerformanceControl.FanControl;

internal interface IFanExecutionSessionFactory
{
    Task<IFanExecutionSession> OpenAsync(CancellationToken cancellationToken);
}
