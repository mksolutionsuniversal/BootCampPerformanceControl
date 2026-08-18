# BootCampSmc driver research boundary

`BootCampSmc.sys` is the planned original open-source Windows kernel transport for BootCamp Performance Control.

The driver is intentionally advanced through small hardware-safety gates. Each gate must be physically validated on a supported machine before the next gate may introduce a broader hardware-access surface.

## Independently verified interoperability facts

On the physically tested `MacBookPro16,1` under Windows 10 Boot Camp:

- the relevant ACPI device is `ACPI\APP0001`,
- Apple installs the device through `oem120.inf` using `NullDeviceInstall.NT`,
- the Apple services section contains `AddService = ,2`, which is the Windows NULL-driver form,
- there is no Apple function service, device/class upper filter or device/class lower filter for this devnode,
- the boot resource set is:
  - I/O ports `0x300-0x31F` (32 ports),
  - memory `0xFE0B0000-0xFE0BFFFF` (64 KiB),
  - IRQ 6,
- with the Apple NULL driver bound, no allocated logical configuration is reported,
- when `BootCampSmc.sys` is bound as the function driver, Windows exposes the same port, memory and IRQ resources as allocated resources,
- the research oracle reported protocol `MMIO`,
- the user-mode SMC fan read/write protocol has been independently recovered and physically validated,
- Maximum Safe RPM and Apple Auto round-trip behavior is physically validated,
- arbitrary SMC writes are outside project scope.

The observed physical addresses are discovery evidence only. Production code must consume the system-assigned translated resources delivered by PnP and must never hard-code the observed addresses.

## Gate 1 - PnP/resource identity: complete

`BootCampPerformanceControl.SmcResourceProbe` established the device-stack and firmware resource identity without opening a device handle or touching registers.

Architecture decision:

- `BootCampSmc.sys` is a normal KMDF **function driver** for `ACPI\APP0001`,
- it is not a filter driver,
- it is not a software-only driver,
- it receives hardware resources through `EvtDevicePrepareHardware`.

## Gate 2 - KMDF skeleton/package validation: complete

The standalone WDK project and INF were validated with:

- x64 Release compilation,
- zero WDK build warnings and errors,
- INF validation,
- package signability validation,
- source and binary safety audits,
- a prepared Apple NULL-driver rollback package.

The Gate 2 skeleton exposed no device interface or SMC IOCTL and performed no MMIO mapping, register access, port access, interrupt connection or SMC transaction.

The driver project remains intentionally separate from the normal .NET solution because application CI must not require a local WDK installation.

## Gate 3 - physical function-driver bind and rollback: complete

Physical validation on `MacBookPro16,1` proved that:

- the test-signed package can be staged in Windows Test Mode,
- Windows selects `BootCampSmc` for `ACPI\APP0001`,
- the function driver starts with PnP status `OK` and problem code `0`,
- the `BootCampSmc` kernel service reaches `RUNNING`,
- the allocated resource set contains the expected translated port, memory and IRQ resources,
- no MMIO or I/O-port register is accessed,
- the device can be hot-rolled back to Apple's `oem120.inf` NULL driver without rebooting,
- after rollback the allocated logical configuration disappears again while the boot resource set remains available,
- the staged test package, stopped service metadata and Driver Store files can be removed cleanly,
- final rollback state is Apple `oem120.inf`, PnP `OK`, problem code `0`.

## Gate 4A - read-only MMIO map/unmap lifecycle: current phase

Gate 4A is deliberately narrower than a register-read experiment.

The driver may:

- enumerate only the translated resource list received by `EvtDevicePrepareHardware`,
- require exactly one non-empty translated `CmResourceTypeMemory` resource,
- fail closed if the memory-resource shape is unexpected,
- map exactly that system-assigned physical range with `MmMapIoSpaceEx`,
- request `PAGE_READONLY | PAGE_NOCACHE`,
- retain the mapping only until `EvtDeviceReleaseHardware`,
- unmap it with `MmUnmapIoSpace`.

Gate 4A must not:

- dereference the mapped address,
- call `READ_REGISTER_*` or `WRITE_REGISTER_*`,
- read or write I/O ports,
- connect the IRQ,
- expose a device interface or IOCTL,
- issue an SMC command,
- hard-code an MMIO base address or register offset.

The purpose of Gate 4A is only to prove the PnP-controlled map/unmap lifecycle on the physical machine.

## Gate 4B - first read-only MMIO observation

Only after Gate 4A physically passes may a later change introduce the first bounded read from the mapped range.

That experiment must establish the required register layout independently. No register offset is accepted merely because it appears in a proprietary binary or because it is conventional on another Mac generation.

## Gate 5 - constrained SMC read transport

Only after read-only MMIO behavior is independently established:

- implement the minimum read operations required by the existing `ISmcTransport` contract,
- compare returned metadata and values with the physically validated research path,
- keep arbitrary physical-memory and arbitrary SMC access unavailable.

## Gate 6 - constrained fan writes

Only after read parity is established:

- expose only Apple Auto and Maximum Safe RPM semantics,
- retain the existing ownership marker, readback verification and rollback logic,
- never expose generic `WriteSmcKey`, arbitrary RPM, arbitrary MMIO, MSR, PCI config-space or physical-memory access.

## Safety rules

- No hard-coded MMIO base address.
- No register offset is accepted solely because it appears in a third-party binary.
- No kernel write is enabled before read-only validation is complete.
- No T1/classic support is inferred from the T2-era `MacBookPro16,1` result.
- No proprietary driver or proprietary source code is redistributed or copied.
- Failure to identify a safe backend means fan writes stay disabled.
