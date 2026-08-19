using BootCampPerformanceControl.FanControl.Smc.CrystalIdea;
using BootCampPerformanceControl.FanControl.Smc.Windows;

namespace BootCampPerformanceControl.Tests.FanControl.Smc;

public sealed class WindowsDeviceIoControlClientTests
{
    [Fact]
    public void CreateOpenException_SharedOpenPreservesGenericFailureMessage()
    {
        var exception = WindowsDeviceIoControlClient.CreateOpenException(
            CrystalIdeaAppleSmcTransport.DevicePath,
            errorCode: 32,
            exclusive: false);

        Assert.Equal(32, exception.NativeErrorCode);
        Assert.Equal(
            "CreateFileW failed for device '\\\\.\\APPLESMC' with Win32 error 32.",
            exception.Message);
    }
}
