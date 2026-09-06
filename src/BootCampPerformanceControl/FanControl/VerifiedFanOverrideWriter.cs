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

        var freshPlan = freshPreparation.Plan;
        var strategy = FanCapabilityFamilyStrategies.Get(freshPlan.Family);
        if (!_writeBackend.SupportsFamily(freshPlan.Family))
        {
            throw new InvalidOperationException(
                $"No bounded writer implementation is available for capability family '{freshPlan.Family}'.");
        }

        var writeStarted = false;

        try
        {
            writeStarted = true;
            await strategy.WriteMaximumSafeStateAsync(
                    _writeBackend,
                    freshPlan,
                    (mask, token) => VerifyGlobalMaskAsync(freshPlan, mask, token),
                    cancellationToken)
                .ConfigureAwait(false);

            await VerifyManualMaximumAsync(freshPlan, cancellationToken)
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
                        freshPlan.Family,
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

        if (!_writeBackend.SupportsFamily(ownershipMarker.Family))
        {
            throw new InvalidOperationException(
                $"No bounded restore writer is available for capability family '{ownershipMarker.Family}'.");
        }

        await RestoreAppleAutoCoreAsync(
                ownershipMarker.Model,
                ownershipMarker.Family,
                ownershipMarker.Targets.Select(target => target.Index).ToArray(),
                CancellationToken.None)
            .ConfigureAwait(false);

        _logger.Info(
            $"Apple Auto restore readback verified for model {ownershipMarker.Model}.");
    }

    private async Task RestoreAppleAutoCoreAsync(
        string model,
        FanCapabilityFamily family,
        IReadOnlyList<FanIndex> fanIndexes,
        CancellationToken cancellationToken)
    {
        Exception? writeException = null;

        try
        {
            await FanCapabilityFamilyStrategies.Get(family)
                .WriteAppleAutoAsync(_writeBackend, fanIndexes, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            writeException = exception;
        }

        try
        {
            await VerifyAppleAutoAsync(model, family, fanIndexes, cancellationToken)
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
        FanCapabilityFamily family,
        IReadOnlyList<FanIndex> fanIndexes,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= _verificationAttempts; attempt++)
        {
            var capability = await _capabilityProbe
                .ProbeAsync(model, cancellationToken)
                .ConfigureAwait(false);

            if (IsVerifiedAppleAuto(capability, family, fanIndexes))
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
        if (capability.Family != plan.Family)
        {
            return false;
        }

        try
        {
            return FanCapabilityFamilyStrategies.Get(plan.Family)
                .IsManualMaximum(capability, plan);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsVerifiedAppleAuto(
        FanControlCapabilityResult capability,
        FanCapabilityFamily family,
        IReadOnlyList<FanIndex> fanIndexes)
    {
        if (!capability.IsReadSupported ||
            !capability.IsHardwareSafetyGateSatisfied ||
            capability.Family != family ||
            capability.Snapshot is null ||
            capability.Snapshot.Fans.Count != fanIndexes.Count ||
            !capability.Snapshot.Fans.Select(fan => fan.Index).SequenceEqual(fanIndexes))
        {
            return false;
        }

        try
        {
            return FanCapabilityFamilyStrategies.Get(family).IsAppleAuto(capability);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private async Task VerifyGlobalMaskAsync(
        FanMaximumSafeRpmPlan plan,
        ushort expectedMask,
        CancellationToken cancellationToken)
    {
        if (plan.Family != FanCapabilityFamily.GlobalMaskFpe2)
        {
            throw new InvalidOperationException(
                "Global mask verification was requested for a non-global capability family.");
        }

        var capability = await _capabilityProbe
            .ProbeAsync(plan.Model, cancellationToken)
            .ConfigureAwait(false);
        if (!capability.IsReadSupported ||
            !capability.IsHardwareSafetyGateSatisfied ||
            capability.Family != plan.Family ||
            capability.Snapshot?.GlobalMode.Value is null ||
            capability.Snapshot.GlobalMode.Value.GetUInt16BigEndian() != expectedMask ||
            capability.Snapshot.Fans.Count != plan.Targets.Count ||
            !capability.Snapshot.Fans.Zip(plan.Targets).All(pair =>
                pair.Second.ExactTargetPayload.Length == 2 &&
                pair.First.Maximum.RawData.Span.SequenceEqual(
                    pair.Second.ExactTargetPayload.Span)))
        {
            throw new InvalidOperationException(
                "GlobalMaskFpe2 manual mode could not be verified before target writes.");
        }
    }

    private static void EnsurePlanStillMatches(
        FanMaximumSafeRpmPlan requestedPlan,
        FanMaximumSafeRpmPlan freshPlan)
    {
        if (!string.Equals(requestedPlan.Model, freshPlan.Model, StringComparison.Ordinal) ||
            requestedPlan.Family != freshPlan.Family ||
            requestedPlan.BaselineGlobalModeMask != freshPlan.BaselineGlobalModeMask ||
            requestedPlan.Targets.Count != freshPlan.Targets.Count ||
            !requestedPlan.Targets.Zip(freshPlan.Targets).All(pair =>
                pair.First.Index == pair.Second.Index &&
                pair.First.TargetRpm.Equals(pair.Second.TargetRpm) &&
                pair.First.BaselineMode == pair.Second.BaselineMode &&
                pair.First.ExactTargetPayload.Span.SequenceEqual(
                    pair.Second.ExactTargetPayload.Span) &&
                pair.First.BaselineTargetPayload.Span.SequenceEqual(
                    pair.Second.BaselineTargetPayload.Span)))
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

        _ = FanCapabilityFamilyStrategies.Get(plan.Family);

        var validFamilyState = plan.Family switch
        {
            FanCapabilityFamily.PerFanModeFloat32 =>
                plan.BaselineGlobalModeMask is null &&
                plan.Targets.All(target =>
                    target.ExactTargetPayload.Length == 4 &&
                    target.BaselineTargetPayload.Length == 4 &&
                    target.BaselineMode == 0),
            FanCapabilityFamily.GlobalMaskFpe2 =>
                plan.BaselineGlobalModeMask == 0 &&
                plan.Targets.All(target =>
                    target.ExactTargetPayload.Length == 2 &&
                    target.BaselineTargetPayload.Length == 2 &&
                    target.BaselineMode is null),
            _ => false
        };
        if (!validFamilyState)
        {
            throw new ArgumentException(
                "Fan override plan does not contain a complete Apple Auto baseline and exact family target state.",
                nameof(plan));
        }
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

        _ = FanCapabilityFamilyStrategies.Get(marker.Family);
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
