# Fan Control and AppleSMC Compatibility Backend

BootCamp Performance Control uses a conservative, fail-closed fan-control design.

The production feature remains deliberately narrow:

- **Apple Auto**
- **Maximum Safe RPM**

BCPC does not expose arbitrary SMC writes, a user-defined RPM slider or minimum-RPM control in `0.5.0-rc.1`.

## Published release-candidate status

Current public lines:

- stable: `0.4.0`
- release candidate: `0.5.0-rc.1`

Published RC identity:

```text
Tag:           v0.5.0-rc.1
Source commit: 27511afee7e1ae092bb53e63d8c1c96b73004c81
ZIP:           BootCampPerformanceControl-0.5.0-rc.1-win-x64.zip
ZIP SHA-256:   B2215F7C6846614F2F1606A5DC11DC2D0BB1A496C66ACBA523B607A8DC65DDD5
Tests:         589 / 589 PASS
```

`0.5.0-rc.1` is a GitHub pre-release. Stable `0.4.0` remains unchanged and remains the latest stable release.

## What changed from stable 0.4.0

Stable `0.4.0` enables production fan writes only for the exact physically verified `MacBookPro16,1` path.

`0.5.0-rc.1` removes that exact-model permission gate and instead requires a fresh live AppleSMC capability-family match immediately before write execution.

This is a runtime compatibility decision, **not** a claim that every T2 Mac is physically validated.

The complete production path has been physically validated end-to-end on:

```text
MacBookPro16,1
MacBook Pro 16-inch (2019)
Apple T2
Windows 10 Boot Camp
```

T1-style `fpe2` / `FS!` fan control remains outside the production write backend.

## Third-party compatibility dependency

BCPC does not ship its own production AppleSMC Windows driver in stable `0.4.0` or release candidate `0.5.0-rc.1`.

The physically verified production backend interoperates with the AppleSMC compatibility driver installed by a separate Windows installation of **Macs Fan Control 1.5.16 (Build 693)**.

Official upstream release:

https://github.com/crystalidea/macs-fan-control/releases/tag/v1.5.16

Use the official Windows installer:

```text
macsfancontrol_setup.exe
```

Do **not** manually copy `applesmc.sys` into Windows.

The validated environment reported:

```text
Service:             AppleSMC
Driver:              applesmc.sys
Driver FileVersion:  1.0.7.0
Driver SHA-256:       2E35DF03B80EF6FC6DA53C44A3C9454C945F4822C8F1F3355EEA2D1E06E53FD5
```

Other Macs Fan Control versions may work, but they are not currently part of BCPC's physically validated interoperability environment.

BCPC does not redistribute Macs Fan Control, `MacsFanControl.exe`, `macsfancontrol_setup.exe`, `applesmc.sys`, or other proprietary binaries. See [../THIRD_PARTY.md](../THIRD_PARTY.md).

## Before enabling fan monitoring

1. Install Macs Fan Control 1.5.16 from the official upstream release if you intentionally want the currently validated compatibility backend.
2. Close the Macs Fan Control application if it is running.
3. Start BCPC normally.
4. Review the detected Mac model and platform status.
5. If BCPC reports the AppleSMC service as installed but stopped, use **Enable Fan Monitoring** only when you intentionally want BCPC to start that already installed service.
6. Review monitoring state, dynamic fan topology, mode and write eligibility before applying a profile.

The AppleSMC device is exclusive. BCPC does not kill another fan-control application and does not silently steal a device handle from another controller.

## Explicit service activation and CPU-only Gaming

Normal BCPC startup does not automatically start AppleSMC.

**Enable Fan Monitoring** is the explicit user action that may launch the small elevated helper and start the already installed `AppleSMC` Windows service.

Gaming Optimised does not silently elevate or start AppleSMC.

If the backend is unavailable, stopped, unsupported, passive (`FNum = 0`) or not safely ownable, the processor Gaming profile can still apply:

```text
Maximum Processor State AC/DC: 95% / 95%
Processor boost AC/DC:         Disabled / Disabled
Fans:                          unchanged / Apple-managed
```

This CPU-only fallback is intentional. Fan capability is additive and must not remove the conservative processor target.

An ambiguous fan state after BCPC has already started hardware writes is different: recovery context is retained and BCPC fails closed rather than continuing on uncertain hardware state.

## Verified capability-family gate

A running AppleSMC service or a T2 model name by itself is not enough to enable writes.

