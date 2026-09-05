# Changelog

All notable changes to BootCamp Performance Control are documented here.

The project follows Semantic Versioning. Release candidates are pre-release builds and should not be treated as final stable releases.

## [Unreleased]

### Documentation

- Polished the repository landing README after the stable `0.4.0` release.
- Synchronized the public fan-control and hardware-compatibility documentation with the completed S0-S7 stabilisation record.
- Documented clean-exit Apple Auto recovery and truthful Partial Gaming / fan-only resume behaviour.
- Updated the experimental `BootCampSmc` research-driver README to reflect the physically completed Gate 5D-B checkpoint rather than the older Gate 4C phase.
- Added contributor and support guidance for hardware-safe development and issue reporting.

No runtime, hardware-control, release-package, or `v0.4.0` binary changes are included in these post-release documentation updates.

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
