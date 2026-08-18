using BootCampPerformanceControl.Logging;

namespace BootCampPerformanceControl.FanControl;

internal sealed class VerifiedFanOverrideWriter : IFanOverrideWriter
{
    private const float RpmComparisonTolerance = 1f;
    private const int DefaultVerificationAttempts = 5;
    private static readonly TimeSpan DefaultVerificationDelay = TimeSpan.FromMilliseconds(100);

    private readonly IFanSmcWriteBackend _writeBackend;
    private readonly IFanCapabilityProbe _capabilityProbe;
    private readonly FanOverridePreflightPolicy _preflightPolicy;
    private readonly FanOverrideRecoveryPolicy _recoveryPolicy;
    private readonly IApplicationLogger _logger;
    private readonly int _verificationAttempts;
    private readonly TimeSpan _verificationDelay;

    public VerifiedFanOverrideWriter(
        IFanSmcWriteBackend writeBackend,
        IFanCapabilityProbe capabilityProbe,
        FanOverridePreflightPolicy preflightPolicy,
        FanOverrideRecoveryPolicy recoveryPolicy,
        IApplicationLogger logger,
        int verificationAttempts = DefaultVerificationAttempts,
        TimeSpan? verificationDelay = null)
    {
        _writeBackend = writeBackend ?? throw new ArgumentNullException(nameof(writeBackend));
        _capabilityProbe = capabilityProbe ?? throw new ArgumentNullException(nameof(capabilityProbe));
        _preflightPolicy = preflightPolicy ?? throw new ArgumentNullException(nameof(preflightPolicy));
        _recoveryPolicy = recoveryPolicy ?? throw new ArgumentNullException(nameof(recoveryPolicy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (verificationAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(verificationAttempts),
                "Verification attempts must be greater than zero.");
        }

        var resolvedDelay = verificationDelay ?? DefaultVerificationDelay;
        if (resolvedDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(verificationDelay),
                "Verification delay cannot be negative.");
        }

        _verificationAttempts = verificationAttempts;
        _verificationDelay = resolvedDelay;
    }

