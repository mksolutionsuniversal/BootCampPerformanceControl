using BootCampPerformanceControl.FanControl.Smc.Windows;

namespace BootCampPerformanceControl.FanControl.Smc.CrystalIdea;

internal sealed class CrystalIdeaAppleSmcTransport : ISmcTransport
{
    internal const string DevicePath = @"\\.\APPLESMC";

    private readonly IDeviceIoControlClient _device;

    public CrystalIdeaAppleSmcTransport(IDeviceIoControlClient device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
    }

    public static CrystalIdeaAppleSmcTransport OpenInstalledDriver()
    {
        return new CrystalIdeaAppleSmcTransport(
            new WindowsDeviceIoControlClient(DevicePath));
    }

    public Task<SmcTransportProtocol> GetProtocolAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var response = _device.Invoke(
            CrystalIdeaAppleSmcIoctl.GetProtocol,
            ReadOnlyMemory<byte>.Empty,
            1);

        return Task.FromResult(CrystalIdeaAppleSmcCodec.ParseProtocol(response));
    }

    public Task<SmcKeyInfo> GetKeyInfoAsync(
        string key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var request = CrystalIdeaAppleSmcCodec.EncodeKey(key);
        var response = _device.Invoke(
            CrystalIdeaAppleSmcIoctl.GetKeyInfo,
            request,
            CrystalIdeaAppleSmcCodec.KeyInfoLength);

        return Task.FromResult(CrystalIdeaAppleSmcCodec.ParseKeyInfo(key, response));
    }

    public Task<ReadOnlyMemory<byte>> ReadKeyAsync(
        string key,
        byte length,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var request = CrystalIdeaAppleSmcCodec.BuildReadKeyRequest(key, length);
        var response = _device.Invoke(
            CrystalIdeaAppleSmcIoctl.ReadKey,
            request,
            length);

        return Task.FromResult<ReadOnlyMemory<byte>>(response);
    }

    public ValueTask DisposeAsync()
    {
        _device.Dispose();
        return ValueTask.CompletedTask;
    }
}
