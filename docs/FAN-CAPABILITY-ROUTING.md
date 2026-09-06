# Fan Capability Routing

## Product rule

BootCamp Performance Control does not grant fan-write permission from an exact Mac model, product year, or a T1/T2 generation label.

For supported Apple Intel Macs, fan capability is discovered from the live AppleSMC key schema and runtime values.

Mac model identifiers remain useful for diagnostics, evidence, known-bad denylisting if ever required, and hardware-validation records. They are not the primary runtime selector for a fan-control writer.

## Runtime decision flow

```text
Apple + Intel
    |
    v
AppleSMC backend available?
    |-- no  -> CPU profile remains available; fan control unavailable
    v
Read FNum
    |-- malformed/out of bounds -> unknown / fail closed / no fan writes
    |-- 0 -> Passive topology; no fan writes exist to perform
    v
Discover F0..F(n-1)
    v
Read exact key metadata and live values
    v
Classify capability family
    |-- PerFanModeFloat32
    |-- GlobalMaskFpe2
    |-- Unknown -> read-only / no fan writes
    v
Run only the bounded writer implemented for that exact live fingerprint
```

Optional key discovery records `Available`, `ConfirmedAbsent`, or `ReadFailed`. Only a positive backend missing-key result can satisfy an absence requirement. A failed IOCTL, access error, or malformed non-zero response remains `ReadFailed`, keeps its diagnostic reason, and prevents write-family classification.

Transport qualification is a write gate, not a prerequisite for attempting safe diagnostic reads. When an unverified transport successfully serves the read requests, BCPC reports the discovered candidate fingerprint but performs zero writes because the hardware safety gate remains false.

## Dynamic topology

`FNum` is the authority for discovered fan count.

BCPC must not hard-code one fan, two fans, or a model-specific fan-count table. For each discovered index `i`, the program operates only on `F{i}` keys that belong to a recognized capability family.

The current four-character indexed-key implementation can represent decimal fan indexes `F0..F9`; a reported count outside the safely representable range must fail closed rather than guessing.

## Passive / fanless Macs

`FNum = 0` is a valid passive topology, not an unsupported Mac.

Expected product behaviour:

- processor Gaming Optimised remains available,
- no SMC fan write is attempted,
- fan controls are disabled/not applicable,
- UI and diagnostics clearly state that no controllable fans were reported by SMC.

A suitable user-facing meaning is: `No controllable fans reported by AppleSMC (passive/fanless topology).`

## Currently observed capability families

### PerFanModeFloat32

Physically observed on a project Mac with:

- dynamic `FNum`,
- per-fan `F{i}Mx`, `F{i}Ac`, `F{i}Tg` as 4-byte `flt ` values,
- per-fan `F{i}Md` mode keys,
- no global `FS! ` key.

This family already has a bounded production write path. Runtime permission must continue to depend on the live fingerprint, not on the model used to validate it.

### GlobalMaskFpe2

Physically observed on a project Mac with:

- dynamic `FNum`,
- per-fan RPM values encoded as 2-byte `fpe2`,
- no per-fan `F{i}Md`,
- global 2-byte `FS! ` manual-control mask.

Physical evidence confirms `fpe2` RPM scale 4 on the validated capture: `0x60DC = 24796`, `24796 / 4 = 6199 RPM`.

The bounded production path for this family is capability-gated and implements only Maximum Safe RPM plus Apple Auto release. It writes the proven `FS! ` masks only for one- and two-fan topologies, copies fresh exact `F{i}Mx` bytes to matching `F{i}Tg` keys, and verifies each transition by readback. Topologies above fan index 1 remain read-only because broader mask semantics have not been proven.

The writer implementation and in-memory transport tests do not constitute physical write qualification. A controlled physical Maximum Safe RPM / Apple Auto round trip remains a separate manual validation step.

The crash-recovery implementation persists exact pre-write mode/target baselines and accepts only deterministic prefixes of BCPC's documented write ordering. Fake/in-memory restart tests cover those boundaries. Restore Apple Auto before downgrading from a live `GlobalMaskFpe2` override because older releases cannot represent this ownership family.

## Unknown fingerprints

An Apple Intel Mac is not rejected merely because its exact model was not previously tested.

If its live SMC fingerprint does not match a bounded writer family, BCPC must:

- keep the CPU profile available,
- perform zero fan writes,
- preserve read-only diagnostics when safely available,
- report the observed fan count, transport, key metadata, raw values and classifier outcome,
- make it easy for testers to attach that report to a GitHub issue.

Unknown means `write capability not verified`, not `unsupported Mac`.

## Maximum Safe RPM

Maximum Safe RPM is always derived per fan from the fresh live `F{i}Mx` value for that same discovered fan.

BCPC must never use a model RPM table or a universal hard-coded target.

Before a write transaction, the application must re-read the capability fingerprint and live maxima. After every hardware write family operation, the resulting state must be read back and verified.

## Public fan-control surface

The public control surface remains intentionally narrow:

- `Apple Auto`
- `Maximum Safe RPM`

No arbitrary SMC key writes, minimum-RPM writes, firmware changes, generic RPM slider, or guessed target values are part of the supported product path.

## Tester contract

A tester should be able to run the normal application on any supported Apple Intel Mac.

The application should self-discover whether fan control is:

- available through a recognized capability family,
- passive/fanless,
- read-only because the fingerprint is unknown,
- unavailable because the AppleSMC compatibility backend is unavailable/busy/not installed.

Diagnostic exports should contain enough non-sensitive SMC capability metadata to let maintainers add support for a newly observed family without asking testers to reverse-engineer their Mac manually.

## Validation vs compatibility

Physical validation records identify machines on which a family was tested. They do not form a runtime whitelist.

A newly encountered Mac may use an already recognized capability family. In that case runtime safety is decided by the exact verified live schema and state, while documentation can separately state which physical machines have been validated by the project.
