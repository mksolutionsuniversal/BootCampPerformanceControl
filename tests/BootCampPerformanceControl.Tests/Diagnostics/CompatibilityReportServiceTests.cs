using System.Reflection;
using BootCampPerformanceControl.Diagnostics;
using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.Profiles;
using BootCampPerformanceControl.SettingsBackup;
using BootCampPerformanceControl.Tests.TestDoubles;

namespace BootCampPerformanceControl.Tests.Diagnostics;

public sealed class CompatibilityReportServiceTests
{
    [Fact]
    public async Task GenerateAsync_WithVerifiedMacAndFanStatus_MapsCompatibilityFields()
    {
        var schemeId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var service = CreateService(
            VerifiedHardwareSnapshot(),
            PowerState(
                schemeId,
                processorMaximumAc: 95,
                processorMaximumDc: 95,
                boostModeAc: 0,
                boostModeDc: 0),
            restoreSnapshotStore: new FakeRestoreSnapshotStore(PowerState()));
        var fanStatus = VerifiedFanStatus();

        var result = await service.GenerateAsync(fanStatus, CancellationToken.None);

        Assert.Contains("BootCamp Performance Control Compatibility Report", result.Content);
        Assert.Contains($"BCPC version: {ExpectedApplicationVersion()}", result.Content);
        Assert.Contains("Manufacturer: Apple Inc.", result.Content);
        Assert.Contains("Mac model identifier: MacBookPro16,1", result.Content);
        Assert.Contains("CPU: Intel(R) Core(TM) i9-9980HK CPU @ 2.40GHz", result.Content);
        Assert.Contains("Core/thread count: 8 cores / 16 threads", result.Content);
        Assert.Contains("  - Intel(R) UHD Graphics 630", result.Content);
        Assert.Contains("  - AMD Radeon Pro 5500M", result.Content);
        Assert.Contains("Windows version/build: Microsoft Windows 11 Pro, 10.0.22631 (Build 22631), 64-bit", result.Content);
        Assert.Contains($"Active power scheme: {schemeId}", result.Content);
        Assert.Contains("PROCTHROTTLEMAX AC: 95%", result.Content);
        Assert.Contains("PROCTHROTTLEMAX DC: 95%", result.Content);
        Assert.Contains("PERFBOOSTMODE AC: 0", result.Content);
        Assert.Contains("PERFBOOSTMODE DC: 0", result.Content);
        Assert.Contains("Processor state readable: Yes", result.Content);
        Assert.Contains("Gaming Optimised eligibility: Yes", result.Content);
        Assert.Contains("Model validation level: PerformanceValidated", result.Content);
        Assert.Contains("Platform support: SupportedIntelMac", result.Content);
        Assert.Contains("Original Restore snapshot present: Yes", result.Content);
        Assert.Contains("AppleSMC backend state: Running", result.Content);
        Assert.Contains("Fan safety state: Read-only verified", result.Content);
        Assert.Contains("Fan 0 RPM: 1840 / 5616 RPM", result.Content);
        Assert.Contains("Fan 1 RPM: 1691 / 5200 RPM", result.Content);
        Assert.Contains("Mode: Apple Auto", result.Content);
        Assert.Contains("Write control state: Disabled", result.Content);
        Assert.Contains("Fan status/details: Verified in test.", result.Content);
    }

    [Fact]
    public async Task GenerateAsync_WhenFanStatusIsUnavailable_RendersFanValuesSafely()
    {
        var service = CreateService(VerifiedHardwareSnapshot());
        var fanStatus = FanControlStatus.CreateUnavailable(
            FanBackendState.NotInstalled,
            FanSafetyState.ReadOnlyUnavailable,
            "AppleSMC read-only backend is unavailable.");

        var result = await service.GenerateAsync(fanStatus, CancellationToken.None);

        Assert.Contains("AppleSMC backend state: Not installed", result.Content);
        Assert.Contains("Fan safety state: Read-only unavailable", result.Content);
        Assert.Contains("Fan 0 RPM: Unavailable", result.Content);
        Assert.Contains("Fan 1 RPM: Unavailable", result.Content);
        Assert.Contains("Mode: Unavailable", result.Content);
        Assert.Contains("Write control state: Disabled", result.Content);
    }

