namespace BootCampPerformanceControl.FanControl;

internal sealed class FanOverrideRecoveryPolicy
{
    public FanOverrideRecoveryDecision Evaluate(
        string currentModel,
        FanOverrideOwnershipMarker marker,
        FanControlCapabilityResult capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentModel);
        ArgumentNullException.ThrowIfNull(marker);
        ArgumentNullException.ThrowIfNull(capability);

        if (!string.Equals(currentModel, marker.Model, StringComparison.Ordinal))
        {
            return Blocked("The ownership marker belongs to a different Mac model.");
        }

        if (!capability.IsReadSupported ||
            !capability.IsHardwareSafetyGateSatisfied ||
            capability.Snapshot is null)
        {
            return Blocked("Recovery is blocked because the hardware safety gate is not satisfied.");
        }

        if (capability.Family != marker.Family)
        {
            return Blocked(
                "Recovery is blocked because the live capability family differs from the ownership marker family.");
        }

        var snapshot = capability.Snapshot;
        if (snapshot.Fans.Count != marker.Targets.Count ||
            !snapshot.Fans.Select(fan => fan.Index)
                .SequenceEqual(marker.Targets.Select(target => target.Index)))
        {
            return Blocked(
                "Recovery is blocked because the current fan topology does not match the application ownership marker.");
        }

        if (!HasValidFamilySpecificMarkerState(marker))
        {
            return Blocked(
                "Recovery is blocked because the ownership marker contains invalid family-specific safety state.");
        }

        IFanCapabilityFamilyStrategy strategy;
        try
        {
            strategy = FanCapabilityFamilyStrategies.Get(marker.Family);
        }
        catch (InvalidOperationException)
        {
            return Blocked(
                "Recovery is blocked because the ownership marker does not identify a bounded writer family.");
        }

        if (strategy.IsAppleAuto(capability))
        {
            return new FanOverrideRecoveryDecision(
                FanOverrideRecoveryAction.None,
                "Every owned fan is already in Apple Auto. The stale ownership marker can be cleared.");
        }

        if (marker.IsTransactionJournal)
        {
            return IsRecognizedTransactionProgress(snapshot, marker)
                ? new FanOverrideRecoveryDecision(
                    FanOverrideRecoveryAction.RestoreAppleAuto,
                    "The live state is an exact deterministic prefix of the BCPC fan transaction journal. Apple Auto recovery is permitted.")
                : Blocked(
                    "Recovery is blocked because the live fan state is not a deterministic prefix of the BCPC transaction journal.");
        }

        FanMaximumSafeRpmPlan expectedPlan;
        try
        {
            expectedPlan = new FanMaximumSafeRpmPlan(
                marker.Model,
                marker.Family,
                marker.Targets.Select(target =>
                    new FanMaximumSafeRpmTarget(
                        target.Index,
                        target.ExpectedTargetRpm)
                    {
                        ExactTargetPayload = target.ExpectedTargetRawHex is null
                            ? ReadOnlyMemory<byte>.Empty
                            : Convert.FromHexString(target.ExpectedTargetRawHex)
                    }));
        }
        catch (FormatException)
        {
            return Blocked(
                "Recovery is blocked because the ownership marker contains malformed raw target state.");
        }

        if (!strategy.IsManualMaximum(capability, expectedPlan))
        {
            return Blocked(
                "Recovery is blocked because the current family-specific modes, targets, or maximum RPM values no longer match the application ownership marker.");
        }

