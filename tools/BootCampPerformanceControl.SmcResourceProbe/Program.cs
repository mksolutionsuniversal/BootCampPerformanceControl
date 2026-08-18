using System.Runtime.InteropServices;
using BootCampPerformanceControl.HardwareDetection;
using Microsoft.Win32;

const string appleSmcRegistryPath = @"SYSTEM\CurrentControlSet\Enum\ACPI\APP0001";
const string appleSmcInstancePrefix = @"ACPI\APP0001\";
const string classRegistryPath = @"SYSTEM\CurrentControlSet\Control\Class";

const uint CrSuccess = 0;
const uint CmLocateDevNodeNormal = 0;
const uint ResTypeAll = 0;
const uint ResTypeMemory = 1;
const uint ResTypeIo = 2;
const uint ResTypeIrq = 4;
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
    PrintRegistryValue(instanceKey, "UpperFilters");
    PrintRegistryValue(instanceKey, "LowerFilters");

    DumpRegistrySubKey(instanceKey, "Control", "DEVICE INSTANCE Control");
    DumpRegistryLogConf(instanceKey);
    DumpDriverInstallRegistry(instanceKey);

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

    var statusResult = NativeMethods.CM_Get_DevNode_Status(
        out var devNodeStatus,
        out var problemNumber,
        devInst,
        0);

    Console.WriteLine(
        $"CM_Get_DevNode_Status: CONFIGRET=0x{statusResult:X8}; "
        + $"Status=0x{devNodeStatus:X8}; Problem=0x{problemNumber:X8} ({problemNumber})");

    if (statusResult != CrSuccess)
    {
        exitCode = 7;
    }

    DumpLogicalConfiguration("ALLOCATED", devInst, AllocLogConf);
    DumpLogicalConfiguration("BOOT", devInst, BootLogConf);
}

Console.WriteLine();
Console.WriteLine("RESOURCE PROBE COMPLETE");
Console.WriteLine("No device handle was opened and no MMIO or I/O-port register was accessed.");
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

            switch (resourceId)
            {
                case ResTypeMemory:
                    PrintMemoryDescriptor(data);
                    break;
                case ResTypeIo:
                    PrintIoPortDescriptor(data);
                    break;
                case ResTypeIrq:
                    PrintIrqDescriptor(data);
                    break;
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

static void PrintMemoryDescriptor(ReadOnlySpan<byte> data)
{
    if (!WindowsMemoryResourceDescriptor.TryParse(data, out var descriptor) ||
        descriptor is null)
    {
        Console.WriteLine("    MEMORY: descriptor was shorter than the documented MEM_DES layout.");
        return;
    }

    Console.WriteLine(
        $"    MEMORY: Base=0x{descriptor.AllocatedBase:X16}; "
        + $"End=0x{descriptor.AllocatedEnd:X16}; "
        + $"Length=0x{descriptor.Length:X} ({descriptor.Length} bytes); "
        + $"Flags=0x{descriptor.Flags:X8}; Type=0x{descriptor.Type:X8}; Count={descriptor.Count}");
}

static void PrintIoPortDescriptor(ReadOnlySpan<byte> data)
{
    if (!WindowsIoPortResourceDescriptor.TryParse(data, out var descriptor) ||
        descriptor is null)
    {
        Console.WriteLine("    IO: descriptor was shorter than the documented IO_DES layout.");
        return;
    }

    Console.WriteLine(
        $"    IO: Base=0x{descriptor.AllocatedBase:X}; "
        + $"End=0x{descriptor.AllocatedEnd:X}; "
        + $"Length=0x{descriptor.Length:X} ({descriptor.Length} ports); "
        + $"Flags=0x{descriptor.Flags:X8}; Type=0x{descriptor.Type:X8}; Count={descriptor.Count}");
}

static void PrintIrqDescriptor(ReadOnlySpan<byte> data)
{
    if (!WindowsIrqResourceDescriptor.TryParse(data, out var descriptor) ||
        descriptor is null)
    {
        Console.WriteLine("    IRQ: descriptor was shorter than the documented x64 IRQ_DES layout.");
        return;
    }

    Console.WriteLine(
        $"    IRQ: Number={descriptor.AllocatedNumber}; Group={descriptor.Group}; "
        + $"Affinity=0x{descriptor.Affinity:X16}; Flags=0x{descriptor.Flags:X4}; "
        + $"Type=0x{descriptor.Type:X8}; Count={descriptor.Count}");
}

static void DumpDriverInstallRegistry(RegistryKey instanceKey)
{
    var driverReference = instanceKey.GetValue(
        "Driver",
        null,
        RegistryValueOptions.DoNotExpandEnvironmentNames) as string;

    Console.WriteLine();
    Console.WriteLine("--- DRIVER INSTALL REGISTRY ---");

    if (string.IsNullOrWhiteSpace(driverReference))
    {
        Console.WriteLine("Driver reference: not present.");
        return;
    }

    Console.WriteLine($"Driver reference: {driverReference}");

    using var driverKey = Registry.LocalMachine.OpenSubKey(
        $@"{classRegistryPath}\{driverReference}",
        writable: false);

    if (driverKey is null)
    {
        Console.WriteLine("Driver install key: not present or not readable.");
        return;
    }

    foreach (var name in new[]
    {
        "InfPath",
        "InfSection",
        "InfSectionExt",
        "ProviderName",
        "DriverVersion",
        "DriverDateData",
        "MatchingDeviceId",
        "DriverDesc",
        "UpperFilters",
        "LowerFilters"
    })
    {
        PrintRegistryValue(driverKey, name);
    }

    var separatorIndex = driverReference.IndexOf('\\');
    if (separatorIndex <= 0)
    {
        return;
    }

    var classGuid = driverReference[..separatorIndex];
    using var classKey = Registry.LocalMachine.OpenSubKey(
        $@"{classRegistryPath}\{classGuid}",
        writable: false);

    if (classKey is null)
    {
        return;
    }

    Console.WriteLine();
    Console.WriteLine("--- DEVICE CLASS FILTERS ---");
    PrintRegistryValue(classKey, "Class");
    PrintRegistryValue(classKey, "ClassDesc");
    PrintRegistryValue(classKey, "UpperFilters");
    PrintRegistryValue(classKey, "LowerFilters");
}

static void DumpRegistryLogConf(RegistryKey instanceKey)
{
    DumpRegistrySubKey(instanceKey, "LogConf", "REGISTRY LogConf");
}

static void DumpRegistrySubKey(
    RegistryKey parentKey,
    string subKeyName,
    string label)
{
    using var subKey = parentKey.OpenSubKey(subKeyName, writable: false);

    Console.WriteLine();
    Console.WriteLine($"--- {label} ---");

    if (subKey is null)
    {
        Console.WriteLine($"{subKeyName} subkey: not present or not readable.");
        return;
    }

    var valueNames = subKey.GetValueNames();
    if (valueNames.Length == 0)
    {
        Console.WriteLine($"{subKeyName} contains no values.");
        return;
    }

    foreach (var valueName in valueNames.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
    {
        object? value;
        RegistryValueKind kind;

        try
        {
            value = subKey.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            kind = subKey.GetValueKind(valueName);
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
    internal static extern uint CM_Get_DevNode_Status(
        out uint pulStatus,
        out uint pulProblemNumber,
        uint dnDevInst,
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
