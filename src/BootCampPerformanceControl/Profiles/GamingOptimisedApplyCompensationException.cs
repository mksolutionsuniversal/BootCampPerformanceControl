using BootCampPerformanceControl.FanControl;

namespace BootCampPerformanceControl.Profiles;

internal sealed class GamingOptimisedApplyCompensationException : Exception
{
    public GamingOptimisedApplyCompensationException(
        string message,
        Exception? operationException,
        FanOverrideRecoveryDecision? recoveryDecision,
        Exception? compensationException = null)
        : base(
            message,
            CreateInnerException(operationException, compensationException))
    {
        OperationException = operationException;
        RecoveryDecision = recoveryDecision;
        CompensationException = compensationException;
    }

    public Exception? OperationException { get; }

    public FanOverrideRecoveryDecision? RecoveryDecision { get; }

    public Exception? CompensationException { get; }

    private static Exception? CreateInnerException(
        Exception? operationException,
        Exception? compensationException)
    {
        if (operationException is not null && compensationException is not null)
        {
            return new AggregateException(operationException, compensationException);
        }

        return operationException ?? compensationException;
    }
}
