using BootCampPerformanceControl.FanControl.Smc;

namespace BootCampPerformanceControl.FanControl;

internal sealed class FanSafetyPolicy
{
    private const float MaximumReportedFanRpm = 10000f;
    private const float RuntimeRpmOvershootAllowance = 250f;
    private const int MaximumRepresentableFanCount = FanIndex.MaximumRepresentableValue + 1;

    public FanControlCapabilityResult Evaluate(
        string model,
        SmcTransportProtocol protocol,
        FanSmcSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(snapshot);

        var writeFailures = new List<string>();
        if (protocol != SmcTransportProtocol.Mmio)
        {
            writeFailures.Add(
                $"Write capability not verified for SMC transport protocol '{protocol}' ({(int)protocol}); MMIO (1) is required for writes.");
        }

        if (!TryDecodeFanCount(snapshot.FanCount, out var fanCount, out var countFailure))
        {
            return new FanControlCapabilityResult(
                false,
                false,
                [.. writeFailures, countFailure],
                protocol,
                snapshot,
                FanCapabilityFamily.Unknown);
        }

        var readFailures = new List<string>();
        if (!ValidateTopology(snapshot, fanCount, readFailures))
        {
            return new FanControlCapabilityResult(
                false,
                false,
                [.. writeFailures, .. readFailures],
                protocol,
                snapshot,
                FanCapabilityFamily.Unknown);
        }

        if (fanCount == 0)
        {
            return new FanControlCapabilityResult(
                true,
                false,
                [
                    .. writeFailures,
                    "No controllable fans reported by AppleSMC (passive/fanless topology)."
                ],
                protocol,
                snapshot,
                FanCapabilityFamily.Passive);
        }

        var family = Classify(snapshot);
        if (family == FanCapabilityFamily.Unknown)
        {
            return new FanControlCapabilityResult(
                false,
                false,
                [.. writeFailures, CreateClassifierFailure(snapshot)],
                protocol,
                snapshot,
                family);
        }

        ValidateRuntimeValues(snapshot, family, readFailures);
        var runtimeValuesValid = readFailures.Count == 0;
        if (family == FanCapabilityFamily.GlobalMaskFpe2 && fanCount > 2)
        {
            writeFailures.Add(
                $"Write capability not verified for this topology: GlobalMaskFpe2 mask semantics are proven only for fan indexes 0 and 1; FNum reported {fanCount}.");
        }

        return new FanControlCapabilityResult(
            runtimeValuesValid,
            runtimeValuesValid && writeFailures.Count == 0,
            [.. writeFailures, .. readFailures],
            protocol,
            snapshot,
            family);
    }

    public FanControlCapabilityResult EvaluateIdentity(string model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return new FanControlCapabilityResult(
            false,
            false,
            Array.Empty<string>(),
            null,
            null,
            FanCapabilityFamily.Unknown);
    }

