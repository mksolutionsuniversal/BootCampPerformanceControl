using System.Windows;
using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.BackendActivation;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.Logging;
using BootCampPerformanceControl.Startup;
using BootCampPerformanceControl.UI;

namespace BootCampPerformanceControl;

public partial class App : System.Windows.Application
{
    private ApplicationInstanceGuard? _instanceGuard;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var startupMode = ApplicationStartupArguments.Parse(e.Args);
        var logger = new FileApplicationLogger();

        if (startupMode == ApplicationStartupMode.StartAppleSmcHelper)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            await RunAppleSmcActivationHelperAsync(logger);
            return;
        }

        if (startupMode == ApplicationStartupMode.Invalid)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            logger.Info("Application startup rejected invalid command-line arguments.");
            Shutdown(ApplicationExitCodes.InvalidArguments);
            return;
        }

        try
        {
            if (ApplicationStartupArguments.RequiresMainApplicationInstanceGuard(startupMode))
            {
                _instanceGuard = ApplicationInstanceGuard.TryAcquire();

                if (_instanceGuard is null)
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    logger.Info("Application startup skipped because another main instance is already running.");
                    System.Windows.MessageBox.Show(
                        "BootCamp Performance Control is already running. Check the system tray.",
                        "BootCamp Performance Control",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    Shutdown(ApplicationExitCodes.Success);
                    return;
                }
            }

            logger.Info("Application start.");
            var composition = AppCompositionRoot.CreateMainApplication(logger);
            var window = new MainWindow(composition);
            MainWindow = window;
            window.ShowForStartup(composition.ViewModel.StartMinimizedToTray);
        }
        catch (Exception exception)
        {
            logger.Error("Application startup failed.", exception);
            System.Windows.MessageBox.Show(
                "Application startup failed. Check the application log for details.",
                "BootCamp Performance Control",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(ApplicationExitCodes.ApplicationStartupFailed);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceGuard?.Dispose();
        _instanceGuard = null;
        base.OnExit(e);
    }

    private async Task RunAppleSmcActivationHelperAsync(
        IApplicationLogger logger)
    {
        var exitCode = ApplicationExitCodes.Failed;

        try
        {
            logger.Info("Elevated AppleSMC activation helper started.");

            var hardwareDetectionService = new HardwareDetectionService(
                new ModelSupportRegistry());
            var helper = new AppleSmcBackendActivationHelper(
                hardwareDetectionService,
                new FanSafetyPolicy(),
                new AppleSmcBackendActivator());
            var result = await helper.RunAsync(CancellationToken.None);

            exitCode = ApplicationExitCodes.FromActivationOutcome(result.Outcome);

            if (result.Outcome == AppleSmcBackendActivationOutcome.Failed
                && result.Exception is not null)
            {
                logger.Error(
                    $"Elevated AppleSMC activation helper completed with outcome "
                        + $"'{result.Outcome}' and exit code {exitCode}.",
                    result.Exception);
            }
            else
            {
                logger.Info(
                    $"Elevated AppleSMC activation helper completed with outcome "
                        + $"'{result.Outcome}' and exit code {exitCode}.");
            }
        }
        catch (Exception exception)
        {
            logger.Error("Elevated AppleSMC activation helper failed.", exception);
        }
        finally
        {
            Shutdown(exitCode);
        }
    }
}