    [Fact]
    public async Task GenerateAsync_WhenPowerReadFails_ReportsProcessorStateNotReadable()
    {
        var service = new CompatibilityReportService(
            new FakeHardwareDetectionService(VerifiedHardwareSnapshot()),
            new FakeFailingPowerManagementService(),
            new FakeRestoreSnapshotStore(),
            new ProfileCatalog(),
            new ProfileExecutionResolver(),
            new TestApplicationLogger());

        var result = await service.GenerateAsync(VerifiedFanStatus(), CancellationToken.None);

        Assert.Contains("Active power scheme: Unknown", result.Content);
        Assert.Contains("Processor state readable: No", result.Content);
        Assert.Contains("Gaming Optimised eligibility: No", result.Content);
    }

    [Fact]
    public async Task GenerateAsync_RedactsPrivacySensitiveValuesFromAllReportSections()
    {
        var hardwareSnapshot = new HardwareSnapshot(
            new ComputerSystemInfo(
                "Apple Inc. DESKTOP-SECRET Serial Number: C02SECRET1234",
                "MacBookPro16,1 USERNAME=Alice",
                @"WORKGROUP\Alice"),
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
                @"Microsoft Windows C:\Users\Alice\Documents Hostname: ALICE-PC",
                "10.0.22631",
                "22631",
                @"DOMAIN\Alice"),
            DateTimeOffset.UtcNow);
        var service = CreateService(hardwareSnapshot);
        var fanStatus = new FanControlStatus(
            FanBackendState.Running,
            FanSafetyState.ReadOnlyVerified,
            new FanReading(1800f, 5600f, FanOperatingMode.AppleAuto),
            null,
            @"Details sent by bob@example.com from 10.0.0.2 COMPUTERNAME=PRIVATE-PC USERPROFILE=C:\Users\Bob");

        var result = await service.GenerateAsync(fanStatus, CancellationToken.None);

