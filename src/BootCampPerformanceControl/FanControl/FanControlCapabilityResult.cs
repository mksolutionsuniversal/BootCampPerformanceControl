using BootCampPerformanceControl.FanControl.Smc;

namespace BootCampPerformanceControl.FanControl;

internal sealed record FanControlCapabilityResult(
    bool IsReadSupported,
    bool IsHardwareVerifiedForFutureWrite,
    IReadOnlyList<string> Failures,
    FanSmcSnapshot? Snapshot)
{
    public static FanControlCapabilityResult Rejected(params string[] failures)
    {
        return new FanControlCapabilityResult(
            IsReadSupported: false,
            IsHardwareVerifiedForFutureWrite: false,
            failures,
            Snapshot: null);
    }
}
