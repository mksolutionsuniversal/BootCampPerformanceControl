using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BootCampPerformanceControl.PowerManagement;

internal static class NativePowerProfileApi
{
    public static Guid GetActiveScheme()
    {
        var result = PowerGetActiveScheme(IntPtr.Zero, out var schemePointer);

        if (result != 0)
        {
            throw CreateWin32Exception(result, nameof(PowerGetActiveScheme));
        }

        if (schemePointer == IntPtr.Zero)
        {
            throw new InvalidOperationException("PowerGetActiveScheme returned a null scheme pointer.");
        }

        try
        {
            return Marshal.PtrToStructure<Guid>(schemePointer);
        }
        finally
        {
            var freeResult = LocalFree(schemePointer);
            if (freeResult != IntPtr.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "LocalFree failed while releasing the active power scheme pointer.");
            }
        }
    }

    public static uint ReadAcValueIndex(Guid schemeGuid, Guid subgroupGuid, Guid settingGuid)
    {
        var scheme = schemeGuid;
        var subgroup = subgroupGuid;
        var setting = settingGuid;
        var result = PowerReadACValueIndex(
            IntPtr.Zero,
            ref scheme,
            ref subgroup,
            ref setting,
            out var value);

        if (result != 0)
        {
            throw CreateWin32Exception(result, nameof(PowerReadACValueIndex));
        }

        return value;
    }

    public static uint ReadDcValueIndex(Guid schemeGuid, Guid subgroupGuid, Guid settingGuid)
    {
        var scheme = schemeGuid;
        var subgroup = subgroupGuid;
        var setting = settingGuid;
        var result = PowerReadDCValueIndex(
            IntPtr.Zero,
            ref scheme,
            ref subgroup,
            ref setting,
            out var value);

        if (result != 0)
        {
            throw CreateWin32Exception(result, nameof(PowerReadDCValueIndex));
        }

        return value;
    }

    private static Win32Exception CreateWin32Exception(uint errorCode, string operation)
    {
        return new Win32Exception(
            unchecked((int)errorCode),
            $"{operation} failed with Win32 error {errorCode}.");
    }

    [DllImport("PowrProf.dll", SetLastError = true)]
    private static extern uint PowerGetActiveScheme(
        IntPtr userRootPowerKey,
        out IntPtr activePolicyGuid);

    [DllImport("PowrProf.dll", SetLastError = true)]
    private static extern uint PowerReadACValueIndex(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        ref Guid subgroupOfPowerSettingsGuid,
        ref Guid powerSettingGuid,
        out uint acValueIndex);

    [DllImport("PowrProf.dll", SetLastError = true)]
    private static extern uint PowerReadDCValueIndex(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        ref Guid subgroupOfPowerSettingsGuid,
        ref Guid powerSettingGuid,
        out uint dcValueIndex);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
