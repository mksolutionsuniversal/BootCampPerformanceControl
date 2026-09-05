using BootCampPerformanceControl.FanControl.Smc;
namespace BootCampPerformanceControl.FanControl;

internal sealed class FanSafetyPolicy
{
    // Conservative model-neutral corruption guard; not an Apple specification or write target.
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

        var readFailures = new List<string>();

        if (protocol != SmcTransportProtocol.Mmio)
        {
            readFailures.Add(
                $"Unexpected SMC transport protocol '{protocol}' ({(int)protocol}); MMIO (1) is required.");
        }

        if (!TryDecodeFanCount(snapshot.FanCount, out var fanCount, out var countFailure))
        {
            readFailures.Add(countFailure);
        }
        else
        {
            ValidateTopology(snapshot, fanCount, readFailures);
        }

        var writeFailures = new List<string>();
        if (readFailures.Count == 0)
        {
            ValidateFamilyWriteGate(snapshot, writeFailures);
        }

        return new FanControlCapabilityResult(
            IsReadSupported: readFailures.Count == 0,
            IsHardwareSafetyGateSatisfied: readFailures.Count == 0 && writeFailures.Count == 0,
            [.. readFailures, .. writeFailures],
            protocol,
            snapshot);
    }

    public FanControlCapabilityResult EvaluateIdentity(
        string model,
        SmcTransportProtocol? protocol = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (protocol.HasValue && protocol.Value != SmcTransportProtocol.Mmio)
        {
            return FanControlCapabilityResult.Rejected(
                protocol,
                $"Unexpected SMC transport protocol '{protocol.Value}' ({(int)protocol.Value}); MMIO (1) is required.");
        }

        return new FanControlCapabilityResult(
            IsReadSupported: false,
            IsHardwareSafetyGateSatisfied: false,
            Array.Empty<string>(),
            protocol,
            Snapshot: null);
    }

    public bool TryDecodeFanCount(
        SmcValue value,
        out int fanCount,
        out string failure)
    {
        ArgumentNullException.ThrowIfNull(value);

        var failures = new List<string>();
        ValidateMetadata(value, "FNum", 1, "ui8 ", 0x80, failures);
        if (failures.Count > 0)
        {
            fanCount = 0;
            failure = failures[0];
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

    private static void ValidateTopology(
        FanSmcSnapshot snapshot,
        int fanCount,
        ICollection<string> failures)
    {
        if (snapshot.Fans.Count != fanCount)
        {
            failures.Add(
                $"Fan topology mismatch. FNum reported {fanCount}, but {snapshot.Fans.Count} fan channels were captured.");
            return;
        }

        for (var value = 0; value < fanCount; value++)
        {
            var fan = snapshot.Fans[value];
            var expectedIndex = new FanIndex(value);
            if (fan.Index != expectedIndex)
            {
                failures.Add(
                    $"Fan topology index mismatch at position {value}; observed index {fan.Index.Value}.");
                continue;
            }

            ValidateMetadata(fan.Maximum, expectedIndex.GetSmcKey("Mx"), 4, "flt ", 0x85, failures);
            ValidateMetadata(fan.Actual, expectedIndex.GetSmcKey("Ac"), 4, "flt ", 0x84, failures);
            ValidateMetadata(fan.Mode, expectedIndex.GetSmcKey("Md"), 1, "ui8 ", 0xD0, failures);
            ValidateMetadata(fan.Target, expectedIndex.GetSmcKey("Tg"), 4, "flt ", 0xD4, failures);

            if (failures.Count == 0)
            {
                ValidateRuntimeValues(fan, failures);
            }
        }
    }

    private static void ValidateRuntimeValues(
        FanSmcChannelSnapshot fan,
        ICollection<string> failures)
    {
        var maximumKey = fan.Index.GetSmcKey("Mx");
        var maximum = fan.Maximum.GetFloat32();
        if (!float.IsFinite(maximum) || maximum <= 0f || maximum > MaximumReportedFanRpm)
        {
            failures.Add(
                $"SMC key '{maximumKey}' reported invalid maximum RPM {maximum}; expected a finite value greater than 0 and no greater than {MaximumReportedFanRpm}.");
            return;
        }

        ValidateRuntimeRpm(fan.Index.GetSmcKey("Ac"), fan.Actual.GetFloat32(), maximum, failures);
        ValidateRuntimeRpm(fan.Index.GetSmcKey("Tg"), fan.Target.GetFloat32(), maximum, failures);
        ValidateMode(fan.Index.GetSmcKey("Md"), fan.Mode.GetUInt8(), failures);
    }

    private static void ValidateFamilyWriteGate(
        FanSmcSnapshot snapshot,
        ICollection<string> failures)
    {
        if (snapshot.Fans.Count == 0)
        {
            failures.Add(
                "The verified T2 SMC write family requires at least one discovered fan; FNum reported a passive topology.");
        }
    }

    private static void ValidateMetadata(
        SmcValue value,
        string expectedKey,
        byte expectedLength,
        string expectedType,
        byte expectedAttributes,
        ICollection<string> failures)
    {
        if (!string.Equals(value.Info.Key, expectedKey, StringComparison.Ordinal) ||
            value.Info.Length != expectedLength ||
            !string.Equals(value.Info.Type, expectedType, StringComparison.Ordinal) ||
            value.Info.Attributes != expectedAttributes)
        {
            failures.Add(
                $"SMC key '{expectedKey}' metadata mismatch. " +
                $"Observed key='{value.Info.Key}', len={value.Info.Length}, type='{value.Info.Type}', attrs=0x{value.Info.Attributes:X2}; " +
                $"expected len={expectedLength}, type='{expectedType}', attrs=0x{expectedAttributes:X2}.");
        }
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

    private static void ValidateMode(
        string key,
        byte value,
        ICollection<string> failures)
    {
        if (value is not 0 and not 1)
        {
            failures.Add($"SMC key '{key}' reported unsupported mode value {value}; expected 0 or 1.");
        }
    }

}
