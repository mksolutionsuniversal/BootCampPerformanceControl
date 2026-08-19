using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using BootCampPerformanceControl.Logging;
using BootCampPerformanceControl.UI;

namespace BootCampPerformanceControl;

public partial class MainWindow : Window
{
    private static readonly TimeSpan FanMonitoringShutdownTimeout = TimeSpan.FromSeconds(3);

    private readonly WindowsSystemTrayIcon _systemTrayIcon;

    private bool _isLoaded;
    private bool _closeDeferralStarted;
    private bool _allowFinalClose;
    private bool _exitRequested;
    private bool _isHiddenToTray;
    private WindowState _windowStateBeforeTray = WindowState.Normal;

    public MainWindow()
        : this(AppCompositionRoot.CreateMainViewModel(new FileApplicationLogger()))
    {
    }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _systemTrayIcon = new WindowsSystemTrayIcon();
        DataContext = viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        _systemTrayIcon.OpenRequested += OnTrayOpenRequested;
        _systemTrayIcon.ExitRequested += OnTrayExitRequested;

        if (System.Windows.Application.Current is { } application)
        {
            application.SessionEnding += OnSessionEnding;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.StartFanMonitoring();

            if (viewModel.RefreshCommand.CanExecute(null))
            {
                viewModel.RefreshCommand.Execute(null);
            }
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowFinalClose)
        {
            return;
        }

        e.Cancel = true;

        if (_closeDeferralStarted)
        {
            return;
        }

        if (!_exitRequested
            && DataContext is MainViewModel optionsViewModel
            && optionsViewModel.MinimizeToTrayOnClose)
        {
            HideToSystemTray();
            return;
        }

        _closeDeferralStarted = true;

        try
        {
            if (DataContext is MainViewModel shutdownViewModel)
            {
                await shutdownViewModel
                    .StopFanMonitoringAsync()
                    .WaitAsync(FanMonitoringShutdownTimeout);
            }
        }
        catch (TimeoutException)
        {
            Trace.TraceWarning(
                $"Fan monitoring did not stop within {FanMonitoringShutdownTimeout.TotalSeconds:0} seconds. Window shutdown will continue.");
        }
        catch (Exception exception)
        {
            Trace.TraceError($"Fan monitoring shutdown failed: {exception}");
        }
        finally
        {
            _allowFinalClose = true;

            try
            {
                Close();
            }
            catch (Exception exception)
            {
                Trace.TraceError($"Final window close failed: {exception}");
            }
        }
    }

    private void HideToSystemTray()
    {
        _windowStateBeforeTray = WindowState == WindowState.Minimized
            ? WindowState.Normal
            : WindowState;
        Hide();
        _isHiddenToTray = true;
        _systemTrayIcon.Show();
        Trace.TraceInformation("Main window hidden to the system tray.");
    }

    private void RestoreFromSystemTray()
    {
        if (!_isHiddenToTray)
        {
            return;
        }

        _systemTrayIcon.Hide();
        Show();
        WindowState = _windowStateBeforeTray;
        _isHiddenToTray = false;
        Activate();
        Trace.TraceInformation("Main window restored from the system tray.");
    }

    private void OnTrayOpenRequested(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(RestoreFromSystemTray);
    }

    private void OnTrayExitRequested(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _exitRequested = true;
            _systemTrayIcon.Hide();
            Close();
        });
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        _exitRequested = true;
        _systemTrayIcon.Hide();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (System.Windows.Application.Current is { } application)
        {
            application.SessionEnding -= OnSessionEnding;
        }

        _systemTrayIcon.OpenRequested -= OnTrayOpenRequested;
        _systemTrayIcon.ExitRequested -= OnTrayExitRequested;
        _systemTrayIcon.Dispose();
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        var aboutWindow = new AboutWindow
        {
            Owner = this
        };
        aboutWindow.ShowDialog();
    }
}
