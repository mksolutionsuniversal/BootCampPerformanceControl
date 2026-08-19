namespace BootCampPerformanceControl.ApplicationSettings;

public sealed record ApplicationOptionsSnapshot(
    ApplicationCloseBehavior CloseBehavior,
    bool StartWithWindows)
{
    public static ApplicationOptionsSnapshot Default { get; } = new(
        ApplicationCloseBehavior.MinimizeToTray,
        StartWithWindows: false);
}
