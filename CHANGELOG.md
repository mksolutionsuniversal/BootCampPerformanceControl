# Changelog

All notable changes to BootCamp Performance Control are documented here.

The project follows Semantic Versioning. Release candidates are pre-release builds and should not be treated as final stable releases.

## [Unreleased]

No unreleased changes are currently recorded after the `0.5.0-rc.1` release-candidate preparation.

## [0.5.0-rc.1] - 2026-09-05

### Added

- Dynamic fan-topology discovery from live `FNum` rather than assuming a fixed two-fan layout.
- Runtime fan-write eligibility for the verified T2-style AppleSMC capability family instead of an exact `MacBookPro16,1` permission gate.
- Model-neutral fan-maximum anti-corruption protection with a broad `10000 RPM` sanity ceiling.
- Dynamic ownership-marker schema v2 for non-legacy topologies while retaining legacy schema-v1 compatibility for the physically validated two-fan `MacBookPro16,1` downgrade path.
- Generalised fan-only resume, clean-exit recovery and startup crash recovery for compatible family members.
- Compatibility reporting for transport, discovered fan count/topology, live fan state and write-eligibility reason.

### Changed

- Gaming Optimised processor behaviour is now explicitly global for every `SupportedIntelMac`:
  - Maximum Processor State AC/DC `95% / 95%`,
  - processor boost disabled on AC/DC.
- Fan control is additive to the processor profile. A missing, stopped, unsupported or externally controlled fan backend no longer removes the conservative CPU Gaming profile.
- A compatible fan transaction derives every target from the fresh live `F{i}Mx` value for the discovered topology.
- Exact `MacBookPro16,1` runtime permission gates were removed from fan apply, resume, restore and startup recovery. The exact model remains only where required for legacy marker downgrade compatibility and historical physical-validation reporting.
- Public documentation now distinguishes runtime capability-family eligibility from physical validation of an individual Mac model.

### Safety

- Fan writes still require `SupportedIntelMac`, MMIO protocol, exact verified `FNum`/`F{i}*` metadata, sane runtime values, a valid topology and Apple Auto before new ownership.
- `FNum = 0` remains passive/read-only and cannot produce fan writes.
- Fan counts outside the supported single-decimal `F0..F9` range fail closed.
- A reported live maximum must be finite, greater than zero and no greater than the broad `10000 RPM` anti-corruption ceiling. This ceiling is not an Apple specification and is never used as a target.
- The production write codec remains restricted to discovered `F0..F9` `Md` and `Tg` keys. No `FS!`, `fpe2`, minimum-RPM control, arbitrary SMC keys or fan-speed slider was added.
- T1-style `fpe2` / global-mask layouts remain write-disabled.
- Manual fan state without BCPC ownership is treated as external control and is not silently taken over.
- If a fan write fails after hardware writes begin, BCPC keeps the existing non-cancellable Apple Auto recovery semantics. CPU-only continuation is allowed only after verified fan recovery; ambiguous fan state retains recovery context and fails closed.
- If processor application fails after a successful fan takeover, BCPC returns owned fans to verified Apple Auto.
- Restore with BCPC fan ownership remains ordered `FANS -> POWER`.

### Automated qualification

For the Phase B candidate before release preparation:

- Debug tests: `589/589` passed.
- Release build: `0 warnings / 0 errors`.
- Release tests: `589/589` passed.
- `git diff --check`: PASS.
- Pull-request Windows CI (`Build and test`): PASS.
- Post-merge Windows CI on `main`: PASS.

### Physical validation

The Phase B runtime was physically validated end-to-end on the reference `MacBookPro16,1` (MacBook Pro 16-inch, 2019, Apple T2) running Windows 10 Boot Camp at pre-release `main` commit `5a041303c67175491a9f36ff1927db8c5484ec30`.

Observed validation included:

- Apple Auto read-only capability preflight: PASS,
- live maxima `F0Mx = 5616 RPM`, `F1Mx = 5200 RPM`,
- production Gaming Optimised apply to Manual / Maximum Safe RPM: PASS,
- CPU read-back at `95% / 95%` with boost `0 / 0`: PASS,
- normal Restore to Apple Auto and exact original `100% / 100%`, boost `2 / 2`: PASS,
- forced-process termination while the override was active: PASS,
- automatic startup fan-only recovery to Apple Auto while preserving Gaming CPU state and processor Restore snapshot: PASS,
- fan-only resume without replacing the original processor snapshot: PASS,
- final exact processor Restore after fan-only resume: PASS.

The release-preparation change from that validated runtime to `0.5.0-rc.1` changes version metadata and documentation only; it does not alter hardware-control logic.

Physical validation of this RC does **not** claim that every Apple T2 Mac has been physically tested. Other machines become fan-write eligible only if their live SMC capability fingerprint satisfies the same guarded family policy.

