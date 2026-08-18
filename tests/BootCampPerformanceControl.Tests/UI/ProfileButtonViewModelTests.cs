using BootCampPerformanceControl.Profiles;
using BootCampPerformanceControl.UI;

namespace BootCampPerformanceControl.Tests.UI;

public sealed class ProfileButtonViewModelTests
{
    [Fact]
    public void GamingOptimised_EnabledOnlyWhenPlatformSupportedAndPowerStateReadable()
    {
        var profile = CreateGamingProfile(isAvailableForDetectedModel: true);
        var command = new AsyncCommand(_ => Task.CompletedTask);

        var enabledButton = new ProfileButtonViewModel(
            profile,
            command,
            isRestoreSnapshotAvailable: false,
            isPowerStateReadable: true);

        Assert.True(enabledButton.IsEnabled);
        Assert.Same(command, enabledButton.Command);
        Assert.Equal("Apply Gaming Optimised.", enabledButton.ToolTip);

        var unreadablePowerButton = new ProfileButtonViewModel(
            profile,
            command,
            isRestoreSnapshotAvailable: false,
            isPowerStateReadable: false);

        Assert.False(unreadablePowerButton.IsEnabled);
        Assert.Null(unreadablePowerButton.Command);
        Assert.Equal("Gaming Optimised requires current processor power settings to be read successfully.", unreadablePowerButton.ToolTip);

        var unsupportedPlatformProfile = CreateGamingProfile(isAvailableForDetectedModel: false);
        var unsupportedPlatformButton = new ProfileButtonViewModel(
            unsupportedPlatformProfile,
            command,
            isRestoreSnapshotAvailable: false,
            isPowerStateReadable: true);

        Assert.False(unsupportedPlatformButton.IsEnabled);
        Assert.Null(unsupportedPlatformButton.Command);
        Assert.Equal("Gaming Optimised is available for supported Intel Mac models.", unsupportedPlatformButton.ToolTip);
    }

    [Fact]
    public void Restore_EnabledOnlyWhenRestoreSnapshotAvailable()
    {
        var profile = CreateRestoreProfile(isAvailableForDetectedModel: true);
        var command = new AsyncCommand(_ => Task.CompletedTask);

        var availableButton = new ProfileButtonViewModel(
            profile,
            command,
            isRestoreSnapshotAvailable: true,
            isPowerStateReadable: true);

        Assert.True(availableButton.IsEnabled);
        Assert.Same(command, availableButton.Command);
        Assert.Equal("Restore the exact original saved power state.", availableButton.ToolTip);

        var unavailableButton = new ProfileButtonViewModel(
            profile,
            command,
            isRestoreSnapshotAvailable: false,
            isPowerStateReadable: true);

        Assert.False(unavailableButton.IsEnabled);
        Assert.Null(unavailableButton.Command);
        Assert.Equal("No original restore snapshot exists yet.", unavailableButton.ToolTip);
    }

    private static PerformanceProfile CreateGamingProfile(bool isAvailableForDetectedModel)
    {
        return new PerformanceProfile(
            "gaming-optimised",
            "Gaming Optimised",
            isAvailableForDetectedModel,
            new ProcessorPowerProfileTarget(95, 95, 0, 0, ProfileUnspecifiedValueSource.None),
            [],
            "Gaming profile.");
    }

    private static PerformanceProfile CreateRestoreProfile(bool isAvailableForDetectedModel)
    {
        return new PerformanceProfile(
            "restore",
            "Restore Original Settings",
            isAvailableForDetectedModel,
            new ProcessorPowerProfileTarget(null, null, null, null, ProfileUnspecifiedValueSource.OriginalRestoreSnapshot),
            [],
            "Restore profile.");
    }
}
