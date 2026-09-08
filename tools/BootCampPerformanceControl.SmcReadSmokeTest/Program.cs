using System.ComponentModel;
using System.Globalization;
using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc;
using BootCampPerformanceControl.FanControl.Smc.CrystalIdea;
using BootCampPerformanceControl.HardwareDetection;

Console.WriteLine("BootCamp Performance Control - Generic SMC Fan Profiler");
Console.WriteLine("READ ONLY: this tool does not issue SMC write requests.");
Console.WriteLine();

try
{
    var hardwareDetection = new HardwareDetectionService();
    var hardware = await hardwareDetection.DetectAsync(CancellationToken.None);
    var verification = hardwareDetection.VerifyModel(hardware);
    var model = verification.Model;

    Console.WriteLine($"Manufacturer: {verification.Manufacturer}");
    Console.WriteLine($"Model:        {model}");
    Console.WriteLine($"Platform:     {verification.PlatformSupport}");

    if (!verification.IsSupportedIntelMac)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine("Refusing this profiler because the detected machine is not an Apple Intel Mac.");
        return 2;
    }

    await using var transport = CrystalIdeaAppleSmcTransport.OpenInstalledDriver();
    var protocol = new AppleSmcProtocol(transport);

    var transportProtocol = await protocol
        .GetProtocolAsync(CancellationToken.None)
        .ConfigureAwait(false);

    Console.WriteLine($"AppleSMC protocol: {transportProtocol} ({(int)transportProtocol})");

    var probes = new Dictionary<string, ProbeResult>(StringComparer.Ordinal);

    var fanCountValue = await protocol
        .ReadKeyAsync("FNum", CancellationToken.None)
        .ConfigureAwait(false);
    probes.Add("FNum", ProbeResult.Success(fanCountValue));

    if (!string.Equals(fanCountValue.Info.Type, "ui8 ", StringComparison.Ordinal) ||
        fanCountValue.Info.Length != 1)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine(
            $"FNum metadata is not supported by the profiler: len={fanCountValue.Info.Length}, type='{fanCountValue.Info.Type}'.");
        return 3;
    }

    var fanCount = fanCountValue.GetUInt8();
    Console.WriteLine($"FNum:              {fanCount}");

    if (fanCount > FanIndex.MaximumRepresentableValue + 1)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine(
            $"FNum={fanCount} exceeds the single-digit SMC fan-key range supported by this profiler.");
        return 3;
    }

    for (var value = 0; value < fanCount; value++)
    {
        var fan = new FanIndex(value);
        foreach (var suffix in new[] { "Mn", "Mx", "Ac", "Tg", "Sf", "Md" })
        {
            var key = fan.GetSmcKey(suffix);
            probes[key] = await ProbeOptionalAsync(protocol, key, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    probes["FS! "] = await ProbeOptionalAsync(protocol, "FS! ", CancellationToken.None)
        .ConfigureAwait(false);

    var family = ClassifyFamily(fanCount, probes);

    Console.WriteLine();
    Console.WriteLine("Key   Len Type Attr Raw         Decoded / status");
    Console.WriteLine("----  --- ---- ---- ----------- ----------------------------------------");

    foreach (var probe in probes.Values)
    {
        Console.WriteLine(FormatProbe(probe));
    }

    Console.WriteLine();
    Console.WriteLine("SMC fan-control fingerprint:");
    Console.WriteLine($"Fan count:           {fanCount}");
    Console.WriteLine($"Candidate family:    {family}");
    Console.WriteLine("Write implementation: NOT USED BY THIS TOOL");
    Console.WriteLine("SMC writes issued:    0");
    Console.WriteLine();
    Console.WriteLine("READ-ONLY GENERIC SMC PROFILER: PASS");
    return 0;
}
catch (Win32Exception exception)
{
    Console.Error.WriteLine($"Windows device access failed: {exception.Message}");
    Console.Error.WriteLine(
        "Close Macs Fan Control and make sure the locally installed AppleSMC service is running before retrying.");
    return 4;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"READ-ONLY GENERIC SMC PROFILER: FAIL - {exception.Message}");
    return 1;
}

static async Task<ProbeResult> ProbeOptionalAsync(
    AppleSmcProtocol protocol,
    string key,
    CancellationToken cancellationToken)
{
    try
    {
        var value = await protocol.ReadKeyAsync(key, cancellationToken).ConfigureAwait(false);
        return ProbeResult.Success(value);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception exception)
    {
        return ProbeResult.Failure(key, exception);
    }
}

static FanControlFamilyCandidate ClassifyFamily(
    int fanCount,
    IReadOnlyDictionary<string, ProbeResult> probes)
{
    if (fanCount == 0)
    {
        return FanControlFamilyCandidate.Passive;
    }

    var perFanFloat32 = true;
    var globalMaskFpe2 = true;

    for (var value = 0; value < fanCount; value++)
    {
        var fan = new FanIndex(value);

        perFanFloat32 &=
            Matches(probes, fan.GetSmcKey("Mx"), "flt ", 4) &&
            Matches(probes, fan.GetSmcKey("Ac"), "flt ", 4) &&
            Matches(probes, fan.GetSmcKey("Tg"), "flt ", 4) &&
            Matches(probes, fan.GetSmcKey("Md"), "ui8 ", 1);

        globalMaskFpe2 &=
            Matches(probes, fan.GetSmcKey("Mx"), "fpe2", 2) &&
            Matches(probes, fan.GetSmcKey("Ac"), "fpe2", 2) &&
            Matches(probes, fan.GetSmcKey("Tg"), "fpe2", 2);
    }

    if (perFanFloat32)
    {
        return FanControlFamilyCandidate.PerFanModeFloat32;
    }

    if (globalMaskFpe2 &&
        probes.TryGetValue("FS! ", out var globalMode) &&
        globalMode.Value is { Info.Length: 2 })
    {
        return FanControlFamilyCandidate.GlobalMaskFpe2;
    }

    return FanControlFamilyCandidate.Unknown;
}

static bool Matches(
    IReadOnlyDictionary<string, ProbeResult> probes,
    string key,
    string type,
    byte length)
{
    return probes.TryGetValue(key, out var probe) &&
           probe.Value is not null &&
           string.Equals(probe.Value.Info.Type, type, StringComparison.Ordinal) &&
           probe.Value.Info.Length == length;
}

static string FormatProbe(ProbeResult probe)
{
    if (probe.Value is null)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0,-4}  {1,-3} {2,-4} {3,-4} {4,-11} {5}",
            probe.Key,
            "---",
            "----",
            "----",
            "-----------",
            $"unavailable: {probe.Error}");
    }

    var value = probe.Value;
    var raw = Convert.ToHexString(value.RawData.Span);
    var decoded = value.Info.Type switch
    {
        "ui8 " when value.Info.Length == 1 =>
            value.GetUInt8().ToString(CultureInfo.InvariantCulture),
        "flt " when value.Info.Length == 4 =>
            $"{value.GetFloat32().ToString("0.###", CultureInfo.InvariantCulture)} RPM/value",
        "fpe2" when value.Info.Length == 2 =>
            "fpe2 candidate (raw preserved; no byte-order assumption in this probe)",
        _ => "raw-only"
    };

    return string.Format(
        CultureInfo.InvariantCulture,
        "{0,-4}  {1,3} {2,-4} 0x{3:X2} {4,-11} {5}",
        value.Info.Key,
        value.Info.Length,
        value.Info.Type,
        value.Info.Attributes,
        raw,
        decoded);
}

internal enum FanControlFamilyCandidate
{
    Unknown = 0,
    Passive = 1,
    PerFanModeFloat32 = 2,
    GlobalMaskFpe2 = 3
}

internal sealed record ProbeResult(
    string Key,
    SmcValue? Value,
    string? Error)
{
    public static ProbeResult Success(SmcValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ProbeResult(value.Info.Key, value, null);
    }

    public static ProbeResult Failure(string key, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(exception);
        return new ProbeResult(key, null, $"{exception.GetType().Name}: {exception.Message}");
    }
}
