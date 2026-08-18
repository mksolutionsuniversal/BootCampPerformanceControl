using BootCampPerformanceControl.Logging;

namespace BootCampPerformanceControl.FanControl;

internal sealed class FanOverrideCoordinator
{
    private readonly FanOverridePreflightPolicy _preflightPolicy;
    private readonly FanOverrideRecoveryPolicy _recoveryPolicy;
    private readonly IFanOverrideOwnershipStore _ownershipStore;
    private readonly IFanOverrideWriter _writer;
    private readonly IApplicationLogger _logger;
    private readonly TimeProvider _timeProvider;

    public FanOverrideCoordinator(
        FanOverridePreflightPolicy preflightPolicy,
        FanOverrideRecoveryPolicy recoveryPolicy,
        IFanOverrideOwnershipStore ownershipStore,
        IFanOverrideWriter writer,
        IApplicationLogger logger,
        TimeProvider? timeProvider = null)
    {
        _preflightPolicy = preflightPolicy ?? throw new ArgumentNullException(nameof(preflightPolicy));
        _recoveryPolicy = recoveryPolicy ?? throw new ArgumentNullException(nameof(recoveryPolicy));
        _ownershipStore = ownershipStore ?? throw new ArgumentNullException(nameof(ownershipStore));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<FanOverrideExecutionResult> ApplyMaximumSafeRpmAsync(
        string model,
        FanControlCapabilityResult capability,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(capability);

        var preparation = _preflightPolicy.PrepareMaximumSafeRpm(model, capability);
        if (!preparation.IsAllowed || preparation.Plan is null)
        {
            return FanOverrideExecutionResult.Blocked(
                preparation.FailureReason ?? "Fan override preflight was blocked.");
        }

        var existingMarker = await _ownershipStore
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existingMarker is not null)
        {
            return FanOverrideExecutionResult.Blocked(
                "Fan override is blocked because an ownership marker already exists and must be recovered first.");
        }

        var marker = FanOverrideOwnershipMarker.FromPlan(
            preparation.Plan,
            _timeProvider.GetUtcNow());

        // Persist ownership before the first possible hardware write. If the process
        // terminates during a partial override, the next run still has recovery context.
        await _ownershipStore
            .SaveNewAsync(marker, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await _writer
                .ApplyMaximumSafeRpmAsync(preparation.Plan, cancellationToken)
                .ConfigureAwait(false);

            _logger.Info(
                $"Fan override writer completed for model {model}. Ownership marker remains active until Apple Auto is restored.");
            return FanOverrideExecutionResult.Applied(marker);
        }
        catch (OperationCanceledException)
        {
            _logger.Info(
                "Fan override writer was canceled after ownership was persisted. The ownership marker was retained for recovery.");
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error(
                "Fan override writer failed after ownership was persisted. The ownership marker was retained for recovery.",
                exception);
            throw;
        }
    }

    public async Task<FanOverrideRecoveryDecision> RecoverAsync(
        string currentModel,
        FanControlCapabilityResult capability,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentModel);
        ArgumentNullException.ThrowIfNull(capability);

        var marker = await _ownershipStore
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);

        if (marker is null)
        {
            return new FanOverrideRecoveryDecision(
                FanOverrideRecoveryAction.None,
                "No fan override ownership marker exists.");
        }

        var decision = _recoveryPolicy.Evaluate(currentModel, marker, capability);

        switch (decision.Action)
        {
            case FanOverrideRecoveryAction.None:
                await _ownershipStore
                    .ClearAsync(cancellationToken)
                    .ConfigureAwait(false);
                _logger.Info("Stale fan override ownership marker cleared because both fans are already in Apple Auto.");
                break;

            case FanOverrideRecoveryAction.RestoreAppleAuto:
                // The writer re-checks ownership immediately before the first restore
                // write and returns only after Apple Auto readback verification.
                await _writer
                    .RestoreAppleAutoAsync(marker, cancellationToken)
                    .ConfigureAwait(false);
                await _ownershipStore
                    .ClearAsync(cancellationToken)
                    .ConfigureAwait(false);
                _logger.Info("Fan override recovery restored verified Apple Auto and cleared the ownership marker.");
                break;

            case FanOverrideRecoveryAction.Blocked:
                _logger.Info($"Fan override recovery blocked. {decision.Reason}");
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported fan override recovery action '{decision.Action}'.");
        }

        return decision;
    }
}