Before production fan writes, BCPC requires `SupportedIntelMac` and re-reads a fresh SMC capability snapshot.

### Transport and count

Required for writes:

```text
Protocol: MMIO (1)
FNum:     ui8, length 1, attributes 0x80
```

Fan count must be between `1` and `10`, mapping to the supported single-decimal `F0..F9` topology.

`FNum = 0` is accepted as a passive/read-only topology and produces zero fan writes.

### Per-fan metadata

For every discovered fan index `i`:

```text
F{i}Mx  flt   4 bytes   attributes 0x85
F{i}Ac  flt   4 bytes   attributes 0x84
F{i}Md  ui8   1 byte    attributes 0xD0
F{i}Tg  flt   4 bytes   attributes 0xD4
```

Verified family mode semantics:

```text
0 = Apple Auto
1 = Manual
```

### Runtime sanity

For every fan, maximum RPM must be finite, greater than zero and no greater than `10000 RPM`.

`10000 RPM` is a broad anti-corruption ceiling only. It is not an Apple specification and BCPC never uses it as a requested target.

Maximum Safe RPM is always the **fresh live `F{i}Mx` value** for the discovered fan.

Current and target readings must remain finite and within the bounded validation policy; mode must decode to a supported Auto/Manual value.

### T1 remains blocked

The known T1-style `fpe2` encoding and global `FS!` manual-control concept do not satisfy this family gate. `0.5.0-rc.1` does not write those keys or infer T1/T2 equivalence.

## Closed write surface

The production SMC write codec remains intentionally narrow.

Allowed discovered fan keys are only:

```text
F0..F9 Md
F0..F9 Tg
```

BCPC does not expose or write:

- `FS!`,
- T1 `fpe2` fan targets,
- fan minimum keys,
- arbitrary SMC keys,
- user-defined RPM values,
- a generic fan-speed slider.

## Ownership rules

Observed Manual mode is not proof that BCPC owns the fans.

If Manual mode is detected without BCPC's persisted ownership context, BCPC treats the state as externally controlled and does not silently take over. Gaming Optimised may still apply the CPU profile while leaving those fans untouched.

A new BCPC Maximum Safe RPM transaction requires every discovered fan to be in verified Apple Auto before ownership is taken.

BCPC persists its ownership marker before the first fan hardware write.

## Ownership marker schemas and downgrade safety

`0.5.0-rc.1` reads:

- legacy schema v1 for the historical two-fan `MacBookPro16,1` layout,
- dynamic schema v2 with indexed targets.

New exact `MacBookPro16,1` two-fan ownership markers intentionally remain schema v1 so stable `0.4.0` can recover after a downgrade. Other compatible models/topologies use schema v2.

Unknown or malformed schemas are preserved and fail closed. Loading an existing v1 marker does not cause it to be rewritten merely because a newer application version is running.

## Gaming Optimised execution

The processor target is independent from fan capability and always uses `95% / 95%`, boost disabled, on `SupportedIntelMac`.

When a compatible fan transaction is available, the high-level flow is:

1. read and persist the processor state required for exact Restore,
2. obtain a fresh AppleSMC capability snapshot,
3. verify the dynamic topology, metadata and runtime sanity,
4. require Apple Auto before new fan ownership,
5. derive every target from fresh live `F{i}Mx`,
6. persist BCPC fan ownership,
7. apply Manual mode to all discovered fans,
8. apply each fresh live maximum target,
9. reassert Manual mode for all fans,
10. read back and verify Maximum Safe RPM,
11. apply and verify the processor profile.

No reads are inserted inside the initial mode/target transaction sequence.

If no safe fan transaction can start because the backend is absent/stopped/unsupported or fans are externally Manual, the CPU profile can proceed without fan writes.

If a fan write fails after writes have begun, BCPC attempts non-cancellable Apple Auto recovery. When that recovery is verified, CPU-only Gaming may continue. If fan state cannot be verified, BCPC retains ownership/recovery context and reports failure rather than guessing.

If processor application fails after BCPC has successfully taken fan ownership, BCPC returns the owned fans to verified Apple Auto.

## Restore ordering

If BCPC has active fan ownership/recovery context, Restore is intentionally ordered:

```text
FANS -> Apple Auto verified
then
POWER -> exact saved processor state
```

If there is no BCPC fan ownership/recovery context, Restore does not invent SMC writes and restores the saved processor state only.

