# Hardware Compatibility

BootCamp Performance Control targets Intel Macs running Windows through Boot Camp.

Processor-profile availability and fan-write availability are intentionally separate concepts. A machine may be eligible for the conservative Windows processor profile while fan writes are unavailable, declined or blocked by the runtime safety gate.

## Current release status

- Stable `0.4.0`: exact `MacBookPro16,1` production fan-write gate.
- Release candidate `0.5.0-rc.2`: Apple-hardware-plus-Intel-CPU platform eligibility, dynamic fan topology, and mechanism-based `PerFanModeFloat32` and bounded `GlobalMaskFpe2` writers selected from exact live capability fingerprints.
- `0.5.0-rc.2` is prepared as the current release candidate for controlled compatibility testing.
- Physical fan-write validation is completed on `MacBookPro16,1` for `PerFanModeFloat32` and on `MacBookPro12,1` for the one-fan `GlobalMaskFpe2` write/readback/Apple Auto round trip.

Previous published RC identity:

```text
Tag:           v0.5.0-rc.1
Source commit: 27511afee7e1ae092bb53e63d8c1c96b73004c81
ZIP:           BootCampPerformanceControl-0.5.0-rc.1-win-x64.zip
ZIP SHA-256:   B2215F7C6846614F2F1606A5DC11DC2D0BB1A496C66ACBA523B607A8DC65DDD5
Tests:         589 / 589 PASS
```

Stable `0.4.0` remains unchanged and remains the latest stable release.

Passing the `0.5.0-rc.2` family gate is a runtime compatibility decision. It is **not** a statement that the detected Mac model has been physically tested by the BCPC project.

`0.5.0-rc.2` determines normal `SupportedIntelMac` eligibility directly from detected Apple hardware and an Intel CPU. Exact model identity remains useful hardware/reporting context, but it is neither a processor-profile whitelist nor a fan-write permission gate.

## Compatibility matrix

| Model / observed family | Processor profile | Fan monitoring | `0.5.0-rc.2` fan writes | Crash fan recovery | Physical evidence |
|---|---:|---:|---:|---:|---|
| `MacBookPro16,1` / `PerFanModeFloat32` | Yes | Yes | Capability-qualified | Yes | **Physically verified end-to-end** |
| `MacBookPro12,1` / `GlobalMaskFpe2` | Yes | Yes | Capability-qualified for proven one-fan topology | Implemented with v4 journal; fake restart-tested | **One-fan physical write/readback/Apple Auto round trip PASS** |
| Other `SupportedIntelMac` | Yes | Capability-dependent | Only when an exact bounded live family fingerprint passes | Only for valid BCPC-owned compatible-family state | No model-specific physical record required for platform eligibility |

## Global Gaming Optimised processor target

For every `SupportedIntelMac`, the current product profile is:

- Maximum Processor State AC/DC: `95% / 95%`
- Processor boost AC/DC: disabled
- Display refresh rate: unchanged

The strongest empirical workload evidence for this target comes from the primary `MacBookPro16,1` test machine. On that system, comparable CS2 testing showed roughly 8–10 °C lower CPU/GPU temperatures, no observed thermal throttling at `95%`, and essentially unchanged gameplay smoothness versus the hotter Turbo-enabled state. Values around `90%` produced noticeable performance loss.

The product therefore uses `95% / 95%` globally for `SupportedIntelMac`, but those measured CS2 performance/temperature results must not be generalized as if every Intel Mac has been benchmarked identically.

## Verified fan capability families

Fan-write permission is no longer granted by a Mac-model whitelist in this release candidate. BCPC re-reads the live AppleSMC capability immediately before a write and requires the complete guarded family fingerprint.

### Transport and fan count

Required for writes:

- AppleSMC protocol/transport: MMIO (`protocol 1`)
- `FNum`: `ui8`, length `1`, attributes `0x80`
- discovered fan count: at least `1` and within the supported single-decimal `F0..F9` range

`FNum = 0` is a valid passive/read-only topology but can never produce a fan write.

Read-only discovery may continue on an unverified transport when its read IOCTLs work, so community reports retain topology, metadata, raw values, and a candidate family. The write safety gate remains false unless the transport is MMIO protocol 1.

### PerFanModeFloat32 metadata

For every discovered fan index `i`, the following schema must match:

```text
F{i}Mx  flt   4 bytes   attributes 0x85
F{i}Ac  flt   4 bytes   attributes 0x84
F{i}Md  ui8   1 byte    attributes 0xD0
F{i}Tg  flt   4 bytes   attributes 0xD4
```

The mode values used by this family are:

- `0` = Apple Auto
- `1` = Manual

### GlobalMaskFpe2 metadata

```text
F{i}Mn  fpe2  2 bytes   attributes 0xC0
F{i}Mx  fpe2  2 bytes   attributes 0xC0
F{i}Ac  fpe2  2 bytes   attributes 0x90
F{i}Tg  fpe2  2 bytes   attributes 0xD0
F{i}Md  absent
FS!     ui16  2 bytes   attributes 0xC0
```

