using BootCampPerformanceControl.FanControl.Smc.Windows;

namespace BootCampPerformanceControl.FanControl.Smc.CrystalIdea;

internal sealed class CrystalIdeaAppleSmcTransportFactory : IAppleSmcTransportFactory
{
    public ISmcTransport Open()
    {
        return new CrystalIdeaAppleSmcTransport(
            WindowsDeviceIoControlClient.OpenExclusive(
                CrystalIdeaAppleSmcTransport.DevicePath));
    }
}
