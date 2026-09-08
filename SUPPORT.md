# Support

BootCamp Performance Control is an open-source project for Intel Macs running Windows through Boot Camp.

Before reporting a problem, check the current [Hardware Compatibility](docs/HARDWARE-COMPATIBILITY.md), [Fan Control](docs/FAN-CONTROL.md), [Third-Party Software](THIRD_PARTY.md), [latest stable release](https://github.com/mksolutionsuniversal/BootCampPerformanceControl/releases/latest), and the current [`0.5.0-rc.2` pre-release](https://github.com/mksolutionsuniversal/BootCampPerformanceControl/releases/tag/v0.5.0-rc.2).

## Current public versions

- Stable: `0.4.0`
- Release candidate: `0.5.0-rc.2`
- Current RC automated qualification baseline: `632 / 632` tests
- Physical fan-write evidence: `MacBookPro16,1` / `PerFanModeFloat32` and `MacBookPro12,1` / one-fan `GlobalMaskFpe2`

Stable `0.4.0` remains the recommended release for normal use. `0.5.0-rc.2` is intended for controlled compatibility testing and uses live capability-family fan-write gating.

## Bug reports and compatibility issues

Use GitHub Issues for normal bugs, compatibility reports and feature requests.

For hardware-related reports, include as much of the following as possible:

- BCPC version,
- Mac model identifier, for example `MacBookPro16,1`,
- Windows version,
- CPU and GPU,
- active Windows power plan when relevant,
- whether the problem involves Apply, Restore, startup recovery, clean exit, tray behaviour, fan-only resume or fan control,
- whether Macs Fan Control / the AppleSMC compatibility service is separately installed,
- Macs Fan Control version and AppleSMC driver file version when known,
- the reported AppleSMC protocol/transport and `FNum` when safely available,
- reported fan mode and write-control status,
- the expected behaviour,
- the observed behaviour,
- reproducible steps,
- relevant BCPC logs or compatibility/diagnostic reports with personal information removed.

For fan-control problems, also report the observed fan mode and RPM state before and after the issue if this can be collected safely without performing additional speculative writes.

## Other Intel Macs and RC compatibility testing

A request for support on a different Intel Mac does **not** imply that the model is physically validated by BCPC.

In `0.5.0-rc.2`, normal `SupportedIntelMac` eligibility derives from Apple hardware plus an Intel CPU, while fan-write eligibility is separately capability-gated. A machine may become fan-write eligible only when the fresh live AppleSMC interface matches the complete verified MMIO + dynamic `FNum` fingerprint for `PerFanModeFloat32` or bounded `GlobalMaskFpe2` and all ownership/safety conditions pass.

That runtime result is still only a compatibility decision. `MacBookPro16,1` provides end-to-end `PerFanModeFloat32` evidence, and `MacBookPro12,1` provides one-fan `GlobalMaskFpe2` write/readback/Apple Auto evidence; neither model is a permission whitelist.

The one-fan `GlobalMaskFpe2` path is physically qualified. Two-fan `FS! = 0003` physical qualification remains pending, and unknown or mismatched global-mask fingerprints remain write-disabled.

Do not test speculative SMC writes merely to produce a bug report. For a new machine, start with read-only capability capture.

## Processor profile guidance

Gaming Optimised globally uses Maximum Processor State `95% / 95%` and Turbo/Boost `Disabled / Disabled` for `SupportedIntelMac`.

This conservative processor target is independent of fan capability. If the fan backend is absent, stopped, passive, unsupported or externally Manual without BCPC ownership, CPU-only Gaming can still apply.

Measured CS2 thermal/performance results published by the project come from the primary `MacBookPro16,1` test machine and should not be treated as benchmark evidence for every Intel Mac.

## Fan-control dependency

Neither stable `0.4.0` nor release candidate `0.5.0-rc.2` ships a production AppleSMC kernel driver.

The physically validated production fan-control environment uses a **separately installed** copy of Macs Fan Control `1.5.16` (Build `693`) with AppleSMC driver file version `1.0.7.0`.

BCPC does not redistribute these third-party binaries.

If AppleSMC is installed but stopped, use BCPC's explicit **Enable Fan Monitoring** action rather than manually copying driver files or forcing undocumented service changes.

Close the Macs Fan Control application before BCPC attempts to own the AppleSMC device. BCPC does not forcibly steal the exclusive device handle from another controller.

## Native BootCampSmc research driver

The `drivers/BootCampSmc/` tree is experimental research and is not the production dependency for stable `0.4.0` or release candidate `0.5.0-rc.2`; its native write transport is not claimed as physically qualified.

Its current physically completed checkpoint is Gate 5D-B on `MacBookPro16,1`.

Do not install or modify the research driver as a troubleshooting step for a normal BCPC support issue unless you are deliberately participating in a separately defined research gate.

## Security vulnerabilities

Do **not** report security-sensitive vulnerabilities in a public issue.

Follow [`SECURITY.md`](SECURITY.md). Use GitHub private vulnerability reporting when available; otherwise use the security contact listed there.

## Safety first

If BCPC reports an unsupported platform, unavailable capability, ambiguous ownership state, invalid topology, external Manual state or failed verification, treat the fail-closed result as intentional.

Do not bypass capability gates, edit SMC keys manually, or disable Restore/recovery safeguards to make a support scenario proceed.
