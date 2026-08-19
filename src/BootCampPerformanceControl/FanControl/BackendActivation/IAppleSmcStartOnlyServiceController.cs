using BootCampPerformanceControl.FanControl.Smc.Windows;

namespace BootCampPerformanceControl.FanControl.BackendActivation;

internal interface IAppleSmcStartOnlyServiceController : IDisposable
{
    AppleSmcServiceState GetState();

    void Start();
}
