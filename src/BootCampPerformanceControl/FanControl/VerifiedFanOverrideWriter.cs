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
            // Confirmed sequence from the read/write research handoff. Do not insert
            // reads between the initial mode writes and target writes.
            writeStarted = true;
            foreach (var target in plan.Targets)
            {
                await _writeBackend
                    .SetManualModeAsync(target.Index, cancellationToken)
                    .ConfigureAwait(false);
            }

            foreach (var target in plan.Targets)
            {
                await _writeBackend
                    .SetTargetRpmAsync(target.Index, target.TargetRpm, cancellationToken)
                    .ConfigureAwait(false);
            }

            foreach (var target in plan.Targets)
            {
                await _writeBackend
                    .SetManualModeAsync(target.Index, cancellationToken)
                    .ConfigureAwait(false);
            }

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
                        plan.Targets.Select(target => target.Index).ToArray(),
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
                "Fan writer restore found every owned fan already in Apple Auto; no write was required.");
            return;
        }

        if (freshDecision.Action != FanOverrideRecoveryAction.RestoreAppleAuto)
        {
            throw new InvalidOperationException(
                $"Fresh fan ownership check blocked Apple Auto restore. {freshDecision.Reason}");
        }

        await RestoreAppleAutoCoreAsync(
                ownershipMarker.Model,
                ownershipMarker.Targets.Select(target => target.Index).ToArray(),
                CancellationToken.None)
            .ConfigureAwait(false);

        _logger.Info(
            $"Apple Auto restore readback verified for model {ownershipMarker.Model}.");
    }

    private async Task RestoreAppleAutoCoreAsync(
        string model,
        IReadOnlyList<FanIndex> fanIndexes,
        CancellationToken cancellationToken)
    {
        Exception? writeException = null;

        foreach (var fanIndex in fanIndexes)
        {
            try
            {
                await _writeBackend
                    .SetAppleAutoAsync(fanIndex, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                writeException = writeException is null
                    ? exception
                    : new AggregateException(writeException, exception);
            }
        }

        try
        {
            await VerifyAppleAutoAsync(model, fanIndexes, cancellationToken)
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
                "An Apple Auto write reported an error, but readback verified every owned fan in Apple Auto.",
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
        IReadOnlyList<FanIndex> fanIndexes,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= _verificationAttempts; attempt++)
        {
            var capability = await _capabilityProbe
                .ProbeAsync(model, cancellationToken)
                .ConfigureAwait(false);

            if (IsVerifiedAppleAuto(capability, fanIndexes))
            {
                return;
            }

            await DelayBeforeRetryAsync(attempt, cancellationToken)
                .ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            "Apple Auto could not be verified by readback for every owned fan.");
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
        if (snapshot.Fans.Count != plan.Targets.Count ||
            !snapshot.Fans.Select(fan => fan.Index)
                .SequenceEqual(plan.Targets.Select(target => target.Index)))
        {
            return false;
        }

        return snapshot.Fans.Zip(plan.Targets).All(pair =>
            pair.First.Mode.GetUInt8() == 1
            && ApproximatelyEqual(pair.First.Target.GetFloat32(), pair.Second.TargetRpm)
            && ApproximatelyEqual(pair.First.Maximum.GetFloat32(), pair.Second.TargetRpm));
    }

    private static bool IsVerifiedAppleAuto(
        FanControlCapabilityResult capability,
        IReadOnlyList<FanIndex> fanIndexes)
    {
        if (!capability.IsReadSupported ||
            !capability.IsHardwareSafetyGateSatisfied ||
            capability.Snapshot is null ||
            capability.Snapshot.Fans.Count != fanIndexes.Count ||
            !capability.Snapshot.Fans.Select(fan => fan.Index).SequenceEqual(fanIndexes))
        {
            return false;
        }

        return capability.Snapshot.Fans.All(fan => fan.Mode.GetUInt8() == 0);
    }

    private static void EnsurePlanStillMatches(
        FanMaximumSafeRpmPlan requestedPlan,
        FanMaximumSafeRpmPlan freshPlan)
    {
        if (!string.Equals(requestedPlan.Model, freshPlan.Model, StringComparison.Ordinal) ||
            requestedPlan.Targets.Count != freshPlan.Targets.Count ||
            !requestedPlan.Targets.Zip(freshPlan.Targets).All(pair =>
                pair.First.Index == pair.Second.Index &&
                ApproximatelyEqual(pair.First.TargetRpm, pair.Second.TargetRpm)))
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

        if (plan.Targets.Count == 0)
        {
            throw new ArgumentException(
                "Fan override plan does not contain any targets.",
                nameof(plan));
        }

        ValidateIndexedTargets(
            plan.Targets.Select(target => (target.Index, target.TargetRpm)),
            "Fan override plan",
            nameof(plan));
    }

    private static void ValidateMarker(FanOverrideOwnershipMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        ArgumentException.ThrowIfNullOrWhiteSpace(marker.Model);

        if (marker.Targets.Count == 0)
        {
            throw new ArgumentException(
                "Fan ownership marker does not contain any targets.",
                nameof(marker));
        }


        ValidateIndexedTargets(
            marker.Targets.Select(target => (target.Index, target.ExpectedTargetRpm)),
            "Fan ownership marker",
            nameof(marker));
    }

    private static void ValidateIndexedTargets(
        IEnumerable<(FanIndex Index, float Rpm)> targets,
        string description,
        string parameterName)
    {
        var position = 0;
        foreach (var target in targets)
        {
            if (target.Index.Value != position ||
                !float.IsFinite(target.Rpm) ||
                target.Rpm <= 0)
            {
                throw new ArgumentException(
                    $"{description} contains invalid or non-contiguous indexed targets.",
                    parameterName);
            }

            position++;
        }
    }
}
