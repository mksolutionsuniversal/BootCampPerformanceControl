using BootCampPerformanceControl.FanControl.Smc;

namespace BootCampPerformanceControl.FanControl;

internal sealed record FanControlCapabilityResult(
    bool IsReadSupported,
    bool IsHardwareVerifiedForFutureWrite,
    IReadOnlyList<string> Failures,
    SmcTransportProtocol? Protocol,
    FanSmcSnapshot? Snapshot)
{
    public static FanControlCapabilityResult Rejected(
        SmcTransportProtocol? protocol,
        params string[] failures)
    {
        return new FanControlCapabilityResult(
            IsReadSupported: false,
            IsHardwareVerifiedForFutureWrite: false,
            failures,
            protocol,
            Snapshot: null);
    }
}
