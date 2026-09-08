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

    public bool SupportsFamily(FanCapabilityFamily family)
    {
        return family is FanCapabilityFamily.PerFanModeFloat32
            or FanCapabilityFamily.GlobalMaskFpe2;
    }

    public Task SetGlobalManualMaskAsync(
        ushort mask,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (mask is not 0x0001 and not 0x0003)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mask),
                "Only the physically evidenced one-fan and two-fan global manual masks are permitted.");
        }

        Span<byte> data = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(data, mask);
        WriteWhitelistedKey("FS! ", data);
        return Task.CompletedTask;
    }

    public Task SetFpe2TargetPayloadAsync(
        FanIndex fan,
        ReadOnlyMemory<byte> exactPayload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (exactPayload.Length != 2)
        {
            throw new ArgumentException(
                "An fpe2 target payload must contain exactly two bytes.",
                nameof(exactPayload));
        }

        WriteWhitelistedKey(GetTargetKey(fan), exactPayload.Span);
        return Task.CompletedTask;
    }

    public Task SetGlobalAppleAutoAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WriteWhitelistedKey("FS! ", [0, 0]);
        return Task.CompletedTask;
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
        return fan.GetSmcKey("Md");
    }

    private static string GetTargetKey(FanIndex fan)
    {
        return fan.GetSmcKey("Tg");
    }
}
