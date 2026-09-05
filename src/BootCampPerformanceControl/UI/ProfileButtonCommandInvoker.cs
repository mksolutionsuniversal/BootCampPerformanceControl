namespace BootCampPerformanceControl.UI;

internal static class ProfileButtonCommandInvoker
{
    public static bool CanExecute(
        IEnumerable<ProfileButtonViewModel> profileButtons,
        string profileId)
    {
        return ResolveExecutableCommand(profileButtons, profileId) is not null;
    }

    public static bool TryExecute(
        IEnumerable<ProfileButtonViewModel> profileButtons,
        string profileId)
    {
        var command = ResolveExecutableCommand(profileButtons, profileId);
        if (command is null)
        {
            return false;
        }

        command.Execute(parameter: null);
        return true;
    }

    private static System.Windows.Input.ICommand? ResolveExecutableCommand(
        IEnumerable<ProfileButtonViewModel> profileButtons,
        string profileId)
    {
        ArgumentNullException.ThrowIfNull(profileButtons);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        ProfileButtonViewModel? match = null;
        foreach (var profileButton in profileButtons)
        {
            if (!string.Equals(
                    profileButton.ProfileId,
                    profileId,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (match is not null)
            {
                return null;
            }

            match = profileButton;
        }

        if (match?.IsEnabled != true || match.Command is null)
        {
            return null;
        }

        return match.Command.CanExecute(parameter: null)
            ? match.Command
            : null;
    }
}
