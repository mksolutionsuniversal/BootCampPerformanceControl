using System.Runtime.InteropServices;
using BootCampPerformanceControl.HardwareDetection;
using Microsoft.Win32;

const string appleSmcRegistryPath = @"SYSTEM\CurrentControlSet\Enum\ACPI\APP0001";
const string appleSmcInstancePrefix = @"ACPI\APP0001\";

const uint CrSuccess = 0;
const uint CmLocateDevNodeNormal = 0;
const uint ResTypeAll = 0;
const uint ResTypeMemory = 1;
const uint AllocLogConf = 2;
const uint BootLogConf = 3;

Console.WriteLine("BootCamp Performance Control - Apple SMC Resource Probe");
Console.WriteLine("READ ONLY: this tool does not open \\.\\APPLESMC and does not map or access MMIO registers.");
Console.WriteLine();
Console.WriteLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}");
Console.WriteLine($"64-bit process: {Environment.Is64BitProcess}");

if (!Environment.Is64BitProcess || RuntimeInformation.ProcessArchitecture != Architecture.X64)
{
    Console.Error.WriteLine("REFUSED: Configuration Manager hardware resource probing must run as native x64 on this target.");
    return 2;
}

using var root = Registry.LocalMachine.OpenSubKey(appleSmcRegistryPath, writable: false);
if (root is null)
{
    Console.Error.WriteLine($"ACPI APP0001 registry node was not found: HKLM\\{appleSmcRegistryPath}");
    return 3;
}

var instanceNames = root.GetSubKeyNames();
if (instanceNames.Length == 0)
{
    Console.Error.WriteLine("ACPI APP0001 exists but contains no device instances.");
    return 4;
}

Console.WriteLine($"Found {instanceNames.Length} ACPI APP0001 instance(s).");

var exitCode = 0;

foreach (var instanceName in instanceNames)
{
    var instanceId = appleSmcInstancePrefix + instanceName;
    Console.WriteLine();
    Console.WriteLine(new string('=', 78));
    Console.WriteLine($"DEVICE INSTANCE: {instanceId}");
    Console.WriteLine(new string('=', 78));

    using var instanceKey = root.OpenSubKey(instanceName, writable: false);
    if (instanceKey is null)
    {
        Console.Error.WriteLine("Unable to open the device instance registry key.");
        exitCode = 5;
        continue;
    }

    PrintRegistryValue(instanceKey, "DeviceDesc");
    PrintRegistryValue(instanceKey, "HardwareID");
    PrintRegistryValue(instanceKey, "CompatibleIDs");
    PrintRegistryValue(instanceKey, "Service");
    PrintRegistryValue(instanceKey, "Driver");
    PrintRegistryValue(instanceKey, "Class");
    PrintRegistryValue(instanceKey, "ClassGUID");
    PrintRegistryValue(instanceKey, "Mfg");
    PrintRegistryValue(instanceKey, "ConfigFlags");
    PrintRegistryValue(instanceKey, "Capabilities");

    DumpRegistryLogConf(instanceKey);

    uint devInst = 0;
    var locateResult = NativeMethods.CM_Locate_DevNodeW(
        ref devInst,
        instanceId,
        CmLocateDevNodeNormal);

    Console.WriteLine();
    Console.WriteLine($"CM_Locate_DevNodeW: CONFIGRET=0x{locateResult:X8}; DEVINST=0x{devInst:X8}");

    if (locateResult != CrSuccess)
    {
        exitCode = 6;
        continue;
    }

    DumpLogicalConfiguration("ALLOCATED", devInst, AllocLogConf);
    DumpLogicalConfiguration("BOOT", devInst, BootLogConf);
}

Console.WriteLine();
Console.WriteLine("RESOURCE PROBE COMPLETE");
Console.WriteLine("No device handle was opened and no MMIO register was mapped or accessed.");
return exitCode;

