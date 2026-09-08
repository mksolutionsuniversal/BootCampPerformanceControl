namespace BootCampPerformanceControl.FanControl;

internal interface IFanSmcWriteBackend
{
    bool SupportsFamily(FanCapabilityFamily family) =>
        family == FanCapabilityFamily.PerFanModeFloat32;

    Task SetManualModeAsync(
        FanIndex fan,
        CancellationToken cancellationToken);

    Task SetTargetRpmAsync(
        FanIndex fan,
        float targetRpm,
        CancellationToken cancellationToken);

    Task SetAppleAutoAsync(
        FanIndex fan,
        CancellationToken cancellationToken);

    Task SetGlobalManualMaskAsync(
        ushort mask,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("GlobalMaskFpe2 writes are not implemented by this backend.");

    Task SetFpe2TargetPayloadAsync(
        FanIndex fan,
        ReadOnlyMemory<byte> exactPayload,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("GlobalMaskFpe2 writes are not implemented by this backend.");

    Task SetGlobalAppleAutoAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("GlobalMaskFpe2 writes are not implemented by this backend.");
}
