using System.Buffers.Binary;
using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.Profiles;

namespace BootCampPerformanceControl.Tests.Profiles;

public sealed class GamingOptimisedRestoreCoordinatorTests
{
    private const string Model = VerifiedHardwareModels.MacBookPro16_1;

    [Fact]
    public async Task RestoreAsync_WrongModel_FailsBeforeFanProbeRecoveryOrPowerRestore()
    {
        var power = new RecordingPowerManagementService();
        var fanProbe = new RecordingFanCapabilityProbe();
        var fanCoordinator = new RecordingFanOverrideCoordinator();
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        var result = await coordinator.RestoreAsync(
            VerifiedHardwareModels.MacBookPro14_3,
            CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Null(result.FanRecovery);
        Assert.Null(result.PowerOperation);
        Assert.Equal(0, fanProbe.ProbeCallCount);
        Assert.Equal(0, fanCoordinator.RecoverCallCount);
        Assert.Equal(0, power.RestoreOriginalSettingsCallCount);
    }

    [Fact]
    public async Task RestoreAsync_FanProbeThrows_DoesNotRecoverFansOrRestorePower()
    {
        var probeException = new InvalidOperationException("probe failed");
        var power = new RecordingPowerManagementService();
        var fanProbe = new RecordingFanCapabilityProbe
        {
            ProbeException = probeException
        };
        var fanCoordinator = new RecordingFanOverrideCoordinator();
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.RestoreAsync(Model, CancellationToken.None));

        Assert.Same(probeException, exception);
        Assert.Equal(1, fanProbe.ProbeCallCount);
        Assert.Equal(0, fanCoordinator.RecoverCallCount);
        Assert.Equal(0, power.RestoreOriginalSettingsCallCount);
    }

