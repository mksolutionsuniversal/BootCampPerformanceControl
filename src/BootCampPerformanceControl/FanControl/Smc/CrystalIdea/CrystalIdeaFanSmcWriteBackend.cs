using System.Buffers.Binary;
using BootCampPerformanceControl.FanControl.Smc.Windows;

namespace BootCampPerformanceControl.FanControl.Smc.CrystalIdea;

// Concrete write backend for the independently installed AppleSMC compatibility driver.
internal sealed class CrystalIdeaFanSmcWriteBackend :
    IFanSmcWriteBackend,
    IAsyncDisposable
{
    private const int WriteOutputBufferLength = 1;

    private readonly IDeviceIoControlClient _device;

    public CrystalIdeaFanSmcWriteBackend(IDeviceIoControlClient device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
    }

    public Task SetManualModeAsync(
        FanIndex fan,
        CancellationToken cancellationToken)
    {
        return WriteModeAsync(fan, 1, cancellationToken);
    }

    public Task SetTargetRpmAsync(
        FanIndex fan,
        float targetRpm,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var targetKey = GetTargetKey(fan);

        if (!float.IsFinite(targetRpm) || targetRpm <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetRpm),
                "The AppleSMC write backend only permits positive finite target RPM values.");
        }

        Span<byte> data = stackalloc byte[sizeof(float)];
        BinaryPrimitives.WriteInt32LittleEndian(
            data,
            BitConverter.SingleToInt32Bits(targetRpm));

        WriteWhitelistedKey(targetKey, data);
        return Task.CompletedTask;
    }

    public Task SetAppleAutoAsync(
        FanIndex fan,
        CancellationToken cancellationToken)
    {
        return WriteModeAsync(fan, 0, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _device.Dispose();
        return ValueTask.CompletedTask;
    }

    private Task WriteModeAsync(
        FanIndex fan,
        byte mode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WriteWhitelistedKey(GetModeKey(fan), [mode]);
        return Task.CompletedTask;
    }

    private void WriteWhitelistedKey(string key, ReadOnlySpan<byte> data)
    {
        var request = CrystalIdeaAppleSmcCodec.BuildWhitelistedFanWriteRequest(key, data);

        // The observed AppleSMC WRITE_KEY contract uses a one-byte output buffer.
        // Its returned byte is intentionally ignored; VerifiedFanOverrideWriter
        // performs the authoritative post-write SMC readback verification.
        _ = _device.Invoke(
            CrystalIdeaAppleSmcIoctl.WriteKey,
            request,
            WriteOutputBufferLength);
    }

    private static string GetModeKey(FanIndex fan)
    {
        return fan switch
        {
            FanIndex.Fan0 => "F0Md",
            FanIndex.Fan1 => "F1Md",
            _ => throw new ArgumentOutOfRangeException(nameof(fan))
        };
    }

    private static string GetTargetKey(FanIndex fan)
    {
        return fan switch
        {
            FanIndex.Fan0 => "F0Tg",
            FanIndex.Fan1 => "F1Tg",
            _ => throw new ArgumentOutOfRangeException(nameof(fan))
        };
    }
}