static void DumpLogicalConfiguration(
    string label,
    uint devInst,
    uint configurationType)
{
    Console.WriteLine();
    Console.WriteLine($"--- {label} LOGICAL CONFIGURATION ---");

    var result = NativeMethods.CM_Get_First_Log_Conf(
        out var logConf,
        devInst,
        configurationType);

    Console.WriteLine($"CM_Get_First_Log_Conf: CONFIGRET=0x{result:X8}");
    if (result != CrSuccess)
    {
        return;
    }

    var resourceHandles = new List<nint>();

    try
    {
        nint previous = logConf;
        var index = 0;

        while (true)
        {
            result = NativeMethods.CM_Get_Next_Res_Des(
                out var resourceHandle,
                previous,
                ResTypeAll,
                out var resourceId,
                0);

            if (result != CrSuccess)
            {
                Console.WriteLine($"Resource enumeration ended with CONFIGRET=0x{result:X8}.");
                break;
            }

            resourceHandles.Add(resourceHandle);
            previous = resourceHandle;

            var sizeResult = NativeMethods.CM_Get_Res_Des_Data_Size(
                out var size,
                resourceHandle,
                0);

            if (sizeResult != CrSuccess)
            {
                Console.WriteLine(
                    $"[{index}] ResourceId=0x{resourceId:X8}; data-size query failed: CONFIGRET=0x{sizeResult:X8}");
                index++;
                continue;
            }

            var data = new byte[checked((int)size)];
            var dataResult = NativeMethods.CM_Get_Res_Des_Data(
                resourceHandle,
                data,
                size,
                0);

            if (dataResult != CrSuccess)
            {
                Console.WriteLine(
                    $"[{index}] ResourceId=0x{resourceId:X8}; data read failed: CONFIGRET=0x{dataResult:X8}");
                index++;
                continue;
            }

            Console.WriteLine(
                $"[{index}] ResourceId=0x{resourceId:X8}; Size={size}; Raw={FormatHex(data)}");

            if (resourceId == ResTypeMemory)
            {
                if (WindowsMemoryResourceDescriptor.TryParse(data, out var descriptor) &&
                    descriptor is not null)
                {
                    Console.WriteLine(
                        $"    MEMORY: Base=0x{descriptor.AllocatedBase:X16}; "
                        + $"End=0x{descriptor.AllocatedEnd:X16}; "
                        + $"Length=0x{descriptor.Length:X} ({descriptor.Length} bytes); "
                        + $"Flags=0x{descriptor.Flags:X8}; Type=0x{descriptor.Type:X8}; Count={descriptor.Count}");
                }
                else
                {
                    Console.WriteLine("    MEMORY: descriptor was shorter than the documented MEM_DES layout.");
                }
            }

            index++;
        }
    }
    finally
    {
        foreach (var resourceHandle in resourceHandles)
        {
            _ = NativeMethods.CM_Free_Res_Des_Handle(resourceHandle);
        }

        _ = NativeMethods.CM_Free_Log_Conf_Handle(logConf);
    }
}

static void DumpRegistryLogConf(RegistryKey instanceKey)
{
    using var logConf = instanceKey.OpenSubKey("LogConf", writable: false);

    Console.WriteLine();
    Console.WriteLine("--- REGISTRY LogConf ---");

    if (logConf is null)
    {
        Console.WriteLine("LogConf subkey: not present or not readable.");
        return;
    }

    var valueNames = logConf.GetValueNames();
    if (valueNames.Length == 0)
    {
        Console.WriteLine("LogConf contains no values.");
        return;
    }

    foreach (var valueName in valueNames.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
    {
        object? value;
        RegistryValueKind kind;

        try
        {
            value = logConf.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            kind = logConf.GetValueKind(valueName);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"{valueName}: read failed - {exception.Message}");
            continue;
        }

        Console.WriteLine($"{valueName} [{kind}]: {FormatRegistryValue(value)}");
    }
}

static void PrintRegistryValue(RegistryKey key, string name)
{
    object? value;

    try
    {
        value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }
    catch (Exception exception)
    {
        Console.WriteLine($"{name}: read failed - {exception.Message}");
        return;
    }

    Console.WriteLine($"{name}: {FormatRegistryValue(value)}");
}

static string FormatRegistryValue(object? value)
{
    return value switch
    {
        null => "<not present>",
        string text => text,
        string[] values => string.Join(" | ", values),
        byte[] bytes => $"{bytes.Length} bytes: {FormatHex(bytes)}",
        _ => value.ToString() ?? "<null>"
    };
}

static string FormatHex(ReadOnlySpan<byte> data)
{
    const int maxBytes = 96;
    var shown = data.Length <= maxBytes ? data : data[..maxBytes];
    var hex = Convert.ToHexString(shown);
    return data.Length <= maxBytes
        ? hex
        : $"{hex}... (+{data.Length - maxBytes} bytes)";
}

internal static class NativeMethods
{
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint CM_Locate_DevNodeW(
        ref uint pdnDevInst,
        string pDeviceId,
        uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    internal static extern uint CM_Get_First_Log_Conf(
        out nint plcLogConf,
        uint dnDevInst,
        uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    internal static extern uint CM_Get_Next_Res_Des(
        out nint prdResDes,
        nint rdResDes,
        uint forResource,
        out uint pResourceId,
        uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    internal static extern uint CM_Get_Res_Des_Data_Size(
        out uint pulSize,
        nint rdResDes,
        uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    internal static extern uint CM_Get_Res_Des_Data(
        nint rdResDes,
        [Out] byte[] buffer,
        uint bufferLen,
        uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    internal static extern uint CM_Free_Res_Des_Handle(nint rdResDes);

    [DllImport("cfgmgr32.dll")]
    internal static extern uint CM_Free_Log_Conf_Handle(nint lcLogConf);
}
