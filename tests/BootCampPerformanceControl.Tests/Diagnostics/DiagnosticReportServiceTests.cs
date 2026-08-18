using System.Reflection;
using BootCampPerformanceControl.Diagnostics;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.Profiles;
using BootCampPerformanceControl.SettingsBackup;
using BootCampPerformanceControl.Tests.TestDoubles;

namespace BootCampPerformanceControl.Tests.Diagnostics;

public sealed class DiagnosticReportServiceTests
{
    [Fact]
    public async Task GenerateAsync_WithVerifiedMacBookPro16_1_MapsHardwarePowerAndProfileSupport()
    {
        var schemeId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var hardwareSnapshot = VerifiedHardwareSnapshot();
        var powerState = PowerState(
            schemeId,
            processorMaximumAc: 95,
            processorMaximumDc: 95,
            boostModeAc: 0,
            boostModeDc: 0);
        var service = CreateService(hardwareSnapshot, powerState);

        var result = await service.GenerateAsync(CancellationToken.None);

        Assert.Contains($"Version: {ExpectedApplicationVersion()}", result.Content);
        Assert.Contains("Windows: Microsoft Windows 11 Pro, 10.0.22631 (Build 22631), 64-bit", result.Content);
        Assert.Contains("Manufacturer: Apple Inc.", result.Content);
        Assert.Contains("Mac Model: MacBookPro16,1", result.Content);
        Assert.Contains("CPU: Intel(R) Core(TM) i9-9980HK CPU @ 2.40GHz", result.Content);
        Assert.Contains("  - Intel(R) UHD Graphics 630", result.Content);
        Assert.Contains("  - AMD Radeon Pro 5500M", result.Content);
        Assert.Contains($"Active Power Scheme: {schemeId}", result.Content);
        Assert.Contains("Model verified: Yes", result.Content);
        Assert.Contains("Gaming Optimised verified: Yes", result.Content);
        Assert.Contains("Model-specific processor power writes allowed: Yes", result.Content);
    }

    [Fact]
    public async Task GenerateAsync_WithNotIndividuallyTestedIntelMac_ReportsWritesAllowedAndModelNotVerified()
    {
        var hardwareSnapshot = VerifiedHardwareSnapshot(model: "MacBookPro15,1");
        var service = CreateService(hardwareSnapshot);

        var result = await service.GenerateAsync(CancellationToken.None);

        Assert.Contains("Mac Model: MacBookPro15,1", result.Content);
        Assert.Contains("Model verified: No", result.Content);
        Assert.Contains("Gaming Optimised verified: Yes", result.Content);
        Assert.Contains("Model-specific processor power writes allowed: Yes", result.Content);
    }

    [Fact]
    public async Task GenerateAsync_WithUnsupportedNonAppleHardware_ReportsWritesNotAllowed()
    {
        var hardwareSnapshot = VerifiedHardwareSnapshot(manufacturer: "PC Manufacturer", model: "Generic PC");
        var service = CreateService(hardwareSnapshot);

        var result = await service.GenerateAsync(CancellationToken.None);

        Assert.Contains("Apple hardware detected: No", result.Content);
        Assert.Contains("Model verified: No", result.Content);
        Assert.Contains("Gaming Optimised verified: No", result.Content);
        Assert.Contains("Model-specific processor power writes allowed: No", result.Content);
    }

    [Fact]
    public async Task GenerateAsync_ReportsAcAndDcProcessorMaximumValues()
    {
        var service = CreateService(
            VerifiedHardwareSnapshot(),
            PowerState(processorMaximumAc: 93, processorMaximumDc: 87));

        var result = await service.GenerateAsync(CancellationToken.None);

        Assert.Contains("PROCTHROTTLEMAX", result.Content);
        Assert.Contains("  AC: 93%", result.Content);
        Assert.Contains("  DC: 87%", result.Content);
    }

    [Fact]
    public async Task GenerateAsync_ReportsAcAndDcPerformanceBoostModeValues()
    {
        var service = CreateService(
            VerifiedHardwareSnapshot(),
            PowerState(boostModeAc: 1, boostModeDc: 2));

        var result = await service.GenerateAsync(CancellationToken.None);

        Assert.Contains("PERFBOOSTMODE", result.Content);
        Assert.Contains("  AC: 1", result.Content);
        Assert.Contains("  DC: 2", result.Content);
    }