    [Fact]
    public async Task RestoreAsync_FanRecoveryBlocked_FailsWithoutPowerRestore()
    {
        var power = new RecordingPowerManagementService();
        var fanProbe = new RecordingFanCapabilityProbe();
        var fanCoordinator = new RecordingFanOverrideCoordinator
        {
            RecoveryResult = new FanOverrideRecoveryDecision(
                FanOverrideRecoveryAction.Blocked,
                "owned manual state changed")
        };
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        var result = await coordinator.RestoreAsync(Model, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.False(result.IsFanBaselineVerified);
        Assert.Equal(FanOverrideRecoveryAction.Blocked, result.FanRecovery?.Action);
        Assert.Null(result.PowerOperation);
        Assert.Equal(0, power.RestoreOriginalSettingsCallCount);
    }

    [Fact]
    public async Task RestoreAsync_FanRecoveryNoneWithManualFans_FailsClosedWithoutPowerRestore()
    {
        var power = new RecordingPowerManagementService();
        var fanProbe = new RecordingFanCapabilityProbe(
            results: [ValidFanCapability(fan0Mode: 1, fan1Mode: 1)]);
        var fanCoordinator = new RecordingFanOverrideCoordinator
        {
            RecoveryResult = new FanOverrideRecoveryDecision(
                FanOverrideRecoveryAction.None,
                "No fan override ownership marker exists.")
        };
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        var result = await coordinator.RestoreAsync(Model, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.False(result.IsFanBaselineVerified);
        Assert.Equal(FanOverrideRecoveryAction.None, result.FanRecovery?.Action);
        Assert.Null(result.PowerOperation);
        Assert.Equal(0, power.RestoreOriginalSettingsCallCount);
    }

    [Fact]
    public async Task RestoreAsync_FanRecoveryNoneWithAppleAutoFans_ProceedsToPowerRestore()
    {
        var power = new RecordingPowerManagementService();
        var fanProbe = new RecordingFanCapabilityProbe(
            results: [ValidFanCapability(fan0Mode: 0, fan1Mode: 0)]);
        var fanCoordinator = new RecordingFanOverrideCoordinator
        {
            RecoveryResult = new FanOverrideRecoveryDecision(
                FanOverrideRecoveryAction.None,
                "Both fans are already Apple Auto.")
        };
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        var result = await coordinator.RestoreAsync(Model, CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.True(result.IsFanBaselineVerified);
        Assert.Equal(FanOverrideRecoveryAction.None, result.FanRecovery?.Action);
        Assert.Equal(1, power.RestoreOriginalSettingsCallCount);
    }

    [Fact]
    public async Task RestoreAsync_FanRecoveryRestoreAppleAuto_ProceedsToPowerRestore()
    {
        var power = new RecordingPowerManagementService();
        var fanProbe = new RecordingFanCapabilityProbe(
            results: [ValidFanCapability(fan0Mode: 1, fan1Mode: 1)]);
        var fanCoordinator = new RecordingFanOverrideCoordinator
        {
            RecoveryResult = new FanOverrideRecoveryDecision(
                FanOverrideRecoveryAction.RestoreAppleAuto,
                "Apple Auto restored.")
        };
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        var result = await coordinator.RestoreAsync(Model, CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.True(result.IsFanBaselineVerified);
        Assert.Equal(FanOverrideRecoveryAction.RestoreAppleAuto, result.FanRecovery?.Action);
        Assert.Equal(1, power.RestoreOriginalSettingsCallCount);
    }

    [Fact]
    public async Task RestoreAsync_SuccessfulRestore_RecordsFansBeforePowerOrder()
    {
        var events = new List<string>();
        var power = new RecordingPowerManagementService(events);
        var fanProbe = new RecordingFanCapabilityProbe(events);
        var fanCoordinator = new RecordingFanOverrideCoordinator(events);
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        var result = await coordinator.RestoreAsync(Model, CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal(["fan-probe", "fan-recover", "power-restore"], events);
        Assert.True(events.IndexOf("fan-recover") < events.IndexOf("power-restore"));
    }

    [Fact]
    public async Task RestoreAsync_FanRecoveryUsesNonCancelableToken()
    {
        var power = new RecordingPowerManagementService();
        var fanProbe = new RecordingFanCapabilityProbe();
        var fanCoordinator = new RecordingFanOverrideCoordinator();
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);
        using var cancellationSource = new CancellationTokenSource();

        await coordinator.RestoreAsync(Model, cancellationSource.Token);

        Assert.Single(fanCoordinator.RecoverTokens);
        Assert.False(fanCoordinator.RecoverTokens[0].CanBeCanceled);
    }

    [Fact]
    public async Task RestoreAsync_CallerCancellationDuringFanRecoveryStopsBeforePowerRestore()
    {
        var events = new List<string>();
        using var cancellationSource = new CancellationTokenSource();
        var power = new RecordingPowerManagementService(events);
        var fanProbe = new RecordingFanCapabilityProbe(events);
        var fanCoordinator = new RecordingFanOverrideCoordinator(events)
        {
            OnRecover = () => cancellationSource.Cancel()
        };
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.RestoreAsync(Model, cancellationSource.Token));

        Assert.Equal(1, fanCoordinator.RecoverCallCount);
        Assert.Equal(0, power.RestoreOriginalSettingsCallCount);
        Assert.Equal(0, fanCoordinator.ApplyCallCount);
        Assert.Equal(["fan-probe", "fan-recover"], events);
    }

    [Fact]
    public async Task RestoreAsync_FanRecoveryAndPowerRestoreSucceed_ReturnsSuccess()
    {
        var power = new RecordingPowerManagementService();
        var fanProbe = new RecordingFanCapabilityProbe();
        var fanCoordinator = new RecordingFanOverrideCoordinator();
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        var result = await coordinator.RestoreAsync(Model, CancellationToken.None);

        Assert.True(result.IsSuccessful);
        Assert.Equal(string.Empty, result.FailureReason);
        Assert.True(result.IsFanBaselineVerified);
        Assert.NotNull(result.FanRecovery);
        Assert.NotNull(result.PowerOperation);
        Assert.True(result.PowerOperation!.IsSuccessful);
    }

    [Fact]
    public async Task RestoreAsync_PowerRestoreReturnsFailure_FailsWithoutFanApply()
    {
        var failedPowerOperation = FailedPowerRestore("power restore failed");
        var power = new RecordingPowerManagementService
        {
            RestoreResult = failedPowerOperation
        };
        var fanProbe = new RecordingFanCapabilityProbe();
        var fanCoordinator = new RecordingFanOverrideCoordinator();
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        var result = await coordinator.RestoreAsync(Model, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.True(result.IsFanBaselineVerified);
        Assert.Same(failedPowerOperation, result.PowerOperation);
        Assert.NotNull(result.FanRecovery);
        Assert.Equal(0, fanCoordinator.ApplyCallCount);
    }

    [Fact]
    public async Task RestoreAsync_PowerRestoreThrows_PropagatesWithoutFanApply()
    {
        var powerException = new InvalidOperationException("power restore failed");
        var power = new RecordingPowerManagementService
        {
            RestoreException = powerException
        };
        var fanProbe = new RecordingFanCapabilityProbe();
        var fanCoordinator = new RecordingFanOverrideCoordinator();
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.RestoreAsync(Model, CancellationToken.None));

        Assert.Same(powerException, exception);
        Assert.Equal(1, power.RestoreOriginalSettingsCallCount);
        Assert.Equal(0, fanCoordinator.ApplyCallCount);
    }

    [Fact]
    public async Task RestoreAsync_PowerRestoreCanceled_PropagatesWithoutFanApply()
    {
        var power = new RecordingPowerManagementService
        {
            RestoreException = new OperationCanceledException("power restore canceled")
        };
        var fanProbe = new RecordingFanCapabilityProbe();
        var fanCoordinator = new RecordingFanOverrideCoordinator();
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.RestoreAsync(Model, CancellationToken.None));

        Assert.Equal(1, power.RestoreOriginalSettingsCallCount);
        Assert.Equal(0, fanCoordinator.ApplyCallCount);
    }

    [Fact]
    public async Task RestoreAsync_TwoAttemptsResumeAfterFanRestoredAndPowerFailure()
    {
        var events = new List<string>();
        var firstPower = new RecordingPowerManagementService(events)
        {
            RestoreResult = FailedPowerRestore("first power restore failed")
        };
        var firstFanProbe = new RecordingFanCapabilityProbe(
            events,
            results: [ValidFanCapability(fan0Mode: 1, fan1Mode: 1)]);
        var firstFanCoordinator = new RecordingFanOverrideCoordinator(events)
        {
            RecoveryResult = new FanOverrideRecoveryDecision(
                FanOverrideRecoveryAction.RestoreAppleAuto,
                "Apple Auto restored.")
        };
        var firstCoordinator = CreateCoordinator(firstPower, firstFanProbe, firstFanCoordinator);

        var firstResult = await firstCoordinator.RestoreAsync(Model, CancellationToken.None);

        var secondPower = new RecordingPowerManagementService(events);
        var secondFanProbe = new RecordingFanCapabilityProbe(
            events,
            results: [ValidFanCapability(fan0Mode: 0, fan1Mode: 0)]);
        var secondFanCoordinator = new RecordingFanOverrideCoordinator(events)
        {
            RecoveryResult = new FanOverrideRecoveryDecision(
                FanOverrideRecoveryAction.None,
                "No fan override ownership marker exists.")
        };
        var secondCoordinator = CreateCoordinator(secondPower, secondFanProbe, secondFanCoordinator);

        var secondResult = await secondCoordinator.RestoreAsync(Model, CancellationToken.None);

        Assert.False(firstResult.IsSuccessful);
        Assert.True(secondResult.IsSuccessful);
        Assert.Equal(0, firstFanCoordinator.ApplyCallCount);
        Assert.Equal(0, secondFanCoordinator.ApplyCallCount);
        Assert.Equal(
            [
                "fan-probe",
                "fan-recover",
                "power-restore",
                "fan-probe",
                "fan-recover",
                "power-restore"
            ],
            events);
    }

    [Fact]
    public async Task RestoreAsync_NeverCallsFanMaximumSafeRpmApply()
    {
        var power = new RecordingPowerManagementService();
        var fanProbe = new RecordingFanCapabilityProbe();
        var fanCoordinator = new RecordingFanOverrideCoordinator();
        var coordinator = CreateCoordinator(power, fanProbe, fanCoordinator);

        await coordinator.RestoreAsync(Model, CancellationToken.None);

        Assert.Equal(0, fanCoordinator.ApplyCallCount);
    }

    private static GamingOptimisedRestoreCoordinator CreateCoordinator(
        RecordingPowerManagementService powerManagementService,
        RecordingFanCapabilityProbe fanCapabilityProbe,
        RecordingFanOverrideCoordinator fanOverrideCoordinator)
    {
        return new GamingOptimisedRestoreCoordinator(
            powerManagementService,
            fanCapabilityProbe,
            fanOverrideCoordinator);
    }

    private static PowerOperationResult SuccessfulPowerRestore()
    {
        var stateAfter = RestoredPowerState();

        return new PowerOperationResult(
            Operation: PowerOperationKind.RestoreOriginalSnapshot,
            IsSuccessful: true,
            TargetSchemeId: stateAfter.SchemeId,
            StateBefore: null,
            RequestedSettings: null,
            StateAfter: stateAfter,
            Verification: null,
            Rollback: null,
            FailureMessage: null);
    }

    private static PowerOperationResult FailedPowerRestore(string failureMessage)
    {
        return SuccessfulPowerRestore() with
        {
            IsSuccessful = false,
            FailureMessage = failureMessage
        };
    }

    private static PowerStateSnapshot RestoredPowerState()
    {
        return new PowerStateSnapshot(
            Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"),
            ProcessorMaximumAc: 100,
            ProcessorMaximumDc: 100,
            BoostModeAc: 2,
            BoostModeDc: 2,
            DateTimeOffset.Parse("2026-01-01T00:00:00+00:00"));
    }

    private static FanControlCapabilityResult ValidFanCapability(
        byte fan0Mode = 0,
        byte fan1Mode = 0)
    {
        return new FanControlCapabilityResult(
            IsReadSupported: true,
            IsHardwareSafetyGateSatisfied: true,
            Failures: [],
            Protocol: SmcTransportProtocol.Mmio,
            Snapshot: ValidFanSnapshot(fan0Mode, fan1Mode));
    }

    private static FanSmcSnapshot ValidFanSnapshot(
        byte fan0Mode,
        byte fan1Mode)
    {
        return new FanSmcSnapshot(
            UInt8("FNum", 2, 0x80),
            Float32("F0Mx", 5321.25f, 0x85),
            Float32("F1Mx", 4789.5f, 0x85),
            Float32("F0Ac", 1800f, 0x84),
            Float32("F1Ac", 1700f, 0x84),
            UInt8("F0Md", fan0Mode, 0xD0),
            UInt8("F1Md", fan1Mode, 0xD0),
            Float32("F0Tg", 1800f, 0xD4),
            Float32("F1Tg", 1700f, 0xD4));
    }

    private static SmcValue Float32(
        string key,
        float value,
        byte attributes)
    {
        Span<byte> rawData = stackalloc byte[sizeof(float)];
        BinaryPrimitives.WriteInt32LittleEndian(
            rawData,
            BitConverter.SingleToInt32Bits(value));

        return new SmcValue(
            new SmcKeyInfo(key, 4, "flt ", attributes),
            rawData);
    }

    private static SmcValue UInt8(
        string key,
        byte value,
        byte attributes)
    {
        return new SmcValue(
            new SmcKeyInfo(key, 1, "ui8 ", attributes),
            [value]);
    }

    private sealed class RecordingPowerManagementService : IPowerManagementService
    {
        private readonly List<string>? _events;

        public RecordingPowerManagementService(List<string>? events = null)
        {
            _events = events;
        }

        public PowerOperationResult RestoreResult { get; init; } = SuccessfulPowerRestore();

        public Exception? RestoreException { get; init; }

        public int ReadCurrentStateCallCount { get; private set; }

        public int ApplyProcessorSettingsCallCount { get; private set; }

        public int RestoreOriginalSettingsCallCount { get; private set; }

        public Task<PowerStateSnapshot> ReadCurrentStateAsync(CancellationToken cancellationToken)
        {
            ReadCurrentStateCallCount++;
            throw new InvalidOperationException("Restore orchestration must not read current power state.");
        }

        public Task<PowerOperationResult> ApplyProcessorSettingsAsync(
            ProcessorPowerSettings requestedSettings,
            CancellationToken cancellationToken)
        {
            ApplyProcessorSettingsCallCount++;
            throw new InvalidOperationException("Restore orchestration must not apply processor settings.");
        }

        public Task<PowerOperationResult> ApplyProcessorSettingsAsync(
            ProcessorPowerSettings requestedSettings,
            PowerStateSnapshot expectedStateBefore,
            CancellationToken cancellationToken)
        {
            ApplyProcessorSettingsCallCount++;
            throw new InvalidOperationException("Restore orchestration must not apply processor settings.");
        }

        public Task<PowerOperationResult> RestoreOriginalSettingsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RestoreOriginalSettingsCallCount++;
            _events?.Add("power-restore");

            if (RestoreException is not null)
            {
                throw RestoreException;
            }

            return Task.FromResult(RestoreResult);
        }
    }

    private sealed class RecordingFanCapabilityProbe : IFanCapabilityProbe
    {
        private readonly List<string>? _events;
        private readonly Queue<FanControlCapabilityResult> _results;

        public RecordingFanCapabilityProbe(
            List<string>? events = null,
            params FanControlCapabilityResult[] results)
        {
            _events = events;
            _results = new Queue<FanControlCapabilityResult>(
                results.Length == 0 ? [ValidFanCapability()] : results);
        }

        public Exception? ProbeException { get; init; }

        public int ProbeCallCount { get; private set; }

        public List<CancellationToken> ProbeTokens { get; } = [];

        public Task<FanControlCapabilityResult> ProbeAsync(
            string model,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProbeCallCount++;
            ProbeTokens.Add(cancellationToken);
            _events?.Add("fan-probe");

            if (ProbeException is not null)
            {
                throw ProbeException;
            }

            return Task.FromResult(
                _results.Count > 1
                    ? _results.Dequeue()
                    : _results.Peek());
        }
    }

    private sealed class RecordingFanOverrideCoordinator : IFanOverrideCoordinator
    {
        private readonly List<string>? _events;

        public RecordingFanOverrideCoordinator(List<string>? events = null)
        {
            _events = events;
        }

        public FanOverrideRecoveryDecision RecoveryResult { get; init; } =
            new(FanOverrideRecoveryAction.RestoreAppleAuto, "Apple Auto restored.");

        public Action? OnRecover { get; init; }

        public int ApplyCallCount { get; private set; }

        public int RecoverCallCount { get; private set; }

        public List<CancellationToken> RecoverTokens { get; } = [];

        public Task<FanOverrideExecutionResult> ApplyMaximumSafeRpmAsync(
            string model,
            FanControlCapabilityResult capability,
            CancellationToken cancellationToken)
        {
            ApplyCallCount++;
            throw new InvalidOperationException("Restore orchestration must never apply Maximum Safe RPM.");
        }

        public Task<FanOverrideRecoveryDecision> RecoverAsync(
            string currentModel,
            FanControlCapabilityResult capability,
            CancellationToken cancellationToken)
        {
            RecoverCallCount++;
            RecoverTokens.Add(cancellationToken);
            _events?.Add("fan-recover");
            OnRecover?.Invoke();
            return Task.FromResult(RecoveryResult);
        }
    }
}
