namespace BootCampPerformanceControl.FanControl.Smc.Windows;

internal interface IDeviceIoControlClient : IDisposable
{
    byte[] Invoke(
        uint controlCode,
        ReadOnlyMemory<byte> input,
        int outputBufferLength);
}
