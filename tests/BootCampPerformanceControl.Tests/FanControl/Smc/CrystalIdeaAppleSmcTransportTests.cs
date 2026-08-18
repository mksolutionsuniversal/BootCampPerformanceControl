using System.IO;
using BootCampPerformanceControl.FanControl.Smc;
using BootCampPerformanceControl.FanControl.Smc.CrystalIdea;
using BootCampPerformanceControl.FanControl.Smc.Windows;

namespace BootCampPerformanceControl.Tests.FanControl.Smc;

public sealed class CrystalIdeaAppleSmcTransportTests
{
    [Fact]
    public async Task GetProtocolAsync_UsesConfirmedProtocolIoctl()
    {
        using var device = new FakeDeviceIoControlClient
        {
            Handler = (controlCode, input, outputBufferLength) =>
            {
                Assert.Equal(CrystalIdeaAppleSmcIoctl.GetProtocol, controlCode);
                Assert.True(input.IsEmpty);
                Assert.Equal(1, outputBufferLength);
                return [0x01];
            }
        };
        await using var transport = new CrystalIdeaAppleSmcTransport(device);

        var result = await transport.GetProtocolAsync(CancellationToken.None);

        Assert.Equal(SmcTransportProtocol.Mmio, result);
        Assert.Equal(1, device.InvocationCount);
    }

    [Fact]
    public async Task GetKeyInfoAsync_UsesConfirmedRequestAndParsesMetadata()
    {
        using var device = new FakeDeviceIoControlClient
        {
            Handler = (controlCode, input, outputBufferLength) =>
            {
                Assert.Equal(CrystalIdeaAppleSmcIoctl.GetKeyInfo, controlCode);
                Assert.Equal(new byte[] { 0x46, 0x30, 0x4D, 0x78 }, input.ToArray());
                Assert.Equal(CrystalIdeaAppleSmcCodec.KeyInfoLength, outputBufferLength);
                return [0x04, 0x66, 0x6C, 0x74, 0x20, 0x85];
            }
        };
        await using var transport = new CrystalIdeaAppleSmcTransport(device);

        var result = await transport.GetKeyInfoAsync("F0Mx", CancellationToken.None);

        Assert.Equal("F0Mx", result.Key);
        Assert.Equal((byte)4, result.Length);
        Assert.Equal("flt ", result.Type);
        Assert.Equal((byte)0x85, result.Attributes);
        Assert.Equal(1, device.InvocationCount);
    }

    [Fact]
    public async Task ReadKeyAsync_UsesConfirmedReadRequest()
    {
        using var device = new FakeDeviceIoControlClient
        {
            Handler = (controlCode, input, outputBufferLength) =>
            {
                Assert.Equal(CrystalIdeaAppleSmcIoctl.ReadKey, controlCode);
                Assert.Equal(
                    new byte[] { 0x46, 0x30, 0x4D, 0x78, 0x04 },
                    input.ToArray());
                Assert.Equal(4, outputBufferLength);
                return [0x00, 0x80, 0xAF, 0x45];
            }
        };
        await using var transport = new CrystalIdeaAppleSmcTransport(device);

        var result = await transport.ReadKeyAsync("F0Mx", 4, CancellationToken.None);

        Assert.Equal(new byte[] { 0x00, 0x80, 0xAF, 0x45 }, result.ToArray());
        Assert.Equal(1, device.InvocationCount);
    }

    [Fact]
    public async Task GetProtocolAsync_RejectsMalformedResponse()
    {
        using var device = new FakeDeviceIoControlClient
        {
            Handler = (_, _, _) => []
        };
        await using var transport = new CrystalIdeaAppleSmcTransport(device);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => transport.GetProtocolAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetProtocolAsync_HonoursCancellationBeforeDeviceAccess()
    {
        using var device = new FakeDeviceIoControlClient();
        await using var transport = new CrystalIdeaAppleSmcTransport(device);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transport.GetProtocolAsync(cancellationSource.Token));

        Assert.Equal(0, device.InvocationCount);
    }

    [Fact]
    public async Task DisposeAsync_DisposesDeviceClient()
    {
        var device = new FakeDeviceIoControlClient();
        var transport = new CrystalIdeaAppleSmcTransport(device);

        await transport.DisposeAsync();

        Assert.True(device.IsDisposed);
    }

    private sealed class FakeDeviceIoControlClient : IDeviceIoControlClient
    {
        public Func<uint, ReadOnlyMemory<byte>, int, byte[]> Handler { get; init; } =
            (_, _, _) => throw new InvalidOperationException("Unexpected device access.");

        public int InvocationCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public byte[] Invoke(
            uint controlCode,
            ReadOnlyMemory<byte> input,
            int outputBufferLength)
        {
            InvocationCount++;
            return Handler(controlCode, input, outputBufferLength);
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
