using System.Windows;
using BootCampPerformanceControl.Logging;
using BootCampPerformanceControl.UI;

namespace BootCampPerformanceControl;

public partial class MainWindow : Window
{
    private bool _isLoaded;

    public MainWindow()
        : this(AppCompositionRoot.CreateMainViewModel(new FileApplicationLogger()))
    {
    }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;

        if (DataContext is MainViewModel viewModel && viewModel.RefreshCommand.CanExecute(null))
        {
            viewModel.RefreshCommand.Execute(null);
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