        return new FanOverrideRecoveryDecision(
            FanOverrideRecoveryAction.RestoreAppleAuto,
            "The current manual/max state matches the application ownership marker. Apple Auto recovery is permitted.");
    }

    private static FanOverrideRecoveryDecision Blocked(string reason)
    {
        return new FanOverrideRecoveryDecision(
            FanOverrideRecoveryAction.Blocked,
            reason);
    }

    private static bool HasValidFamilySpecificMarkerState(
        FanOverrideOwnershipMarker marker)
    {
        if (marker.IsTransactionJournal)
        {
            return HasValidTransactionJournalState(marker);
        }

        if (marker.Family == FanCapabilityFamily.PerFanModeFloat32)
        {
            return marker.ExpectedGlobalModeMask is null &&
                marker.Targets.All(target => target.ExpectedTargetRawHex is null);
        }

        if (marker.Family != FanCapabilityFamily.GlobalMaskFpe2)
        {
            return false;
        }

        ushort expectedMask;
        try
        {
            expectedMask = GlobalMaskFpe2Strategy.GetManualMask(marker.Targets.Count);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        return marker.ExpectedGlobalModeMask == expectedMask &&
            marker.Targets.All(target =>
            {
                try
                {
                    if (target.ExpectedTargetRawHex is not { Length: 4 } rawHex)
                    {
                        return false;
                    }

                    var raw = Convert.FromHexString(rawHex);
                    var decodedRpm = ((raw[0] << 8) | raw[1]) / 4f;
                    return raw.Length == 2 && decodedRpm == target.ExpectedTargetRpm;
                }
                catch (Exception exception) when (
                    exception is FormatException or IndexOutOfRangeException)
                {
                    return false;
                }
            });
    }

    private static bool HasValidTransactionJournalState(
        FanOverrideOwnershipMarker marker)
    {
        var payloadLength = marker.Family == FanCapabilityFamily.GlobalMaskFpe2
            ? 2
            : 4;
        if (marker.BaselineTargets.Count != marker.Targets.Count ||
            !marker.BaselineTargets.Select(target => target.Index)
                .SequenceEqual(marker.Targets.Select(target => target.Index)) ||
            marker.Targets.Any(target =>
                !TryGetRaw(target.ExpectedTargetRawHex, payloadLength, out var raw) ||
                !ExpectedRawMatchesRpm(raw, target.ExpectedTargetRpm, marker.Family)) ||
            marker.BaselineTargets.Any(target =>
                !TryGetRaw(target.TargetRawHex, payloadLength, out _)))
        {
            return false;
        }

        if (marker.Family == FanCapabilityFamily.PerFanModeFloat32)
        {
            return marker.ExpectedGlobalModeMask is null &&
                marker.BaselineGlobalModeMask is null &&
                marker.BaselineTargets.All(target => target.Mode == 0);
        }

        if (marker.Family != FanCapabilityFamily.GlobalMaskFpe2)
        {
            return false;
        }

        try
        {
            return marker.ExpectedGlobalModeMask ==
                    GlobalMaskFpe2Strategy.GetManualMask(marker.Targets.Count) &&
                marker.BaselineGlobalModeMask == 0 &&
                marker.BaselineTargets.All(target => target.Mode is null);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsRecognizedTransactionProgress(
        Smc.FanSmcSnapshot snapshot,
        FanOverrideOwnershipMarker marker)
    {
        var expectedTargets = marker.Targets
            .Select(target => Convert.FromHexString(target.ExpectedTargetRawHex!))
            .ToArray();
        var baselineTargets = marker.BaselineTargets
            .Select(target => Convert.FromHexString(target.TargetRawHex))
            .ToArray();

        if (!snapshot.Fans.Select((fan, index) =>
                fan.Maximum.RawData.Span.SequenceEqual(expectedTargets[index]))
            .All(matches => matches))
        {
            return false;
        }

        if (marker.Family == FanCapabilityFamily.GlobalMaskFpe2)
        {
            return snapshot.GlobalMode.Value!.GetUInt16BigEndian() ==
                    marker.ExpectedGlobalModeMask &&
                TargetsMatchValidPrefix(snapshot, expectedTargets, baselineTargets);
        }

        var modes = snapshot.Fans.Select(fan => fan.Mode!.GetUInt8()).ToArray();
        if (modes.All(mode => mode == 1))
        {
            return TargetsMatchValidPrefix(snapshot, expectedTargets, baselineTargets);
        }

        var firstAuto = Array.IndexOf(modes, (byte)0);
        var isManualAcquisitionPrefix = firstAuto > 0 &&
            modes.Take(firstAuto).All(mode => mode == 1) &&
            modes.Skip(firstAuto).All(mode => mode == 0);
        return isManualAcquisitionPrefix &&
            snapshot.Fans.Select((fan, index) =>
                fan.Target.RawData.Span.SequenceEqual(baselineTargets[index]))
            .All(matches => matches);
    }

    private static bool TargetsMatchValidPrefix(
        Smc.FanSmcSnapshot snapshot,
        IReadOnlyList<byte[]> expectedTargets,
        IReadOnlyList<byte[]> baselineTargets)
    {
        for (var prefixLength = 0; prefixLength <= snapshot.Fans.Count; prefixLength++)
        {
            var matches = true;
            for (var index = 0; index < snapshot.Fans.Count; index++)
            {
                var required = index < prefixLength
                    ? expectedTargets[index]
                    : baselineTargets[index];
                if (!snapshot.Fans[index].Target.RawData.Span.SequenceEqual(required))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ExpectedRawMatchesRpm(
        byte[] raw,
        float expectedRpm,
        FanCapabilityFamily family)
    {
        var decoded = family == FanCapabilityFamily.GlobalMaskFpe2
            ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(raw) / 4f
            : BitConverter.Int32BitsToSingle(
                System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(raw));
        return float.IsFinite(decoded) && decoded == expectedRpm;
    }

    private static bool TryGetRaw(
        string? rawHex,
        int expectedLength,
        out byte[] raw)
    {
        raw = Array.Empty<byte>();
        if (rawHex is null || rawHex.Length != expectedLength * 2)
        {
            return false;
        }

        try
        {
            raw = Convert.FromHexString(rawHex);
            return raw.Length == expectedLength;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
