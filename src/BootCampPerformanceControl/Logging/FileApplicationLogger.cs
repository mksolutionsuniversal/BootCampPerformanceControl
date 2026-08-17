using System.Diagnostics;
using System.IO;

namespace BootCampPerformanceControl.Logging;

public sealed class FileApplicationLogger : IApplicationLogger
{
    private readonly object _syncRoot = new();
    private readonly string _logDirectory;
    private readonly string _logFilePath;

    public FileApplicationLogger()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _logDirectory = Path.Combine(localAppData, "BootCampPerformanceControl", "Logs");
        _logFilePath = Path.Combine(_logDirectory, $"app-{DateTimeOffset.Now:yyyyMMdd}.log");
    }

    public void Info(string message)
    {
        Write("INFO", message);
    }

    public void Error(string message, Exception exception)
    {
        Write("ERROR", $"{message}{Environment.NewLine}{exception}");
    }

    private void Write(string level, string message)
    {
        try
        {
            var line = $"{DateTimeOffset.Now:O} [{level}] {message}";

            lock (_syncRoot)
            {
                Directory.CreateDirectory(_logDirectory);
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Logging failed: {exception.Message}");
        }
    }
}
