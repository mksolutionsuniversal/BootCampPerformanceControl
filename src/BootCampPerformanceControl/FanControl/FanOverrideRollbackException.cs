namespace BootCampPerformanceControl.FanControl;

internal sealed class FanOverrideRollbackException : Exception
{
    public FanOverrideRollbackException(
        Exception operationException,
        Exception rollbackException)
        : base(
            "Fan override failed and emergency Apple Auto rollback could not be verified.",
            new AggregateException(operationException, rollbackException))
    {
        OperationException = operationException
            ?? throw new ArgumentNullException(nameof(operationException));
        RollbackException = rollbackException
            ?? throw new ArgumentNullException(nameof(rollbackException));
    }

    public Exception OperationException { get; }

    public Exception RollbackException { get; }
}
