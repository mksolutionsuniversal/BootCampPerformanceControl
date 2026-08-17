using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BootCampPerformanceControl.PowerManagement;

internal sealed class NativePowerProfileApi : IPowerProfileApi
{
    public Guid GetActiveScheme()
    {
        var result = PowerGetActiveSchemeNative(IntPtr.Zero, out var schemePointer);

        if (result != 0)
        {
            throw CreateWin32Exception(result, "PowerGetActiveScheme");
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

    public uint ReadAcValueIndex(Guid schemeGuid, Guid subgroupGuid, Guid settingGuid)
    {
        var scheme = schemeGuid;
        var subgroup = subgroupGuid;
        var setting = settingGuid;
        var result = PowerReadAcValueIndexNative(
            IntPtr.Zero,
            ref scheme,
            ref subgroup,
            ref setting,
            out var value);

        if (result != 0)
        {
            throw CreateWin32Exception(result, "PowerReadACValueIndex");
        }

        return value;
    }

    public uint ReadDcValueIndex(Guid schemeGuid, Guid subgroupGuid, Guid settingGuid)
    {
        var scheme = schemeGuid;
        var subgroup = subgroupGuid;
        var setting = settingGuid;
        var result = PowerReadDcValueIndexNative(
            IntPtr.Zero,
            ref scheme,
            ref subgroup,
            ref setting,
            out var value);

        if (result != 0)
        {
            throw CreateWin32Exception(result, "PowerReadDCValueIndex");
        }

        return value;
    }

    public void WriteAcValueIndex(
        Guid schemeGuid,
        Guid subgroupGuid,
        Guid settingGuid,
        uint value)
    {
        var scheme = schemeGuid;
        var subgroup = subgroupGuid;
        var setting = settingGuid;
        var result = PowerWriteAcValueIndexNative(
            IntPtr.Zero,
            ref scheme,
            ref subgroup,
            ref setting,
            value);

        if (result != 0)
        {
            throw CreateWin32Exception(result, "PowerWriteACValueIndex");
        }
    }

    public void WriteDcValueIndex(
        Guid schemeGuid,
        Guid subgroupGuid,
        Guid settingGuid,
        uint value)
    {
        var scheme = schemeGuid;
        var subgroup = subgroupGuid;
        var setting = settingGuid;
        var result = PowerWriteDcValueIndexNative(
            IntPtr.Zero,
            ref scheme,
            ref subgroup,
            ref setting,
            value);

        if (result != 0)
        {
            throw CreateWin32Exception(result, "PowerWriteDCValueIndex");
        }
    }

    public void SetActiveScheme(Guid schemeGuid)
    {
        var scheme = schemeGuid;
        var result = PowerSetActiveSchemeNative(IntPtr.Zero, ref scheme);

        if (result != 0)
        {
            throw CreateWin32Exception(result, "PowerSetActiveScheme");
        }
    }

    private static Win32Exception CreateWin32Exception(uint errorCode, string operation)
    {
        return new Win32Exception(
            unchecked((int)errorCode),
            $"{operation} failed with Win32 error {errorCode}.");
    }

    [DllImport("PowrProf.dll", EntryPoint = "PowerGetActiveScheme", ExactSpelling = true)]
    private static extern uint PowerGetActiveSchemeNative(
        IntPtr userRootPowerKey,
        out IntPtr activePolicyGuid);

    [DllImport("PowrProf.dll", EntryPoint = "PowerReadACValueIndex", ExactSpelling = true)]
    private static extern uint PowerReadAcValueIndexNative(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        ref Guid subgroupOfPowerSettingsGuid,
        ref Guid powerSettingGuid,
        out uint acValueIndex);

    [DllImport("PowrProf.dll", EntryPoint = "PowerReadDCValueIndex", ExactSpelling = true)]
    private static extern uint PowerReadDcValueIndexNative(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        ref Guid subgroupOfPowerSettingsGuid,
        ref Guid powerSettingGuid,
        out uint dcValueIndex);

    [DllImport("PowrProf.dll", EntryPoint = "PowerWriteACValueIndex", ExactSpelling = true)]
    private static extern uint PowerWriteAcValueIndexNative(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        ref Guid subgroupOfPowerSettingsGuid,
        ref Guid powerSettingGuid,
        uint acValueIndex);

    [DllImport("PowrProf.dll", EntryPoint = "PowerWriteDCValueIndex", ExactSpelling = true)]
    private static extern uint PowerWriteDcValueIndexNative(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        ref Guid subgroupOfPowerSettingsGuid,
        ref Guid powerSettingGuid,
        uint dcValueIndex);

    [DllImport("PowrProf.dll", EntryPoint = "PowerSetActiveScheme", ExactSpelling = true)]
    private static extern uint PowerSetActiveSchemeNative(
        IntPtr userRootPowerKey,
        ref Guid schemeGuid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
