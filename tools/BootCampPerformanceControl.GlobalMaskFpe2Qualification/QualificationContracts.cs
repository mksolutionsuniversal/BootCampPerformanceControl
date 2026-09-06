using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc;
using BootCampPerformanceControl.FanControl.Smc.Windows;

namespace BootCampPerformanceControl.GlobalMaskFpe2Qualification;

internal sealed record QualificationOptions(bool IsExecuteArmed)
{
    public const string ConfirmationToken = "GLOBALMASK-FPE2-PHYSICAL-WRITE";

    public static QualificationOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return new QualificationOptions(
            args.Count == 3 &&
            string.Equals(args[0], "--execute", StringComparison.Ordinal) &&
            string.Equals(args[1], "--confirm", StringComparison.Ordinal) &&
            string.Equals(args[2], ConfirmationToken, StringComparison.Ordinal));
    }
}

internal sealed record QualificationMachine(
    string Manufacturer,
    string Model,
    bool IsAppleIntel,
    string WindowsVersion,
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset CapturedAtLocal);

internal sealed record QualificationProcessConflict(string ProcessName, int ProcessId);

internal enum QualificationOutcome
{
    DryRun,
    Refused,
    Pass,
    Fail
}

internal sealed record QualificationRunResult(
    QualificationOutcome Outcome,
    string Message,
    Exception? PrimaryFailure = null,
    Exception? RollbackFailure = null)
{
    public bool IsPass => Outcome == QualificationOutcome.Pass;
}

internal interface IQualificationOutput
{
    void WriteLine(string message = "");
}

internal interface IGlobalMaskQualificationSession : IAsyncDisposable
{
    AppleSmcServiceState GetServiceState();

    Task<FanControlCapabilityResult> ProbeAsync(
        string model,
        CancellationToken cancellationToken);

    Task<SmcValue> ReadKeyAsync(
        string key,
        CancellationToken cancellationToken);

    Task WriteKeyAsync(
        string key,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken);
}

internal sealed record QualificationWriteAttempt(
    int Number,
    string Key,
    string RawHex,
    CancellationToken CancellationToken);

internal sealed class QualificationWriteLedger
{
    private readonly IGlobalMaskQualificationSession _session;
    private readonly IQualificationOutput _output;
    private readonly List<QualificationWriteAttempt> _attempts = [];

    public QualificationWriteLedger(
        IGlobalMaskQualificationSession session,
        IQualificationOutput output)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    public IReadOnlyList<QualificationWriteAttempt> Attempts => _attempts;

    public async Task WriteAsync(
        string key,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        ValidatePermittedWrite(key, payload.Span);

        var attempt = new QualificationWriteAttempt(
            _attempts.Count + 1,
            key,
            Convert.ToHexString(payload.Span),
            cancellationToken);
        _attempts.Add(attempt);
        _output.WriteLine($"WRITE #{attempt.Number}: {attempt.Key} {attempt.RawHex}");

        await _session.WriteKeyAsync(key, payload, cancellationToken)
            .ConfigureAwait(false);
    }

    public void PrintSummary()
    {
        _output.WriteLine();
        _output.WriteLine($"SMC writes issued: {_attempts.Count}");
        _output.WriteLine($"Total SMC write attempts: {_attempts.Count}");
        _output.WriteLine("Keys written:");
        if (_attempts.Count == 0)
        {
            _output.WriteLine("(none)");
            return;
        }

        foreach (var attempt in _attempts)
        {
            _output.WriteLine($"{attempt.Number}: {attempt.Key} {attempt.RawHex}");
        }
    }

    private static void ValidatePermittedWrite(string key, ReadOnlySpan<byte> payload)
    {
        if (string.Equals(key, "FS! ", StringComparison.Ordinal))
        {
            if (payload.SequenceEqual(new byte[] { 0x00, 0x00 }) ||
                payload.SequenceEqual(new byte[] { 0x00, 0x01 }))
            {
                return;
            }

            throw new InvalidOperationException(
                "The qualification tool permits only FS! payload 0000 or 0001.");
        }

        if (string.Equals(key, "F0Tg", StringComparison.Ordinal) && payload.Length == 2)
        {
            return;
        }

        throw new InvalidOperationException(
            $"The qualification tool forbids SMC write key '{key}'.");
    }
}
