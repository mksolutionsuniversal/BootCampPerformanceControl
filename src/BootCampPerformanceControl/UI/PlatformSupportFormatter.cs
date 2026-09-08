using BootCampPerformanceControl.HardwareDetection;

namespace BootCampPerformanceControl.UI;

public static class PlatformSupportFormatter
{
    public static string FormatPlatformSupport(PlatformSupportStatus status)
    {
        return status switch
        {
            PlatformSupportStatus.SupportedIntelMac => "Supported Intel Mac",
            PlatformSupportStatus.UnsupportedNonApple => "Unsupported - non-Apple hardware",
            PlatformSupportStatus.UnsupportedNonIntel => "Unsupported - non-Intel Apple platform",
            PlatformSupportStatus.DetectionIncomplete => "Detection incomplete",
            _ => "Not checked"
        };
    }
}
