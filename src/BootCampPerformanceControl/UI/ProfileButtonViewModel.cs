using BootCampPerformanceControl.Profiles;

namespace BootCampPerformanceControl.UI;

public sealed class ProfileButtonViewModel
{
    public ProfileButtonViewModel(PerformanceProfile profile)
    {
        DisplayName = profile.DisplayName;
        IsEnabled = false;
        ToolTip = profile.IsAvailableForDetectedModel
            ? "Disabled in this read-only milestone."
            : $"{profile.DisplayName} is not available for the detected model.";
    }

    public string DisplayName { get; }

    public bool IsEnabled { get; }

    public string ToolTip { get; }
}
