using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using BootCampPerformanceControl.Logging;
using BootCampPerformanceControl.Profiles;
using BootCampPerformanceControl.UI;

namespace BootCampPerformanceControl;

public partial class MainWindow : Window
{
    private const string GamingOptimisedProfileId = "gaming-optimised";
    private const string RestoreProfileId = "restore";
    private static readonly TimeSpan FanMonitoringShutdownTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ActiveOperationShutdownTimeout = TimeSpan.FromSeconds(10);

    private readonly WindowsSystemTrayIcon _systemTrayIcon;
    private readonly CleanExitFanRecoveryService _cleanExitFanRecoveryService;
    private readonly IApplicationLogger _logger;

    private bool _isLoaded;
    private bool _closeDeferralStarted;
    private bool _allowFinalClose;
    private bool _exitRequested;
    private bool _isHiddenToTray;
    private WindowState _windowStateBeforeTray = WindowState.Normal;

    public MainWindow()
        : this(AppCompositionRoot.CreateMainApplication(new FileApplicationLogger()))
    {
    }

    internal MainWindow(MainApplicationComposition composition)
        : this(
            composition.ViewModel,
            composition.CleanExitFanRecoveryService,
            composition.Logger)
    {
    }

    internal MainWindow(
        MainViewModel viewModel,
        CleanExitFanRecoveryService cleanExitFanRecoveryService,
        IApplicationLogger logger)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(cleanExitFanRecoveryService);
        ArgumentNullException.ThrowIfNull(logger);

        InitializeComponent();
        _systemTrayIcon = new WindowsSystemTrayIcon();
        _cleanExitFanRecoveryService = cleanExitFanRecoveryService;
        _logger = logger;
        DataContext = viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        _systemTrayIcon.OpenRequested += OnTrayOpenRequested;
        _systemTrayIcon.ProfileActionsStateRefreshRequested += OnTrayProfileActionsStateRefreshRequested;
        _systemTrayIcon.GamingOptimisedRequested += OnTrayGamingOptimisedRequested;
        _systemTrayIcon.RestoreOriginalSettingsRequested += OnTrayRestoreOriginalSettingsRequested;
        _systemTrayIcon.ExitRequested += OnTrayExitRequested;

        if (System.Windows.Application.Current is { } application)
        {
            application.SessionEnding += OnSessionEnding;
        }
    }

    internal void ShowForStartup(bool startMinimizedToTray)
    {
        if (!startMinimizedToTray)
        {
            Show();
            return;
        }

        var originalOpacity = Opacity;
        var originalShowActivated = ShowActivated;
        var originalShowInTaskbar = ShowInTaskbar;

        void RestorePresentation()
        {
            Opacity = originalOpacity;
            ShowActivated = originalShowActivated;
            ShowInTaskbar = originalShowInTaskbar;
        }

        void OnStartupLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnStartupLoaded;

            // OnLoaded was registered by the constructor before this temporary
            // handler, so normal monitoring, refresh, and startup recovery have
            // already been initiated before the window is hidden.
            HideToSystemTray();
            RestorePresentation();
            _logger.Info("Application started minimized to the system tray.");
        }

        Loaded += OnStartupLoaded;
        Opacity = 0;
        ShowActivated = false;
        ShowInTaskbar = false;

        try
        {
            Show();
        }
        catch
        {
            Loaded -= OnStartupLoaded;
            RestorePresentation();
            throw;
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
        IsEnabled = false;

        try
        {
            if (DataContext is MainViewModel shutdownViewModel)
            {
                var monitoringStopped = await TryStopFanMonitoringAsync(shutdownViewModel);
                if (monitoringStopped)
                {
                    var activeOperationCompleted = await WaitForActiveOperationAsync(shutdownViewModel);
                    if (activeOperationCompleted)
                    {
                        await TryRestoreOwnedFansForExitAsync();
                    }
                }
            }
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

    private async Task<bool> TryStopFanMonitoringAsync(MainViewModel viewModel)
    {
        try
        {
            await viewModel
                .StopFanMonitoringAsync()
                .WaitAsync(FanMonitoringShutdownTimeout);
            return true;
        }
        catch (TimeoutException)
        {
            var message =
                $"Fan monitoring did not stop within {FanMonitoringShutdownTimeout.TotalSeconds:0} seconds. "
                + "Clean exit fan recovery will be skipped to avoid opening a concurrent AppleSMC session; any ownership marker is retained for startup recovery.";
            Trace.TraceWarning(message);
            _logger.Info(message);
            return false;
        }
        catch (Exception exception)
        {
            Trace.TraceError($"Fan monitoring shutdown failed: {exception}");
            _logger.Error(
                "Fan monitoring shutdown failed. Clean exit fan recovery will be skipped; any ownership marker is retained for startup recovery.",
                exception);
            return false;
        }
    }

    private async Task<bool> WaitForActiveOperationAsync(MainViewModel viewModel)
    {
        if (!viewModel.IsBusy)
        {
            return true;
        }

        _logger.Info("Clean exit is waiting for the active UI operation to finish before fan recovery.");

        using var timeoutSource = new CancellationTokenSource(ActiveOperationShutdownTimeout);
        try
        {
            while (viewModel.IsBusy)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), timeoutSource.Token);
            }

            return true;
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            var message =
                $"The active UI operation did not finish within {ActiveOperationShutdownTimeout.TotalSeconds:0} seconds. "
                + "Clean exit fan recovery will be skipped to avoid racing an in-flight operation; any ownership marker is retained for startup recovery.";
            Trace.TraceWarning(message);
            _logger.Info(message);
            return false;
        }
    }

    private async Task TryRestoreOwnedFansForExitAsync()
    {
        try
        {
            await _cleanExitFanRecoveryService
                .RestoreOwnedFansAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            // The recovery service logs the safety failure and deliberately leaves
            // any surviving ownership marker in place for next-start recovery.
            Trace.TraceError($"Clean exit fan recovery failed: {exception}");
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

    private void OnTrayProfileActionsStateRefreshRequested(object? sender, EventArgs e)
    {
        RunOnDispatcher(RefreshTrayProfileActionsState);
    }

    private void OnTrayGamingOptimisedRequested(object? sender, EventArgs e)
    {
        RunOnDispatcher(() => TryExecuteTrayProfileAction(GamingOptimisedProfileId));
    }

    private void OnTrayRestoreOriginalSettingsRequested(object? sender, EventArgs e)
    {
        RunOnDispatcher(() => TryExecuteTrayProfileAction(RestoreProfileId));
    }

    private void RefreshTrayProfileActionsState()
    {
        var profileButtons = DataContext is MainViewModel viewModel
            ? viewModel.ProfileButtons
            : [];

        _systemTrayIcon.SetProfileActionsEnabled(
            ProfileButtonCommandInvoker.CanExecute(profileButtons, GamingOptimisedProfileId),
            ProfileButtonCommandInvoker.CanExecute(profileButtons, RestoreProfileId));
    }

    private void TryExecuteTrayProfileAction(string profileId)
    {
        if (DataContext is MainViewModel viewModel)
        {
            ProfileButtonCommandInvoker.TryExecute(viewModel.ProfileButtons, profileId);
        }
    }

    private void RunOnDispatcher(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.Invoke(action);
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
        _systemTrayIcon.ProfileActionsStateRefreshRequested -= OnTrayProfileActionsStateRefreshRequested;
        _systemTrayIcon.GamingOptimisedRequested -= OnTrayGamingOptimisedRequested;
        _systemTrayIcon.RestoreOriginalSettingsRequested -= OnTrayRestoreOriginalSettingsRequested;
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
