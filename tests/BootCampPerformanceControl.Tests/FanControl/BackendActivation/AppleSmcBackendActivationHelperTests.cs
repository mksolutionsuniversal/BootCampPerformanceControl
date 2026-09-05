using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.BackendActivation;
using BootCampPerformanceControl.HardwareDetection;

namespace BootCampPerformanceControl.Tests.FanControl.BackendActivation;

public sealed class AppleSmcBackendActivationHelperTests
{
    [Fact]
    public async Task RunAsync_UnverifiedPlatform_DoesNotCallActivator()
    {
        var activator = new FakeBackendActivator();
        var helper = CreateHelper(
            new ModelVerificationResult(
                "Unknown",
                VerifiedHardwareModels.MacBookPro16_1,
                PlatformSupportStatus.DetectionIncomplete,
                ModelValidationLevel.NotIndividuallyTested,
                "Hardware detection incomplete."),
            activator);

        var result = await helper.RunAsync(CancellationToken.None);

        Assert.Equal(AppleSmcBackendActivationOutcome.UnsupportedModel, result.Outcome);
        Assert.Equal(0, activator.StartCallCount);
    }

    [Fact]
    public async Task RunAsync_SupportedIntelMacOutsideExactWritePolicy_CanActivateReadOnlyBackend()
    {
        var activator = new FakeBackendActivator
        {
            Result = new AppleSmcBackendActivationResult(
                AppleSmcBackendActivationOutcome.Running,
                "Running.")
        };
        var helper = CreateHelper(
            SupportedIntelMac(VerifiedHardwareModels.MacBookPro14_3),
            activator);

        var result = await helper.RunAsync(CancellationToken.None);

        Assert.Same(activator.Result, result);
        Assert.Equal(1, activator.StartCallCount);
    }

    [Fact]
    public async Task RunAsync_ExactVerifiedModel_CallsActivatorOnce()
    {
        var activator = new FakeBackendActivator
        {
            Result = new AppleSmcBackendActivationResult(
                AppleSmcBackendActivationOutcome.Running,
                "Running.")
        };
        var helper = CreateHelper(
            SupportedIntelMac(VerifiedHardwareModels.MacBookPro16_1),
            activator);

        var result = await helper.RunAsync(CancellationToken.None);

        Assert.Same(activator.Result, result);
        Assert.Equal(1, activator.StartCallCount);
    }

    [Fact]
    public async Task RunAsync_HardwareDetectionFailure_DoesNotCallActivator()
    {
        var expectedException = new InvalidOperationException("Detection failed.");
        var hardwareDetection = new FakeHardwareDetectionService(
            SupportedIntelMac(VerifiedHardwareModels.MacBookPro16_1))
        {
            DetectException = expectedException
        };
        var activator = new FakeBackendActivator();
        var helper = new AppleSmcBackendActivationHelper(
            hardwareDetection,
            new FanSafetyPolicy(),
            activator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => helper.RunAsync(CancellationToken.None));

        Assert.Same(expectedException, exception);
        Assert.Equal(0, activator.StartCallCount);
    }

    [Fact]
    public async Task RunAsync_PreCanceled_DoesNotDetectOrActivate()
    {
        var hardwareDetection = new FakeHardwareDetectionService(
            SupportedIntelMac(VerifiedHardwareModels.MacBookPro16_1));
        var activator = new FakeBackendActivator();
        var helper = new AppleSmcBackendActivationHelper(
            hardwareDetection,
            new FanSafetyPolicy(),
            activator);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => helper.RunAsync(cancellationSource.Token));

        Assert.Equal(0, hardwareDetection.DetectCallCount);
        Assert.Equal(0, activator.StartCallCount);
    }

    private static AppleSmcBackendActivationHelper CreateHelper(
        ModelVerificationResult verification,
        FakeBackendActivator activator)
    {
        return new AppleSmcBackendActivationHelper(
            new FakeHardwareDetectionService(verification),
            new FanSafetyPolicy(),
            activator);
    }

    private static ModelVerificationResult SupportedIntelMac(string model)
    {
        return new ModelVerificationResult(
            "Apple Inc.",
            model,
            PlatformSupportStatus.SupportedIntelMac,
            ModelValidationLevel.PerformanceValidated,
            "Supported.");
    }

    private sealed class FakeHardwareDetectionService : IHardwareDetectionService
    {
        private static readonly HardwareSnapshot Snapshot = new(
            new ComputerSystemInfo("Apple Inc.", "Test", "x64-based PC"),
            new ProcessorInfo("Intel processor", "GenuineIntel", 8, 16, 2400),
            Array.Empty<VideoControllerInfo>(),
            OperatingSystem: null,
            DateTimeOffset.UtcNow);

        private readonly ModelVerificationResult _verification;

        public FakeHardwareDetectionService(ModelVerificationResult verification)
        {
            _verification = verification;
        }

        public int DetectCallCount { get; private set; }

        public Exception? DetectException { get; init; }

        public Task<HardwareSnapshot> DetectAsync(CancellationToken cancellationToken)
        {
            DetectCallCount++;
            cancellationToken.ThrowIfCancellationRequested();

            if (DetectException is not null)
            {
                return Task.FromException<HardwareSnapshot>(DetectException);
            }

            return Task.FromResult(Snapshot);
        }

        public ModelVerificationResult VerifyModel(HardwareSnapshot snapshot)
        {
            return _verification;
        }
    }

    private sealed class FakeBackendActivator : IAppleSmcBackendActivator
    {
        public int StartCallCount { get; private set; }

        public AppleSmcBackendActivationResult Result { get; init; } = new(
            AppleSmcBackendActivationOutcome.Failed,
            "Not configured.");

        public Task<AppleSmcBackendActivationResult> StartAsync(
            CancellationToken cancellationToken)
        {
            StartCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Result);
        }
    }
}
