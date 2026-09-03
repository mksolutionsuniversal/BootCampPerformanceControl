using BootCampPerformanceControl.FanControl;

namespace BootCampPerformanceControl.Profiles;

internal sealed class FanExecutionSessionCleanupException : Exception
{
    public FanExecutionSessionCleanupException(
        string message,
        Exception operationException,
        Exception cleanupException)
        : base(message, new AggregateException(operationException, cleanupException))
    {
        OperationException = operationException
            ?? throw new ArgumentNullException(nameof(operationException));
        CleanupException = cleanupException
            ?? throw new ArgumentNullException(nameof(cleanupException));
    }

    public FanExecutionSessionCleanupException(
        string message,
        string operationFailureReason,
        FanOverrideRecoveryDecision? recoveryDecision,
        Exception cleanupException)
        : base(message, cleanupException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationFailureReason);

        OperationFailureReason = operationFailureReason;
        RecoveryDecision = recoveryDecision;
        CleanupException = cleanupException
            ?? throw new ArgumentNullException(nameof(cleanupException));
    }

    public Exception? OperationException { get; }

    public string? OperationFailureReason { get; }

    public FanOverrideRecoveryDecision? RecoveryDecision { get; }

    public Exception CleanupException { get; }
}
