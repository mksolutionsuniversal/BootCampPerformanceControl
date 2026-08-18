using System.Windows;

namespace BootCampPerformanceControl.UI;

public sealed class WpfUserConfirmationService : IUserConfirmationService
{
    public bool ConfirmUntestedModelApply(string modelName)
    {
        const string title = "Untested Mac Model";
        var message =
            "This Mac model has not been individually performance-tested." + Environment.NewLine + Environment.NewLine +
            "Gaming Optimised will limit the maximum processor state to 95% and disable CPU Boost." + Environment.NewLine + Environment.NewLine +
            "Thermal and performance behaviour may vary by model." + Environment.NewLine + Environment.NewLine +
            "Your original processor power settings will be saved before changes are applied." + Environment.NewLine + Environment.NewLine +
            "Do you want to continue?";

        var result = MessageBox.Show(
            message,
            title,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        return result == MessageBoxResult.OK;
    }
}