See `docs/0.5.0-rc.1-HARDWARE-VALIDATION.md` for the preserved test record.

### Documentation

- Polished the repository landing README after the stable `0.4.0` release.
- Synchronized public fan-control and hardware-compatibility documentation with dynamic topology and capability-family gating.
- Documented CPU-only Gaming fallback when fan control is unavailable or not safely ownable.
- Documented clean-exit Apple Auto recovery, Partial Gaming / fan-only resume behaviour and startup crash recovery.
- Updated the experimental `BootCampSmc` research-driver boundary without making it a production dependency.
- Added contributor and support guidance for hardware-safe development and issue reporting.

### Distribution

- The release remains Windows x64 ZIP-only.
- BCPC does not bundle Macs Fan Control, `applesmc.sys`, the native experimental `BootCampSmc` driver, or any other third-party/kernel driver.
- The physically validated compatibility environment still uses a separately installed copy of Macs Fan Control `1.5.16 (Build 693)` with AppleSMC driver file version `1.0.7.0`.

## [0.4.0] - 2026-09-05

### Stable release

- Promoted the qualified `0.4.0-rc.1` candidate to stable `0.4.0`.
- No hardware-control logic or safety-boundary changes were introduced by the stable release preparation.
- The Windows x64 release remains ZIP-only; the installer is deferred to a later release.

### Final qualification

- Release tests: `557/557` passed.
- Release build and hardened package creation: PASS.
- Full physical Gaming / clean-exit / Partial Gaming fan-only resume / exact Restore lifecycle on `MacBookPro16,1`: PASS.
- Forced-process termination with durable recovery context, automatic startup fan-only Apple Auto recovery, and final exact processor Restore: PASS.
- Final package forensic validation confirmed `LICENSE` and `THIRD_PARTY.md`, prohibited driver / Macs Fan Control content absent, valid ZIP SHA-256 sidecar, and successful published-app launch / clean exit smoke test.

## [0.4.0-rc.1] - 2026-09-04

### Added

- Production fan-control execution for the physically verified `MacBookPro16,1` path.
- Maximum Safe RPM planning derived from live verified `F0Mx` / `F1Mx` SMC maxima.
- Transactional Gaming Optimised execution combining guarded fan takeover with processor profile application.
- Shared exclusive AppleSMC execution sessions for verified read/write transactions.
- Crash-safe fan ownership persistence before the first fan hardware write.
- Startup fan-only recovery to Apple Auto after an unexpected BCPC process termination.
- Resumable Restore ordering: fans first, then exact saved processor power state.
- Truthful UI reporting of verified write capability, observed Manual mode and unsupported models.

### Changed

- Gaming Optimised on the verified `MacBookPro16,1` path now applies:
  - Maximum Processor State AC/DC `95% / 95%`,
  - processor boost disabled,
  - Maximum Safe RPM for both fans.
- `MacBookPro16,1` no longer falls back to CPU-only Gaming Optimised if the required verified fan transaction cannot run.
- Fan write status is now capability-based rather than displayed as permanently disabled.
- About/diagnostic wording now distinguishes monitoring capability, write capability and observed hardware state.

### Safety

- Fan writes remain disabled for all models except the exact verified `MacBookPro16,1` path.
- Fresh SMC capability and metadata are re-read before fan writes.
- Fan write failure leaves processor settings untouched.
- Processor apply failure triggers Apple Auto compensation when BCPC has already taken fan ownership.
- Startup recovery restores fans only; it does not automatically restore processor settings.
- Manual mode observed in hardware is not treated as proof of BCPC ownership.
- AppleSMC is not automatically started because of a recovery marker.
- No CrystalIDEA / Macs Fan Control binary or proprietary driver is redistributed by BCPC.

### Physical validation

Validated on a real `MacBookPro16,1` (MacBook Pro 16-inch, 2019, Apple T2) running Windows 10 Boot Camp:

- production WPF Gaming Optimised apply: PASS,
- Maximum Safe RPM read-back: PASS,
- exact processor state read-back: PASS,
- normal Restore to Apple Auto + original processor state: PASS,
- forced-process-crash state persistence: PASS,
- startup fan-only crash recovery: PASS,
- final explicit Restore to the exact original processor state: PASS.

Automated validation at the 0.4J checkpoint:

- Debug tests: `534/534`,
- Release tests: `534/534`,
- Release build: `0 warnings / 0 errors`,
- `git diff --check`: PASS.

### Third-party interoperability

The verified compatibility environment uses a separately installed copy of **Macs Fan Control 1.5.16 (Build 693)** with AppleSMC driver file version `1.0.7.0`.

BCPC does not distribute or bundle those third-party files. See `THIRD_PARTY.md` and `docs/FAN-CONTROL.md`.

## Earlier releases

For earlier release history and archived release candidates, see the repository's GitHub Releases page and commit history.
