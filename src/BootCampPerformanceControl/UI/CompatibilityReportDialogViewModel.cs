using System.Windows.Input;
using BootCampPerformanceControl.Diagnostics;
using BootCampPerformanceControl.Logging;

namespace BootCampPerformanceControl.UI;

internal sealed class CompatibilityReportDialogViewModel : ViewModelBase
{
    private readonly CompatibilityReportResult _report;
    private readonly IClipboardService _clipboardService;
    private readonly ICompatibilityReportFileSaveService _fileSaveService;
    private readonly IGitHubIssueLauncher _gitHubIssueLauncher;
    private readonly IApplicationLogger _logger;

    private bool _isBusy;
    private string _statusMessage = "Review the report before sharing it.";

    public CompatibilityReportDialogViewModel(
        CompatibilityReportResult report,
        IClipboardService clipboardService,
        ICompatibilityReportFileSaveService fileSaveService,
        IGitHubIssueLauncher gitHubIssueLauncher,
        IApplicationLogger logger)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(clipboardService);
        ArgumentNullException.ThrowIfNull(fileSaveService);
        ArgumentNullException.ThrowIfNull(gitHubIssueLauncher);
        ArgumentNullException.ThrowIfNull(logger);

        _report = report;
        _clipboardService = clipboardService;
        _fileSaveService = fileSaveService;
        _gitHubIssueLauncher = gitHubIssueLauncher;
        _logger = logger;

        CopyReportCommand = new AsyncCommand(
            CopyReportAsync,
            canExecute: () => !IsBusy,
            onException: OnCopyReportException);
        SaveReportCommand = new AsyncCommand(
            SaveReportAsync,
            canExecute: () => !IsBusy,
            onCanceled: OnSaveReportCanceled,
            onException: OnSaveReportException);
        OpenGitHubIssueCommand = new AsyncCommand(
            OpenGitHubIssueAsync,
            canExecute: () => !IsBusy,
            onException: OnOpenGitHubIssueException);
    }

    public string ReportText => _report.Content;

    public string SuggestedFileName => _report.SuggestedFileName;

    public string GitHubIssueUrl => _gitHubIssueLauncher.NewIssueUrl;

    public ICommand CopyReportCommand { get; }

    public ICommand SaveReportCommand { get; }

    public ICommand OpenGitHubIssueCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyCommandsCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    private Task CopyReportAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsBusy = true;

        try
        {
            _clipboardService.SetText(ReportText);
            StatusMessage = "Compatibility report copied to the clipboard.";
            _logger.Info("Compatibility report copied to the clipboard.");
            return Task.CompletedTask;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveReportAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var saved = await _fileSaveService
                .SaveAsync(_report, cancellationToken);

            StatusMessage = saved
                ? "Compatibility report saved."
                : "Compatibility report save canceled.";
            _logger.Info(saved
                ? "Compatibility report saved."
                : "Compatibility report save canceled.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task OpenGitHubIssueAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsBusy = true;

        try
        {
            _gitHubIssueLauncher.OpenNewIssue();
            StatusMessage = "GitHub issue page opened. Paste the copied report into the issue.";
            _logger.Info("GitHub compatibility issue page opened.");
            return Task.CompletedTask;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnCopyReportException(Exception exception)
    {
        StatusMessage = "Compatibility report could not be copied. Check the log for details.";
        _logger.Error("Copying the compatibility report to the clipboard failed.", exception);
    }

    private void OnSaveReportCanceled(OperationCanceledException exception)
    {
        StatusMessage = "Compatibility report save canceled.";
        _logger.Info($"Compatibility report save canceled: {exception.Message}");
    }

    private void OnSaveReportException(Exception exception)
    {
        StatusMessage = "Compatibility report could not be saved. Check the log for details.";
        _logger.Error("Saving the compatibility report failed.", exception);
    }

    private void OnOpenGitHubIssueException(Exception exception)
    {
        StatusMessage = "GitHub issue page could not be opened. Check the log for details.";
        _logger.Error("Opening the GitHub compatibility issue page failed.", exception);
    }

    private void NotifyCommandsCanExecuteChanged()
    {
        if (CopyReportCommand is AsyncCommand copyCommand)
        {
            copyCommand.NotifyCanExecuteChanged();
        }

        if (SaveReportCommand is AsyncCommand saveCommand)
        {
            saveCommand.NotifyCanExecuteChanged();
        }

        if (OpenGitHubIssueCommand is AsyncCommand openCommand)
        {
            openCommand.NotifyCanExecuteChanged();
        }
    }
}
