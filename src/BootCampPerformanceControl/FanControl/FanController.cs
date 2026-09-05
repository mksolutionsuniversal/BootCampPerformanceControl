using BootCampPerformanceControl.FanControl.Smc;

namespace BootCampPerformanceControl.FanControl;

internal sealed class FanController
{
    private const float RpmComparisonTolerance = 1f;

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

        if (!capability.IsReadSupported || capability.Snapshot is null)
        {
            return new FanControllerReadResult(
                CreateUnavailableStatus(capability),
                capability);
        }

        var snapshot = capability.Snapshot;
        var writeControlState = GetObservedWriteControlState(capability);

        return new FanControllerReadResult(
            new FanControlStatus(
                FanBackendState.Running,
                FanSafetyState.ReadOnlyVerified,
                snapshot.Fans.Select(fan => new FanChannelReading(
                    fan.Index.Value,
                    new FanReading(
                        fan.Actual.GetFloat32(),
                        fan.Maximum.GetFloat32(),
                        GetMode(fan.Mode.GetUInt8())))).ToArray(),
                "The AppleSMC read-only protocol and fan metadata were verified.",
                writeControlState)
            {
                TransportDisplayText = FormatTransport(capability.Protocol),
                ReportedFanCount = TryGetReportedFanCount(snapshot),
                DiscoveredFanCount = snapshot.Fans.Count
            },
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
            reason) with
        {
            TransportDisplayText = FormatTransport(capability.Protocol),
            ReportedFanCount = TryGetReportedFanCount(capability.Snapshot),
            DiscoveredFanCount = capability.Snapshot?.Fans.Count
        };
    }

    private static int? TryGetReportedFanCount(FanSmcSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        try
        {
            return snapshot.FanCount.GetUInt8();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string FormatTransport(SmcTransportProtocol? protocol)
    {
        return protocol switch
        {
            SmcTransportProtocol.Mmio => "MMIO (protocol 1)",
            SmcTransportProtocol.Unknown => "Unknown (protocol 0)",
            _ => "Unavailable"
        };
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

    private static FanWriteControlState GetObservedWriteControlState(
        FanControlCapabilityResult capability)
    {
        var snapshot = capability.Snapshot
            ?? throw new InvalidOperationException(
                "A verified fan capability must include an SMC snapshot.");

        if (!capability.IsHardwareSafetyGateSatisfied)
        {
            return FanWriteControlState.NotAvailable;
        }

        if (snapshot.Fans.All(fan => fan.Mode.GetUInt8() == 1
            && ApproximatelyEqual(fan.Target.GetFloat32(), fan.Maximum.GetFloat32())))
        {
            return FanWriteControlState.MaximumSafeRpmDetected;
        }

        return snapshot.Fans.Any(fan => fan.Mode.GetUInt8() == 1)
            ? FanWriteControlState.ManualModeDetected
            : FanWriteControlState.Available;
    }

    private static bool ApproximatelyEqual(float left, float right)
    {
        return float.IsFinite(left)
            && float.IsFinite(right)
            && MathF.Abs(left - right) <= RpmComparisonTolerance;
    }
}
