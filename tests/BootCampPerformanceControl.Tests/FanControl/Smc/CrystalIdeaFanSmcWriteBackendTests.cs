using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc.CrystalIdea;
using BootCampPerformanceControl.FanControl.Smc.Windows;

namespace BootCampPerformanceControl.Tests.FanControl.Smc;

public sealed class CrystalIdeaFanSmcWriteBackendTests
{
    [Fact]
    public void WriteIoctl_MatchesConfirmedValue()
    {
        Assert.Equal(0x220004u, CrystalIdeaAppleSmcIoctl.WriteKey);
    }

    [Fact]
    public async Task SetManualModeAsync_Fan0_UsesConfirmedWriteRequest()
    {
        using var device = ExpectSingleWrite(
            new byte[] { 0x46, 0x30, 0x4D, 0x64, 0x01, 0x01 });
        await using var backend = new CrystalIdeaFanSmcWriteBackend(device);

        await backend.SetManualModeAsync(new FanIndex(0), CancellationToken.None);

        Assert.Equal(1, device.InvocationCount);
    }

    [Fact]
    public async Task SetManualModeAsync_Fan1_UsesConfirmedWriteRequest()
    {
        using var device = ExpectSingleWrite(
            new byte[] { 0x46, 0x31, 0x4D, 0x64, 0x01, 0x01 });
        await using var backend = new CrystalIdeaFanSmcWriteBackend(device);

        await backend.SetManualModeAsync(new FanIndex(1), CancellationToken.None);

        Assert.Equal(1, device.InvocationCount);
    }

    [Theory]
    [InlineData(0, 0x30, 4321.25f, 0x00, 0x0A, 0x87, 0x45)]
    [InlineData(1, 0x31, 4789.5f, 0x00, 0xAC, 0x95, 0x45)]
    [InlineData(9, 0x39, 4800f, 0x00, 0x00, 0x96, 0x45)]
    public async Task SetTargetRpmAsync_EncodesSuppliedRpmAsLittleEndianFloat32(
        int fanValue,
        byte fanAscii,
        float targetRpm,
        byte byte0,
        byte byte1,
        byte byte2,
        byte byte3)
    {
        using var device = ExpectSingleWrite(
            new byte[] { 0x46, fanAscii, 0x54, 0x67, 0x04, byte0, byte1, byte2, byte3 });
        await using var backend = new CrystalIdeaFanSmcWriteBackend(device);
        var fan = (FanIndex)fanValue;

        await backend.SetTargetRpmAsync(fan, targetRpm, CancellationToken.None);

        Assert.Equal(1, device.InvocationCount);
    }

    [Theory]
    [InlineData(0, 0x30)]
    [InlineData(1, 0x31)]
    public async Task SetAppleAutoAsync_WritesOnlyPerFanModeZero(
        int fanValue,
        byte fanAscii)
    {
        using var device = ExpectSingleWrite(
            new byte[] { 0x46, fanAscii, 0x4D, 0x64, 0x01, 0x00 });
        await using var backend = new CrystalIdeaFanSmcWriteBackend(device);
        var fan = (FanIndex)fanValue;

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
        await using var backend = new CrystalIdeaFanSmcWriteBackend(device);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => backend.SetTargetRpmAsync(
                new FanIndex(0),
                targetRpm,
                CancellationToken.None));

        Assert.Equal(0, device.InvocationCount);
    }

    [Fact]
    public async Task WriteOperations_HonourCancellationBeforeDeviceAccess()
    {
        using var device = new FakeDeviceIoControlClient();
        await using var backend = new CrystalIdeaFanSmcWriteBackend(device);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => backend.SetManualModeAsync(
                new FanIndex(0),
                cancellationSource.Token));

        Assert.Equal(0, device.InvocationCount);
    }

    [Fact]
    public async Task InvalidFanIndex_IsRejectedBeforeDeviceAccess()
    {
        using var device = new FakeDeviceIoControlClient();
        await using var backend = new CrystalIdeaFanSmcWriteBackend(device);

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

    [Theory]
    [InlineData("FS! ")]
    [InlineData("F0Mx")]
    [InlineData("F0Ac")]
    [InlineData("F0Mn")]
    [InlineData("F10Md")]
    [InlineData("FAMd")]
    [InlineData("F0md")]
    public void Codec_RejectsKeysOutsideF0ThroughF9ModeAndTargetWhitelist(string key)
    {
        Assert.Throws<ArgumentException>(
            () => CrystalIdeaAppleSmcCodec.BuildWhitelistedFanWriteRequest(
                key,
                new byte[] { 0 }));
    }

    [Fact]
    public void Codec_AcceptsHighestRepresentableFanModeAndTargetKeys()
    {
        var modeRequest = CrystalIdeaAppleSmcCodec.BuildWhitelistedFanWriteRequest(
            "F9Md",
            new byte[] { 1 });
        var targetRequest = CrystalIdeaAppleSmcCodec.BuildWhitelistedFanWriteRequest(
            "F9Tg",
            new byte[] { 0, 0, 0, 0 });

        Assert.Equal(new byte[] { 0x46, 0x39, 0x4D, 0x64, 0x01, 0x01 }, modeRequest);
        Assert.Equal(new byte[] { 0x46, 0x39, 0x54, 0x67, 0x04, 0, 0, 0, 0 }, targetRequest);
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
    public async Task WriteOutputByte_IsNotTreatedAsVerification()
    {
        using var device = new FakeDeviceIoControlClient
        {
            Handler = (controlCode, input, outputBufferLength) =>
            {
                Assert.Equal(CrystalIdeaAppleSmcIoctl.WriteKey, controlCode);
                Assert.Equal(
                    new byte[] { 0x46, 0x30, 0x4D, 0x64, 0x01, 0x00 },
                    input.ToArray());
                Assert.Equal(1, outputBufferLength);
                return [0x00];
            }
        };
        await using var backend = new CrystalIdeaFanSmcWriteBackend(device);

        await backend.SetAppleAutoAsync(new FanIndex(0), CancellationToken.None);

        Assert.Equal(1, device.InvocationCount);
    }

    [Fact]
    public async Task DisposeAsync_DisposesDeviceClient()
    {
        var device = new FakeDeviceIoControlClient();
        var backend = new CrystalIdeaFanSmcWriteBackend(device);

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
