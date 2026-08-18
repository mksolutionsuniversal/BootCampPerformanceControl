# BootCampSmc driver research boundary

`BootCampSmc.sys` is the planned original open-source Windows kernel transport for BootCamp Performance Control.

This directory intentionally does **not** contain an installable driver yet.

## Independently verified interoperability facts

On the physically tested `MacBookPro16,1` under Windows 10 Boot Camp:

- the relevant ACPI device is exposed under `ACPI\APP0001`,
- the research oracle reported protocol `MMIO`,
- static interoperability research observed memory-resource discovery and MMIO mapping behavior,
- the user-mode SMC protocol for fan read/write operations has been independently recovered and physically validated,
- Maximum Safe RPM and Apple Auto round-trip behavior is physically validated,
- arbitrary SMC writes are outside project scope.

These facts are sufficient to define the desired high-level transport contract, but they are **not** sufficient to safely hard-code a physical address, register layout, or PnP binding strategy.

## Current phase: resource discovery only

Before creating an INF or mapping any register, the project must determine the actual Windows device-stack and resource assignment for `ACPI\APP0001` on the verified target.

`BootCampPerformanceControl.SmcResourceProbe` therefore collects, read-only:

- ACPI device instance IDs,
- registry `Service`, `Driver`, class and hardware metadata,
- `LogConf` registry data,
- allocated logical resources,
- boot logical resources,
- decoded physical memory ranges for `ResType_Mem` descriptors.

The probe does not:

- open `\\.\APPLESMC`,
- install a driver,
- claim an ACPI device,
- map physical memory,
- read or write MMIO registers,
- issue SMC commands.

## Driver implementation gates

### Gate 1 - PnP/resource identity

Required evidence:

- exact `ACPI\APP0001` instance metadata,
- existing service/function-driver relationship,
- allocated and boot memory resources,
- confirmation that the resource assignment is stable and belongs to the Apple SMC device.

No INF binding is allowed before this gate is reviewed.

### Gate 2 - KMDF skeleton

After Gate 1, create a minimal KMDF driver that:

- uses normal PnP callbacks,
- records translated resources in `EvtDevicePrepareHardware`,
- releases them in `EvtDeviceReleaseHardware`,
- exposes diagnostics only,
- performs no SMC protocol transactions.

### Gate 3 - read-only MMIO mapping

Only after the translated resource is unambiguous:

- map only the assigned translated `CmResourceTypeMemory` range,
- keep the mapping bounded to the resource length,
- add no arbitrary physical-memory API,
- perform no writes,
- validate register/protocol behavior against the existing research oracle.

### Gate 4 - constrained SMC read transport

Implement only the read operations required by the existing `ISmcTransport` contract and compare returned values with the physically validated research path.

### Gate 5 - constrained fan writes

Only after read parity is established:

- expose only Apple Auto and Maximum Safe RPM semantics,
- retain the existing ownership marker, readback verification and rollback logic,
- never expose generic `WriteSmcKey`, arbitrary RPM, arbitrary MMIO, MSR, PCI config-space or physical-memory access.

## Safety rules

- No hard-coded MMIO base address.
- No register offset is accepted solely because it appears in a third-party binary.
- No kernel write is enabled before a read-only validation phase.
- No T1/classic support is inferred from the T2-era MacBookPro16,1 result.
- No proprietary driver or proprietary source code is redistributed or copied.
- Failure to identify a safe backend means fan writes stay disabled.
