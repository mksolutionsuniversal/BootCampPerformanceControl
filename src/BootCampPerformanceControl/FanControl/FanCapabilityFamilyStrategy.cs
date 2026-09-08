namespace BootCampPerformanceControl.FanControl;

internal interface IFanCapabilityFamilyStrategy
{
    FanCapabilityFamily Family { get; }

    FanMaximumSafeRpmPlan CreateMaximumSafeRpmPlan(
        string model,
        FanControlCapabilityResult capability);

    FanOperatingMode GetFanMode(
        FanControlCapabilityResult capability,
        FanIndex fan);

    bool IsAppleAuto(FanControlCapabilityResult capability);

    bool IsManualMaximum(
        FanControlCapabilityResult capability,
        FanMaximumSafeRpmPlan plan);

    Task WriteMaximumSafeStateAsync(
        IFanSmcWriteBackend backend,
        FanMaximumSafeRpmPlan plan,
        Func<ushort, CancellationToken, Task> verifyGlobalMaskAsync,
        CancellationToken cancellationToken);

    Task WriteAppleAutoAsync(
        IFanSmcWriteBackend backend,
        IReadOnlyList<FanIndex> fanIndexes,
        CancellationToken cancellationToken);
}

internal static class FanCapabilityFamilyStrategies
{
    private static readonly IFanCapabilityFamilyStrategy PerFan =
        new PerFanModeFloat32Strategy();
    private static readonly IFanCapabilityFamilyStrategy Global =
        new GlobalMaskFpe2Strategy();

    public static IFanCapabilityFamilyStrategy Get(FanCapabilityFamily family)
    {
        return family switch
        {
            FanCapabilityFamily.PerFanModeFloat32 => PerFan,
            FanCapabilityFamily.GlobalMaskFpe2 => Global,
            _ => throw new InvalidOperationException(
                $"Capability family '{family}' has no bounded write strategy.")
        };
    }
}

internal sealed class PerFanModeFloat32Strategy : IFanCapabilityFamilyStrategy
{
    private const float RpmComparisonTolerance = 1f;

    public FanCapabilityFamily Family => FanCapabilityFamily.PerFanModeFloat32;

    public FanMaximumSafeRpmPlan CreateMaximumSafeRpmPlan(
        string model,
        FanControlCapabilityResult capability)
    {
        EnsureFamily(capability);
        return new FanMaximumSafeRpmPlan(
            model,
            Family,
            capability.Snapshot!.Fans.Select(fan => new FanMaximumSafeRpmTarget(
                fan.Index,
                fan.Maximum.GetFloat32())
            {
                ExactTargetPayload = fan.Maximum.RawData.ToArray(),
                BaselineTargetPayload = fan.Target.RawData.ToArray(),
                BaselineMode = fan.Mode!.GetUInt8()
            }));
    }

    public FanOperatingMode GetFanMode(
        FanControlCapabilityResult capability,
        FanIndex fan)
    {
        EnsureFamily(capability);
        return capability.Snapshot!.Fans[fan.Value].Mode!.GetUInt8() switch
        {
            0 => FanOperatingMode.AppleAuto,
            1 => FanOperatingMode.Manual,
            _ => FanOperatingMode.Unknown
        };
    }

    public bool IsAppleAuto(FanControlCapabilityResult capability)
    {
        return IsUsable(capability) &&
            capability.Snapshot!.Fans.Count > 0 &&
            capability.Snapshot.Fans.All(fan => fan.Mode!.GetUInt8() == 0);
    }

    public bool IsManualMaximum(
        FanControlCapabilityResult capability,
        FanMaximumSafeRpmPlan plan)
    {
        return IsUsable(capability) &&
            TopologyMatches(capability, plan) &&
            capability.Snapshot!.Fans.Zip(plan.Targets).All(pair =>
                pair.First.Mode!.GetUInt8() == 1 &&
                ApproximatelyEqual(pair.First.Target.GetFloat32(), pair.Second.TargetRpm) &&
                ApproximatelyEqual(pair.First.Maximum.GetFloat32(), pair.Second.TargetRpm));
    }

