# BootCampSmc experimental research driver

> **Status: experimental research only.** `BootCampSmc.sys` is not the production fan-control dependency for stable BCPC `0.4.0`, is not included in stable release packages, and must not be presented as a generally supported Apple SMC driver.

`BootCampSmc` is an independently authored KMDF function-driver research path for BootCamp Performance Control.

Its purpose is to establish a legally clean, hardware-safe native Windows transport for Apple SMC interoperability without copying or redistributing proprietary implementation code.

The current physically completed checkpoint is **Gate 5D-B** on the project's primary `MacBookPro16,1` / Apple T2 test machine.

## Product relationship

Stable BCPC `0.4.0` currently uses a different production path:

```text
BootCamp Performance Control
        |
        +-- Production compatibility backend
        |       |
        |       +-- separately installed AppleSMC compatibility driver
        |           from Macs Fan Control 1.5.16 (Build 693)
        |
        +-- Native BootCampSmc research path
                |
                +-- independently authored KMDF driver
                    research / fallback / future option
```

BCPC does not redistribute Macs Fan Control, `MacsFanControl.exe`, `macsfancontrol_setup.exe`, or `applesmc.sys`.

The native research driver is paused as a product dependency because public kernel-driver distribution introduces signing/certification and support complexity. That decision is practical; Gate 5D-B itself physically passed.

## Independently verified interoperability facts

On the physically tested `MacBookPro16,1` under Windows 10 Boot Camp:

- the relevant ACPI device is `ACPI\APP0001`,
- Apple's baseline device installation uses a NULL-driver form,
- the observed firmware resource set includes I/O ports, one MMIO range and IRQ 6,
- when `BootCampSmc.sys` is bound as the function driver, Windows exposes the system-assigned translated resources through PnP,
- the research path uses the translated resource supplied by Windows rather than a hard-coded physical base address,
- the T2-era research transport uses MMIO,
- the constrained SMC read-command transport has been physically exercised,
- fixed-key metadata queries for `F0Mx` and `F1Mx` completed successfully,
- Apple NULL-driver rollback was physically verified after the research run.

Observed physical addresses from the test machine are discovery evidence only. Production-quality driver code must always consume system-assigned translated resources and must never hard-code the observed MMIO base address.

## Current source boundary

The current source under `drivers/BootCampSmc/` implements the bounded Gate 5D-B research path.

The source includes fixed constants and routines for:

```text
GET_KEY_INFO(F0Mx)
GET_KEY_INFO(F1Mx)
```

The current implementation deliberately does **not** provide:

- arbitrary SMC key reads,
- arbitrary SMC key writes,
- generic user-controlled MMIO access,
- generic physical-memory access,
- arbitrary RPM control,
- CPU MSR access,
- PCI configuration-space access,
- a production BCPC fan-control device interface,
- a stable public kernel-driver API.

The constrained command path requires a writable non-cached MMIO mapping because issuing an SMC read command itself requires bounded MMIO register writes. That does not make the current driver an arbitrary-write transport.

## Gate history

### Gate 1 — PnP/resource identity: complete

`BootCampPerformanceControl.SmcResourceProbe` established the target device-stack and firmware resource identity without issuing SMC transactions.

Architecture decision:

- `BootCampSmc.sys` is a normal KMDF **function driver** for `ACPI\APP0001`,
- it is not a filter driver,
- it is not a software-only driver,
- hardware resources are received through `EvtDevicePrepareHardware`.

### Gate 2 — KMDF skeleton/package validation: complete

The standalone WDK project and INF were validated for x64 Release and package/signability checks.

At this stage the driver exposed no SMC transaction path and performed no register access.

The WDK project remains separate from the normal .NET solution so application CI does not require a local WDK installation.

### Gate 3 — physical function-driver bind and rollback: complete

Physical validation on `MacBookPro16,1` proved that:

