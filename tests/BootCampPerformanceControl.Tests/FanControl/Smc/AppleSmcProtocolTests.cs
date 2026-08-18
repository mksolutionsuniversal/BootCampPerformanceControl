using BootCampPerformanceControl.FanControl.Smc;

namespace BootCampPerformanceControl.Tests.FanControl.Smc;

public sealed class AppleSmcProtocolTests
{
    [Fact]
    public async Task GetProtocolAsync_ReturnsTransportProtocol()
    {
        await using var transport = new FakeSmcTransport();
        var protocol = new AppleSmcProtocol(transport);

        var result = await protocol.GetProtocolAsync(CancellationToken.None);

        Assert.Equal(SmcTransportProtocol.Mmio, result);
    }

    [Fact]
    public async Task ReadKeyAsync_UsesMetadataLengthAndPreservesRawData()
    {
        await using var transport = new FakeSmcTransport();
        transport.Add(
            new SmcKeyInfo("F0Mx", 4, "flt ", 0x85),
            [0x00, 0x80, 0xAF, 0x45]);
        var protocol = new AppleSmcProtocol(transport);

        var result = await protocol.ReadKeyAsync("F0Mx", CancellationToken.None);

        Assert.Equal("F0Mx", result.Info.Key);
        Assert.Equal((byte)4, result.Info.Length);
        Assert.Equal("flt ", result.Info.Type);
        Assert.Equal((byte)0x85, result.Info.Attributes);
        Assert.Equal(new byte[] { 0x00, 0x80, 0xAF, 0x45 }, result.RawData.ToArray());
        Assert.Equal(("F0Mx", (byte)4), Assert.Single(transport.ReadRequests));
    }

    [Fact]
    public async Task ReadKeyAsync_DecodesConfirmedUInt8Value()
    {
        await using var transport = new FakeSmcTransport();
        transport.Add(
            new SmcKeyInfo("FNum", 1, "ui8 ", 0x80),
            [0x02]);
        var protocol = new AppleSmcProtocol(transport);

        var result = await protocol.ReadKeyAsync("FNum", CancellationToken.None);

        Assert.Equal((byte)2, result.GetUInt8());
    }

    [Fact]
    public async Task ReadKeyAsync_DecodesConfirmedLittleEndianFloat32Value()
    {
        await using var transport = new FakeSmcTransport();
        transport.Add(
            new SmcKeyInfo("F0Mx", 4, "flt ", 0x85),
            [0x00, 0x80, 0xAF, 0x45]);
        var protocol = new AppleSmcProtocol(transport);

        var result = await protocol.ReadKeyAsync("F0Mx", CancellationToken.None);

        Assert.Equal(5616.0f, result.GetFloat32());
    }

    [Theory]
    [InlineData("")]
    [InlineData("F0M")]
    [InlineData("F0Max")]
    [InlineData("F0Mą")]
    public async Task ReadKeyAsync_RejectsInvalidKeys(string key)
    {
        await using var transport = new FakeSmcTransport();
        var protocol = new AppleSmcProtocol(transport);

        await Assert.ThrowsAsync<ArgumentException>(
            () => protocol.ReadKeyAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task ReadKeyAsync_RejectsMetadataForDifferentKey()
    {
        await using var transport = new FakeSmcTransport
        {
            KeyInfoOverride = new SmcKeyInfo("F1Mx", 4, "flt ", 0x85)
        };
        var protocol = new AppleSmcProtocol(transport);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => protocol.ReadKeyAsync("F0Mx", CancellationToken.None));
    }

    [Fact]
    public async Task ReadKeyAsync_RejectsValueLengthAboveDriverLimit()
    {
        await using var transport = new FakeSmcTransport
        {
            KeyInfoOverride = new SmcKeyInfo("F0Mx", 33, "flt ", 0x85)
        };
        var protocol = new AppleSmcProtocol(transport);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => protocol.ReadKeyAsync("F0Mx", CancellationToken.None));
    }

    [Fact]
    public async Task ReadKeyAsync_RejectsRawLengthMismatch()
    {
        await using var transport = new FakeSmcTransport();
        transport.Add(
            new SmcKeyInfo("F0Mx", 4, "flt ", 0x85),
            [0x00, 0x80]);
        var protocol = new AppleSmcProtocol(transport);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => protocol.ReadKeyAsync("F0Mx", CancellationToken.None));
    }

    private sealed class FakeSmcTransport : ISmcTransport
    {
        private readonly Dictionary<string, (SmcKeyInfo Info, byte[] Data)> _values = [];

        public SmcKeyInfo? KeyInfoOverride { get; init; }

        public List<(string Key, byte Length)> ReadRequests { get; } = [];

        public void Add(SmcKeyInfo info, byte[] data)
        {
            _values[info.Key] = (info, data);
        }

        public Task<SmcTransportProtocol> GetProtocolAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(SmcTransportProtocol.Mmio);
        }

        public Task<SmcKeyInfo> GetKeyInfoAsync(
            string key,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (KeyInfoOverride is not null)
            {
                return Task.FromResult(KeyInfoOverride);
            }

            if (_values.TryGetValue(key, out var value))
            {
                return Task.FromResult(value.Info);
            }

            throw new KeyNotFoundException(key);
        }

        public Task<ReadOnlyMemory<byte>> ReadKeyAsync(
            string key,
            byte length,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadRequests.Add((key, length));

            if (_values.TryGetValue(key, out var value))
            {
                return Task.FromResult<ReadOnlyMemory<byte>>(value.Data);
            }

            throw new KeyNotFoundException(key);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
