# Hardware Compatibility

BootCamp Performance Control targets Intel Macs running Windows through Boot Camp.

Processor-profile availability and fan-write availability are intentionally separate concepts. A machine may be eligible for Windows processor power-management changes while remaining completely blocked from Apple SMC fan writes.

## Compatibility matrix

| Model | Platform | Processor profile | Fan monitoring | Fan writes | Crash fan recovery | Validation status |
|---|---|---:|---:|---:|---:|---|
| `MacBookPro16,1` | MacBook Pro 16-inch (2019), Intel, Apple T2 | Yes | Yes | Yes | Yes | **Physically verified** |
| `MacBookPro14,3` | MacBook Pro 15-inch (2017), Intel, Apple T1 | Observed / capability-gated | Not production-verified | **Disabled** | **Disabled** | Processor behaviour observed; fan validation pending |
| Other Intel Macs | Intel Boot Camp | Capability-gated | Model-dependent / not claimed | **Disabled unless explicitly whitelisted** | Disabled unless fan writes are verified | Not individually fan-write validated |

## MacBookPro16,1 — fully verified path

Primary physical validation machine:

- Model: `MacBookPro16,1`
- MacBook Pro 16-inch (2019)
- Apple T2
- Intel Core i9-9980HK
- AMD Radeon Pro 5500M
- Windows 10 Boot Camp

### Processor behaviour

Empirical workload testing on the primary machine established the current Gaming Optimised profile:

- Maximum Processor State: `95%`
- Processor boost: disabled
- Display refresh rate: unchanged

On this machine, `95%` removed the tested Turbo Boost behaviour, reduced CPU/GPU temperatures by roughly 8–10 °C in comparable CS2 testing, and avoided observed thermal throttling while preserving essentially the same gameplay smoothness. Lower values around `90%` caused noticeable performance loss and are therefore not used as the verified Gaming Optimised default.

### Fan behaviour

Production fan control has been physically validated end-to-end:

1. BCPC reads a fresh SMC capability snapshot.
2. The exact model, MMIO transport, fan count, key metadata and runtime ranges must pass the existing safety policy.
3. The Maximum Safe RPM plan is derived from the live verified `F0Mx` / `F1Mx` values.
4. BCPC persists its fan-override ownership marker before the first fan hardware write.
5. Both fans are moved to Manual mode and their targets are set to the verified live maxima.
6. Read-back verifies Manual mode and Maximum Safe RPM.
7. Processor settings are applied only after the fan phase succeeds.
8. Restore returns fans to Apple Auto before restoring the exact saved processor state.

### Crash recovery

A forced-process termination was physically tested while Gaming Optimised was active.

Observed and verified behaviour:

- fans remained in Manual / Maximum Safe RPM after the process was killed,
- the fan ownership marker remained on disk,
- the processor restore snapshot remained on disk,
- CPU Maximum State remained `95%`,
- processor boost remained disabled,
- a normal BCPC restart automatically restored **fans only** to Apple Auto,
- the fan ownership marker was cleared only after verified recovery,
- processor settings remained in the Gaming state,
- explicit Restore then returned the exact original processor state and removed the processor snapshot.

## MacBookPro14,3 — T1 test machine

`MacBookPro14,3` is intentionally not treated as equivalent to `MacBookPro16,1`.

Known project state:

- Intel MacBook Pro 15-inch (2017)
- Touch Bar / Apple T1
- dedicated Radeon GPU
- Windows Boot Camp
- thermal throttling observed
- `99%` Maximum Processor State improved behaviour in informal testing
- a reliable `95%` benchmark remains deferred until cooling-system maintenance is completed

BCPC production fan writes remain **disabled** on this model. Independent T1 read/write/read-back/restore validation is required before any whitelist expansion.

## What “T2 support” means in 0.4.0

The project has **initial verified Apple T2 fan-control support**, but that does **not** mean every T2 Mac is supported for fan writes.

The current production fan-write whitelist is the exact `MacBookPro16,1` path. Other T2 models must be independently validated before fan writes are enabled.

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