- the test-signed package can bind to `ACPI\APP0001` in the controlled research environment,
- the KMDF function driver starts successfully,
- Windows supplies the expected translated hardware resources,
- the machine can be restored to Apple's NULL-driver baseline,
- the test package/service can be removed cleanly afterward.

### Gate 4A — MMIO map/unmap lifecycle: complete

Physical validation proved the PnP-controlled mapping lifecycle using the system-assigned memory resource.

The driver mapped only the required translated range and released the mapping in `EvtDeviceReleaseHardware`.

### Gate 4B — MMIO register-layout research: complete

Independent interoperability research identified the bounded T2-era MMIO transaction fields needed for the next research steps.

The first bounded status observation used offset `0x4005` and did not issue an SMC command.

### Gate 4C — first read-only MMIO observation: complete / physical PASS

The first physical register observation completed successfully on the real T2 SMC device.

This closed the original read-only MMIO gate and allowed progression to the constrained read-command transport.

### Gate 5C — bounded `READ_KEY FNum` transport: complete / physical PASS

The research driver advanced to the minimum fixed-key SMC read-command path required to read `FNum`.

The transaction was physically validated and remained constrained by CI safety checks.

### Gate 5D-B — bounded `GET_KEY_INFO(F0Mx/F1Mx)`: complete / physical PASS

This is the current closed research checkpoint.

The independently authored driver physically executed exactly the intended fixed-key metadata transactions:

```text
GET_KEY_INFO(F0Mx)
GET_KEY_INFO(F1Mx)
```

Both completed successfully with:

```text
completion NTSTATUS = 0x00000000
command result       = 0x00
```

Observed metadata:

```text
F0Mx:
type       = "flt "
length     = 4
attributes = 0x85

F1Mx:
type       = "flt "
length     = 4
attributes = 0x85
```

The controlled A/B/A validation ended with Apple's baseline device state restored and the staged research package removed.

Gate 5D-B is therefore a **closed experimental fact** and should not be rerun merely to accumulate duplicate confidence evidence.

## What remains future work

The native driver should not be expanded casually just because Gate 5D-B passed.

Any future native-driver work must continue as small, independently reviewable safety gates.

Potential later research may include only the minimum additional operations required by a clearly defined product need.

If constrained fan writes are ever added to this native path, they must preserve the same BCPC semantics already required by the production application:

- exact model whitelist,
- fresh capability validation,
- Apple Auto and Maximum Safe RPM only,
- ownership persisted before the first write,
- read-back verification,
- rollback/failsafe behaviour,
- no arbitrary SMC write API.

## Safety rules

- Never hard-code a physical MMIO base address.
- Never generalize the T2 `MacBookPro16,1` result to T1, classic Intel Macs, or other T2 models.
- Never expose arbitrary MMIO or arbitrary SMC access for convenience.
- Never expose generic fan RPM writes when the product requirement is only Apple Auto / Maximum Safe RPM.
- Never add CPU MSR, firmware, PCI config-space or arbitrary physical-memory operations to this driver as a shortcut.
- Every hardware-affecting expansion must be static-reviewed first and physically validated on the exact target model.
- A failed or ambiguous validation result means the capability remains disabled.

## Legal and clean-room boundary

The native driver must remain independently authored.

The project may use independently verifiable interoperability facts and license-compatible open-source references to corroborate protocol behaviour, but it must not copy proprietary implementation code.

The repository and release artifacts must not redistribute proprietary Macs Fan Control / CrystalIDEA binaries or AppleSMC driver files.

See:

- [`../../THIRD_PARTY.md`](../../THIRD_PARTY.md)
- [`../../docs/FAN-CONTROL.md`](../../docs/FAN-CONTROL.md)
- [`../../docs/HARDWARE-COMPATIBILITY.md`](../../docs/HARDWARE-COMPATIBILITY.md)

## Current conclusion

The native T2 transport research is technically useful and has reached a working Gate 5D-B checkpoint, but stable BCPC `0.4.0` intentionally ships **without** this kernel driver.

The public application must remain usable and safe even if native-driver research is paused indefinitely.
