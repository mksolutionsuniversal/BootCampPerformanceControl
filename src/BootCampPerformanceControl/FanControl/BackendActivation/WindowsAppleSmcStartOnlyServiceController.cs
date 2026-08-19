using System.ComponentModel;
using System.Runtime.InteropServices;
using BootCampPerformanceControl.FanControl.Smc.Windows;
using Microsoft.Win32.SafeHandles;

namespace BootCampPerformanceControl.FanControl.BackendActivation;

internal sealed class WindowsAppleSmcStartOnlyServiceController
    : IAppleSmcStartOnlyServiceController
{
    internal const string ServiceName = "AppleSMC";

    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStart = 0x0010;
    private const int ScStatusProcessInfo = 0;

    private readonly SafeServiceHandle _serviceManager;
    private readonly SafeServiceHandle _service;

    public WindowsAppleSmcStartOnlyServiceController()
    {
        _serviceManager = OpenSCManagerW(
            null,
            null,
            ScManagerConnect);

        if (_serviceManager.IsInvalid)
        {
            var errorCode = Marshal.GetLastWin32Error();
            _serviceManager.Dispose();
            throw CreateWin32Exception(errorCode, "OpenSCManagerW");
        }

        try
        {
            _service = OpenService(ServiceQueryStatus | ServiceStart);
        }
        catch
        {
            _serviceManager.Dispose();
            throw;
        }
    }

    public AppleSmcServiceState GetState()
    {
        var success = QueryServiceStatusEx(
            _service,
            ScStatusProcessInfo,
            out var status,
            Marshal.SizeOf<ServiceStatusProcess>(),
            out _);

        if (!success)
        {
            var errorCode = Marshal.GetLastWin32Error();
            throw CreateWin32Exception(errorCode, "QueryServiceStatusEx");
        }

        return (AppleSmcServiceState)status.CurrentState;
    }

    public void Start()
    {
        if (StartServiceW(_service, 0, IntPtr.Zero))
        {
            return;
        }

        var errorCode = Marshal.GetLastWin32Error();
        throw CreateWin32Exception(errorCode, "StartServiceW");
    }

    public void Dispose()
    {
        _service.Dispose();
        _serviceManager.Dispose();
    }

    private SafeServiceHandle OpenService(uint desiredAccess)
    {
        var service = OpenServiceW(
            _serviceManager,
            ServiceName,
            desiredAccess);

        if (service.IsInvalid)
        {
            var errorCode = Marshal.GetLastWin32Error();
            service.Dispose();
            throw CreateWin32Exception(
                errorCode,
                "OpenServiceW(SERVICE_QUERY_STATUS | SERVICE_START)");
        }

        return service;
    }

    private static Win32Exception CreateWin32Exception(
        int errorCode,
        string operation)
    {
        return new Win32Exception(
            errorCode,
            $"{operation} failed for Windows service '{ServiceName}' with Win32 error {errorCode}.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    private sealed class SafeServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeServiceHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle()
        {
            return CloseServiceHandle(handle);
        }
    }

    [DllImport(
        "advapi32.dll",
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        SetLastError = true)]
    private static extern SafeServiceHandle OpenSCManagerW(
        string? machineName,
        string? databaseName,
        uint desiredAccess);

    [DllImport(
        "advapi32.dll",
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        SetLastError = true)]
    private static extern SafeServiceHandle OpenServiceW(
        SafeServiceHandle serviceManager,
        string serviceName,
        uint desiredAccess);

    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(
        SafeServiceHandle service,
        int infoLevel,
        out ServiceStatusProcess serviceStatus,
        int bufferSize,
        out int bytesNeeded);

    [DllImport(
        "advapi32.dll",
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartServiceW(
        SafeServiceHandle service,
        int argumentCount,
        IntPtr arguments);

    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr serviceHandle);
}
