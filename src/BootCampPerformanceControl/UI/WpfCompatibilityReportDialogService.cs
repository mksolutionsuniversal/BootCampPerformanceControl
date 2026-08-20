using System.Windows;
using BootCampPerformanceControl.Diagnostics;
using BootCampPerformanceControl.Logging;

namespace BootCampPerformanceControl.UI;

internal sealed class WpfCompatibilityReportDialogService : ICompatibilityReportDialogService
{
    private readonly IApplicationLogger _logger;

    public WpfCompatibilityReportDialogService(IApplicationLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Show(CompatibilityReportResult report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var viewModel = new CompatibilityReportDialogViewModel(
            report,
            new WindowsClipboardService(),
            new WpfCompatibilityReportFileSaveService(),
            new GitHubIssueLauncher(),
            _logger);
        var dialog = new CompatibilityReportDialog
        {
            DataContext = viewModel,
            Owner = System.Windows.Application.Current?.MainWindow
        };

        dialog.ShowDialog();
    }
}