        Assert.Contains("Manufacturer: Apple Inc.", result.Content);
        Assert.Contains("Mac model identifier: MacBookPro16,1", result.Content);
        Assert.Contains("[Redacted email]", result.Content);
        Assert.Contains("[Redacted path]", result.Content);
        Assert.Contains("[Redacted environment variable]", result.Content);
        Assert.DoesNotContain("alice@example.com", result.Content);
        Assert.DoesNotContain("bob@example.com", result.Content);
        Assert.DoesNotContain(@"C:\Users\Alice", result.Content);
        Assert.DoesNotContain(@"C:\Users\Bob", result.Content);
        Assert.DoesNotContain("192.168.1.10", result.Content);
        Assert.DoesNotContain("10.0.0.2", result.Content);
        Assert.DoesNotContain("00-11-22-33-44-55", result.Content);
        Assert.DoesNotContain("ABCDE-FGHIJ-KLMNO-PQRST-UVWXY", result.Content);
        Assert.DoesNotContain("DESKTOP-SECRET", result.Content);
        Assert.DoesNotContain(@"DOMAIN\Alice", result.Content);
        Assert.DoesNotContain(@"WORKGROUP\Alice", result.Content);
        Assert.DoesNotContain(@"\\SERVER\Share", result.Content);
        Assert.DoesNotContain("C02SECRET1234", result.Content);
        Assert.DoesNotContain("ALICE-PC", result.Content);
        Assert.DoesNotContain("COMPUTERNAME=PRIVATE-PC", result.Content);
        Assert.DoesNotContain("USERPROFILE=", result.Content);
        Assert.DoesNotContain("USERNAME=Alice", result.Content);
    }

    [Fact]
    public async Task GenerateAsync_SuggestedFileNameContainsSanitizedModelAndVersion()
    {
        var service = CreateService(VerifiedHardwareSnapshot(model: "MacBookPro16:1/Debug*Name?"));

        var result = await service.GenerateAsync(FanControlStatus.NotChecked, CancellationToken.None);

        Assert.StartsWith(
            "BootCampPerformanceControl-Compatibility-MacBookPro16_1_Debug_Name_",
            result.SuggestedFileName,
            StringComparison.Ordinal);
        Assert.EndsWith("-0.3.0-rc.1.txt", result.SuggestedFileName, StringComparison.Ordinal);
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            Assert.DoesNotContain(invalidCharacter, result.SuggestedFileName);
        }
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

        await service.GenerateAsync(FanControlStatus.NotChecked, CancellationToken.None);

        Assert.Equal(1, powerManagementService.ReadCurrentStateCallCount);
        Assert.Equal(0, powerManagementService.ApplyProcessorSettingsCallCount);
        Assert.Equal(0, powerManagementService.ApplyProcessorSettingsWithExpectedStateCallCount);
        Assert.Equal(0, powerManagementService.RestoreOriginalSettingsCallCount);
        Assert.Equal(0, restoreSnapshotStore.GetOriginalRestoreSnapshotCallCount);
        Assert.Equal(0, restoreSnapshotStore.TrySaveOriginalRestoreSnapshotCallCount);
        Assert.Equal(0, restoreSnapshotStore.ReplaceOriginalRestoreSnapshotCallCount);
        Assert.Equal(0, restoreSnapshotStore.ClearOriginalRestoreSnapshotCallCount);
    }

    private static CompatibilityReportService CreateService(
        HardwareSnapshot hardwareSnapshot,
        PowerStateSnapshot? powerState = null,
        FakePowerManagementService? powerManagementService = null,
        FakeRestoreSnapshotStore? restoreSnapshotStore = null)
    {
        return new CompatibilityReportService(
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

    private static FanControlStatus VerifiedFanStatus()
    {
        return new FanControlStatus(
            FanBackendState.Running,
            FanSafetyState.ReadOnlyVerified,
            new FanReading(1840f, 5616f, FanOperatingMode.AppleAuto),
            new FanReading(1691f, 5200f, FanOperatingMode.AppleAuto),
            "Verified in test.");
    }

    private static string ExpectedApplicationVersion()
    {
        return typeof(CompatibilityReportService)
            .Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? typeof(CompatibilityReportService).Assembly.GetName().Version?.ToString()
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

        public Task<PowerStateSnapshot> ReadCurrentStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCurrentStateCallCount++;
            return Task.FromResult(_powerState);
        }

        public Task<PowerOperationResult> ApplyProcessorSettingsAsync(
            ProcessorPowerSettings requestedSettings,
            CancellationToken cancellationToken)
        {
            ApplyProcessorSettingsCallCount++;
            throw new InvalidOperationException("Compatibility reporting must not apply processor settings.");
        }

        public Task<PowerOperationResult> ApplyProcessorSettingsAsync(
            ProcessorPowerSettings requestedSettings,
            PowerStateSnapshot expectedStateBefore,
            CancellationToken cancellationToken)
        {
            ApplyProcessorSettingsWithExpectedStateCallCount++;
            throw new InvalidOperationException("Compatibility reporting must not apply processor settings.");
        }

        public Task<PowerOperationResult> RestoreOriginalSettingsAsync(CancellationToken cancellationToken)
        {
            RestoreOriginalSettingsCallCount++;
            throw new InvalidOperationException("Compatibility reporting must not restore processor settings.");
        }
    }

    private sealed class FakeFailingPowerManagementService : IPowerManagementService
    {
        public Task<PowerStateSnapshot> ReadCurrentStateAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Power read failed.");
        }

        public Task<PowerOperationResult> ApplyProcessorSettingsAsync(
            ProcessorPowerSettings requestedSettings,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Compatibility reporting must not apply processor settings.");
        }

        public Task<PowerOperationResult> ApplyProcessorSettingsAsync(
            ProcessorPowerSettings requestedSettings,
            PowerStateSnapshot expectedStateBefore,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Compatibility reporting must not apply processor settings.");
        }

        public Task<PowerOperationResult> RestoreOriginalSettingsAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Compatibility reporting must not restore processor settings.");
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
            throw new InvalidOperationException("Compatibility reporting must not save restore snapshots.");
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
            throw new InvalidOperationException("Compatibility reporting must not replace restore snapshots.");
        }

        public Task ClearOriginalRestoreSnapshotAsync(CancellationToken cancellationToken)
        {
            ClearOriginalRestoreSnapshotCallCount++;
            throw new InvalidOperationException("Compatibility reporting must not clear restore snapshots.");
        }
    }
}