    public bool TryDecodeFanCount(
        SmcValue value,
        out int fanCount,
        out string failure)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!Matches(value, "FNum", 1, "ui8 ", 0x80))
        {
            fanCount = 0;
            failure = MetadataMismatch(value, "FNum", 1, "ui8 ", 0x80);
            return false;
        }

        fanCount = value.GetUInt8();
        if (fanCount > MaximumRepresentableFanCount)
        {
            failure =
                $"SMC key 'FNum' reported {fanCount} fans, but four-character fan keys can safely represent at most {MaximumRepresentableFanCount}.";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private static bool ValidateTopology(
        FanSmcSnapshot snapshot,
        int fanCount,
        ICollection<string> failures)
    {
        if (snapshot.Fans.Count != fanCount)
        {
            failures.Add(
                $"Fan topology mismatch. FNum reported {fanCount}, but {snapshot.Fans.Count} fan channels were captured.");
            return false;
        }

        for (var value = 0; value < fanCount; value++)
        {
            if (snapshot.Fans[value].Index != new FanIndex(value))
            {
                failures.Add(
                    $"Fan topology index mismatch at position {value}; observed index {snapshot.Fans[value].Index.Value}.");
            }
        }

        return failures.Count == 0;
    }

    private static FanCapabilityFamily Classify(FanSmcSnapshot snapshot)
    {
        var perFan = snapshot.GlobalMode.State == SmcKeyObservationState.ConfirmedAbsent;
        var global = snapshot.GlobalMode.Value is { } globalMode &&
            Matches(globalMode, "FS! ", 2, "ui16", 0xC0);

        foreach (var fan in snapshot.Fans)
        {
            var index = fan.Index;
            perFan &=
                Matches(fan.MaximumObservation.Value, index.GetSmcKey("Mx"), 4, "flt ", 0x85) &&
                Matches(fan.ActualObservation.Value, index.GetSmcKey("Ac"), 4, "flt ", 0x84) &&
                Matches(fan.TargetObservation.Value, index.GetSmcKey("Tg"), 4, "flt ", 0xD4) &&
                Matches(fan.ModeObservation.Value, index.GetSmcKey("Md"), 1, "ui8 ", 0xD0);

            global &=
                Matches(fan.Minimum.Value, index.GetSmcKey("Mn"), 2, "fpe2", 0xC0) &&
                Matches(fan.MaximumObservation.Value, index.GetSmcKey("Mx"), 2, "fpe2", 0xC0) &&
                Matches(fan.ActualObservation.Value, index.GetSmcKey("Ac"), 2, "fpe2", 0x90) &&
                Matches(fan.TargetObservation.Value, index.GetSmcKey("Tg"), 2, "fpe2", 0xD0) &&
                fan.ModeObservation.State == SmcKeyObservationState.ConfirmedAbsent;
        }

        if (perFan)
        {
            return FanCapabilityFamily.PerFanModeFloat32;
        }

        return global
            ? FanCapabilityFamily.GlobalMaskFpe2
            : FanCapabilityFamily.Unknown;
    }

    private static void ValidateRuntimeValues(
        FanSmcSnapshot snapshot,
        FanCapabilityFamily family,
        ICollection<string> failures)
    {
        foreach (var fan in snapshot.Fans)
        {
            var maximum = DecodeRpm(fan.Maximum, family);
            if (!float.IsFinite(maximum) || maximum <= 0f || maximum > MaximumReportedFanRpm)
            {
                failures.Add(
                    $"SMC key '{fan.Index.GetSmcKey("Mx")}' reported invalid maximum RPM {maximum}; expected a finite value greater than 0 and no greater than {MaximumReportedFanRpm}.");
                continue;
            }

            ValidateRuntimeRpm(fan.Index.GetSmcKey("Ac"), DecodeRpm(fan.Actual, family), maximum, failures);
            ValidateRuntimeRpm(fan.Index.GetSmcKey("Tg"), DecodeRpm(fan.Target, family), maximum, failures);

            if (family == FanCapabilityFamily.PerFanModeFloat32)
            {
                var mode = fan.Mode!.GetUInt8();
                if (mode is not 0 and not 1)
                {
                    failures.Add(
                        $"SMC key '{fan.Index.GetSmcKey("Md")}' reported unsupported mode value {mode}; expected 0 or 1.");
                }
            }
            else
            {
                var minimum = fan.Minimum.Value!.GetFpe2();
                if (minimum < 0f || minimum > maximum)
                {
                    failures.Add(
                        $"SMC key '{fan.Index.GetSmcKey("Mn")}' reported implausible minimum RPM {minimum}; expected 0..{maximum}.");
                }
            }
        }

        if (family == FanCapabilityFamily.GlobalMaskFpe2)
        {
            var mask = snapshot.GlobalMode.Value!.GetUInt16BigEndian();
            var provenMask = snapshot.Fans.Count switch
            {
                1 => 0x0001,
                2 => 0x0003,
                _ => (int?)null
            };

            if (provenMask.HasValue && (mask & ~provenMask.Value) != 0)
            {
                failures.Add(
                    $"SMC key 'FS! ' reported unsupported manual mask 0x{mask:X4}; observed state is outside the proven mask range.");
            }
        }
    }

    internal static float DecodeRpm(SmcValue value, FanCapabilityFamily family)
    {
        return family switch
        {
            FanCapabilityFamily.PerFanModeFloat32 => value.GetFloat32(),
            FanCapabilityFamily.GlobalMaskFpe2 => value.GetFpe2(),
            _ => throw new InvalidOperationException(
                $"Capability family '{family}' does not define an RPM decoder.")
        };
    }

    private static bool Matches(
        SmcValue? value,
        string key,
        byte length,
        string type,
        byte attributes)
    {
        return value is not null &&
            string.Equals(value.Info.Key, key, StringComparison.Ordinal) &&
            value.Info.Length == length &&
            string.Equals(value.Info.Type, type, StringComparison.Ordinal) &&
            value.Info.Attributes == attributes &&
            value.RawData.Length == length;
    }

    private static string MetadataMismatch(
        SmcValue value,
        string expectedKey,
        byte expectedLength,
        string expectedType,
        byte expectedAttributes)
    {
        return $"SMC key '{expectedKey}' metadata mismatch. " +
            $"Observed key='{value.Info.Key}', len={value.Info.Length}, type='{value.Info.Type}', attrs=0x{value.Info.Attributes:X2}; " +
            $"expected len={expectedLength}, type='{expectedType}', attrs=0x{expectedAttributes:X2}.";
    }

    private static string CreateClassifierFailure(FanSmcSnapshot snapshot)
    {
        var observed = snapshot.Fans
            .SelectMany(fan => fan.Observations)
            .Append(snapshot.GlobalMode)
            .Select(observation =>
            {
                if (observation.Value is { } value)
                {
                    var info = value.Info;
                    return $"{info.Key}[available,type='{info.Type}',len={info.Length},attrs=0x{info.Attributes:X2}]";
                }

                return observation.State == SmcKeyObservationState.ConfirmedAbsent
                    ? $"{observation.Key}[absent]"
                    : $"{observation.Key}[read failed: {observation.Failure}]";
            });

        return "Write capability not verified: SMC metadata mismatch; the live fan fingerprint does not match a bounded writer family. Observed: "
            + string.Join(", ", observed) + ".";
    }

    private static void ValidateRuntimeRpm(
        string key,
        float value,
        float maximum,
        ICollection<string> failures)
    {
        if (!float.IsFinite(value) || value < 0f || value > maximum + RuntimeRpmOvershootAllowance)
        {
            failures.Add(
                $"SMC key '{key}' reported implausible RPM {value}; expected 0..{maximum + RuntimeRpmOvershootAllowance}.");
        }
    }
}
