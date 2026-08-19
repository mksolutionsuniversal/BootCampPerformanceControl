namespace BootCampPerformanceControl.FanControl.Smc.Windows;

internal interface IAppleSmcServiceController : IDisposable
{
    AppleSmcServiceState GetState();

    void Start();

    void Stop();
}
