using System.Buffers.Binary;
using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc;

namespace BootCampPerformanceControl.Tests.TestDoubles;

internal sealed class TestFanExecutionSessionFactory : IFanExecutionSessionFactory
{
    public Func<Task<IFanExecutionSession>>? OpenSessionHandler { get; init; }
    public int OpenCallCount { get; private set; }

    public Task<IFanExecutionSession> OpenAsync(CancellationToken cancellationToken)
    {
        OpenCallCount++;
        if (OpenSessionHandler is not null)
        {
            return OpenSessionHandler();
        }

        return Task.FromResult<IFanExecutionSession>(new TestFanExecutionSession());
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
            CreateFloat32Value("F0Mx", 5321.25f),
            CreateFloat32Value("F1Mx", 4789.5f),
            CreateFloat32Value("F0Ac", 1800f),
            CreateFloat32Value("F1Ac", 1700f),
            new SmcValue(new SmcKeyInfo("F0Md", 1, "ui8 ", 0xD0), [0]),
            new SmcValue(new SmcKeyInfo("F1Md", 1, "ui8 ", 0xD0), [0]),
            CreateFloat32Value("F0Tg", 1800f),
            CreateFloat32Value("F1Tg", 1700f));
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
    public Func<string, FanControlCapabilityResult, CancellationToken, Task<FanOverrideExecutionResult>>? ApplyHandler { get; init; }
    public Func<string, FanControlCapabilityResult, CancellationToken, Task<FanOverrideRecoveryDecision>>? RecoverHandler { get; init; }

    public Task<FanOverrideExecutionResult> ApplyMaximumSafeRpmAsync(
        string model,
        FanControlCapabilityResult freshCapability,
        CancellationToken cancellationToken)
    {
        if (ApplyHandler is not null)
        {
            return ApplyHandler(model, freshCapability, cancellationToken);
        }

        var marker = new FanOverrideOwnershipMarker(
            model,
            5321.25f,
            4789.5f,
            DateTimeOffset.UtcNow);

        return Task.FromResult(FanOverrideExecutionResult.Applied(marker));
    }

    public Task<FanOverrideRecoveryDecision> RecoverAsync(
        string model,
        FanControlCapabilityResult freshCapability,
        CancellationToken cancellationToken)
    {
        if (RecoverHandler is not null)
        {
            return RecoverHandler(model, freshCapability, cancellationToken);
        }

        return Task.FromResult(new FanOverrideRecoveryDecision(
            FanOverrideRecoveryAction.RestoreAppleAuto,
            "Restored Apple Auto."));
    }
}
