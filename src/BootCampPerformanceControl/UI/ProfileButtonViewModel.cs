using System.Windows.Input;
using BootCampPerformanceControl.Profiles;

namespace BootCampPerformanceControl.UI;

public sealed class ProfileButtonViewModel
{
    private const string GamingOptimisedProfileId = "gaming-optimised";
    private const string RestoreProfileId = "restore";

    public ProfileButtonViewModel(
        PerformanceProfile profile,
        ICommand? command,
        bool isRestoreSnapshotAvailable = false,
        bool isPowerStateReadable = false,
        bool hasFanRecoveryContext = false,
        bool isExactVerifiedMacBookPro16_1 = false,
        bool isPartialGamingState = false)
    {
        ProfileId = profile.Id;
        DisplayName = profile.DisplayName;
        IsEnabled = IsProfileEnabled(
            profile,
            isRestoreSnapshotAvailable,
            isPowerStateReadable,
            hasFanRecoveryContext,
            isExactVerifiedMacBookPro16_1);
        Command = IsEnabled ? command : null;
        ToolTip = CreateToolTip(
            profile,
            IsEnabled,
            isRestoreSnapshotAvailable,
            isPowerStateReadable,
            hasFanRecoveryContext,
            isExactVerifiedMacBookPro16_1,
            isPartialGamingState);
    }

    public string ProfileId { get; }

    public string DisplayName { get; }

    public bool IsEnabled { get; }

    public ICommand? Command { get; }

    public string ToolTip { get; }

    private static bool IsProfileEnabled(
        PerformanceProfile profile,
        bool isRestoreSnapshotAvailable,
        bool isPowerStateReadable,
        bool hasFanRecoveryContext,
        bool isExactVerifiedMacBookPro16_1)
    {
        if (string.Equals(
                profile.Id,
                GamingOptimisedProfileId,
                StringComparison.OrdinalIgnoreCase))
        {
            if (isExactVerifiedMacBookPro16_1 && hasFanRecoveryContext)
            {
                return false;
            }

            return profile.IsAvailableForDetectedModel && isPowerStateReadable;
        }

        if (string.Equals(
                profile.Id,
                RestoreProfileId,
                StringComparison.OrdinalIgnoreCase))
        {
            return isRestoreSnapshotAvailable || hasFanRecoveryContext;
        }

        return false;
    }

    private static string CreateToolTip(
        PerformanceProfile profile,
        bool isEnabled,
        bool isRestoreSnapshotAvailable,
        bool isPowerStateReadable,
        bool hasFanRecoveryContext,
        bool isExactVerifiedMacBookPro16_1,
        bool isPartialGamingState)
    {
        if (isEnabled)
        {
            if (string.Equals(
                    profile.Id,
                    GamingOptimisedProfileId,
                    StringComparison.OrdinalIgnoreCase)
                && isPartialGamingState)
            {
                return "Re-enable Maximum Safe RPM fans without changing the active Gaming CPU settings or original restore snapshot.";
            }

            if (string.Equals(
                    profile.Id,
                    RestoreProfileId,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (hasFanRecoveryContext && isRestoreSnapshotAvailable)
                {
                    return "Restore fan control to Apple Auto and restore the exact original saved power state.";
                }

                if (hasFanRecoveryContext)
                {
                    return "Restore fan control to Apple Auto.";
                }

                return "Restore the exact original saved power state.";
            }

            return $"Apply {profile.DisplayName}.";
        }

        if (string.Equals(
                profile.Id,
                GamingOptimisedProfileId,
                StringComparison.OrdinalIgnoreCase))
        {
            if (isExactVerifiedMacBookPro16_1 && hasFanRecoveryContext)
            {
                return "Restore the previous fan override before applying Gaming Optimised again.";
            }

            if (!profile.IsAvailableForDetectedModel)
            {
                return "Gaming Optimised is available for supported Intel Mac models.";
            }

            if (!isPowerStateReadable)
            {
                return "Gaming Optimised requires current processor power settings to be read successfully.";
            }
        }

        if (string.Equals(
                profile.Id,
                RestoreProfileId,
                StringComparison.OrdinalIgnoreCase))
        {
            return (isRestoreSnapshotAvailable || hasFanRecoveryContext)
                ? "Restore is not currently executable."
                : "No original restore snapshot exists yet.";
        }

        return $"{profile.DisplayName} is not available.";
    }
}
