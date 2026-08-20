using System.Windows;

namespace BootCampPerformanceControl.UI;

internal sealed class WindowsClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        System.Windows.Clipboard.SetText(text);
    }
}
