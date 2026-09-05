# Contributing to BootCamp Performance Control

Thank you for contributing to BootCamp Performance Control (BCPC).

BCPC can change processor power-management state and, when a live verified capability family is present, Apple SMC fan-control state. Hardware safety therefore takes priority over implementation speed.

## Current project baseline

- Latest stable release: `0.4.0`
- Current release candidate: `0.5.0-rc.1`
- Current RC automated baseline: `589 / 589` tests
- Current physically validated production fan path: `MacBookPro16,1` / Apple T2
- Current RC fan-write policy: guarded live capability-family gating, not exact-model permission alone

`0.5.0-rc.1` is a pre-release. Passing the runtime capability-family gate on another T2-era Mac does not mean that model has been physically validated by the project.

## Development model

The default branch is `main` and is treated as the current development source of truth.

Use a short-lived topic branch and open a pull request back to `main`.

Keep changes small enough to review and validate independently. Avoid combining unrelated refactors, UI work and hardware-control changes in one pull request.

Commit messages, code, class names and technical comments should be written in English.

Published release tags are immutable checkpoints. Do not move stable or release-candidate tags after publication.

## Before changing hardware-affecting code

Follow the project's read-before-write rule:

1. read and record the current state,
2. validate the target hardware/model/capability,
3. persist any required recovery state before writing,
4. perform the smallest necessary change,
5. read back and verify the result,
6. preserve a deterministic Restore/rollback path.

For a new or broader hardware path, start in read-only mode.

Do not enable a write path merely because a similar Mac model, T1/T2 generation, SMC key name or third-party implementation appears compatible.

## Fan-control changes

Production fan writes in `0.5.0-rc.1` are capability-family-gated.

The current write family requires all of the following at runtime:

- platform status `SupportedIntelMac`,
- MMIO AppleSMC protocol,
- exact verified `FNum` metadata,
- a supported discovered fan count within `F0..F9`,
- exact per-fan `Mx/Ac/Md/Tg` metadata for the verified FLT4/per-fan family,
- sane finite runtime values,
- Apple Auto before new ownership,
- no conflicting external Manual state,
- fresh preflight immediately before a write.

`MacBookPro16,1` is the only model physically validated end-to-end for this production path so far. New models must still be tested in controlled stages even if the runtime fingerprint passes.

Any fan-control proposal must preserve these invariants:

- fail closed on unknown or ambiguous hardware,
- fresh capability validation before writes,
- Apple Auto / Maximum Safe RPM semantics only unless a separately reviewed product requirement exists,
- fan ownership persisted before the first hardware write,
- verified read-back after writes,
- Apple Auto recovery/failsafe behaviour,
- Restore ordering: fans first, then exact saved processor state,
- no inference of BCPC ownership from observed Manual mode alone,
- no arbitrary SMC key or generic RPM write surface,
- no T1 `fpe2` / `FS!` writes through the T2-style path.

Do not generalize the physically observed `MacBookPro16,1` RPM values as limits for another model. `0.5.0-rc.1` intentionally derives targets from fresh live `F{i}Mx` and uses `10000 RPM` only as a broad anti-corruption ceiling, not as an Apple specification or write target.

## Processor power-management changes

Use official Windows power-management mechanisms.

BCPC does not use CPU undervolting, CPU MSR writes, firmware modification or display-timing modification in the current stable/RC lines.

Restore must restore the actual saved pre-change state. Do not assume the original value was `100%` or a Windows default.

Gaming Optimised globally uses Maximum Processor State `95% / 95%` and Turbo/Boost `Disabled / Disabled` for `SupportedIntelMac`. Fan capability is additive and must not block the conservative CPU profile merely because fan control is absent, stopped, passive, unsupported or externally owned.

## Native BootCampSmc research driver

`drivers/BootCampSmc/` is an experimental independently authored KMDF research path. It is not the production fan-control dependency for stable `0.4.0` or release candidate `0.5.0-rc.1` and is not shipped in their release ZIPs.

The current physically completed checkpoint is Gate 5D-B fixed-key `GET_KEY_INFO(F0Mx/F1Mx)` metadata discovery on `MacBookPro16,1`.

Do not broaden that driver to arbitrary SMC, MMIO, RPM, MSR, PCI configuration-space or physical-memory access.

Any future native-driver gate must be separately reviewed and physically validated before a broader gate is attempted.

## Third-party and legal boundary

BCPC does not redistribute Macs Fan Control, `MacsFanControl.exe`, `macsfancontrol_setup.exe`, `applesmc.sys`, or other proprietary third-party binaries.

Do not submit proprietary implementation code, decompiled proprietary source, copied proprietary routines, licensing/DRM bypasses, or bundled third-party binaries.

Interoperability facts may be documented when independently established. Open-source references must be license-compatible and should be used to corroborate facts rather than copied blindly.

See [`THIRD_PARTY.md`](THIRD_PARTY.md).

## Build and tests

Requirements:

- Windows x64
- .NET 8 SDK

Build:

```powershell
dotnet build BootCampPerformanceControl.sln -c Release
```

Run tests:

```powershell
dotnet test BootCampPerformanceControl.sln -c Release
```

For release-packaging changes also run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-release.ps1
```

The current `0.5.0-rc.1` qualification baseline contains `589` automated tests. A change should not reduce coverage or bypass existing safety checks without an explicit reviewed reason.

## Pull-request expectations

A pull request should state:

- what changes,
- why it is needed,
- whether hardware behaviour changes,
- which Mac model(s) or runtime capability families are affected,
- which validation was performed,
- whether Restore/recovery semantics are affected,
- whether release packaging or third-party boundaries are affected.

For hardware-affecting work, include exact test evidence rather than relying only on assumptions or simulator/unit-test results.

## Security-sensitive issues

Do not disclose security-sensitive vulnerabilities in a public pull request or issue before coordinating a fix.

See [`SECURITY.md`](SECURITY.md).
