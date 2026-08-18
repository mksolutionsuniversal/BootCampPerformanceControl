using System.ComponentModel;
using System.Globalization;
using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc;
using BootCampPerformanceControl.FanControl.Smc.CrystalIdea;
using BootCampPerformanceControl.HardwareDetection;

const string expectedModel = VerifiedHardwareModels.MacBookPro16_1;

Console.WriteLine("BootCamp Performance Control - SMC Read Smoke Test");
Console.WriteLine("READ ONLY: this tool does not issue SMC write requests.");
Console.WriteLine();

try
{
    var hardwareDetection = new HardwareDetectionService(new ModelSupportRegistry());
    var hardware = await hardwareDetection.DetectAsync(CancellationToken.None);
    var model = hardware.ComputerSystem.Model;

    Console.WriteLine($"Model: {model}");

    if (!string.Equals(model, expectedModel, StringComparison.Ordinal))
    {
        Console.Error.WriteLine(
            $"Refusing this integration test because the verified research target is '{expectedModel}'.");
        return 2;
    }

    await using var transport = CrystalIdeaAppleSmcTransport.OpenInstalledDriver();
    var controller = new FanController(
        new FanCapabilityProbe(
            new AppleSmcProtocol(transport),
            new FanSafetyPolicy()));

    var controllerResult = await controller.ReadStatusAsync(model, CancellationToken.None);
    var capability = controllerResult.Capability;

    if (capability.Protocol.HasValue)
    {
        Console.WriteLine($"Protocol: {capability.Protocol.Value} ({(int)capability.Protocol.Value})");
    }

    if (!capability.IsReadSupported ||
        !capability.IsHardwareSafetyGateSatisfied ||
        capability.Snapshot is null)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine("FAN SAFETY GATE: BLOCKED");
        foreach (var failure in capability.Failures)
        {
            Console.Error.WriteLine($"- {failure}");
        }

        Console.Error.WriteLine(controllerResult.Status.DisplayText);
        return 3;
    }

    Console.WriteLine();
    Console.WriteLine("Key   Len Type Attr Raw         Decoded");
    Console.WriteLine("----  --- ---- ---- ----------- ----------------");

    foreach (var value in capability.Snapshot.Values)
    {
        Console.WriteLine(FormatValue(value));
    }

    Console.WriteLine();
    Console.WriteLine("Fan capability:");
    Console.WriteLine("READ SUPPORT:         SUPPORTED");
    Console.WriteLine("HARDWARE SAFETY GATE: VERIFIED");
    Console.WriteLine("WRITE IMPLEMENTATION: NOT ENABLED");
    Console.WriteLine();
    Console.WriteLine(controllerResult.Status.DisplayText);
    Console.WriteLine();
    Console.WriteLine("READ-ONLY SMC ROUND-TRIP: PASS");
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
    Console.Error.WriteLine($"READ-ONLY SMC ROUND-TRIP: FAIL - {exception.Message}");
    return 1;
}

static string FormatValue(SmcValue value)
{
    var raw = Convert.ToHexString(value.RawData.Span);
    var decoded = value.Info.Type switch
    {
        "ui8 " when value.Info.Length == 1 =>
            value.GetUInt8().ToString(CultureInfo.InvariantCulture),
        "flt " when value.Info.Length == 4 =>
            value.GetFloat32().ToString("0.###", CultureInfo.InvariantCulture),
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
