using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc.CrystalIdea;
using BootCampPerformanceControl.FanControl.Smc.Windows;

namespace BootCampPerformanceControl.Tests.FanControl.Smc;

public sealed class CrystalIdeaResearchFanSmcWriteBackendTests
{
    [Fact]
    public async Task SetManualModeAsync_Fan0_UsesConfirmedWriteRequest()
    {
        using var device = ExpectSingleWrite(
            new byte[] { 0x46, 0x30, 0x4D, 0x64, 0x01, 0x01 });
        await using var backend = new CrystalIdeaResearchFanSmcWriteBackend(device);

        await backend.SetManualModeAsync(FanIndex.Fan0, CancellationToken.None);

        Assert.Equal(1, device.InvocationCount);
    }

    [Fact]
    public async Task SetManualModeAsync_Fan1_UsesConfirmedWriteRequest()
    {
        using var device = ExpectSingleWrite(
            new byte[] { 0x46, 0x31, 0x4D, 0x64, 0x01, 0x01 });
        await using var backend = new CrystalIdeaResearchFanSmcWriteBackend(device);

        await backend.SetManualModeAsync(FanIndex.Fan1, CancellationToken.None);

        Assert.Equal(1, device.InvocationCount);
    }

    [Fact]
    public async Task SetTargetRpmAsync_Fan0_EncodesVerifiedMaximumAsLittleEndianFloat32()
    {
        using var device = ExpectSingleWrite(
            new byte[] { 0x46, 0x30, 0x54, 0x67, 0x04, 0x00, 0x80, 0xAF, 0x45 });
        await using var backend = new CrystalIdeaResearchFanSmcWriteBackend(device);

        await backend.SetTargetRpmAsync(FanIndex.Fan0, 5616f, CancellationToken.None);

        Assert.Equal(1, device.InvocationCount);
    }

    [Fact]
    public async Task SetTargetRpmAsync_Fan1_EncodesVerifiedMaximumAsLittleEndianFloat32()
    {
        using var device = ExpectSingleWrite(
            new byte[] { 0x46, 0x31, 0x54, 0x67, 0x04, 0x00, 0x80, 0xA2, 0x45 });
        await using var backend = new CrystalIdeaResearchFanSmcWriteBackend(device);

        await backend.SetTargetRpmAsync(FanIndex.Fan1, 5200f, CancellationToken.None);

        Assert.Equal(1, device.InvocationCount);
    }

    [Theory]
    [InlineData(FanIndex.Fan0, 0x30)]
    [InlineData(FanIndex.Fan1, 0x31)]
    public async Task SetAppleAutoAsync_WritesOnlyPerFanModeZero(
        FanIndex fan,
        byte fanAscii)
    {
        using var device = ExpectSingleWrite(
            new byte[] { 0x46, fanAscii, 0x4D, 0x64, 0x01, 0x00 });
        await using var backend = new CrystalIdeaResearchFanSmcWriteBackend(device);

        await backend.SetAppleAutoAsync(fan, CancellationToken.None);

        Assert.Equal(1, device.InvocationCount);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public async Task SetTargetRpmAsync_RejectsInvalidValuesBeforeDeviceAccess(float targetRpm)
    {
        using var device = new FakeDeviceIoControlClient();
        await using var backend = new CrystalIdeaResearchFanSmcWriteBackend(device);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => backend.SetTargetRpmAsync(
                FanIndex.Fan0,
                targetRpm,
                CancellationToken.None));

        Assert.Equal(0, device.InvocationCount);
    }

    [Fact]
    public async Task WriteOperations_HonourCancellationBeforeDeviceAccess()
    {
        using var device = new FakeDeviceIoControlClient();
        await using var backend = new CrystalIdeaResearchFanSmcWriteBackend(device);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => backend.SetManualModeAsync(
                FanIndex.Fan0,
                cancellationSource.Token));

        Assert.Equal(0, device.InvocationCount);
    }

    [Fact]
    public async Task InvalidFanIndex_IsRejectedBeforeDeviceAccess()
    {
        using var device = new FakeDeviceIoControlClient();
        await using var backend = new CrystalIdeaResearchFanSmcWriteBackend(device);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => backend.SetAppleAutoAsync(
                (FanIndex)99,
                CancellationToken.None));

        Assert.Equal(0, device.InvocationCount);
    }

    [Fact]
    public void Codec_RejectsNonWhitelistedWriteKey()
    {
        Assert.Throws<ArgumentException>(
            () => CrystalIdeaAppleSmcCodec.BuildWhitelistedFanWriteRequest(
                "F0Ac",
                new byte[] { 0, 0, 0, 0 }));
    }

    [Fact]
    public void Codec_RejectsWrongLengthForWhitelistedKey()
    {
        Assert.Throws<ArgumentException>(
            () => CrystalIdeaAppleSmcCodec.BuildWhitelistedFanWriteRequest(
                "F0Md",
                new byte[] { 0, 1 }));
    }

    [Fact]
    public async Task DisposeAsync_DisposesDeviceClient()
    {
        var device = new FakeDeviceIoControlClient();
        var backend = new CrystalIdeaResearchFanSmcWriteBackend(device);

        await backend.DisposeAsync();

        Assert.True(device.IsDisposed);
    }

    private static FakeDeviceIoControlClient ExpectSingleWrite(byte[] expectedInput)
    {
        return new FakeDeviceIoControlClient
        {
            Handler = (controlCode, input, outputBufferLength) =>
            {
                Assert.Equal(CrystalIdeaAppleSmcIoctl.WriteKey, controlCode);
                Assert.Equal(expectedInput, input.ToArray());
                Assert.Equal(1, outputBufferLength);

                // Observed successful WRITE_KEY calls returned one byte. Its value
                // is intentionally not interpreted by production code.
                return [0x46];
            }
        };
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
