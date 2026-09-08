using System.IO;

namespace BootCampPerformanceControl.FanControl.Smc;

internal sealed class SmcKeyNotFoundException : IOException
{
    public SmcKeyNotFoundException(string key)
        : base($"AppleSMC positively reported that key '{key}' does not exist.")
    {
        Key = key;
    }

    public string Key { get; }
}
