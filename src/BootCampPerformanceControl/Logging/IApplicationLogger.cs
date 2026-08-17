namespace BootCampPerformanceControl.Logging;

public interface IApplicationLogger
{
    void Info(string message);

    void Error(string message, Exception exception);
}
