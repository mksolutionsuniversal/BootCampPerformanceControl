using System.Windows;
using BootCampPerformanceControl.ApplicationInfo;

namespace BootCampPerformanceControl.UI;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionTextBlock.Text = $"Version {ApplicationVersionProvider.GetInformationalVersion()}";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
