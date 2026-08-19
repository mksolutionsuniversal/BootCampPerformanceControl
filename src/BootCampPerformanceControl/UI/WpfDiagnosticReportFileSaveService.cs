using System.IO;
using System.Text;
using BootCampPerformanceControl.Diagnostics;
using Microsoft.Win32;

namespace BootCampPerformanceControl.UI;

public sealed class WpfDiagnosticReportFileSaveService : IDiagnosticReportFileSaveService
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    public async Task<bool> SaveAsync(
        DiagnosticReportResult report,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        cancellationToken.ThrowIfCancellationRequested();

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = report.SuggestedFileName,
            DefaultExt = ".txt",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await File.WriteAllTextAsync(
            dialog.FileName,
            report.Content,
            Utf8WithoutBom,
            cancellationToken).ConfigureAwait(false);
        return true;
    }
}
