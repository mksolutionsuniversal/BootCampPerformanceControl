namespace BootCampPerformanceControl.PowerManagement;

public enum PowerOperationKind
{
    ApplyProcessorSettings,
    RestoreOriginalSnapshot
}

public sealed record PowerOperationResult(
    PowerOperationKind Operation,
    bool IsSuccessful,
    Guid? TargetSchemeId,
    PowerStateSnapshot? StateBefore,
    ProcessorPowerSettings? RequestedSettings,
    PowerStateSnapshot? StateAfter,
    PowerStateVerification? Verification,
    PowerRollbackResult? Rollback,
    string? FailureMessage);

public sealed record PowerRollbackResult(
    bool IsSuccessful,
    PowerStateSnapshot? ActiveStateAfterRollback,
    PowerStateVerification? ActiveStateVerification,
    PowerStateVerification? TargetSchemeVerification,
    string? FailureMessage);
