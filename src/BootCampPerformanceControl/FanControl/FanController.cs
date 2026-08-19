using System.Globalization;

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
        var fan0Mode = FormatMode(snapshot.Fan0Mode.GetUInt8());
        var fan1Mode = FormatMode(snapshot.Fan1Mode.GetUInt8());

        var displayText = string.Format(
            CultureInfo.InvariantCulture,
            "Fan Control: read-only verified. Fan 0: {0:0} / {1:0} RPM ({2}); Fan 1: {3:0} / {4:0} RPM ({5}). Write control is not enabled.",
            fan0Actual,
            fan0Maximum,
            fan0Mode,
            fan1Actual,
            fan1Maximum,
            fan1Mode);

        return new FanControllerReadResult(
            new FanControlStatus(
                IsAvailable: true,
                DisplayText: displayText),
            capability);
    }

    internal static FanControlStatus CreateUnavailableStatus(
        FanControlCapabilityResult capability)
    {
        ArgumentNullException.ThrowIfNull(capability);

        var reason = capability.Failures.Count == 0
            ? "Fan read capability is not available."
            : string.Join(" ", capability.Failures);

        return new FanControlStatus(
            IsAvailable: false,
            DisplayText: $"Fan Control: read-only unavailable. {reason}");
    }

    private static string FormatMode(byte mode)
    {
        return mode switch
        {
            0 => "Apple Auto",
            1 => "Manual",
            _ => $"Unknown mode {mode}"
        };
    }
}