The RPM decoder is unsigned big-endian `raw / 4`. `FS! ` is the mode authority. Write support is bounded to the proven one- and two-fan masks; higher fan counts remain read-only even when the rest of the family schema matches.

The classifier records each optional key as `Available`, `ConfirmedAbsent`, or `ReadFailed`. Only backend-confirmed absence may match `F{i}Md absent` or `FS! absent`; transport and malformed-response failures never count as absence.

### Runtime sanity requirements

For every discovered fan:

- reported maximum RPM must be finite,
- maximum RPM must be greater than zero,
- maximum RPM must be no greater than `10000 RPM`,
- live actual/target values must be finite and within the bounded policy,
- mode must decode to a supported Auto/Manual value.

The `10000 RPM` value is deliberately broad anti-corruption protection. It is **not** an Apple specification, not a recommended fan speed and never a write target.

Maximum Safe RPM always comes from the **fresh live `F{i}Mx`** value for that fan.

### Ownership requirements

A new BCPC fan takeover additionally requires:

- current platform status `SupportedIntelMac`,
- a fresh complete capability-family match,
- every discovered fan in verified Apple Auto,
- no conflicting BCPC ownership marker,
- no external Manual state.

If fans are already Manual without valid BCPC ownership, BCPC does not silently take control. The CPU Gaming profile remains available independently.

## MacBookPro16,1 — PerFanModeFloat32 physical evidence

Primary physical validation machine:

- Model: `MacBookPro16,1`
- MacBook Pro 16-inch (2019)
- Apple T2
- Intel Core i9-9980HK
- AMD Radeon Pro 5500M
- Windows 10 Boot Camp, build 19045

### Live Phase B observations on 2026-09-05

Read-only baseline:

```text
F0 actual/max: 5036 / 5616 RPM
F1 actual/max: 4658 / 5200 RPM
Mode:          Apple Auto
Write state:   Available (verified SMC capability family)
CPU:           100 / 100
Boost:         2 / 2 (Aggressive)
```

Gaming Optimised read-back:

```text
F0 actual/max: 5587 / 5616 RPM
F1 actual/max: 5208 / 5200 RPM
Mode:          Manual
Write state:   Maximum Safe RPM detected (Manual mode)
CPU:           95 / 95
Boost:         0 / 0 (Disabled)
Restore:       original processor snapshot available
```

The `5208 RPM` instantaneous reading was eight RPM above the reported `5200 RPM` maximum while the commanded target remained the fresh live `5200 RPM` maximum. This small observed read-back variance is within the existing bounded runtime tolerance and is not evidence that BCPC commanded a value above `F1Mx`.

Normal Restore returned the fans to Apple Auto and restored the exact original processor state (`100 / 100`, boost `2 / 2`).

A forced-process termination was then physically tested while Gaming Optimised was active. On restart, BCPC returned the owned fans to Apple Auto while intentionally preserving `95 / 95`, boost `0 / 0` and the original processor Restore snapshot. Fan-only resume was then exercised without replacing that snapshot, followed by a successful final exact Restore.

See [0.5.0-rc.1 Hardware Validation Record](0.5.0-rc.1-HARDWARE-VALIDATION.md).

## MacBookPro12,1 — one-fan GlobalMaskFpe2 physical qualification

Physical validation on `2026-09-08` confirmed the bounded one-fan `GlobalMaskFpe2` mechanism on `MacBookPro12,1`.

Fresh pre-write state included:

```text
Protocol: Mmio (1)
FNum: 1
Family: GlobalMaskFpe2
FS! : 0000
F0Md: ConfirmedAbsent
F0Mx: 60DC = 6199 RPM
```

The controlled write/readback sequence was:

```text
FS! 0000 -> 0001
readback FS! = 0001
re-read F0Mx = 60DC
F0Tg <- exact raw 60DC
readback F0Tg = 60DC
FS! 0001 -> 0000
readback FS! = 0000
final family probe PASS
```

Only three SMC write attempts were issued: `FS! 0001`, `F0Tg 60DC`, and `FS! 0000`. The qualifier exited `0`, reported `PHYSICAL QUALIFICATION: PASS`, and the wrapper restored AppleSMC to its original `Stopped` state.

This physically qualifies the observed one-fan mechanism. It does not physically qualify two-fan `FS! = 0003`, does not establish generic T1 support, and does not qualify the native `BootCampSmc` research driver for writes.

See [0.5.0-rc.2 GlobalMaskFpe2 Hardware Validation Record](0.5.0-rc.2-GLOBALMASK-FPE2-HARDWARE-VALIDATION.md).

## Restore, clean exit and crash recovery

When BCPC has valid fan ownership/recovery context, explicit Restore remains ordered:

```text
FANS -> verified Apple Auto
then
POWER -> exact saved processor state
```

Clean exit and startup recovery are intentionally fan-safety operations rather than automatic processor Restore operations. They return BCPC-owned compatible-family fans to Apple Auto while preserving the user's processor Gaming state and saved processor snapshot.

Startup recovery additionally requires the current model to match the persisted marker model, current platform status to remain `SupportedIntelMac`, the AppleSMC backend to be available, and a fresh capability/topology/target match. Any mismatch prevents speculative writes and retains recovery context.

