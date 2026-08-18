# BootCamp Performance Control

BootCamp Performance Control is an open-source Windows utility for Intel Macs running Windows through Boot Camp. Its purpose is to reduce unnecessary CPU heat and thermal throttling using safe Windows power-management mechanisms.

## Alpha warning

This is an alpha release: `0.1.0-alpha.1`.

Use it only if you understand that hardware and power-management behavior can vary across Boot Camp installations. This alpha is model-conservative: the first enabled profile has been built strictly for the verified MacBookPro16,1 path, and unsupported or unverified models do not receive model-specific writes. When an original processor-state snapshot is available, use Restore to return to the pre-application processor state.

## Current alpha support

- Primary verified model: MacBookPro16,1
- Machine family: MacBook Pro 16-inch, 2019
- Operating system: Windows 10 Boot Camp
- Verified test CPU: Intel Core i9-9980HK

Enabled in `0.1.0-alpha.1`:

- Gaming Optimised
  - CPU Maximum State: 95%
  - Turbo Boost: Disabled
  - Display unchanged
- Restore
  - Restores the exact original processor power state saved before the first profile change

## Important safety behavior

- Reads the current power state before writes
- Persists an original restore snapshot before modification
- Verifies state after writes
- Attempts rollback on failed verification
- Restore returns to the saved original values, not assumed defaults
- Unsupported or unverified models do not receive model-specific writes

## Not implemented or not enabled in 0.1.0-alpha.1

- Custom fan control
- Balanced execution
- Full Performance execution
- Custom refresh-rate or display changes
- Undervolting
- MSR writes
- Kernel-mode drivers
- Firmware modifications

## Requirements

- Windows 10 running through Boot Camp on an Intel Mac
- .NET 8 SDK to build from source
- Administrator rights may be required by Windows power-management operations
- A verified supported model is required before model-specific writes are enabled

## Build

Build the solution:

```powershell
dotnet build BootCampPerformanceControl.sln -c Release
```

Create the self-contained Windows x64 alpha publish using the publish script:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-alpha.ps1
```

Or publish directly using the `win-x64-alpha` profile:

```powershell
dotnet publish src/BootCampPerformanceControl/BootCampPerformanceControl.csproj `
  /p:PublishProfile=win-x64-alpha `
  -o artifacts/BootCampPerformanceControl-0.1.0-alpha.1-win-x64
```

The published output will be placed in:
`artifacts/BootCampPerformanceControl-0.1.0-alpha.1-win-x64/`

The publish output is intentionally a standalone folder and not an installer.

## Test

Run the automated test suite:

```powershell
dotnet test BootCampPerformanceControl.sln -c Release
```

## Current project status

The first alpha focuses on a small verified control surface:

- Read hardware and current Windows power state
- Enable Gaming Optimised only for verified MacBookPro16,1 hardware
- Save the original power snapshot before the first profile write
- Enable Restore only when an original snapshot exists
- Keep Balanced, Full Performance, fan control, and display changes disabled

Do not assume support for other Intel Mac models until they have explicit verification and tests.
