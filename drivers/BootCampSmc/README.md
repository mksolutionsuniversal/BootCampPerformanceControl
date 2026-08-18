# BootCampSmc driver research boundary

`BootCampSmc.sys` is the planned original open-source Windows kernel transport for BootCamp Performance Control.

The current implementation is a **Gate 2 KMDF function-driver skeleton**. It is source-complete enough to build as a driver package, but it must not be installed until the project passes the remaining build/package review steps documented below.

## Independently verified interoperability facts

On the physically tested `MacBookPro16,1` under Windows 10 Boot Camp:

- the relevant ACPI device is `ACPI\APP0001`,
- Apple installs the device through `oem120.inf` using `NullDeviceInstall.NT`,
- the Apple services section contains `AddService = ,2`, which is the Windows NULL-driver form,
- there is no device service, no device/class upper filter and no device/class lower filter,
- the boot resource set is:
  - I/O ports `0x300-0x31F` (32 ports),
  - memory `0xFE0B0000-0xFE0BFFFF` (64 KiB),
  - IRQ 6,
- before a function driver is bound, no allocated logical configuration is reported,
- the research oracle reported protocol `MMIO`,
- the user-mode SMC fan read/write protocol has been independently recovered and physically validated,
- Maximum Safe RPM and Apple Auto round-trip behavior is physically validated,
- arbitrary SMC writes are outside project scope.

The observed boot addresses are discovery evidence only. Production code must consume the system-assigned translated resources delivered by PnP and must not hard-code the boot resource addresses.

## Gate 1 - PnP/resource identity: complete

`BootCampPerformanceControl.SmcResourceProbe` established the existing device-stack and firmware resource identity without opening a device handle or touching registers.

Architecture decision:

- `BootCampSmc.sys` is a normal KMDF **function driver** for `ACPI\APP0001`,
- it is not a filter driver,
- it is not a software-only driver,
- it must receive hardware resources through `EvtDevicePrepareHardware`.

## Gate 2 - KMDF skeleton: current phase

The current skeleton:

- binds only to `ACPI\APP0001`,
- uses normal KMDF PnP callbacks,
- receives raw and translated resource lists in `EvtDevicePrepareHardware`,
- records the first translated port, memory and interrupt descriptors in device context,
- logs resource metadata for diagnostics,
- clears the cached metadata in `EvtDeviceReleaseHardware`,
- exposes no SMC device interface or SMC IOCTL,
- maps no physical memory,
- reads no MMIO register,
- writes no MMIO register,
- reads no I/O port,
- writes no I/O port,
- connects no interrupt,
- performs no SMC transaction.

The driver project is intentionally separate from the .NET solution because normal application CI must not depend on a local WDK installation.

### Gate 2 validation required before installation

Before any physical driver installation:

1. Build `BootCampSmc.vcxproj` with an installed Windows Driver Kit.
2. Require zero compiler warnings and zero errors.
3. Run Microsoft INF/package validation tools against `BootCampSmc.inf`.
4. Inspect the generated package and signing state.
5. Re-audit the source to confirm that no register-access primitive exists.
6. Prepare an explicit uninstall/rollback command that restores the Apple NULL-driver package for `ACPI\APP0001` if the test driver does not start cleanly.
7. Only then perform the first physical PnP bind test.

The first physical test is allowed to prove only that:

- the driver binds to `ACPI\APP0001`,
- PnP starts it without a problem code,
- `EvtDevicePrepareHardware` receives translated port/memory/interrupt resources,
- the translated resources correspond to the already observed firmware resource identity.

No MMIO or I/O-port access is permitted during this test.

## Gate 3 - read-only MMIO mapping

Only after Gate 2 physical PnP/resource delivery is verified:

- map only the assigned translated `CmResourceTypeMemory` range,
- keep the mapping bounded to the resource length,
- add no arbitrary physical-memory API,
- perform no register writes,
- independently establish the required register/protocol behavior before implementing SMC reads.

## Gate 4 - constrained SMC read transport

Implement only the read operations required by the existing `ISmcTransport` contract and compare returned values with the physically validated research path.

## Gate 5 - constrained fan writes

Only after read parity is established:

- expose only Apple Auto and Maximum Safe RPM semantics,
- retain the existing ownership marker, readback verification and rollback logic,
- never expose generic `WriteSmcKey`, arbitrary RPM, arbitrary MMIO, MSR, PCI config-space or physical-memory access.

## Safety rules

- No hard-coded MMIO base address.
- No register offset is accepted solely because it appears in a third-party binary.
- No kernel write is enabled before a read-only validation phase.
- No T1/classic support is inferred from the T2-era `MacBookPro16,1` result.
- No proprietary driver or proprietary source code is redistributed or copied.
- Failure to identify a safe backend means fan writes stay disabled.
