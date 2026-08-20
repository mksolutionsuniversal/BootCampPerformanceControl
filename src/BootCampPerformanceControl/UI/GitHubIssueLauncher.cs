using System.Diagnostics;

namespace BootCampPerformanceControl.UI;

internal sealed class GitHubIssueLauncher : IGitHubIssueLauncher
{
    internal const string RepositoryNewIssueUrl =
        "https://github.com/mksolutionsuniversal/BootCampPerformanceControl/issues/new";

    public string NewIssueUrl => RepositoryNewIssueUrl;

    public void OpenNewIssue()
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = RepositoryNewIssueUrl,
            UseShellExecute = true
        });

        if (process is null)
        {
            throw new InvalidOperationException("The default browser could not be opened.");
        }
    }
}
