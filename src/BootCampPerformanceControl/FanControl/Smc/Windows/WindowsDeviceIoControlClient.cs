using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BootCampPerformanceControl.FanControl.Smc.Windows;

internal sealed class WindowsDeviceIoControlClient : IDeviceIoControlClient
{
    private const int ErrorSharingViolation = 32;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;

    private readonly SafeFileHandle _handle;

    public WindowsDeviceIoControlClient(string devicePath)
        : this(devicePath, exclusive: false)
    {
    }

    private WindowsDeviceIoControlClient(string devicePath, bool exclusive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);

        var shareMode = exclusive
            ? 0
            : FileShareRead | FileShareWrite;

        _handle = CreateFileW(
            devicePath,
            GenericRead | GenericWrite,
            shareMode,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal,
            IntPtr.Zero);

        if (_handle.IsInvalid)
        {
            var errorCode = Marshal.GetLastWin32Error();
            _handle.Dispose();
            throw CreateOpenException(devicePath, errorCode, exclusive);
        }
    }

    internal static WindowsDeviceIoControlClient OpenExclusive(string devicePath)
    {
        return new WindowsDeviceIoControlClient(devicePath, exclusive: true);
    }

    internal static Win32Exception CreateOpenException(
        string devicePath,
        int errorCode,
        bool exclusive)
    {
        if (exclusive && errorCode == ErrorSharingViolation)
        {
            return new Win32Exception(
                errorCode,
                $"CreateFileW could not open AppleSMC device '{devicePath}' exclusively "
                + "because it is already in use by another application, such as another "
                + $"fan-control application (Win32 error {errorCode}: ERROR_SHARING_VIOLATION).");
        }

        return new Win32Exception(
            errorCode,
            $"CreateFileW failed for device '{devicePath}' with Win32 error {errorCode}.");
    }

    public byte[] Invoke(
        uint controlCode,
        ReadOnlyMemory<byte> input,
        int outputBufferLength)
    {
        if (outputBufferLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputBufferLength));
        }

        var inputBuffer = input.IsEmpty ? null : input.ToArray();
        var outputBuffer = new byte[outputBufferLength];

        var success = DeviceIoControl(
            _handle,
            controlCode,
            inputBuffer,
            inputBuffer?.Length ?? 0,
            outputBuffer,
            outputBuffer.Length,
            out var bytesReturned,
            IntPtr.Zero);

        if (!success)
        {
            var errorCode = Marshal.GetLastWin32Error();
            throw new Win32Exception(
                errorCode,
                $"DeviceIoControl 0x{controlCode:X8} failed with Win32 error {errorCode}.");
        }

        if (bytesReturned < 0 || bytesReturned > outputBuffer.Length)
        {
            throw new InvalidOperationException(
                $"DeviceIoControl 0x{controlCode:X8} returned invalid byte count {bytesReturned}.");
        }

        return bytesReturned == outputBuffer.Length
            ? outputBuffer
            : outputBuffer.AsSpan(0, bytesReturned).ToArray();
    }

    public void Dispose()
    {
        _handle.Dispose();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint ioControlCode,
        byte[]? inputBuffer,
        int inputBufferSize,
        byte[] outputBuffer,
        int outputBufferSize,
        out int bytesReturned,
        IntPtr overlapped);
}
