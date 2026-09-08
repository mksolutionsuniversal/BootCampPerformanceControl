# Security Policy

## Supported Versions

Security fixes are provided for the latest stable release of BootCamp Performance Control and, where practical, the current release candidate.

Current public lines:

- stable: `0.4.0`
- release candidate: `0.5.0-rc.2`

`0.5.0-rc.2` is a pre-release. Normal `SupportedIntelMac` eligibility derives from detected Apple hardware plus an Intel CPU; production fan writes are independently restricted to guarded live `PerFanModeFloat32` or bounded `GlobalMaskFpe2` capability-family matches. It does not claim physical validation of every Intel Mac.

## Reporting a Vulnerability

Please do not report security-sensitive vulnerabilities in a public GitHub issue.

Use GitHub private vulnerability reporting when available.

If private vulnerability reporting is unavailable, report security-sensitive issues by email to security@mkus.co.uk.

Security reports should include:

- the affected BCPC version,
- the affected Mac model and Windows version when relevant,
- whether the AppleSMC compatibility backend was installed/running,
- whether the issue occurred during monitoring, Gaming apply, Restore, clean exit, startup recovery or fan-only resume,
- steps to reproduce the issue,
- the expected and observed behaviour,
- relevant logs with personal or sensitive information removed.

## Hardware Safety

BootCamp Performance Control can interact with:

- Windows processor power-management settings, and
- when the guarded runtime policy permits it, Apple SMC fan-control state through a separately installed compatibility driver.

Reports involving any of the following are treated as safety-relevant:

- unexpected processor power-setting changes,
- failed or incomplete Restore operations,
- unexpected fan-mode or fan-target changes,
- failure to return BCPC-owned hardware to Apple Auto,
- crash-recovery failures,
- fan writes occurring when the live capability-family gate should have blocked them,
- fan writes occurring through the wrong capability-family semantics or outside the exact bounded `PerFanModeFloat32` / `GlobalMaskFpe2` fingerprints,
- recovery ownership markers being cleared without verified hardware recovery,
- replacement or loss of the original processor Restore snapshot during fan-only resume.

## Fan-control safety boundary

Stable `0.4.0` uses the historical exact `MacBookPro16,1` production fan-write gate.

Release candidate `0.5.0-rc.2` uses a strict live capability-family decision at write time. Production fan writes require, at minimum:

- `SupportedIntelMac`,
- MMIO AppleSMC protocol,
- exact verified `FNum` metadata,
- a supported dynamic topology within `F0..F9`,
- exact complete metadata for either `PerFanModeFloat32` or the bounded one-/two-fan `GlobalMaskFpe2` family,
- sane finite runtime values,
- Apple Auto before new BCPC ownership,
- fresh preflight before the write.

The production write surface remains restricted to discovered per-fan mode/target keys for `PerFanModeFloat32` and the proven global `FS! ` mode plus per-fan targets for bounded `GlobalMaskFpe2`. Arbitrary SMC keys, minimum-RPM controls, unknown fingerprints and user-defined fan-speed sliders remain outside the RC write path.

`MacBookPro16,1` remains physically validated end-to-end for `PerFanModeFloat32`, while `MacBookPro12,1` has passed one-fan `GlobalMaskFpe2` write/readback/Apple Auto qualification. Two-fan global-mask physical qualification remains pending. Runtime compatibility on another machine is not equivalent to project physical validation, and neither model identifier grants writer permission.

BCPC does not infer ownership from observed Manual mode alone. A Manual state without BCPC ownership context is treated as externally controlled and is not silently taken over.

BCPC does not bundle or redistribute the third-party AppleSMC compatibility driver or the experimental native `BootCampSmc` research driver. See [THIRD_PARTY.md](THIRD_PARTY.md), [docs/FAN-CONTROL.md](docs/FAN-CONTROL.md), and [drivers/BootCampSmc/README.md](drivers/BootCampSmc/README.md).