    public async Task WriteMaximumSafeStateAsync(
        IFanSmcWriteBackend backend,
        FanMaximumSafeRpmPlan plan,
        Func<ushort, CancellationToken, Task> verifyGlobalMaskAsync,
        CancellationToken cancellationToken)
    {
        foreach (var target in plan.Targets)
        {
            await backend.SetManualModeAsync(target.Index, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var target in plan.Targets)
        {
            await backend.SetTargetRpmAsync(
                    target.Index,
                    target.TargetRpm,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var target in plan.Targets)
        {
            await backend.SetManualModeAsync(target.Index, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task WriteAppleAutoAsync(
        IFanSmcWriteBackend backend,
        IReadOnlyList<FanIndex> fanIndexes,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;
        foreach (var fan in fanIndexes)
        {
            try
            {
                await backend.SetAppleAutoAsync(fan, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = failure is null
                    ? exception
                    : new AggregateException(failure, exception);
            }
        }

        if (failure is not null)
        {
            throw failure;
        }
    }

    private void EnsureFamily(FanControlCapabilityResult capability)
    {
        if (!IsUsable(capability))
        {
            throw new InvalidOperationException(
                "PerFanModeFloat32 operations require a verified live capability snapshot.");
        }
    }

    private bool IsUsable(FanControlCapabilityResult capability)
    {
        return capability.Family == Family &&
            capability.IsReadSupported &&
            capability.IsHardwareSafetyGateSatisfied &&
            capability.Snapshot is not null;
    }

    private bool TopologyMatches(
        FanControlCapabilityResult capability,
        FanMaximumSafeRpmPlan plan)
    {
        return plan.Family == Family &&
            capability.Snapshot!.Fans.Select(fan => fan.Index)
                .SequenceEqual(plan.Targets.Select(target => target.Index));
    }

    private static bool ApproximatelyEqual(float left, float right)
    {
        return float.IsFinite(left) &&
            float.IsFinite(right) &&
            MathF.Abs(left - right) <= RpmComparisonTolerance;
    }
}

internal sealed class GlobalMaskFpe2Strategy : IFanCapabilityFamilyStrategy
{
    public FanCapabilityFamily Family => FanCapabilityFamily.GlobalMaskFpe2;

    public FanMaximumSafeRpmPlan CreateMaximumSafeRpmPlan(
        string model,
        FanControlCapabilityResult capability)
    {
        EnsureFamily(capability);
        return new FanMaximumSafeRpmPlan(
            model,
            Family,
            capability.Snapshot!.Fans.Select(fan =>
                new FanMaximumSafeRpmTarget(
                    fan.Index,
                    fan.Maximum.GetFpe2())
                {
                    ExactTargetPayload = fan.Maximum.RawData.ToArray(),
                    BaselineTargetPayload = fan.Target.RawData.ToArray()
                }),
            capability.Snapshot.GlobalMode.Value!.GetUInt16BigEndian());
    }

    public FanOperatingMode GetFanMode(
        FanControlCapabilityResult capability,
        FanIndex fan)
    {
        EnsureReadableFamily(capability);
        if (capability.Snapshot!.Fans.Count > 2)
        {
            return FanOperatingMode.Unknown;
        }

        var mask = capability.Snapshot!.GlobalMode.Value!.GetUInt16BigEndian();
        return (mask & (1 << fan.Value)) == 0
            ? FanOperatingMode.AppleAuto
            : FanOperatingMode.Manual;
    }

    public bool IsAppleAuto(FanControlCapabilityResult capability)
    {
        return IsUsable(capability) &&
            capability.Snapshot!.Fans.Count > 0 &&
            capability.Snapshot.GlobalMode.Value!.GetUInt16BigEndian() == 0;
    }

    public bool IsManualMaximum(
        FanControlCapabilityResult capability,
        FanMaximumSafeRpmPlan plan)
    {
        if (!IsUsable(capability) || !TopologyMatches(capability, plan))
        {
            return false;
        }

        var expectedMask = GetManualMask(plan.Targets.Count);
        if (capability.Snapshot!.GlobalMode.Value!.GetUInt16BigEndian() != expectedMask)
        {
            return false;
        }

        return capability.Snapshot.Fans.Zip(plan.Targets).All(pair =>
            pair.Second.ExactTargetPayload.Length == 2 &&
            pair.First.Target.RawData.Span.SequenceEqual(pair.Second.ExactTargetPayload.Span) &&
            pair.First.Maximum.RawData.Span.SequenceEqual(pair.Second.ExactTargetPayload.Span));
    }

    public async Task WriteMaximumSafeStateAsync(
        IFanSmcWriteBackend backend,
        FanMaximumSafeRpmPlan plan,
        Func<ushort, CancellationToken, Task> verifyGlobalMaskAsync,
        CancellationToken cancellationToken)
    {
        var mask = GetManualMask(plan.Targets.Count);
        await backend.SetGlobalManualMaskAsync(mask, cancellationToken)
            .ConfigureAwait(false);
        await verifyGlobalMaskAsync(mask, cancellationToken)
            .ConfigureAwait(false);

        foreach (var target in plan.Targets)
        {
            if (target.ExactTargetPayload.Length != 2)
            {
                throw new InvalidOperationException(
                    "GlobalMaskFpe2 requires an exact two-byte fresh maximum payload.");
            }

            await backend.SetFpe2TargetPayloadAsync(
                    target.Index,
                    target.ExactTargetPayload,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public Task WriteAppleAutoAsync(
        IFanSmcWriteBackend backend,
        IReadOnlyList<FanIndex> fanIndexes,
        CancellationToken cancellationToken)
    {
        _ = GetManualMask(fanIndexes.Count);
        return backend.SetGlobalAppleAutoAsync(cancellationToken);
    }

    internal static ushort GetManualMask(int fanCount)
    {
        return fanCount switch
        {
            1 => 0x0001,
            2 => 0x0003,
            _ => throw new InvalidOperationException(
                $"GlobalMaskFpe2 write capability is not verified for {fanCount} fans.")
        };
    }

    private void EnsureFamily(FanControlCapabilityResult capability)
    {
        if (!IsUsable(capability))
        {
            throw new InvalidOperationException(
                "GlobalMaskFpe2 operations require a verified live capability snapshot and proven mask topology.");
        }
    }

    private void EnsureReadableFamily(FanControlCapabilityResult capability)
    {
        if (capability.Family != Family ||
            !capability.IsReadSupported ||
            capability.Snapshot is null)
        {
            throw new InvalidOperationException(
                "GlobalMaskFpe2 state requires a recognized readable capability snapshot.");
        }
    }

    private bool IsUsable(FanControlCapabilityResult capability)
    {
        return capability.Family == Family &&
            capability.IsReadSupported &&
            capability.IsHardwareSafetyGateSatisfied &&
            capability.Snapshot is not null;
    }

    private bool TopologyMatches(
        FanControlCapabilityResult capability,
        FanMaximumSafeRpmPlan plan)
    {
        return plan.Family == Family &&
            capability.Snapshot!.Fans.Select(fan => fan.Index)
                .SequenceEqual(plan.Targets.Select(target => target.Index));
    }
}
