namespace BootCampPerformanceControl.UI;

internal interface IGitHubIssueLauncher
{
    string NewIssueUrl { get; }

    void OpenNewIssue();
}
