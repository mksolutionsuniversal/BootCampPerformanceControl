namespace BootCampPerformanceControl.FanControl.Smc.Windows;

internal sealed class NonOwningDeviceIoControlClient : IDeviceIoControlClient
{
    private readonly IDeviceIoControlClient _inner;

    public NonOwningDeviceIoControlClient(IDeviceIoControlClient inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public byte[] Invoke(
        uint controlCode,
        ReadOnlyMemory<byte> input,
        int outputBufferLength)
    {
        return _inner.Invoke(controlCode, input, outputBufferLength);
    }

    public void Dispose()
    {
    }
}
