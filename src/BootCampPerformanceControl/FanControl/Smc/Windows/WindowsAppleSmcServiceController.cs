using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BootCampPerformanceControl.FanControl.Smc.Windows;

internal sealed class WindowsAppleSmcServiceController : IAppleSmcServiceController
{
    internal const string ServiceName = "AppleSMC";

    private const int ErrorServiceCannotAcceptControl = 1061;
    private const int ErrorServiceNotActive = 1062;
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStart = 0x0010;
    private const uint ServiceStop = 0x0020;
    private const uint ServiceControlStop = 0x00000001;
    private const int ScStatusProcessInfo = 0;

    private readonly SafeServiceHandle _serviceManager;
    private readonly SafeServiceHandle _queryService;
    private readonly object _controlServiceSync = new();

    private SafeServiceHandle? _controlService;

    public WindowsAppleSmcServiceController()
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
            _queryService = OpenService(
                ServiceQueryStatus,
                "OpenServiceW(SERVICE_QUERY_STATUS)");
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
            _queryService,
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
        var controlService = GetOrOpenControlService();

        if (!StartServiceW(controlService, 0, IntPtr.Zero))
        {
            var errorCode = Marshal.GetLastWin32Error();
            throw CreateWin32Exception(errorCode, "StartServiceW");
        }
    }

    public void Stop()
    {
        SafeServiceHandle controlService;

        lock (_controlServiceSync)
        {
            controlService = _controlService
                ?? throw new InvalidOperationException(
                    $"Windows service '{ServiceName}' cannot be stopped because "
                    + "this controller did not start it.");
        }

        if (ControlService(controlService, ServiceControlStop, out _))
        {
            return;
        }

        var errorCode = Marshal.GetLastWin32Error();

        if (CanTreatStopFailureAsSuccess(errorCode, observedState: null))
        {
            return;
        }

        if (errorCode != ErrorServiceCannotAcceptControl)
        {
            throw CreateWin32Exception(
                errorCode,
                "ControlService(SERVICE_CONTROL_STOP)");
        }

        AppleSmcServiceState observedState;

        try
        {
            observedState = GetState();
        }
        catch (Exception statusException)
        {
            throw new Win32Exception(
                errorCode,
                $"ControlService(SERVICE_CONTROL_STOP) failed for Windows service "
                + $"'{ServiceName}' with Win32 error {errorCode}, and its state could not "
                + $"be verified: {statusException.Message}");
        }

        if (CanTreatStopFailureAsSuccess(errorCode, observedState))
        {
            return;
        }

        throw new Win32Exception(
            errorCode,
            $"ControlService(SERVICE_CONTROL_STOP) failed for Windows service "
            + $"'{ServiceName}' with Win32 error {errorCode}. Observed service state: "
            + $"'{observedState}' ({(uint)observedState}).");
    }

    internal static bool CanTreatStopFailureAsSuccess(
        int errorCode,
        AppleSmcServiceState? observedState)
    {
        return errorCode == ErrorServiceNotActive
            || errorCode == ErrorServiceCannotAcceptControl
                && observedState is AppleSmcServiceState.Stopped or
                    AppleSmcServiceState.StopPending;
    }

    public void Dispose()
    {
        lock (_controlServiceSync)
        {
            _controlService?.Dispose();
            _controlService = null;
        }

        _queryService.Dispose();
        _serviceManager.Dispose();
    }

    private SafeServiceHandle GetOrOpenControlService()
    {
        lock (_controlServiceSync)
        {
            if (_controlService is not null)
            {
                return _controlService;
            }

            var controlService = OpenService(
                ServiceStart | ServiceStop,
                "OpenServiceW(SERVICE_START | SERVICE_STOP)");

            _controlService = controlService;
            return controlService;
        }
    }

    private SafeServiceHandle OpenService(uint desiredAccess, string operation)
    {
        var service = OpenServiceW(
            _serviceManager,
            ServiceName,
            desiredAccess);

        if (service.IsInvalid)
        {
            var errorCode = Marshal.GetLastWin32Error();
            service.Dispose();
            throw CreateWin32Exception(errorCode, operation);
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
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
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
    private static extern bool ControlService(
        SafeServiceHandle service,
        uint control,
        out ServiceStatus serviceStatus);

    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr serviceHandle);
}
