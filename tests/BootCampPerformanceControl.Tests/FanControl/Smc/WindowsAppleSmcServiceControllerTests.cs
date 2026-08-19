using BootCampPerformanceControl.FanControl.Smc.Windows;

namespace BootCampPerformanceControl.Tests.FanControl.Smc;

public sealed class WindowsAppleSmcServiceControllerTests
{
    private const int ErrorServiceCannotAcceptControl = 1061;
    private const int ErrorServiceNotActive = 1062;

    [Fact]
    public void CanTreatStopFailureAsSuccess_ServiceNotActive_ReturnsTrue()
    {
        var result = WindowsAppleSmcServiceController.CanTreatStopFailureAsSuccess(
            ErrorServiceNotActive,
            observedState: null);

        Assert.True(result);
    }

    [Theory]
    [InlineData((uint)AppleSmcServiceState.Stopped)]
    [InlineData((uint)AppleSmcServiceState.StopPending)]
    public void CanTreatStopFailureAsSuccess_CannotAcceptControlInSafeStopState_ReturnsTrue(
        uint rawObservedState)
    {
        var result = WindowsAppleSmcServiceController.CanTreatStopFailureAsSuccess(
            ErrorServiceCannotAcceptControl,
            (AppleSmcServiceState)rawObservedState);

        Assert.True(result);
    }

    [Theory]
    [InlineData((uint)AppleSmcServiceState.StartPending)]
    [InlineData((uint)AppleSmcServiceState.Running)]
    [InlineData((uint)AppleSmcServiceState.ContinuePending)]
    [InlineData((uint)AppleSmcServiceState.PausePending)]
    [InlineData((uint)AppleSmcServiceState.Paused)]
    public void CanTreatStopFailureAsSuccess_CannotAcceptControlInOtherState_ReturnsFalse(
        uint rawObservedState)
    {
        var result = WindowsAppleSmcServiceController.CanTreatStopFailureAsSuccess(
            ErrorServiceCannotAcceptControl,
            (AppleSmcServiceState)rawObservedState);

        Assert.False(result);
    }

    [Fact]
    public void CanTreatStopFailureAsSuccess_CannotAcceptControlWithoutState_ReturnsFalse()
    {
        var result = WindowsAppleSmcServiceController.CanTreatStopFailureAsSuccess(
            ErrorServiceCannotAcceptControl,
            observedState: null);

        Assert.False(result);
    }

    [Fact]
    public void CanTreatStopFailureAsSuccess_UnrelatedError_ReturnsFalse()
    {
        var result = WindowsAppleSmcServiceController.CanTreatStopFailureAsSuccess(
            errorCode: 5,
            AppleSmcServiceState.Stopped);

        Assert.False(result);
    }
}
