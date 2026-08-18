namespace BootCampPerformanceControl.FanControl.Smc;

internal interface ISmcTransport : IAsyncDisposable
{
    Task<SmcTransportProtocol> GetProtocolAsync(CancellationToken cancellationToken);

    Task<SmcKeyInfo> GetKeyInfoAsync(string key, CancellationToken cancellationToken);

    Task<ReadOnlyMemory<byte>> ReadKeyAsync(
        string key,
        byte length,
        CancellationToken cancellationToken);
}
