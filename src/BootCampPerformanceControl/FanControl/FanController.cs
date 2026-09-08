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

        var readings = capability.Family is FanCapabilityFamily.PerFanModeFloat32
            or FanCapabilityFamily.GlobalMaskFpe2
            ? CreateReadings(capability)
            : Array.Empty<FanChannelReading>();

        var details = capability.Family switch
        {
            FanCapabilityFamily.Passive =>
                "No controllable fans reported by AppleSMC (passive/fanless topology).",
            FanCapabilityFamily.Unknown =>
                "Write capability not verified. Read-only capability fingerprint is available in the compatibility report.",
            _ when capability.IsHardwareSafetyGateSatisfied =>
                "The AppleSMC read-only protocol and fan capability family were verified.",
            _ => string.Join(" ", capability.Failures)
        };

        return new FanControllerReadResult(
            new FanControlStatus(
                FanBackendState.Running,
                FanSafetyState.ReadOnlyVerified,
                readings,
                details,
                writeControlState)
            {
                TransportDisplayText = FormatTransport(capability.Protocol),
                ReportedFanCount = TryGetReportedFanCount(snapshot),
                DiscoveredFanCount = snapshot.Fans.Count,
                CapabilityFamily = capability.Family,
                CapabilityDiagnostics = CreateCapabilityDiagnostics(capability)
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
            DiscoveredFanCount = capability.Snapshot?.Fans.Count,
            CapabilityFamily = capability.Family,
            CapabilityDiagnostics = CreateCapabilityDiagnostics(capability)
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

    private static IReadOnlyList<FanChannelReading> CreateReadings(
        FanControlCapabilityResult capability)
    {
        var strategy = FanCapabilityFamilyStrategies.Get(capability.Family);
        return capability.Snapshot!.Fans.Select(fan => new FanChannelReading(
            fan.Index.Value,
            new FanReading(
                FanSafetyPolicy.DecodeRpm(fan.Actual, capability.Family),
                FanSafetyPolicy.DecodeRpm(fan.Maximum, capability.Family),
                strategy.GetFanMode(capability, fan.Index)))).ToArray();
    }

    private static FanWriteControlState GetObservedWriteControlState(
        FanControlCapabilityResult capability)
    {
        if (!capability.IsHardwareSafetyGateSatisfied)
        {
            return FanWriteControlState.NotAvailable;
        }

        var strategy = FanCapabilityFamilyStrategies.Get(capability.Family);
        var plan = strategy.CreateMaximumSafeRpmPlan(string.Empty, capability);
        if (strategy.IsManualMaximum(capability, plan))
        {
            return FanWriteControlState.MaximumSafeRpmDetected;
        }

        return !strategy.IsAppleAuto(capability)
            ? FanWriteControlState.ManualModeDetected
            : FanWriteControlState.Available;
    }

    private static IReadOnlyList<string> CreateCapabilityDiagnostics(
        FanControlCapabilityResult capability)
    {
        if (capability.Snapshot is null)
        {
            return capability.Failures;
        }

        var lines = new List<string>();
        lines.Add(FormatObservation(
            SmcKeyObservation.Available(capability.Snapshot.FanCount)));
        foreach (var fan in capability.Snapshot.Fans)
        {
            foreach (var observation in fan.Observations)
            {
                lines.Add(FormatObservation(observation));
            }
        }

        lines.Add(FormatObservation(capability.Snapshot.GlobalMode));
        lines.AddRange(capability.Failures.Select(failure => $"Classifier: {failure}"));
        return lines;
    }

    private static string FormatObservation(SmcKeyObservation observation)
    {
        if (observation.Value is null)
        {
            return observation.State == SmcKeyObservationState.ConfirmedAbsent
                ? $"{observation.Key}: absent"
                : $"{observation.Key}: read failed: {observation.Failure}";
        }

        var value = observation.Value;
        var decoded = value.Info.Type switch
        {
            "flt " when value.Info.Length == 4 => $"; rpm={value.GetFloat32():0.###}",
            "fpe2" when value.Info.Length == 2 => $"; rpm={value.GetFpe2():0.###}",
            "ui8 " when value.Info.Length == 1 => $"; value={value.GetUInt8()}",
            "ui16" when value.Info.Length == 2 => $"; value=0x{value.GetUInt16BigEndian():X4}",
            _ => string.Empty
        };
        return $"{value.Info.Key}: type='{value.Info.Type}'; len={value.Info.Length}; attrs=0x{value.Info.Attributes:X2}; raw={Convert.ToHexString(value.RawData.Span)}{decoded}";
    }
}
