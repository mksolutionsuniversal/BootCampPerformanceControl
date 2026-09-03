namespace BootCampPerformanceControl.Logging;

internal sealed class NullApplicationLogger : IApplicationLogger
{
    public static NullApplicationLogger Instance { get; } = new();

    public void Info(string message)
    {
    }

    public void Error(string message, Exception exception)
    {
    }
}
