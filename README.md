# BootCamp Performance Control

BootCamp Performance Control (BCPC) is an open-source Windows utility for Intel Macs running Windows through Boot Camp.

Its goal is to reduce unnecessary heat and thermal throttling using conservative Windows processor power management and, on explicitly verified hardware, guarded Apple SMC fan control.

## Release status

- **Current release train:** `0.4.0-rc.1`
- **Release type:** pre-release / release candidate
- **Fan-control milestone:** physically validated on `MacBookPro16,1` (MacBook Pro 16-inch, 2019, Apple T2)

`0.4.0-rc.1` is intentionally not called `1.0`. Fan writes remain model-gated and are enabled only where the hardware path has been independently validated.

## What BCPC does

### Gaming Optimised

On the fully validated `MacBookPro16,1` path:

- Maximum Processor State AC/DC: `95% / 95%`
- Intel processor boost (`PERFBOOSTMODE`) AC/DC: `Disabled / Disabled`
- Fans: `Maximum Safe RPM`, derived from the live verified SMC maximum values
- Display settings: unchanged

On other supported Intel Macs, Gaming Optimised remains processor-only unless that exact model is separately verified for fan writes.

### Restore Original Settings

BCPC restores the actual state captured before the profile was applied. It does not assume that the previous values were `100%` or Windows defaults.

For the verified `MacBookPro16,1` fan-control path, Restore returns the fans to Apple Auto before restoring the saved Windows processor power state.

### Fan monitoring and recovery

On verified hardware BCPC can:

- read live fan RPM, maximum RPM and mode,
- report Apple Auto / Manual state,
- apply Maximum Safe RPM through a guarded transaction,
- persist fan-override ownership before the first hardware write,
- recover the fans to Apple Auto after an unexpected BCPC process termination,
- leave the saved processor profile untouched during startup fan recovery so the user can explicitly choose Restore.

## Hardware compatibility

### Fully verified

**MacBookPro16,1 — MacBook Pro 16-inch (2019), Apple T2**

Physical validation includes:

- Windows processor Apply / exact read-back / Restore,
- production fan monitoring,
- Maximum Safe RPM apply,
- Apple Auto restore,
- forced-process-crash recovery,
- exact processor-state restoration after recovery.

Primary validation machine:

- Windows 10 Boot Camp
- Intel Core i9-9980HK
- AMD Radeon Pro 5500M

### Not yet fan-write verified

**MacBookPro14,3 — MacBook Pro 15-inch (2017), Apple T1**

Processor power-management behaviour has been observed, but the BCPC fan-write backend is not enabled for this model. Independent T1 fan write/read-back/restore validation is still required.

### Other Intel Macs

Processor profile availability is capability-based. Fan writes are **not** automatically enabled because a Mac has an Intel CPU, T1, or T2. Every fan-write model must be explicitly verified and whitelisted.

See [Hardware Compatibility](docs/HARDWARE-COMPATIBILITY.md) for the detailed matrix.

## Fan-control dependency: Macs Fan Control 1.5.16

BCPC does **not** include, redistribute, modify, mirror, or install Macs Fan Control or its AppleSMC driver.

The currently verified AppleSMC compatibility backend interoperates with a **separately installed** Windows copy of:

- **Macs Fan Control 1.5.16 (Build 693)**
- AppleSMC driver file version observed in the validated environment: `1.0.7.0`

Download and install that version from the official CrystalIDEA GitHub release:

- https://github.com/crystalidea/macs-fan-control/releases/tag/v1.5.16

Use the official Windows installer (`macsfancontrol_setup.exe`). Do not copy `applesmc.sys` manually.

After installation:

1. Close the Macs Fan Control application if it is running. The AppleSMC device is exclusive and BCPC will not forcibly take ownership from another controller.
2. Start BCPC normally.
3. On a verified model, use **Enable Fan Monitoring** if the AppleSMC service is installed but stopped. This is the explicit action that may request elevation to start the service.
4. Apply **Gaming Optimised** only after BCPC reports the verified fan capability as available.

Other versions of Macs Fan Control may work, but they are not part of the current verified compatibility matrix.

See [Fan Control and AppleSMC Compatibility Backend](docs/FAN-CONTROL.md) and [Third-Party Software](THIRD_PARTY.md).

## Safety model

BCPC follows fail-closed rules for hardware-affecting operations:

- read current state before writing,
- persist the original processor Restore snapshot before modification,
- verify expected current state before processor writes,
- re-read fan capability immediately before fan writes,
- derive fan targets from live verified SMC maxima rather than hard-coded RPM values,
- allow only whitelisted fan mode/target keys,
- verify hardware state after writes,
- attempt Apple Auto compensation if the processor phase fails after fan takeover,
- restore fans before processor settings,
- retain recovery context across crashes,
- never infer BCPC fan ownership from Manual mode alone,
- never auto-start AppleSMC merely because a stale recovery marker exists,
- never write fans on an unverified model.

BCPC does not perform CPU undervolting, CPU MSR writes, firmware modification, or display-timing modification in this release.

## Installation

### Release build

1. Download the `win-x64` ZIP and accompanying `.sha256` file from the BCPC GitHub Releases page.
2. Optionally verify the ZIP's SHA-256 against the accompanying `.sha256` file.
3. Extract the ZIP to a normal user-writable folder.
4. Run `BootCampPerformanceControl.exe`.

For verified fan control, separately install the tested Macs Fan Control
dependency described above. BCPC does **not** bundle Macs Fan Control, AppleSMC,
or any third-party driver.

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

The publish script creates a versioned self-contained `win-x64` directory, ZIP,
and accompanying ZIP `.sha256` file in `artifacts`.

## Documentation

- [Hardware Compatibility](docs/HARDWARE-COMPATIBILITY.md)
- [Fan Control and AppleSMC Compatibility Backend](docs/FAN-CONTROL.md)
- [Third-Party Software](THIRD_PARTY.md)
- [Security Policy](SECURITY.md)
- [Changelog](CHANGELOG.md)

## License

BootCamp Performance Control is licensed under the [MIT License](LICENSE).

Third-party software is not covered by the BCPC MIT license. See [THIRD_PARTY.md](THIRD_PARTY.md).

## Disclaimer

BootCamp Performance Control is an independent open-source project by MK Universal Solutions LTD and is not affiliated with, endorsed by, or sponsored by Apple Inc., Microsoft Corporation, CrystalIDEA, or Macs Fan Control.

Apple, Mac, MacBook Pro, Boot Camp, Windows, Microsoft, Macs Fan Control, CrystalIDEA, and other product names are trademarks or registered trademarks of their respective owners.
