using System.Windows;
using BootCampPerformanceControl.Logging;
using BootCampPerformanceControl.UI;

namespace BootCampPerformanceControl;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logger = new FileApplicationLogger();
        logger.Info("Application start.");

        try
        {
            var window = new MainWindow(AppCompositionRoot.CreateMainViewModel(logger));
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            logger.Error("Application startup failed.", exception);
            MessageBox.Show(
                "Application startup failed. Check the application log for details.",
                "BootCamp Performance Control",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