    [Fact]
    public async Task GenerateAsync_ReportsRestoreSnapshotPresence()
    {
        var restoreSnapshot = PowerState();
        var serviceWithSnapshot = CreateService(
            VerifiedHardwareSnapshot(),
            restoreSnapshotStore: new FakeRestoreSnapshotStore(restoreSnapshot));
        var serviceWithoutSnapshot = CreateService(
            VerifiedHardwareSnapshot(),
            restoreSnapshotStore: new FakeRestoreSnapshotStore());

        var reportWithSnapshot = await serviceWithSnapshot.GenerateAsync(CancellationToken.None);
        var reportWithoutSnapshot = await serviceWithoutSnapshot.GenerateAsync(CancellationToken.None);

        Assert.Contains("Original restore snapshot present: Yes", reportWithSnapshot.Content);
        Assert.Contains("Original restore snapshot present: No", reportWithoutSnapshot.Content);
    }

    [Fact]
    public async Task GenerateAsync_WithMissingOptionalHardwareFields_RendersUnknownSafely()
    {
        var hardwareSnapshot = new HardwareSnapshot(
            new ComputerSystemInfo(string.Empty, string.Empty, string.Empty),
            Processor: null,
            VideoControllers: [],
            OperatingSystem: null,
            DateTimeOffset.UtcNow);
        var service = CreateService(hardwareSnapshot);

        var result = await service.GenerateAsync(CancellationToken.None);

        Assert.Contains("Windows: Unknown", result.Content);
        Assert.Contains("Manufacturer: Unknown", result.Content);
        Assert.Contains("Mac Model: Unknown", result.Content);
        Assert.Contains("CPU: Unknown", result.Content);
        Assert.Contains("  - Unknown", result.Content);
    }

    [Fact]
    public async Task GenerateAsync_SuggestedFileNameContainsDetectedMacModel()
    {
        var service = CreateService(VerifiedHardwareSnapshot());

        var result = await service.GenerateAsync(CancellationToken.None);

        Assert.Equal(
            "BootCampPerformanceControl-Diagnostics-MacBookPro16,1.txt",
            result.SuggestedFileName);
    }