## Ownership-marker compatibility

The current runtime reads all existing marker schemas:

- schema v1: legacy two-fan `MacBookPro16,1` ownership document,
- schema v2: dynamic indexed fan targets,
- schema v3: explicit capability family, topology, targets, exact raw target state where required, timestamp and family-specific global mode state.
- schema v4: in-progress transaction journal with the exact pre-write Apple Auto target/mode baseline and exact expected target payloads.

Current transactions start with schema v4 and atomically move to a final marker after successful acquisition. The exact two-fan `MacBookPro16,1` / `PerFanModeFloat32` final marker remains schema v1 for stable `0.4.0` downgrade recovery; other final markers use schema v3. This persistence-only model check is not a runtime writer whitelist. Legacy v1/v2 documents are accepted only as `PerFanModeFloat32` ownership and are never reinterpreted as global-mask ownership.

Older versions cannot represent `GlobalMaskFpe2` ownership. Restore Apple Auto before downgrading to a version that predates this family.

Malformed or unknown marker schemas are preserved and fail closed rather than being deleted or guessed.

## MacBookPro14,3 — T1 test machine

Runtime classification does not treat `MacBookPro14,3`, any other model, or a T1/T2 label as a writer selector.

Known project state:

- Intel MacBook Pro 15-inch (2017)
- Touch Bar / Apple T1
- dedicated Radeon GPU
- Windows Boot Camp
- thermal throttling observed
- `99%` Maximum Processor State improved behaviour in informal testing
- reliable comparative `95%` benchmarking remains deferred until cooling-system maintenance is completed

Physical one-fan `GlobalMaskFpe2` write/readback/Apple Auto qualification is now complete on `MacBookPro12,1`. That is mechanism evidence only and must not be generalized into a T1-wide compatibility claim. A physical two-fan machine remains required before the `FS! = 0003` path can receive its own BCPC-owned physical write qualification.

Hard-crash recovery of the implementation's deterministic partial write prefixes remains covered by fake/in-memory restart tests. Physical hard-crash recovery for `GlobalMaskFpe2` has not yet been exercised and is not implied by the short controlled round trip.

## What capability-family support means in `0.5.0-rc.2`

It means BCPC can enable one guarded writer only when the **live** AppleSMC interface exactly matches that writer's verified mechanism schema and runtime state.

It does **not** mean:

- every Mac in a chip generation has been physically tested,
- every machine containing a T1 or T2 chip is guaranteed to expose either known schema,
- model identity alone can enable writes,
- BCPC will generate unknown SMC timings/keys or guess a fan-control protocol.

Physical write references now include `MacBookPro16,1` for `PerFanModeFloat32` and `MacBookPro12,1` for the one-fan `GlobalMaskFpe2` round trip. Additional machines should still be validated with read-only capability capture first, then controlled write/read-back/Auto-restore testing.

## AppleSMC compatibility dependency

The physically verified environment uses a separately installed copy of:

- Macs Fan Control `1.5.16`
- application build: `693`
- AppleSMC driver file version: `1.0.7.0`

Preserved forensic hashes for the tested environment:

```text
Macs Fan Control 1.5.16 installer:
A87E90FB6BEE36BE1A8076F79B9A90C79AD386680B26485EB04705E09BB8439C

MacsFanControl.exe 1.5.16.693:
E2727AF9BC1ECF0A6BC1F67A7865DF19A0F5160C8FCBE8DDAA6BFB24B73109F3

applesmc.sys 1.0.7.0:
2E35DF03B80EF6FC6DA53C44A3C9454C945F4822C8F1F3355EEA2D1E06E53FD5
```

These hashes document the tested interoperability environment. BCPC does not redistribute any of these third-party files.

The official upstream release page for the tested version is:

https://github.com/crystalidea/macs-fan-control/releases/tag/v1.5.16

See [Fan Control and AppleSMC Compatibility Backend](FAN-CONTROL.md) for installation and safety details.

## Native BootCampSmc research driver

The repository also contains an independently authored experimental KMDF research driver under `drivers/BootCampSmc/`.

Its physically completed T2 research boundary currently reaches Gate 5D-B fixed-key `GET_KEY_INFO(F0Mx/F1Mx)` metadata transactions on `MacBookPro16,1`.

This research driver is not the production fan-control dependency for `0.5.0-rc.2`, is not included in release packages, and is not claimed as physically write-qualified or as generic T1/T2 support.

## Safe validation order for another Mac

For a new Intel Mac, especially an untested T2-era model, use this order:

1. capture model/CPU/GPU/Windows information,
2. perform read-only AppleSMC protocol and `FNum` discovery,
3. inspect per-fan metadata and live values,
4. stop if the capability family does not match exactly,
5. only then perform controlled Maximum Safe RPM write/read-back testing,
6. verify Apple Auto Restore,
7. verify processor snapshot/Restore,
8. test crash/startup recovery only after the earlier stages pass.

Do not use another model merely as evidence that T1/T2 behaviour is interchangeable.