BCPC never re-applies Maximum Safe RPM merely because processor Restore fails afterward.

## Clean exit behaviour

A clean application exit is intentionally different from **Restore Original Settings**.

When BCPC owns an active compatible-family fan override and the user exits the application cleanly:

- BCPC restores owned fans to verified Apple Auto,
- the fan-ownership marker is cleared only after verified recovery,
- the processor remains in its current Gaming state,
- the original processor Restore snapshot remains available.

This preserves fan safety without silently undoing the user's processor profile.

## Partial Gaming and fan-only resume

After clean fan recovery or startup fan-only crash recovery, BCPC may observe this valid split state:

```text
Processor Maximum State:      95% / 95%
Processor boost:              Disabled / Disabled
Processor Restore snapshot:   present
Fans:                         Apple Auto
Fan ownership marker:         absent
```

BCPC reports that split state truthfully.

Activating Gaming Optimised again may perform a **fan-only resume** when the current machine still passes the family gate. The original processor Restore snapshot is not recreated or replaced.

A later explicit **Restore Original Settings** still restores the exact processor state captured before the original Gaming transaction.

## Crash recovery

The ownership marker is durable across process termination.

If BCPC is killed while its verified override is active:

- fans may remain in Manual / Maximum Safe RPM,
- the ownership marker remains,
- the processor Restore snapshot remains.

On the next startup, BCPC does not infer physical state from the marker alone. A recovery write requires, at minimum:

- current model exactly matches `marker.Model`,
- current platform remains `SupportedIntelMac`,
- AppleSMC is already available,
- a fresh capability-family probe succeeds,
- current topology/index set matches the marker,
- expected target/max values match within policy,
- recovery policy permits Apple Auto release.

Any mismatch prevents speculative writes and retains recovery context.

When recovery is permitted, BCPC restores **fans only** to Apple Auto, verifies read-back and clears ownership only after successful verification.

BCPC does not automatically restore the saved processor profile at startup. The user retains explicit control through **Restore Original Settings**.

If AppleSMC is stopped, startup recovery does not silently elevate or start it.

## Physical validation

The complete production lifecycle was physically validated on a real `MacBookPro16,1` / Apple T2 machine on 2026-09-05 against the Phase B runtime merged at:

```text
5a041303c67175491a9f36ff1927db8c5484ec30
```

Observed read-only baseline:

```text
F0: 5036 / 5616 RPM
F1: 4658 / 5200 RPM
Mode: Apple Auto
CPU: 100 / 100
Boost: 2 / 2
```

Observed Gaming Optimised read-back:

```text
F0: 5587 / 5616 RPM
F1: 5208 / 5200 RPM
Mode: Manual
CPU: 95 / 95
Boost: 0 / 0
```

The Fan 1 actual read-back briefly exceeded its reported maximum by eight RPM while the commanded target remained the fresh live `5200 RPM` maximum; this was within the bounded runtime tolerance.

Normal Restore returned Apple Auto and the exact original processor state. Forced-process termination demonstrated durable ownership/snapshot persistence and automatic startup **fan-only** Apple Auto recovery while CPU remained in Gaming state. Fan-only resume was then exercised without replacing the original CPU snapshot, followed by successful final exact Restore.

See [0.5.0-rc.1 Hardware Validation Record](0.5.0-rc.1-HARDWARE-VALIDATION.md).

This physical validation supports the reference model. It does not certify every T2-family machine.

## Native BootCampSmc research path

BCPC also contains an independently authored experimental KMDF research driver under `drivers/BootCampSmc/`.

That driver is **not** the production fan-control dependency for `0.5.0-rc.1` and is **not** included in release packages. Its physically completed research boundary currently reaches Gate 5D-B fixed-key `GET_KEY_INFO(F0Mx/F1Mx)` metadata transactions on `MacBookPro16,1`.

See [../drivers/BootCampSmc/README.md](../drivers/BootCampSmc/README.md).

## Future validation

Additional compatible T2-family machines should be validated in stages:

1. read-only hardware/platform capture,
2. read-only AppleSMC protocol and topology capture,
3. verify exact family metadata and runtime sanity,
4. controlled Maximum Safe RPM apply/read-back,
5. explicit Apple Auto Restore verification,
6. processor snapshot/Restore round-trip,
7. crash/startup recovery only after the earlier stages pass.

T1 support remains a separate engineering track and must not reuse T2-family write assumptions.
