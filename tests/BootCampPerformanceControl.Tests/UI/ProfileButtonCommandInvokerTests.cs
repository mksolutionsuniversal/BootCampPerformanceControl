using System.Windows.Input;
using BootCampPerformanceControl.Profiles;
using BootCampPerformanceControl.UI;

namespace BootCampPerformanceControl.Tests.UI;

public sealed class ProfileButtonCommandInvokerTests
{
    [Fact]
    public void ExactProfileIds_ExecuteTheExistingCommandInstances()
    {
        var gamingCommand = new TrackingCommand(canExecute: true);
        var restoreCommand = new TrackingCommand(canExecute: true);
        var gamingButton = CreateGamingButton(gamingCommand, isEnabled: true);
        var restoreButton = CreateRestoreButton(restoreCommand, isEnabled: true);
        var profileButtons = new[] { gamingButton, restoreButton };

        Assert.Same(gamingCommand, gamingButton.Command);
        Assert.Same(restoreCommand, restoreButton.Command);
        Assert.True(ProfileButtonCommandInvoker.CanExecute(
            profileButtons,
            "gaming-optimised"));
        Assert.True(ProfileButtonCommandInvoker.TryExecute(
            profileButtons,
            "gaming-optimised"));
        Assert.True(ProfileButtonCommandInvoker.CanExecute(
            profileButtons,
            "restore"));
        Assert.True(ProfileButtonCommandInvoker.TryExecute(
            profileButtons,
            "restore"));

        Assert.Equal(1, gamingCommand.ExecuteCallCount);
        Assert.Equal(1, restoreCommand.ExecuteCallCount);
        Assert.Null(gamingCommand.LastParameter);
        Assert.Null(restoreCommand.LastParameter);
    }

    [Fact]
    public void MissingOrDisabledProfile_CannotExecute()
    {
        var disabledCommand = new TrackingCommand(canExecute: true);
        var disabledRestore = CreateRestoreButton(
            disabledCommand,
            isEnabled: false);
        var profileButtons = new[] { disabledRestore };

        Assert.False(ProfileButtonCommandInvoker.CanExecute(
            profileButtons,
            "restore"));
        Assert.False(ProfileButtonCommandInvoker.TryExecute(
            profileButtons,
            "restore"));
        Assert.False(ProfileButtonCommandInvoker.CanExecute(
            profileButtons,
            "gaming-optimised"));
        Assert.False(ProfileButtonCommandInvoker.TryExecute(
            profileButtons,
            "gaming-optimised"));
        Assert.Equal(0, disabledCommand.ExecuteCallCount);
    }

    [Fact]
    public void CommandCanExecuteFalse_BlocksExecution()
    {
        var command = new TrackingCommand(canExecute: false);
        var profileButtons = new[]
        {
            CreateGamingButton(command, isEnabled: true)
        };

        Assert.False(ProfileButtonCommandInvoker.CanExecute(
            profileButtons,
            "gaming-optimised"));
        Assert.False(ProfileButtonCommandInvoker.TryExecute(
            profileButtons,
            "gaming-optimised"));

        Assert.Equal(2, command.CanExecuteCallCount);
        Assert.Equal(0, command.ExecuteCallCount);
    }

    [Fact]
    public void DuplicateProfileId_FailsClosedWithoutExecution()
    {
        var firstCommand = new TrackingCommand(canExecute: true);
        var secondCommand = new TrackingCommand(canExecute: true);
        var profileButtons = new[]
        {
            CreateGamingButton(firstCommand, isEnabled: true),
            CreateGamingButton(secondCommand, isEnabled: true)
        };

        Assert.False(ProfileButtonCommandInvoker.CanExecute(
            profileButtons,
            "gaming-optimised"));
        Assert.False(ProfileButtonCommandInvoker.TryExecute(
            profileButtons,
            "gaming-optimised"));
        Assert.Equal(0, firstCommand.ExecuteCallCount);
        Assert.Equal(0, secondCommand.ExecuteCallCount);
    }

    private static ProfileButtonViewModel CreateGamingButton(
        ICommand command,
        bool isEnabled)
    {
        return new ProfileButtonViewModel(
            new PerformanceProfile(
                "gaming-optimised",
                "Gaming Optimised",
                IsAvailableForDetectedModel: isEnabled,
                new ProcessorPowerProfileTarget(
                    95,
                    95,
                    0,
                    0,
                    ProfileUnspecifiedValueSource.None),
                Settings: [],
                "Gaming profile."),
            command,
            isPowerStateReadable: isEnabled);
    }

    private static ProfileButtonViewModel CreateRestoreButton(
        ICommand command,
        bool isEnabled)
    {
        return new ProfileButtonViewModel(
            new PerformanceProfile(
                "restore",
                "Restore Original Settings",
                IsAvailableForDetectedModel: true,
                new ProcessorPowerProfileTarget(
                    null,
                    null,
                    null,
                    null,
                    ProfileUnspecifiedValueSource.OriginalRestoreSnapshot),
                Settings: [],
                "Restore profile."),
            command,
            isRestoreSnapshotAvailable: isEnabled,
            isPowerStateReadable: true);
    }

    private sealed class TrackingCommand : ICommand
    {
        private readonly bool _canExecute;

        public TrackingCommand(bool canExecute)
        {
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public int CanExecuteCallCount { get; private set; }

        public int ExecuteCallCount { get; private set; }

        public object? LastParameter { get; private set; }

        public bool CanExecute(object? parameter)
        {
            CanExecuteCallCount++;
            return _canExecute;
        }

        public void Execute(object? parameter)
        {
            ExecuteCallCount++;
            LastParameter = parameter;
        }
    }
}
