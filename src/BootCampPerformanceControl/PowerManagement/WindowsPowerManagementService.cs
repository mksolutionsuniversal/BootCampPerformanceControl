using BootCampPerformanceControl.Logging;
using BootCampPerformanceControl.SettingsBackup;

namespace BootCampPerformanceControl.PowerManagement;

public sealed class WindowsPowerManagementService : IPowerManagementService
{
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly IPowerProfileApi _powerApi;
    private readonly IRestoreSnapshotStore _restoreSnapshotStore;
    private readonly IApplicationLogger _logger;

    public WindowsPowerManagementService(
        IRestoreSnapshotStore restoreSnapshotStore,
        IApplicationLogger logger)
        : this(new NativePowerProfileApi(), restoreSnapshotStore, logger)
    {
    }

    internal WindowsPowerManagementService(
        IPowerProfileApi powerApi,
        IRestoreSnapshotStore restoreSnapshotStore,
        IApplicationLogger logger)
    {
        _powerApi = powerApi;
        _restoreSnapshotStore = restoreSnapshotStore;
        _logger = logger;
    }

    public Task<PowerStateSnapshot> ReadCurrentStateAsync(CancellationToken cancellationToken)
    {
        return Task.Run(ReadCurrentState, cancellationToken);
    }

    public Task<PowerOperationResult> ApplyProcessorSettingsAsync(
        ProcessorPowerSettings requestedSettings,
        CancellationToken cancellationToken)
    {
        return ApplyProcessorSettingsSerializedAsync(
            requestedSettings,
            expectedStateBefore: null,
            cancellationToken);
    }

    public Task<PowerOperationResult> ApplyProcessorSettingsAsync(
        ProcessorPowerSettings requestedSettings,
        PowerStateSnapshot expectedStateBefore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedStateBefore);

