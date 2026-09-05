namespace BootCampPerformanceControl.ApplicationSettings;

public interface IApplicationOptionsService
{
    ApplicationOptionsSnapshot Load();

    void SetCloseBehavior(ApplicationCloseBehavior closeBehavior);

    void SetStartWithWindows(bool enabled);

    void SetStartMinimizedToTray(bool enabled);
}