    [Fact]
    public async Task GenerateAsync_SuggestedFileNameSanitisesInvalidCharacters()
    {
        var service = CreateService(VerifiedHardwareSnapshot(model: "MacBookPro16:1/Debug*Name?"));

        var result = await service.GenerateAsync(CancellationToken.None);

        Assert.Contains("MacBookPro16_1_Debug_Name_", result.SuggestedFileName);
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            Assert.DoesNotContain(invalidCharacter, result.SuggestedFileName);
        }
    }

    [Fact]
    public async Task GenerateAsync_RedactsPrivacySensitiveValuesFromSuppliedDiagnosticData()
    {
        var hardwareSnapshot = new HardwareSnapshot(
            new ComputerSystemInfo(
                "Apple Inc. DESKTOP-SECRET",
                VerifiedHardwareModels.MacBookPro16_1,
                @"WORKGROUP\PRIVATE"),
            new ProcessorInfo(
                "Intel CPU alice@example.com ABCDE-FGHIJ-KLMNO-PQRST-UVWXY",
                "GenuineIntel",
                8,
                16,
                2400),
            [
                new VideoControllerInfo(
                    @"AMD Radeon 192.168.1.10 00-11-22-33-44-55 \\SERVER\Share\GPU",
                    @"C:\Users\Alice\driver.inf",
                    4_294_967_296)
            ],
            new OperatingSystemInfo(
                @"Microsoft Windows C:\Users\Alice\Documents DESKTOP-SECRET",
                "10.0.22631",
                "22631",
                @"DOMAIN\Alice"),
            DateTimeOffset.UtcNow);
        var service = CreateService(hardwareSnapshot);

        var result = await service.GenerateAsync(CancellationToken.None);

        Assert.DoesNotContain("alice@example.com", result.Content);
        Assert.DoesNotContain(@"C:\Users\Alice", result.Content);
        Assert.DoesNotContain("192.168.1.10", result.Content);
        Assert.DoesNotContain("00-11-22-33-44-55", result.Content);
        Assert.DoesNotContain("ABCDE-FGHIJ-KLMNO-PQRST-UVWXY", result.Content);
        Assert.DoesNotContain("DESKTOP-SECRET", result.Content);
        Assert.DoesNotContain(@"DOMAIN\Alice", result.Content);
        Assert.DoesNotContain(@"\\SERVER\Share", result.Content);
        Assert.DoesNotContain("WORKGROUP", result.Content);
    }

    [Fact]
    public async Task GenerateAsync_DoesNotInvokePowerOrRestoreWriteMethods()
    {
        var powerManagementService = new FakePowerManagementService(PowerState());
        var restoreSnapshotStore = new FakeRestoreSnapshotStore(PowerState());
        var service = CreateService(
            VerifiedHardwareSnapshot(),
            powerManagementService: powerManagementService,
            restoreSnapshotStore: restoreSnapshotStore);

        await service.GenerateAsync(CancellationToken.None);

        Assert.Equal(1, powerManagementService.ReadCurrentStateCallCount);
        Assert.Equal(0, powerManagementService.ApplyProcessorSettingsCallCount);
        Assert.Equal(0, powerManagementService.ApplyProcessorSettingsWithExpectedStateCallCount);
        Assert.Equal(0, powerManagementService.RestoreOriginalSettingsCallCount);
        Assert.Equal(0, restoreSnapshotStore.GetOriginalRestoreSnapshotCallCount);
        Assert.Equal(0, restoreSnapshotStore.TrySaveOriginalRestoreSnapshotCallCount);
        Assert.Equal(0, restoreSnapshotStore.ReplaceOriginalRestoreSnapshotCallCount);
        Assert.Equal(0, restoreSnapshotStore.ClearOriginalRestoreSnapshotCallCount);
    }

    [Fact]
    public async Task GenerateAsync_WhenCanceledAfterPowerReadBeforeRestorePresence_PropagatesCancellation()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var powerManagementService = new FakePowerManagementService(PowerState())
        {
            AfterReadCurrentState = cancellationTokenSource.Cancel
        };
        var restoreSnapshotStore = new FakeRestoreSnapshotStore(PowerState());
        var service = CreateService(
            VerifiedHardwareSnapshot(),
            powerManagementService: powerManagementService,
            restoreSnapshotStore: restoreSnapshotStore);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.GenerateAsync(cancellationTokenSource.Token));

        Assert.Equal(1, powerManagementService.ReadCurrentStateCallCount);
        Assert.Equal(0, restoreSnapshotStore.HasOriginalRestoreSnapshotCallCount);
    }

    private static DiagnosticReportService CreateService(
        HardwareSnapshot hardwareSnapshot,
        PowerStateSnapshot? powerState = null,
        FakePowerManagementService? powerManagementService = null,
        FakeRestoreSnapshotStore? restoreSnapshotStore = null)
    {
        return new DiagnosticReportService(
            new FakeHardwareDetectionService(hardwareSnapshot),
            powerManagementService ?? new FakePowerManagementService(powerState ?? PowerState()),
            restoreSnapshotStore ?? new FakeRestoreSnapshotStore(),
            new ProfileCatalog(),
            new ProfileExecutionResolver(),
            new TestApplicationLogger());
    }

    private static HardwareSnapshot VerifiedHardwareSnapshot(
        string manufacturer = "Apple Inc.",
        string model = VerifiedHardwareModels.MacBookPro16_1)
    {
        return new HardwareSnapshot(
            new ComputerSystemInfo(manufacturer, model, "x64-based PC"),
            new ProcessorInfo(
                "Intel(R) Core(TM) i9-9980HK CPU @ 2.40GHz",
                "GenuineIntel",
                8,
                16,
                2400),
            [
                new VideoControllerInfo("Intel(R) UHD Graphics 630", "31.0.101.2115", 1_073_741_824),
                new VideoControllerInfo("AMD Radeon Pro 5500M", "31.0.12027.9001", 4_294_967_296)
            ],
            new OperatingSystemInfo(
                "Microsoft Windows 11 Pro",
                "10.0.22631",
                "22631",
                "64-bit"),
            DateTimeOffset.UtcNow);
    }

    private static PowerStateSnapshot PowerState(
        Guid? schemeId = null,
        uint processorMaximumAc = 100,
        uint processorMaximumDc = 100,
        uint boostModeAc = 2,
        uint boostModeDc = 2)
    {
        return new PowerStateSnapshot(
            schemeId ?? Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            processorMaximumAc,
            processorMaximumDc,
            boostModeAc,
            boostModeDc,
            DateTimeOffset.UtcNow);
    }

    private static string ExpectedApplicationVersion()
    {
        return typeof(DiagnosticReportService)
            .Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? typeof(DiagnosticReportService).Assembly.GetName().Version?.ToString()
            ?? string.Empty;
    }

    private sealed class FakeHardwareDetectionService : IHardwareDetectionService
    {
        private readonly HardwareDetectionService _hardwareDetectionService = new(new ModelSupportRegistry());
        private readonly HardwareSnapshot _snapshot;

        public FakeHardwareDetectionService(HardwareSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public Task<HardwareSnapshot> DetectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_snapshot);
        }

        public ModelVerificationResult VerifyModel(HardwareSnapshot snapshot)
        {
            return _hardwareDetectionService.VerifyModel(snapshot);
        }
    }

    private sealed class FakePowerManagementService : IPowerManagementService
    {
        private readonly PowerStateSnapshot _powerState;

        public FakePowerManagementService(PowerStateSnapshot powerState)
        {
            _powerState = powerState;
        }

        public int ReadCurrentStateCallCount { get; private set; }

        public int ApplyProcessorSettingsCallCount { get; private set; }

        public int ApplyProcessorSettingsWithExpectedStateCallCount { get; private set; }

        public int RestoreOriginalSettingsCallCount { get; private set; }

        public Action? AfterReadCurrentState { get; set; }

        public Task<PowerStateSnapshot> ReadCurrentStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCurrentStateCallCount++;
            AfterReadCurrentState?.Invoke();
            return Task.FromResult(_powerState);
        }

        public Task<PowerOperationResult> ApplyProcessorSettingsAsync(
            ProcessorPowerSettings requestedSettings,
            CancellationToken cancellationToken)
        {
            ApplyProcessorSettingsCallCount++;
            throw new InvalidOperationException("Diagnostics must not apply processor settings.");
        }

        public Task<PowerOperationResult> ApplyProcessorSettingsAsync(
            ProcessorPowerSettings requestedSettings,
            PowerStateSnapshot expectedStateBefore,
            CancellationToken cancellationToken)
        {
            ApplyProcessorSettingsWithExpectedStateCallCount++;
            throw new InvalidOperationException("Diagnostics must not apply processor settings.");
        }

        public Task<PowerOperationResult> RestoreOriginalSettingsAsync(CancellationToken cancellationToken)
        {
            RestoreOriginalSettingsCallCount++;
            throw new InvalidOperationException("Diagnostics must not restore processor settings.");
        }
    }

    private sealed class FakeRestoreSnapshotStore : IRestoreSnapshotStore
    {
        private readonly PowerStateSnapshot? _snapshot;

        public FakeRestoreSnapshotStore(PowerStateSnapshot? snapshot = null)
        {
            _snapshot = snapshot;
        }

        public bool HasOriginalRestoreSnapshot
        {
            get
            {
                HasOriginalRestoreSnapshotCallCount++;
                return _snapshot is not null;
            }
        }

        public int HasOriginalRestoreSnapshotCallCount { get; private set; }

        public int TrySaveOriginalRestoreSnapshotCallCount { get; private set; }

        public int GetOriginalRestoreSnapshotCallCount { get; private set; }

        public int ReplaceOriginalRestoreSnapshotCallCount { get; private set; }

        public int ClearOriginalRestoreSnapshotCallCount { get; private set; }

        public Task<bool> TrySaveOriginalRestoreSnapshotAsync(
            PowerStateSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            TrySaveOriginalRestoreSnapshotCallCount++;
            throw new InvalidOperationException("Diagnostics must not save restore snapshots.");
        }

        public Task<PowerStateSnapshot?> GetOriginalRestoreSnapshotAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetOriginalRestoreSnapshotCallCount++;
            return Task.FromResult(_snapshot);
        }

        public Task ReplaceOriginalRestoreSnapshotAsync(
            PowerStateSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            ReplaceOriginalRestoreSnapshotCallCount++;
            throw new InvalidOperationException("Diagnostics must not replace restore snapshots.");
        }

        public Task ClearOriginalRestoreSnapshotAsync(CancellationToken cancellationToken)
        {
            ClearOriginalRestoreSnapshotCallCount++;
            throw new InvalidOperationException("Diagnostics must not clear restore snapshots.");
        }
    }
}
