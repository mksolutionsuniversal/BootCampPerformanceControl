namespace BootCampPerformanceControl.FanControl;

internal sealed class FanController
{
    private readonly IFanCapabilityProbe _capabilityProbe;

    public FanController(IFanCapabilityProbe capabilityProbe)
    {
        _capabilityProbe = capabilityProbe ?? throw new ArgumentNullException(nameof(capabilityProbe));
    }

    public async Task<FanControllerReadResult> ReadStatusAsync(
        string model,
        CancellationToken cancellationToken)
    {
        var capability = await _capabilityProbe
            .ProbeAsync(model, cancellationToken)
            .ConfigureAwait(false);

        if (!capability.IsReadSupported ||
            !capability.IsHardwareSafetyGateSatisfied ||
            capability.Snapshot is null)
        {
            return new FanControllerReadResult(
                CreateUnavailableStatus(capability),
                capability);
        }

        var snapshot = capability.Snapshot;
        var fan0Actual = snapshot.Fan0Actual.GetFloat32();
        var fan1Actual = snapshot.Fan1Actual.GetFloat32();
        var fan0Maximum = snapshot.Fan0Maximum.GetFloat32();
        var fan1Maximum = snapshot.Fan1Maximum.GetFloat32();
        var fan0Mode = GetMode(snapshot.Fan0Mode.GetUInt8());
        var fan1Mode = GetMode(snapshot.Fan1Mode.GetUInt8());

        return new FanControllerReadResult(
            new FanControlStatus(
                FanBackendState.Running,
                FanSafetyState.ReadOnlyVerified,
                new FanReading(fan0Actual, fan0Maximum, fan0Mode),
                new FanReading(fan1Actual, fan1Maximum, fan1Mode),
                "The AppleSMC read-only protocol and fan metadata were verified."),
            capability);
    }

    internal static FanControlStatus CreateUnavailableStatus(
        FanControlCapabilityResult capability)
    {
        ArgumentNullException.ThrowIfNull(capability);

        var reason = capability.Failures.Count == 0
            ? "Fan read capability is not available."
            : string.Join(" ", capability.Failures);

        return FanControlStatus.CreateUnavailable(
            FanBackendState.Running,
            FanSafetyState.ReadOnlyUnavailable,
            reason);
    }

    private static FanOperatingMode GetMode(byte mode)
    {
        return mode switch
        {
            0 => FanOperatingMode.AppleAuto,
            1 => FanOperatingMode.Manual,
            _ => FanOperatingMode.Unknown
        };
    }
}
