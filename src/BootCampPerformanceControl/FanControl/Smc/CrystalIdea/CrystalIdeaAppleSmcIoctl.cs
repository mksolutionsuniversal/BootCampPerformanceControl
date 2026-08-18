namespace BootCampPerformanceControl.FanControl.Smc.CrystalIdea;

internal static class CrystalIdeaAppleSmcIoctl
{
    internal const uint ReadKey = 0x220000;
    internal const uint WriteKey = 0x220004;
    internal const uint GetKeyInfo = 0x22000C;
    internal const uint GetProtocol = 0x220020;
}
