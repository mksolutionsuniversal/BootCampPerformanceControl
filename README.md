# BootCamp Performance Control

BootCamp Performance Control is an open-source Windows utility for Intel Macs running Windows through Boot Camp. Its purpose is to reduce unnecessary CPU heat and thermal throttling using safe Windows power-management mechanisms.

## Release status

- **Current release:** `0.2.0`
- **Previous release:** `0.1.0-alpha.1`

## Compatibility model

BootCamp Performance Control targets Intel Macs running Windows through Boot Camp.

Execution permission is capability-based:

- **Platform support:** Gaming Optimised is available on supported Intel Mac platforms (`SupportedIntelMac`) when the required Windows processor power settings can be read successfully.
- **Model validation:** Model validation metadata is informational and does not grant or deny execution permission.

### Validation metadata status

- **MacBookPro16,1** (`PerformanceValidated`):
  - 16-inch MacBook Pro (2019, Intel Core i9-9980HK)
  - Full Apply / exact read-back / restart persistence / Restore round-trip tested
  - Thermal and performance workload testing completed
- **MacBookPro14,3** (`NotIndividuallyTested`):
  - 15-inch MacBook Pro (2017)
  - Application startup and processor power setting application have been observed
  - Full controlled Restore round-trip has not yet been independently confirmed
- **Other supported Intel Macs** (`NotIndividuallyTested` unless separately validated):
  - Eligible for Gaming Optimised execution when Apple hardware, Intel CPU, and Windows processor power settings are readable
  - A first-use confirmation warning is shown before applying Gaming Optimised on models that have not been individually performance-tested

## Profiles

The product exposes two actions:

- **Gaming Optimised**
  - Maximum Processor State AC/DC: `95%` / `95%` (`PROCTHROTTLEMAX`)
  - CPU Boost AC/DC: `Disabled (0)` / `Disabled (0)` (`PERFBOOSTMODE`)
- **Restore Original Settings**
  - Restores the exact original saved processor power settings captured before changes were applied

## Safety behavior

BootCamp Performance Control follows strict fail-closed safety principles:

- Reads current Windows processor power state before any writes
- Captures and persists an original Restore snapshot before any modification
- Uses expected-state preconditions to detect concurrent configuration changes
- Performs exact read-back verification after writing Windows processor power settings
- Executes an automatic rollback attempt if write or read-back verification fails
- Restores exact original saved values rather than assumed factory defaults
- Model validation is not a write-permission gate

## Out of scope / Not included in 0.2.0

- Fan control (planned for a future release; not included in version 0.2.0)
- Custom refresh-rate or display changes
- CPU undervolting
- MSR (Model-Specific Register) writes
- Kernel-mode drivers
- Firmware modifications

## Requirements

- Intel Mac running Windows (Windows 10 / Windows 11) through Boot Camp
- .NET 8 SDK to build from source
- Administrator permissions (required for Windows power-management operations)

## Build

Build the solution:

```powershell
dotnet build BootCampPerformanceControl.sln -c Release
```

Create the self-contained Windows x64 release publish using the publish script:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-release.ps1
```

Or publish directly using the `win-x64-release` profile:

```powershell
dotnet publish src/BootCampPerformanceControl/BootCampPerformanceControl.csproj `
  /p:PublishProfile=win-x64-release `
  -o artifacts/BootCampPerformanceControl-0.2.0-win-x64
```

## Test

Run the automated test suite:

```powershell
dotnet test BootCampPerformanceControl.sln -c Release
```

## Security

Please see [SECURITY.md](SECURITY.md) for vulnerability reporting and hardware-safety information.

## License

This project is licensed under the [MIT License](LICENSE).

## Disclaimer

This is an independent open-source project and is not affiliated with, endorsed by, or sponsored by Apple Inc. or Microsoft Corporation.

Apple, Mac, MacBook Pro, Boot Camp, Windows, and Microsoft are trademarks or registered trademarks of their respective owners.
