using BootCampPerformanceControl.FanControl.Smc.Windows;

namespace BootCampPerformanceControl.FanControl.Smc.CrystalIdea;

internal sealed class AppleSmcServiceStateException : InvalidOperationException
{
    public AppleSmcServiceStateException(AppleSmcServiceState state)
        : base(
            $"Windows service '{WindowsAppleSmcServiceController.ServiceName}' is not running. "
            + $"Observed state: '{state}' ({(uint)state}).")
    {
        State = state;
    }

    public AppleSmcServiceState State { get; }
}