        return ApplyProcessorSettingsSerializedAsync(
            requestedSettings,
            expectedStateBefore,
            cancellationToken);
    }

    private async Task<PowerOperationResult> ApplyProcessorSettingsSerializedAsync(
        ProcessorPowerSettings requestedSettings,
        PowerStateSnapshot? expectedStateBefore,
        CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ApplyProcessorSettingsCoreAsync(
                requestedSettings,
                expectedStateBefore,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<PowerOperationResult> ApplyProcessorSettingsCoreAsync(
        ProcessorPowerSettings requestedSettings,
        PowerStateSnapshot? expectedStateBefore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestedSettings);

        var operation = PowerOperationKind.ApplyProcessorSettings;
        var validation = ProcessorPowerSettingsValidator.Validate(requestedSettings);

        if (!validation.IsValid)
        {
            _logger.Info($"Apply processor settings rejected by validation. {validation.ErrorMessage}");
            return CreatePreflightFailure(operation, requestedSettings, validation.ErrorMessage);
        }

        cancellationToken.ThrowIfCancellationRequested();

        PowerStateSnapshot stateBefore;
        try
        {
            stateBefore = await ReadCurrentStateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error("Apply processor settings failed while reading the initial state.", exception);
            return CreatePreflightFailure(operation, requestedSettings, exception.Message);
        }

        if (expectedStateBefore is not null)
        {
            var preconditionVerification = PowerStateVerification.Compare(
                expectedStateBefore.SchemeId,
                ProcessorPowerSettings.FromSnapshot(expectedStateBefore),
                stateBefore);

            if (!preconditionVerification.IsSuccessful)
            {
                const string failureMessage =
                    "Apply expected-state precondition failed; no restore snapshot or power setting was changed.";
                _logger.Info(
                    $"{failureMessage} Expected: {FormatState(expectedStateBefore)}. "
                    + $"Actual: {FormatState(stateBefore)}.");
                return new PowerOperationResult(
                    operation,
                    IsSuccessful: false,
                    stateBefore.SchemeId,
                    stateBefore,
                    requestedSettings,
                    StateAfter: null,
                    preconditionVerification,
                    Rollback: null,
                    failureMessage);
            }
        }

        _logger.Info(
            $"Apply processor settings attempt. Before: {FormatState(stateBefore)}. "
            + $"Requested: {FormatSettings(requestedSettings)}.");

        try
        {
            var snapshotSaved = await _restoreSnapshotStore.TrySaveOriginalRestoreSnapshotAsync(
                stateBefore,
                cancellationToken).ConfigureAwait(false);
            _logger.Info(snapshotSaved
                ? "Original restore snapshot saved before the first power write."
                : "Existing original restore snapshot retained before the power write.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error(
                "Apply processor settings aborted because the original restore snapshot could not be ensured.",
                exception);
            return CreatePreflightFailure(
                operation,
                requestedSettings,
                $"The original restore snapshot could not be ensured: {exception.Message}",
                stateBefore.SchemeId,
                stateBefore);
        }

        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(
            () => ExecuteChange(
                operation,
                stateBefore.SchemeId,
                requestedSettings,
                stateBefore,
                stateBefore),
            CancellationToken.None).ConfigureAwait(false);
    }

    public async Task<PowerOperationResult> RestoreOriginalSettingsAsync(
        CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RestoreOriginalSettingsCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<PowerOperationResult> RestoreOriginalSettingsCoreAsync(
        CancellationToken cancellationToken)
    {
        const PowerOperationKind operation = PowerOperationKind.RestoreOriginalSnapshot;
        cancellationToken.ThrowIfCancellationRequested();

        PowerStateSnapshot? originalSnapshot;
        try
        {
            originalSnapshot = await _restoreSnapshotStore.GetOriginalRestoreSnapshotAsync(
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error("Restore failed while loading the original restore snapshot.", exception);
            return CreatePreflightFailure(operation, requestedSettings: null, exception.Message);
        }

        if (originalSnapshot is null)
        {
            const string message = "No original restore snapshot is available.";
            _logger.Info($"Restore rejected. {message}");
            return CreatePreflightFailure(operation, requestedSettings: null, message);
        }

        var requestedSettings = ProcessorPowerSettings.FromSnapshot(originalSnapshot);

        try
        {
            await _restoreSnapshotStore.TrySaveOriginalRestoreSnapshotAsync(
                originalSnapshot,
                cancellationToken).ConfigureAwait(false);
            _logger.Info("Restore persistent-backup integrity preflight completed successfully.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error(
                "Restore preflight failed because the persistent original backup could not be verified.",
                exception);
            return CreatePreflightFailure(
                operation,
                requestedSettings,
                $"Persistent restore-backup integrity check failed: {exception.Message}",
                originalSnapshot.SchemeId);
        }

        var validation = ProcessorPowerSettingsValidator.Validate(requestedSettings);
        if (!validation.IsValid)
        {
            _logger.Info($"Restore rejected because the original snapshot is invalid. {validation.ErrorMessage}");
            return CreatePreflightFailure(
                operation,
                requestedSettings,
                validation.ErrorMessage,
                originalSnapshot.SchemeId);
        }

        PowerStateSnapshot stateBefore;
        PowerStateSnapshot targetStateBefore;
        try
        {
            stateBefore = await ReadCurrentStateAsync(cancellationToken).ConfigureAwait(false);
            targetStateBefore = stateBefore.SchemeId == originalSnapshot.SchemeId
                ? stateBefore
                : await ReadSchemeStateAsync(originalSnapshot.SchemeId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error("Restore failed while reading the state before the operation.", exception);
            return CreatePreflightFailure(
                operation,
                requestedSettings,
                exception.Message,
                originalSnapshot.SchemeId);
        }

        _logger.Info(
            $"Restore attempt. Original snapshot captured at {originalSnapshot.CapturedAt:O}. "
            + $"Before: {FormatState(stateBefore)}. Target SchemeId={originalSnapshot.SchemeId}; "
            + $"Requested: {FormatSettings(requestedSettings)}.");

        cancellationToken.ThrowIfCancellationRequested();

        var restoreResult = await Task.Run(
            () => ExecuteChange(
                operation,
                originalSnapshot.SchemeId,
                requestedSettings,
                stateBefore,
                targetStateBefore),
            CancellationToken.None).ConfigureAwait(false);

        if (!restoreResult.IsSuccessful || restoreResult.Verification?.IsSuccessful != true)
        {
            return restoreResult;
        }

        try
        {
            await _restoreSnapshotStore.ClearOriginalRestoreSnapshotAsync(
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Error(
                "Power settings were restored and verified, but restore-session cleanup failed.",
                exception);
            return restoreResult with
            {
                IsSuccessful = false,
                Rollback = null,
                FailureMessage = "Power settings were restored and verified, but restore-session cleanup failed: "
                    + exception.Message
            };
        }

        _logger.Info("Original restore snapshot cleared; restore session closed successfully.");
        return restoreResult;
    }

    private PowerOperationResult ExecuteChange(
        PowerOperationKind operation,
        Guid targetSchemeId,
        ProcessorPowerSettings requestedSettings,
        PowerStateSnapshot activeStateBefore,
        PowerStateSnapshot targetStateBefore)
    {
        PowerStateSnapshot? stateAfter = null;

        try
        {
            WriteProcessorSettings(operation.ToString(), targetSchemeId, requestedSettings);
            ActivateScheme(operation.ToString(), targetSchemeId);

            stateAfter = ReadCurrentState();
            _logger.Info($"{operation} read-back. {FormatState(stateAfter)}.");

            var verification = PowerStateVerification.Compare(
                targetSchemeId,
                requestedSettings,
                stateAfter);
            _logger.Info($"{operation} verification success: {verification.IsSuccessful}.");

            if (verification.IsSuccessful)
            {
                _logger.Info($"{operation} completed successfully.");
                return new PowerOperationResult(
                    operation,
                    IsSuccessful: true,
                    targetSchemeId,
                    activeStateBefore,
                    requestedSettings,
                    stateAfter,
                    verification,
                    Rollback: null,
                    FailureMessage: null);
            }

            var rollback = AttemptRollback(activeStateBefore, targetStateBefore);
            return new PowerOperationResult(
                operation,
                IsSuccessful: false,
                targetSchemeId,
                activeStateBefore,
                requestedSettings,
                stateAfter,
                verification,
                rollback,
                "Power-setting read-back verification failed.");
        }
        catch (Exception exception)
        {
            _logger.Error($"{operation} native operation failed; rollback will be attempted.", exception);
            var rollback = AttemptRollback(activeStateBefore, targetStateBefore);
            var verification = PowerStateVerification.Compare(
                targetSchemeId,
                requestedSettings,
                stateAfter);

            return new PowerOperationResult(
                operation,
                IsSuccessful: false,
                targetSchemeId,
                activeStateBefore,
                requestedSettings,
                stateAfter,
                verification,
                rollback,
                exception.Message);
        }
    }

    private PowerRollbackResult AttemptRollback(
        PowerStateSnapshot activeStateBefore,
        PowerStateSnapshot targetStateBefore)
    {
        _logger.Info(
            $"Rollback attempt. Active state to restore: {FormatState(activeStateBefore)}. "
            + $"Target scheme state to restore: {FormatState(targetStateBefore)}.");

        var errors = new List<string>();

        TryWriteProcessorSettingsForRollback(
            "Rollback target scheme",
            targetStateBefore.SchemeId,
            ProcessorPowerSettings.FromSnapshot(targetStateBefore),
            errors);

        if (activeStateBefore.SchemeId != targetStateBefore.SchemeId)
        {
            TryWriteProcessorSettingsForRollback(
                "Rollback active scheme",
                activeStateBefore.SchemeId,
                ProcessorPowerSettings.FromSnapshot(activeStateBefore),
                errors);
        }

        TryRollbackStep(
            () => ActivateScheme("Rollback", activeStateBefore.SchemeId),
            "Rollback failed while reactivating the previous scheme.",
            errors);

        PowerStateSnapshot? activeStateAfter = null;
        PowerStateSnapshot? targetStateAfter = null;

        TryRollbackStep(
            () => activeStateAfter = ReadCurrentState(),
            "Rollback failed while reading back the active state.",
            errors);

        if (activeStateBefore.SchemeId == targetStateBefore.SchemeId)
        {
            targetStateAfter = activeStateAfter;
        }
        else
        {
            TryRollbackStep(
                () => targetStateAfter = ReadSchemeState(targetStateBefore.SchemeId),
                "Rollback failed while reading back the target scheme state.",
                errors);
        }

        var activeVerification = PowerStateVerification.Compare(
            activeStateBefore.SchemeId,
            ProcessorPowerSettings.FromSnapshot(activeStateBefore),
            activeStateAfter);
        var targetVerification = PowerStateVerification.Compare(
            targetStateBefore.SchemeId,
            ProcessorPowerSettings.FromSnapshot(targetStateBefore),
            targetStateAfter);
        var succeeded = errors.Count == 0
            && activeVerification.IsSuccessful
            && targetVerification.IsSuccessful;

        if (!activeVerification.IsSuccessful)
        {
            errors.Add("The active state did not match the pre-operation state after rollback.");
        }

        if (!targetVerification.IsSuccessful)
        {
            errors.Add("The target scheme did not match its pre-operation state after rollback.");
        }

        var failureMessage = errors.Count == 0 ? null : string.Join(" ", errors);
        _logger.Info(
            $"Rollback result. Success: {succeeded}. "
            + $"Read-back: {(activeStateAfter is null ? "unavailable" : FormatState(activeStateAfter))}. "
            + $"Details: {failureMessage ?? "verification passed"}.");

        return new PowerRollbackResult(
            succeeded,
            activeStateAfter,
            activeVerification,
            targetVerification,
            failureMessage);
    }

    private void TryWriteProcessorSettingsForRollback(
        string operationName,
        Guid schemeId,
        ProcessorPowerSettings settings,
        ICollection<string> errors)
    {
        var subgroup = PowerSettingGuids.ProcessorSettingsSubgroup;
        var processorMaximum = PowerSettingGuids.ProcessorMaximumThrottle;
        var boostMode = PowerSettingGuids.ProcessorPerformanceBoostMode;

        TryRollbackStep(
            () =>
            {
                LogWriteAttempt(operationName, schemeId, "ProcessorMaximumAc", settings.ProcessorMaximumAc);
                _powerApi.WriteAcValueIndex(
                    schemeId,
                    subgroup,
                    processorMaximum,
                    settings.ProcessorMaximumAc);
            },
            $"{operationName} failed for ProcessorMaximumAc.",
            errors);

        TryRollbackStep(
            () =>
            {
                LogWriteAttempt(operationName, schemeId, "ProcessorMaximumDc", settings.ProcessorMaximumDc);
                _powerApi.WriteDcValueIndex(
                    schemeId,
                    subgroup,
                    processorMaximum,
                    settings.ProcessorMaximumDc);
            },
            $"{operationName} failed for ProcessorMaximumDc.",
            errors);

        TryRollbackStep(
            () =>
            {
                LogWriteAttempt(operationName, schemeId, "BoostModeAc", settings.BoostModeAc);
                _powerApi.WriteAcValueIndex(
                    schemeId,
                    subgroup,
                    boostMode,
                    settings.BoostModeAc);
            },
            $"{operationName} failed for BoostModeAc.",
            errors);

        TryRollbackStep(
            () =>
            {
                LogWriteAttempt(operationName, schemeId, "BoostModeDc", settings.BoostModeDc);
                _powerApi.WriteDcValueIndex(
                    schemeId,
                    subgroup,
                    boostMode,
                    settings.BoostModeDc);
            },
            $"{operationName} failed for BoostModeDc.",
            errors);
    }

    private void TryRollbackStep(Action action, string failureMessage, ICollection<string> errors)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            errors.Add($"{failureMessage} {exception.Message}");
            _logger.Error(failureMessage, exception);
        }
    }

    private void WriteProcessorSettings(
        string operationName,
        Guid schemeId,
        ProcessorPowerSettings settings)
    {
        var subgroup = PowerSettingGuids.ProcessorSettingsSubgroup;
        var processorMaximum = PowerSettingGuids.ProcessorMaximumThrottle;
        var boostMode = PowerSettingGuids.ProcessorPerformanceBoostMode;

        LogWriteAttempt(operationName, schemeId, "ProcessorMaximumAc", settings.ProcessorMaximumAc);
        _powerApi.WriteAcValueIndex(schemeId, subgroup, processorMaximum, settings.ProcessorMaximumAc);

        LogWriteAttempt(operationName, schemeId, "ProcessorMaximumDc", settings.ProcessorMaximumDc);
        _powerApi.WriteDcValueIndex(schemeId, subgroup, processorMaximum, settings.ProcessorMaximumDc);

        LogWriteAttempt(operationName, schemeId, "BoostModeAc", settings.BoostModeAc);
        _powerApi.WriteAcValueIndex(schemeId, subgroup, boostMode, settings.BoostModeAc);

        LogWriteAttempt(operationName, schemeId, "BoostModeDc", settings.BoostModeDc);
        _powerApi.WriteDcValueIndex(schemeId, subgroup, boostMode, settings.BoostModeDc);
    }

    private void ActivateScheme(string operationName, Guid schemeId)
    {
        _logger.Info($"{operationName}: PowerSetActiveScheme attempt. SchemeId={schemeId}.");
        _powerApi.SetActiveScheme(schemeId);
    }

    private void LogWriteAttempt(string operationName, Guid schemeId, string settingName, uint value)
    {
        _logger.Info(
            $"{operationName}: native power write attempt. SchemeId={schemeId}; "
            + $"Setting={settingName}; Value={value}.");
    }

    private Task<PowerStateSnapshot> ReadSchemeStateAsync(
        Guid schemeId,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => ReadSchemeState(schemeId), cancellationToken);
    }

    private PowerStateSnapshot ReadCurrentState()
    {
        return ReadSchemeState(_powerApi.GetActiveScheme());
    }

    private PowerStateSnapshot ReadSchemeState(Guid schemeId)
    {
        var subgroup = PowerSettingGuids.ProcessorSettingsSubgroup;
        var processorMaximum = PowerSettingGuids.ProcessorMaximumThrottle;
        var boostMode = PowerSettingGuids.ProcessorPerformanceBoostMode;

        return new PowerStateSnapshot(
            schemeId,
            _powerApi.ReadAcValueIndex(schemeId, subgroup, processorMaximum),
            _powerApi.ReadDcValueIndex(schemeId, subgroup, processorMaximum),
            _powerApi.ReadAcValueIndex(schemeId, subgroup, boostMode),
            _powerApi.ReadDcValueIndex(schemeId, subgroup, boostMode),
            DateTimeOffset.UtcNow);
    }

    private static PowerOperationResult CreatePreflightFailure(
        PowerOperationKind operation,
        ProcessorPowerSettings? requestedSettings,
        string failureMessage,
        Guid? targetSchemeId = null,
        PowerStateSnapshot? stateBefore = null)
    {
        return new PowerOperationResult(
            operation,
            IsSuccessful: false,
            targetSchemeId,
            stateBefore,
            requestedSettings,
            StateAfter: null,
            Verification: null,
            Rollback: null,
            failureMessage);
    }

    private static string FormatState(PowerStateSnapshot state)
    {
        return $"SchemeId={state.SchemeId}; {FormatSettings(ProcessorPowerSettings.FromSnapshot(state))}";
    }

    private static string FormatSettings(ProcessorPowerSettings settings)
    {
        return $"ProcessorMaximumAc={settings.ProcessorMaximumAc}; "
            + $"ProcessorMaximumDc={settings.ProcessorMaximumDc}; "
            + $"BoostModeAc={settings.BoostModeAc}; BoostModeDc={settings.BoostModeDc}";
    }
}
