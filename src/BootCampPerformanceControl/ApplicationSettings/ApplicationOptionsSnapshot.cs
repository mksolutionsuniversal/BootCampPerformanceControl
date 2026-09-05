namespace BootCampPerformanceControl.ApplicationSettings;

public sealed record ApplicationOptionsSnapshot(
    ApplicationCloseBehavior CloseBehavior,
    bool StartWithWindows,
    bool StartMinimizedToTray = false)
{
    public static ApplicationOptionsSnapshot Default { get; } = new(
        ApplicationCloseBehavior.MinimizeToTray,
        StartWithWindows: false,
        StartMinimizedToTray: false);
}
