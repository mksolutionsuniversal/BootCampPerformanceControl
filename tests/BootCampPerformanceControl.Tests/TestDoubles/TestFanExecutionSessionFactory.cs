using System.Buffers.Binary;
using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc;

namespace BootCampPerformanceControl.Tests.TestDoubles;

internal sealed class TestFanExecutionSessionFactory : IFanExecutionSessionFactory
{
    private readonly IFanOverrideOwnershipStore? _ownershipStore;

    public TestFanExecutionSessionFactory(IFanOverrideOwnershipStore? ownershipStore = null)
    {
        _ownershipStore = ownershipStore;
    }

    public Func<Task<IFanExecutionSession>>? OpenSessionHandler { get; init; }
    public int OpenCallCount { get; private set; }

    public Task<IFanExecutionSession> OpenAsync(CancellationToken cancellationToken)
    {
        OpenCallCount++;
        if (OpenSessionHandler is not null)
        {
            return OpenSessionHandler();
        }

        return Task.FromResult<IFanExecutionSession>(new TestFanExecutionSession(
            overrideCoordinator: new TestFanOverrideCoordinator(_ownershipStore)));
    }
}

internal sealed class TestFanExecutionSession : IFanExecutionSession
{
    public TestFanExecutionSession(
        IFanCapabilityProbe? capabilityProbe = null,
        IFanOverrideCoordinator? overrideCoordinator = null)
    {
        CapabilityProbe = capabilityProbe ?? new TestFanCapabilityProbe();
        OverrideCoordinator = overrideCoordinator ?? new TestFanOverrideCoordinator();
    }

    public IFanCapabilityProbe CapabilityProbe { get; }
    public IFanOverrideCoordinator OverrideCoordinator { get; }

    public Func<ValueTask>? DisposeHandler { get; init; }
    public int DisposeCallCount { get; private set; }

    public ValueTask DisposeAsync()
    {
        DisposeCallCount++;
        return DisposeHandler?.Invoke() ?? ValueTask.CompletedTask;
    }
}

internal sealed class TestFanCapabilityProbe : IFanCapabilityProbe
{
    public Func<string, CancellationToken, Task<FanControlCapabilityResult>>? Handler { get; init; }
    public int ProbeCallCount { get; private set; }

    public Task<FanControlCapabilityResult> ProbeAsync(string model, CancellationToken cancellationToken)
    {
        ProbeCallCount++;
        if (Handler is not null)
        {
            return Handler(model, cancellationToken);
        }

        return Task.FromResult(new FanControlCapabilityResult(
            IsReadSupported: true,
            IsHardwareSafetyGateSatisfied: true,
            Failures: [],
            SmcTransportProtocol.Mmio,
            Snapshot: CreateTestFanSnapshot()));
    }

    public static FanSmcSnapshot CreateTestFanSnapshot()
    {
        return new FanSmcSnapshot(
            new SmcValue(new SmcKeyInfo("FNum", 1, "ui8 ", 0x80), [2]),
            [
                new FanSmcChannelSnapshot(
                    new FanIndex(0),
                    CreateFloat32Value("F0Mx", 5321.25f),
                    CreateFloat32Value("F0Ac", 1800f),
                    new SmcValue(new SmcKeyInfo("F0Md", 1, "ui8 ", 0xD0), [0]),
                    CreateFloat32Value("F0Tg", 1800f)),
                new FanSmcChannelSnapshot(
                    new FanIndex(1),
                    CreateFloat32Value("F1Mx", 4789.5f),
                    CreateFloat32Value("F1Ac", 1700f),
                    new SmcValue(new SmcKeyInfo("F1Md", 1, "ui8 ", 0xD0), [0]),
                    CreateFloat32Value("F1Tg", 1700f))
            ]);
    }

    public static SmcValue CreateFloat32Value(string key, float value)
    {
        Span<byte> rawData = stackalloc byte[sizeof(float)];
        BinaryPrimitives.WriteInt32LittleEndian(
            rawData,
            BitConverter.SingleToInt32Bits(value));

        return new SmcValue(
            new SmcKeyInfo(key, 4, "flt ", 0x85),
            rawData);
    }
}

internal sealed class TestFanOverrideCoordinator : IFanOverrideCoordinator
{
    private readonly IFanOverrideOwnershipStore? _ownershipStore;

    public TestFanOverrideCoordinator(IFanOverrideOwnershipStore? ownershipStore = null)
    {
        _ownershipStore = ownershipStore;
    }

    public Func<string, FanControlCapabilityResult, CancellationToken, Task<FanOverrideExecutionResult>>? ApplyHandler { get; init; }
    public Func<string, FanControlCapabilityResult, CancellationToken, Task<FanOverrideRecoveryDecision>>? RecoverHandler { get; init; }

    public async Task<FanOverrideExecutionResult> ApplyMaximumSafeRpmAsync(
        string model,
        FanControlCapabilityResult freshCapability,
        CancellationToken cancellationToken)
    {
        if (ApplyHandler is not null)
        {
            return await ApplyHandler(model, freshCapability, cancellationToken);
        }

        var marker = new FanOverrideOwnershipMarker(
            model,
            5321.25f,
            4789.5f,
            DateTimeOffset.UtcNow);

        if (_ownershipStore is not null)
        {
            await _ownershipStore.SaveNewAsync(marker, cancellationToken);
        }

        return FanOverrideExecutionResult.Applied(marker);
    }

    public async Task<FanOverrideRecoveryDecision> RecoverAsync(
        string model,
        FanControlCapabilityResult freshCapability,
        CancellationToken cancellationToken)
    {
        if (RecoverHandler is not null)
        {
            return await RecoverHandler(model, freshCapability, cancellationToken);
        }

        if (_ownershipStore is not null)
        {
            await _ownershipStore.ClearAsync(cancellationToken);
        }

        return new FanOverrideRecoveryDecision(
            FanOverrideRecoveryAction.RestoreAppleAuto,
            "Restored Apple Auto.");
    }
}
