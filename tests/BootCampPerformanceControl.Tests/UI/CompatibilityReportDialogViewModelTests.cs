using BootCampPerformanceControl.Diagnostics;
using BootCampPerformanceControl.Tests.TestDoubles;
using BootCampPerformanceControl.UI;

namespace BootCampPerformanceControl.Tests.UI;

public sealed class CompatibilityReportDialogViewModelTests
{
    [Fact]
    public void CopyReportCommand_CopiesExactlyVisibleReportText()
    {
        var report = CreateReport();
        var clipboard = new FakeClipboardService();
        var viewModel = CreateViewModel(report, clipboardService: clipboard);

        viewModel.CopyReportCommand.Execute(null);

        Assert.Equal(report.Content, viewModel.ReportText);
        Assert.Equal(report.Content, clipboard.LastText);
        Assert.Equal(1, clipboard.SetTextCallCount);
        Assert.Equal("Compatibility report copied to the clipboard.", viewModel.StatusMessage);
    }

    [Fact]
    public void CopyReportCommand_WhenClipboardFails_ReportsErrorAndLogsException()
    {
        var exception = new InvalidOperationException("Clipboard unavailable.");
        var clipboard = new FakeClipboardService
        {
            SetTextException = exception
        };
        var logger = new TestApplicationLogger();
        var viewModel = CreateViewModel(
            CreateReport(),
            clipboardService: clipboard,
            logger: logger);

        viewModel.CopyReportCommand.Execute(null);

        Assert.Equal(
            "Compatibility report could not be copied. Check the log for details.",
            viewModel.StatusMessage);
        var error = Assert.Single(logger.Errors);
        Assert.Same(exception, error.Exception);
    }

    [Fact]
    public void SaveReportCommand_SavesCurrentReport()
    {
        var report = CreateReport();
        var fileSaveService = new FakeCompatibilityReportFileSaveService();
        var viewModel = CreateViewModel(report, fileSaveService: fileSaveService);

        viewModel.SaveReportCommand.Execute(null);

        Assert.Equal(1, fileSaveService.SaveCallCount);
        Assert.Equal(report, fileSaveService.LastReport);
        Assert.Equal("Compatibility report saved.", viewModel.StatusMessage);
    }

    [Fact]
    public void SaveReportCommand_WhenUserCancels_ReportsCanceledWithoutError()
    {
        var fileSaveService = new FakeCompatibilityReportFileSaveService
        {
            SaveResult = false
        };
        var logger = new TestApplicationLogger();
        var viewModel = CreateViewModel(
            CreateReport(),
            fileSaveService: fileSaveService,
            logger: logger);

        viewModel.SaveReportCommand.Execute(null);

        Assert.Equal("Compatibility report save canceled.", viewModel.StatusMessage);
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public void OpenGitHubIssueCommand_UsesOnlyTheFixedRepositoryHttpsUrl()
    {
        var launcher = new FakeGitHubIssueLauncher();
        var viewModel = CreateViewModel(CreateReport(), gitHubIssueLauncher: launcher);

        viewModel.OpenGitHubIssueCommand.Execute(null);

        Assert.Equal(GitHubIssueLauncher.RepositoryNewIssueUrl, viewModel.GitHubIssueUrl);
        Assert.Equal(GitHubIssueLauncher.RepositoryNewIssueUrl, launcher.NewIssueUrl);
        Assert.Equal(1, launcher.OpenCallCount);
        Assert.Equal("GitHub issue page opened. Paste the copied report into the issue.", viewModel.StatusMessage);
    }

    [Fact]
    public void OpenGitHubIssueCommand_WhenBrowserFails_ReportsErrorAndLogsException()
    {
        var exception = new InvalidOperationException("Browser unavailable.");
        var launcher = new FakeGitHubIssueLauncher
        {
            OpenException = exception
        };
        var logger = new TestApplicationLogger();
        var viewModel = CreateViewModel(
            CreateReport(),
            gitHubIssueLauncher: launcher,
            logger: logger);

        viewModel.OpenGitHubIssueCommand.Execute(null);

        Assert.Equal(
            "GitHub issue page could not be opened. Check the log for details.",
            viewModel.StatusMessage);
        var error = Assert.Single(logger.Errors);
        Assert.Same(exception, error.Exception);
    }

    private static CompatibilityReportDialogViewModel CreateViewModel(
        CompatibilityReportResult report,
        FakeClipboardService? clipboardService = null,
        FakeCompatibilityReportFileSaveService? fileSaveService = null,
        FakeGitHubIssueLauncher? gitHubIssueLauncher = null,
        TestApplicationLogger? logger = null)
    {
        return new CompatibilityReportDialogViewModel(
            report,
            clipboardService ?? new FakeClipboardService(),
            fileSaveService ?? new FakeCompatibilityReportFileSaveService(),
            gitHubIssueLauncher ?? new FakeGitHubIssueLauncher(),
            logger ?? new TestApplicationLogger());
    }

    private static CompatibilityReportResult CreateReport()
    {
        return new CompatibilityReportResult(
            "Visible sanitized compatibility report.",
            "BootCampPerformanceControl-Compatibility-MacBookPro16,1-0.3.0-rc.1.txt");
    }

    private sealed class FakeClipboardService : IClipboardService
    {
        public int SetTextCallCount { get; private set; }

        public string? LastText { get; private set; }

        public Exception? SetTextException { get; set; }

        public void SetText(string text)
        {
            SetTextCallCount++;
            LastText = text;

            if (SetTextException is not null)
            {
                throw SetTextException;
            }
        }
    }

    private sealed class FakeCompatibilityReportFileSaveService : ICompatibilityReportFileSaveService
    {
        public bool SaveResult { get; set; } = true;

        public int SaveCallCount { get; private set; }

        public CompatibilityReportResult? LastReport { get; private set; }

        public Task<bool> SaveAsync(
            CompatibilityReportResult report,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCallCount++;
            LastReport = report;
            return Task.FromResult(SaveResult);
        }
    }

    private sealed class FakeGitHubIssueLauncher : IGitHubIssueLauncher
    {
        public string NewIssueUrl => GitHubIssueLauncher.RepositoryNewIssueUrl;

        public int OpenCallCount { get; private set; }

        public Exception? OpenException { get; set; }

        public void OpenNewIssue()
        {
            OpenCallCount++;

            if (OpenException is not null)
            {
                throw OpenException;
            }
        }
    }
}
