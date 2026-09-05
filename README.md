# BootCamp Performance Control

[![Release](https://img.shields.io/github/v/release/mksolutionsuniversal/BootCampPerformanceControl?sort=semver)](https://github.com/mksolutionsuniversal/BootCampPerformanceControl/releases/latest)
[![CI](https://github.com/mksolutionsuniversal/BootCampPerformanceControl/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/mksolutionsuniversal/BootCampPerformanceControl/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-lightgrey.svg)](https://github.com/mksolutionsuniversal/BootCampPerformanceControl/releases/latest)

BootCamp Performance Control (BCPC) is an open-source Windows utility for Intel Macs running Windows through Boot Camp.

Its goal is to reduce unnecessary heat and thermal throttling using conservative Windows processor power management and guarded Apple SMC fan control when a verified runtime capability family is present.

**[Download the latest stable release](https://github.com/mksolutionsuniversal/BootCampPerformanceControl/releases/latest)** · [All releases and pre-releases](https://github.com/mksolutionsuniversal/BootCampPerformanceControl/releases) · [Hardware compatibility](docs/HARDWARE-COMPATIBILITY.md) · [Fan control](docs/FAN-CONTROL.md) · [Changelog](CHANGELOG.md)

## Release status

- **Current stable release:** `0.4.0`
- **Current release candidate:** `0.5.0-rc.1`
- **Stable status:** `0.4.0` remains the recommended stable build.
- **RC fan-control milestone:** dynamic topology plus capability-family T2-style fan-write eligibility.
- **Physical fan-control validation:** end-to-end on `MacBookPro16,1` (MacBook Pro 16-inch, 2019, Apple T2).

`0.5.0-rc.1` is a pre-release and is intentionally separate from stable `0.4.0`. It broadens runtime fan-write eligibility from an exact-model gate to a strict live SMC capability-family gate. That does **not** mean every T2 Mac has been physically validated.

> **Important:** Gaming Optimised uses Maximum Processor State `95% / 95%` and disables Turbo/Boost on AC and DC for every `SupportedIntelMac`. Fan control is additive and remains independently capability-gated.

## Quick start

For normal use, start with the latest stable release. For `0.5.0-rc.1` testing, select the pre-release from the [GitHub Releases page](https://github.com/mksolutionsuniversal/BootCampPerformanceControl/releases).

1. Download the matching `win-x64` ZIP and `.sha256` file.
2. Optionally verify the ZIP SHA-256.
3. Extract the ZIP to a normal user-writable folder.
4. Run `BootCampPerformanceControl.exe`.
5. Review the detected Mac model and current processor/fan state before applying any profile.

For the AppleSMC compatibility backend, BCPC currently interoperates with a separately installed Windows copy of Macs Fan Control 1.5.16. BCPC does **not** bundle Macs Fan Control, AppleSMC, or any third-party driver.

## What BCPC does

### Gaming Optimised

On every `SupportedIntelMac`:

- Maximum Processor State AC/DC: `95% / 95%`
- Intel processor boost (`PERFBOOSTMODE`) AC/DC: `Disabled / Disabled`
- Display settings: unchanged

Fan control is additive:

- if the live AppleSMC backend matches the verified MMIO + `FNum` + per-fan FLT4 capability family and all ownership/safety checks pass, Gaming Optimised also applies `Maximum Safe RPM` to every discovered fan using fresh live `F{i}Mx` values;
- if AppleSMC is missing, stopped, unsupported, reports `FNum = 0`, or the fans are already Manual without BCPC ownership, Gaming Optimised remains available as a CPU-only profile;
- if fan state becomes ambiguous after BCPC has started hardware writes, the recovery context is retained and the operation fails closed rather than guessing.

Only `MacBookPro16,1` has completed BCPC end-to-end physical validation for this production fan path so far. Other machines are release-candidate compatibility targets only when their live capability fingerprint passes the same guarded policy.

### Restore Original Settings

BCPC restores the actual processor state captured before the profile was applied. It does not assume that the previous values were `100%` or Windows defaults.

If BCPC currently has fan ownership/recovery context, Restore returns the owned fans to verified Apple Auto **before** restoring the saved Windows processor state. Without BCPC fan ownership, Restore does not invent fan work and restores the processor snapshot only.

### Fan monitoring and recovery

When the AppleSMC backend can be safely read, BCPC can:

- discover the live fan count dynamically,
- read per-fan current RPM, maximum RPM, target and mode,
- report Apple Auto / Manual state,
- distinguish read capability from write eligibility,
- apply Maximum Safe RPM only after the full family/safety gate succeeds,
- persist fan-override ownership before the first hardware write,
- recover owned fans to Apple Auto after an unexpected BCPC process termination,
- leave the saved processor profile untouched during startup fan recovery so the user can explicitly choose Restore.

## Hardware compatibility

### Physically verified end-to-end

**MacBookPro16,1 — MacBook Pro 16-inch (2019), Apple T2**

Physical validation includes:

- Windows processor Apply / exact read-back / Restore,
- production fan monitoring and dynamic topology discovery,
- Maximum Safe RPM apply and read-back,
- Apple Auto restore,
- forced-process-crash recovery,
- fan-only resume without replacing the original processor Restore snapshot,
- exact processor-state restoration after recovery.

Primary validation machine:

- Windows 10 Boot Camp
- Intel Core i9-9980HK
- AMD Radeon Pro 5500M

See [0.5.0-rc.1 Hardware Validation Record](docs/0.5.0-rc.1-HARDWARE-VALIDATION.md).

### T1 fan writes remain blocked

**MacBookPro14,3 — MacBook Pro 15-inch (2017), Apple T1**

Processor power-management behaviour has been observed, but its `fpe2` / global-mask fan family is not the verified T2-style family used by this RC. Production fan writes remain disabled pending independent T1 work.

### Other Intel Macs

The processor Gaming profile is available on `SupportedIntelMac` systems. Fan-write eligibility is not granted merely because a machine is an Intel Mac or is believed to contain T2.

`0.5.0-rc.1` requires the fresh runtime AppleSMC fingerprint documented in [Hardware Compatibility](docs/HARDWARE-COMPATIBILITY.md) and [Fan Control](docs/FAN-CONTROL.md). A machine that passes that family gate may be eligible for writes even if its model is not individually whitelisted, but that is not a claim of physical validation for that model.

## Fan-control dependency: Macs Fan Control 1.5.16

BCPC does **not** include, redistribute, modify, mirror, or install Macs Fan Control or its AppleSMC driver.

The physically validated AppleSMC compatibility environment uses a **separately installed** Windows copy of:

- **Macs Fan Control 1.5.16 (Build 693)**
- AppleSMC driver file version observed in the validated environment: `1.0.7.0`

Download and install that version from the official CrystalIDEA GitHub release:

- https://github.com/crystalidea/macs-fan-control/releases/tag/v1.5.16

Use the official Windows installer (`macsfancontrol_setup.exe`). Do not copy `applesmc.sys` manually.

After installation:

1. Close the Macs Fan Control application if it is running. The AppleSMC device is exclusive and BCPC will not forcibly take ownership from another controller.
2. Start BCPC normally.
3. Use **Enable Fan Monitoring** if the AppleSMC service is installed but stopped and you explicitly want to activate monitoring. This is the user action that may request elevation to start the service.
4. Review BCPC's reported monitoring state, family write state and fan mode before relying on fan control.

Gaming Optimised does not silently start AppleSMC. If the fan backend is unavailable or not safely ownable, the conservative CPU profile can still apply.

Other versions of Macs Fan Control may work, but they are not part of the currently physically validated interoperability environment.

See [Fan Control and AppleSMC Compatibility Backend](docs/FAN-CONTROL.md) and [Third-Party Software](THIRD_PARTY.md).

## Safety model

BCPC follows fail-closed rules for hardware-affecting operations:

- read current state before writing,
- persist the original processor Restore snapshot before modification,
- verify expected current state before processor writes,
- re-read fan capability immediately before fan writes,
- require the exact verified runtime fan-family metadata rather than trusting a model name,
- derive fan targets from fresh live SMC maxima rather than hard-coded RPM values,
- reject non-finite, non-positive or implausibly high maximum RPM data,
- allow only whitelisted per-fan mode/target keys,
- never use `FS!`, T1 `fpe2`, arbitrary SMC writes, minimum-RPM controls or a manual fan-speed slider in this RC,
- verify hardware state after writes,
- attempt Apple Auto compensation if the processor phase fails after fan takeover,
- restore owned fans before processor settings,
- retain recovery context across crashes,
- never infer BCPC fan ownership from Manual mode alone,
- never auto-start AppleSMC merely because a stale recovery marker exists,
- preserve CPU-only Gaming when fan control is unavailable or safely declined,
- fail closed if fan state is ambiguous after writes begin.

BCPC does not perform CPU undervolting, CPU MSR writes, firmware modification, or display-timing modification in this release candidate.

## Installation details

### Release build

1. Download the `win-x64` ZIP and accompanying `.sha256` file from the BCPC GitHub Releases page.
2. Optionally verify the ZIP's SHA-256 against the accompanying `.sha256` file.
3. Extract the ZIP to a normal user-writable folder.
4. Run `BootCampPerformanceControl.exe`.

For fan control, separately install the tested Macs Fan Control dependency described above. BCPC does **not** bundle Macs Fan Control, AppleSMC, or any third-party driver.

The published Windows x64 build is self-contained and includes the required .NET runtime files.

### Administrator permissions

BCPC runs normally without blanket elevation. Windows requests administrator permission only for operations that require it, such as changing protected processor power settings or explicitly starting the AppleSMC compatibility service.

## Build from source

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

Create the self-contained Windows x64 release package:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-release.ps1
```

The publish script creates a versioned self-contained `win-x64` directory, ZIP, and accompanying ZIP `.sha256` file in `artifacts`.

## Documentation

- [Latest stable release](https://github.com/mksolutionsuniversal/BootCampPerformanceControl/releases/latest)
- [All releases and pre-releases](https://github.com/mksolutionsuniversal/BootCampPerformanceControl/releases)
- [Hardware Compatibility](docs/HARDWARE-COMPATIBILITY.md)
- [Fan Control and AppleSMC Compatibility Backend](docs/FAN-CONTROL.md)
- [0.5.0-rc.1 Hardware Validation Record](docs/0.5.0-rc.1-HARDWARE-VALIDATION.md)
- [Third-Party Software](THIRD_PARTY.md)
- [Security Policy](SECURITY.md)
- [Changelog](CHANGELOG.md)

## License

BootCamp Performance Control is licensed under the [MIT License](LICENSE).

Third-party software is not covered by the BCPC MIT license. See [THIRD_PARTY.md](THIRD_PARTY.md).

## Disclaimer

BootCamp Performance Control is an independent open-source project by MK Universal Solutions LTD and is not affiliated with, endorsed by, or sponsored by Apple Inc., Microsoft Corporation, CrystalIDEA, or Macs Fan Control.

Apple, Mac, MacBook Pro, Boot Camp, Windows, Microsoft, Macs Fan Control, CrystalIDEA, and other product names are trademarks or registered trademarks of their respective owners.