    public async Task ApplyMaximumSafeRpmAsync(
        FanMaximumSafeRpmPlan plan,
        CancellationToken cancellationToken)
    {
        ValidatePlan(plan);

        // Re-read immediately before the first write so a stale coordinator snapshot
        // cannot authorize taking ownership after another actor changed fan state.
        var freshCapability = await _capabilityProbe
            .ProbeAsync(plan.Model, cancellationToken)
            .ConfigureAwait(false);
        var freshPreparation = _preflightPolicy.PrepareMaximumSafeRpm(
            plan.Model,
            freshCapability);

        if (!freshPreparation.IsAllowed || freshPreparation.Plan is null)
        {
            throw new InvalidOperationException(
                "Fresh fan override preflight was blocked. "
                + (freshPreparation.FailureReason ?? "No failure reason was provided."));
        }

        EnsurePlanStillMatches(plan, freshPreparation.Plan);

        var writeStarted = false;

        try
        {
            // Confirmed MacBookPro16,1 sequence from the read/write research handoff.
            // Do not insert reads between the initial mode writes and target writes.
            writeStarted = true;
            await _writeBackend
                .SetManualModeAsync(FanIndex.Fan0, cancellationToken)
                .ConfigureAwait(false);
            await _writeBackend
                .SetManualModeAsync(FanIndex.Fan1, cancellationToken)
                .ConfigureAwait(false);
            await _writeBackend
                .SetTargetRpmAsync(FanIndex.Fan0, plan.Fan0TargetRpm, cancellationToken)
                .ConfigureAwait(false);
            await _writeBackend
                .SetTargetRpmAsync(FanIndex.Fan1, plan.Fan1TargetRpm, cancellationToken)
                .ConfigureAwait(false);
            await _writeBackend
                .SetManualModeAsync(FanIndex.Fan0, cancellationToken)
                .ConfigureAwait(false);
            await _writeBackend
                .SetManualModeAsync(FanIndex.Fan1, cancellationToken)
                .ConfigureAwait(false);

            await VerifyManualMaximumAsync(plan, cancellationToken)
                .ConfigureAwait(false);

            _logger.Info(
                $"Maximum-safe fan override readback verified for model {plan.Model}.");
        }
        catch (Exception operationException) when (writeStarted)
        {
            try
            {
                // Once a hardware write has started, caller cancellation must not leave
                // a partial manual state behind. Emergency rollback is independent of
                // the caller token and must itself finish with Apple Auto readback.
                await RestoreAppleAutoCoreAsync(
                        plan.Model,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                _logger.Info(
                    "Fan override failed after writes started; emergency Apple Auto rollback was verified.");
            }
            catch (Exception rollbackException)
            {
                _logger.Error(
                    "Fan override failed and emergency Apple Auto rollback could not be verified.",
                    rollbackException);
                throw new FanOverrideRollbackException(
                    operationException,
                    rollbackException);
            }

            throw;
        }
    }

    public async Task RestoreAppleAutoAsync(
        FanOverrideOwnershipMarker ownershipMarker,
        CancellationToken cancellationToken)
    {
        ValidateMarker(ownershipMarker);

        // Re-evaluate ownership immediately before restore to close the race between
        // coordinator recovery policy evaluation and the first hardware write.
        var freshCapability = await _capabilityProbe
            .ProbeAsync(ownershipMarker.Model, cancellationToken)
            .ConfigureAwait(false);
        var freshDecision = _recoveryPolicy.Evaluate(
            ownershipMarker.Model,
            ownershipMarker,
            freshCapability);

        if (freshDecision.Action == FanOverrideRecoveryAction.None)
        {
            _logger.Info(
                "Fan writer restore found both fans already in Apple Auto; no write was required.");
            return;
        }

        if (freshDecision.Action != FanOverrideRecoveryAction.RestoreAppleAuto)
        {
            throw new InvalidOperationException(
                $"Fresh fan ownership check blocked Apple Auto restore. {freshDecision.Reason}");
        }

        await RestoreAppleAutoCoreAsync(
                ownershipMarker.Model,
                CancellationToken.None)
            .ConfigureAwait(false);

        _logger.Info(
            $"Apple Auto restore readback verified for model {ownershipMarker.Model}.");
    }

    private async Task RestoreAppleAutoCoreAsync(
        string model,
        CancellationToken cancellationToken)
    {
        Exception? writeException = null;

        try
        {
            await _writeBackend
                .SetAppleAutoAsync(FanIndex.Fan0, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            writeException = exception;
        }

        try
        {
            await _writeBackend
                .SetAppleAutoAsync(FanIndex.Fan1, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            writeException = writeException is null
                ? exception
                : new AggregateException(writeException, exception);
        }

        try
        {
            await VerifyAppleAutoAsync(model, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception verificationException)
        {
            if (writeException is not null)
            {
                throw new AggregateException(
                    "Apple Auto restore writes and readback verification did not complete cleanly.",
                    writeException,
                    verificationException);
            }

            throw;
        }

        if (writeException is not null)
        {
            // Readback is the final source of truth. A low-level write may report an
            // error after the firmware has already accepted the state transition.
            _logger.Error(
                "An Apple Auto write reported an error, but readback verified both fans in Apple Auto.",
                writeException);
        }
    }

    private async Task VerifyManualMaximumAsync(
        FanMaximumSafeRpmPlan plan,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= _verificationAttempts; attempt++)
        {
            var capability = await _capabilityProbe
                .ProbeAsync(plan.Model, cancellationToken)
                .ConfigureAwait(false);

            if (IsVerifiedManualMaximum(capability, plan))
            {
                return;
            }

            await DelayBeforeRetryAsync(attempt, cancellationToken)
                .ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            "Maximum-safe fan override could not be verified by readback.");
    }

    private async Task VerifyAppleAutoAsync(
        string model,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= _verificationAttempts; attempt++)
        {
            var capability = await _capabilityProbe
                .ProbeAsync(model, cancellationToken)
                .ConfigureAwait(false);

            if (IsVerifiedAppleAuto(capability))
            {
                return;
            }

            await DelayBeforeRetryAsync(attempt, cancellationToken)
                .ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            "Apple Auto could not be verified by readback for both fans.");
    }

    private async Task DelayBeforeRetryAsync(
        int completedAttempt,
        CancellationToken cancellationToken)
    {
        if (completedAttempt >= _verificationAttempts ||
            _verificationDelay == TimeSpan.Zero)
        {
            return;
        }

        await Task.Delay(_verificationDelay, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsVerifiedManualMaximum(
        FanControlCapabilityResult capability,
        FanMaximumSafeRpmPlan plan)
    {
        if (!capability.IsReadSupported ||
            !capability.IsHardwareSafetyGateSatisfied ||
            capability.Snapshot is null)
        {
            return false;
        }

        var snapshot = capability.Snapshot;
        return snapshot.Fan0Mode.GetUInt8() == 1
            && snapshot.Fan1Mode.GetUInt8() == 1
            && ApproximatelyEqual(snapshot.Fan0Target.GetFloat32(), plan.Fan0TargetRpm)
            && ApproximatelyEqual(snapshot.Fan1Target.GetFloat32(), plan.Fan1TargetRpm)
            && ApproximatelyEqual(snapshot.Fan0Maximum.GetFloat32(), plan.Fan0TargetRpm)
            && ApproximatelyEqual(snapshot.Fan1Maximum.GetFloat32(), plan.Fan1TargetRpm);
    }

    private static bool IsVerifiedAppleAuto(FanControlCapabilityResult capability)
    {
        return capability.IsReadSupported
            && capability.IsHardwareSafetyGateSatisfied
            && capability.Snapshot is not null
            && capability.Snapshot.Fan0Mode.GetUInt8() == 0
            && capability.Snapshot.Fan1Mode.GetUInt8() == 0;
    }

    private static void EnsurePlanStillMatches(
        FanMaximumSafeRpmPlan requestedPlan,
        FanMaximumSafeRpmPlan freshPlan)
    {
        if (!string.Equals(requestedPlan.Model, freshPlan.Model, StringComparison.Ordinal) ||
            !ApproximatelyEqual(requestedPlan.Fan0TargetRpm, freshPlan.Fan0TargetRpm) ||
            !ApproximatelyEqual(requestedPlan.Fan1TargetRpm, freshPlan.Fan1TargetRpm))
        {
            throw new InvalidOperationException(
                "Fan maximum RPM values changed after the original preflight. No fan write was attempted.");
        }
    }

    private static bool ApproximatelyEqual(float left, float right)
    {
        return float.IsFinite(left)
            && float.IsFinite(right)
            && MathF.Abs(left - right) <= RpmComparisonTolerance;
    }

    private static void ValidatePlan(FanMaximumSafeRpmPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(plan.Model);

        if (!float.IsFinite(plan.Fan0TargetRpm) || plan.Fan0TargetRpm <= 0 ||
            !float.IsFinite(plan.Fan1TargetRpm) || plan.Fan1TargetRpm <= 0)
        {
            throw new ArgumentException(
                "Fan override plan contains an invalid target RPM.",
                nameof(plan));
        }
    }

    private static void ValidateMarker(FanOverrideOwnershipMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        ArgumentException.ThrowIfNullOrWhiteSpace(marker.Model);

        if (!float.IsFinite(marker.Fan0ExpectedTargetRpm) || marker.Fan0ExpectedTargetRpm <= 0 ||
            !float.IsFinite(marker.Fan1ExpectedTargetRpm) || marker.Fan1ExpectedTargetRpm <= 0)
        {
            throw new ArgumentException(
                "Fan ownership marker contains an invalid expected target RPM.",
                nameof(marker));
        }
    }
}
