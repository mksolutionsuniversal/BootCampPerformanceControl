using System.Text;

namespace BootCampPerformanceControl.GlobalMaskFpe2Qualification;

internal sealed class QualificationTextOutput : IQualificationOutput, IDisposable
{
    private readonly StreamWriter _writer;

    private QualificationTextOutput(string logPath, StreamWriter writer)
    {
        LogPath = logPath;
        _writer = writer;
    }

    public string LogPath { get; }

    public static QualificationTextOutput Create(DateTimeOffset utcNow)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop))
        {
            desktop = Environment.CurrentDirectory;
        }

        Directory.CreateDirectory(desktop);
        var stem = $"BCPC-GLOBALMASK-FPE2-QUALIFICATION-{utcNow:yyyyMMdd-HHmmss}";
        for (var suffix = 0; ; suffix++)
        {
            var fileName = suffix == 0 ? $"{stem}.txt" : $"{stem}-{suffix}.txt";
            var path = Path.Combine(desktop, fileName);
            try
            {
                var stream = new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 4096,
                    FileOptions.WriteThrough);
                var writer = new StreamWriter(stream, new UTF8Encoding(false))
                {
                    AutoFlush = true
                };
                return new QualificationTextOutput(path, writer);
            }
            catch (IOException) when (File.Exists(path))
            {
            }
        }
    }

    public void WriteLine(string message = "")
    {
        Console.WriteLine(message);
        _writer.WriteLine(message);
    }

    public void Dispose() => _writer.Dispose();
}
