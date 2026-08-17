using System.Windows.Input;
using BootCampPerformanceControl.Profiles;

namespace BootCampPerformanceControl.UI;

public sealed class ProfileButtonViewModel
{
    private const string GamingOptimisedProfileId = "gaming-optimised";
    private const string RestoreProfileId = "restore";

    public ProfileButtonViewModel(
        PerformanceProfile profile,
        ICommand command,
        bool isRestoreSnapshotAvailable = false)
    {
        ProfileId = profile.Id;
        DisplayName = profile.DisplayName;
        IsEnabled = IsProfileEnabled(profile, isRestoreSnapshotAvailable);
        Command = IsEnabled ? command : null;
        ToolTip = CreateToolTip(profile, IsEnabled, isRestoreSnapshotAvailable);
    }

    public string ProfileId { get; }

    public string DisplayName { get; }

    public bool IsEnabled { get; }

    public ICommand? Command { get; }

    public string ToolTip { get; }

    private static bool IsProfileEnabled(
        PerformanceProfile profile,
        bool isRestoreSnapshotAvailable)
    {
        if (string.Equals(
                profile.Id,
                GamingOptimisedProfileId,
                StringComparison.OrdinalIgnoreCase))
        {
            return profile.IsAvailableForDetectedModel;
        }

        if (string.Equals(
                profile.Id,
                RestoreProfileId,
                StringComparison.OrdinalIgnoreCase))
        {
            return isRestoreSnapshotAvailable;
        }

        return false;
    }

    private static string CreateToolTip(
        PerformanceProfile profile,
        bool isEnabled,
        bool isRestoreSnapshotAvailable)
    {
        if (isEnabled)
        {
            if (string.Equals(
                    profile.Id,
                    RestoreProfileId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Restore the exact original saved power state.";
            }

            return $"Apply {profile.DisplayName}.";
        }

        if (string.Equals(
                profile.Id,
                GamingOptimisedProfileId,
                StringComparison.OrdinalIgnoreCase))
        {
            return $"{profile.DisplayName} requires a verified compatible MacBookPro16,1 before it can be applied.";
        }

        if (string.Equals(
                profile.Id,
                RestoreProfileId,
                StringComparison.OrdinalIgnoreCase))
        {
            return isRestoreSnapshotAvailable
                ? "Restore is not currently executable."
                : "No original restore snapshot exists yet.";
        }

        return $"{profile.DisplayName} is not yet connected for execution.";
    }
}
