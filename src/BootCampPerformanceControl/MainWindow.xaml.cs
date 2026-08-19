using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using BootCampPerformanceControl.Logging;
using BootCampPerformanceControl.UI;

namespace BootCampPerformanceControl;

public partial class MainWindow : Window
{
    private static readonly TimeSpan FanMonitoringShutdownTimeout = TimeSpan.FromSeconds(3);

    private bool _isLoaded;
    private bool _closeDeferralStarted;
    private bool _allowFinalClose;

    public MainWindow()
        : this(AppCompositionRoot.CreateMainViewModel(new FileApplicationLogger()))
    {
    }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
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

        _closeDeferralStarted = true;

        try
        {
            if (DataContext is MainViewModel viewModel)
            {
                await viewModel
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

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        var aboutWindow = new AboutWindow
        {
            Owner = this
        };
        aboutWindow.ShowDialog();
    }
}
