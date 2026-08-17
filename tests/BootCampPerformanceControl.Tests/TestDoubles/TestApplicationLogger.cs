using BootCampPerformanceControl.Logging;

namespace BootCampPerformanceControl.Tests.TestDoubles;

internal sealed class TestApplicationLogger : IApplicationLogger
{
    public List<string> InformationMessages { get; } = [];

    public List<(string Message, Exception Exception)> Errors { get; } = [];

    public void Info(string message)
    {
        InformationMessages.Add(message);
    }

    public void Error(string message, Exception exception)
    {
        Errors.Add((message, exception));
    }
}
