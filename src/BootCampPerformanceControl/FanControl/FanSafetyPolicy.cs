using BootCampPerformanceControl.FanControl.Smc;
using BootCampPerformanceControl.HardwareDetection;

namespace BootCampPerformanceControl.FanControl;

internal sealed class FanSafetyPolicy
{
    private const float Fan0MaximumMinimumRpm = 5000f;
    private const float Fan0MaximumMaximumRpm = 6200f;
    private const float Fan1MaximumMinimumRpm = 4600f;
    private const float Fan1MaximumMaximumRpm = 5800f;
    private const float RuntimeRpmOvershootAllowance = 250f;

    public FanControlCapabilityResult Evaluate(
        string model,
        SmcTransportProtocol protocol,
        FanSmcSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(snapshot);

        var failures = new List<string>();

        if (!string.Equals(model, VerifiedHardwareModels.MacBookPro16_1, StringComparison.Ordinal))
        {
            failures.Add(
                $"Fan control is not verified for model '{model}'. Expected '{VerifiedHardwareModels.MacBookPro16_1}'.");
        }

        if (protocol != SmcTransportProtocol.Mmio)
        {
            failures.Add($"Unexpected SMC transport protocol '{protocol}' ({(int)protocol}); MMIO (1) is required.");
        }

        ValidateMetadata(snapshot.FanCount, "FNum", 1, "ui8 ", 0x80, failures);
        ValidateMetadata(snapshot.Fan0Maximum, "F0Mx", 4, "flt ", 0x85, failures);
        ValidateMetadata(snapshot.Fan1Maximum, "F1Mx", 4, "flt ", 0x85, failures);
        ValidateMetadata(snapshot.Fan0Actual, "F0Ac", 4, "flt ", 0x84, failures);
        ValidateMetadata(snapshot.Fan1Actual, "F1Ac", 4, "flt ", 0x84, failures);
        ValidateMetadata(snapshot.Fan0Mode, "F0Md", 1, "ui8 ", 0xD0, failures);
        ValidateMetadata(snapshot.Fan1Mode, "F1Md", 1, "ui8 ", 0xD0, failures);
        ValidateMetadata(snapshot.Fan0Target, "F0Tg", 4, "flt ", 0xD4, failures);
        ValidateMetadata(snapshot.Fan1Target, "F1Tg", 4, "flt ", 0xD4, failures);

        if (failures.Count == 0)
        {
            ValidateDecodedValues(snapshot, failures);
        }

        var compatible = failures.Count == 0;
        return new FanControlCapabilityResult(
            IsReadSupported: compatible,
            IsHardwareVerifiedForFutureWrite: compatible,
            failures,
            protocol,
            snapshot);
    }

    public FanControlCapabilityResult EvaluateIdentity(
        string model,
        SmcTransportProtocol? protocol = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!string.Equals(model, VerifiedHardwareModels.MacBookPro16_1, StringComparison.Ordinal))
        {
            return FanControlCapabilityResult.Rejected(
                protocol,
                $"Fan control is not verified for model '{model}'. Expected '{VerifiedHardwareModels.MacBookPro16_1}'.");
        }

        if (protocol.HasValue && protocol.Value != SmcTransportProtocol.Mmio)
        {
            return FanControlCapabilityResult.Rejected(
                protocol,
                $"Unexpected SMC transport protocol '{protocol.Value}' ({(int)protocol.Value}); MMIO (1) is required.");
        }

        return new FanControlCapabilityResult(
            IsReadSupported: false,
            IsHardwareVerifiedForFutureWrite: false,
            Array.Empty<string>(),
            protocol,
            Snapshot: null);
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

    private static void ValidateDecodedValues(
        FanSmcSnapshot snapshot,
        ICollection<string> failures)
    {
        if (snapshot.FanCount.GetUInt8() != 2)
        {
            failures.Add($"Expected exactly 2 fans, observed {snapshot.FanCount.GetUInt8()}.");
        }

        var fan0Maximum = snapshot.Fan0Maximum.GetFloat32();
        var fan1Maximum = snapshot.Fan1Maximum.GetFloat32();

        // These are conservative compatibility envelopes around the values verified on
        // MacBookPro16,1 hardware. They are safety gates, not claimed Apple specifications.
        ValidateRange("F0Mx", fan0Maximum, Fan0MaximumMinimumRpm, Fan0MaximumMaximumRpm, failures);
        ValidateRange("F1Mx", fan1Maximum, Fan1MaximumMinimumRpm, Fan1MaximumMaximumRpm, failures);

        ValidateRuntimeRpm("F0Ac", snapshot.Fan0Actual.GetFloat32(), fan0Maximum, failures);
        ValidateRuntimeRpm("F1Ac", snapshot.Fan1Actual.GetFloat32(), fan1Maximum, failures);
        ValidateRuntimeRpm("F0Tg", snapshot.Fan0Target.GetFloat32(), fan0Maximum, failures);
        ValidateRuntimeRpm("F1Tg", snapshot.Fan1Target.GetFloat32(), fan1Maximum, failures);

        ValidateMode("F0Md", snapshot.Fan0Mode.GetUInt8(), failures);
        ValidateMode("F1Md", snapshot.Fan1Mode.GetUInt8(), failures);
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

    private static void ValidateRange(
        string key,
        float value,
        float minimum,
        float maximum,
        ICollection<string> failures)
    {
        if (!float.IsFinite(value) || value < minimum || value > maximum)
        {
            failures.Add(
                $"SMC key '{key}' reported value {value}; verified compatibility range is {minimum}..{maximum} RPM.");
        }
    }
}
