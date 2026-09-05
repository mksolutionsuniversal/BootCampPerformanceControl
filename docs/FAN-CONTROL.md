# Fan Control and AppleSMC Compatibility Backend

BootCamp Performance Control uses a conservative, model-gated fan-control design.

The first production fan-control goal is deliberately narrow:

- **Apple Auto**
- **Maximum Safe RPM**

BCPC does not expose an arbitrary fan-speed slider in `0.4.0`.

## Current production support

Production fan writes are currently enabled only for the exact verified model:

```text
MacBookPro16,1
MacBook Pro 16-inch (2019)
Apple T2
```

Other Intel, T1 or T2 Macs are not assumed compatible simply because they use Apple SMC hardware.

## Third-party compatibility dependency

BCPC does not ship its own production AppleSMC Windows driver in this release.

The verified production backend interoperates with the AppleSMC compatibility driver installed by a separate Windows installation of **Macs Fan Control 1.5.16 (Build 693)**.

Official upstream release:

https://github.com/crystalidea/macs-fan-control/releases/tag/v1.5.16

Use the official Windows installer:

```text
macsfancontrol_setup.exe
```

Do **not** manually copy `applesmc.sys` into Windows.

The validated environment reported:

```text
Service: AppleSMC
Driver: applesmc.sys
Driver FileVersion: 1.0.7.0
```

Preserved hash for the tested driver:

```text
SHA-256:
2E35DF03B80EF6FC6DA53C44A3C9454C945F4822C8F1F3355EEA2D1E06E53FD5
```

Other Macs Fan Control versions may work, but they are not currently part of BCPC's verified compatibility matrix.

## Legal and distribution boundary

BCPC follows a bring-your-own-binary compatibility model:

```text
User independently installs Macs Fan Control
        ->
BCPC detects the separately installed AppleSMC service/device
        ->
BCPC uses the compatible transport only on an explicitly verified model
```

The BCPC repository and release packages must not:

- redistribute Macs Fan Control binaries,
- bundle `applesmc.sys`,
- mirror the proprietary installer,
- copy proprietary implementation code,
- bypass licensing, activation or DRM,
- present the third-party driver as a BCPC component.

Known-compatible third-party versions are documented only for interoperability and reproducibility.

See [../THIRD_PARTY.md](../THIRD_PARTY.md).

## Before enabling fan monitoring

1. Install Macs Fan Control 1.5.16 from the official upstream release.
2. Close the Macs Fan Control application if it is running.
3. Start BCPC normally.
4. Confirm that BCPC detects the exact Mac model.
5. If BCPC reports the AppleSMC service as installed but stopped, use **Enable Fan Monitoring**.

The AppleSMC device is exclusive. BCPC does not kill another fan-control application and does not silently steal a device handle from another controller.

## Explicit service activation

Normal BCPC startup does not automatically start AppleSMC.

**Enable Fan Monitoring** is the explicit user action that may launch the small elevated helper and start the already installed `AppleSMC` Windows service.

The normal fan polling path remains non-elevated.

Gaming Optimised does not silently elevate or start AppleSMC. On the exact verified `MacBookPro16,1` path, if the required service/capability is unavailable, Gaming Optimised fails closed rather than falling back to CPU-only execution.

## Verified capability gate

A running AppleSMC service by itself is not enough to enable writes.

Before production fan writes, BCPC verifies the exact hardware identity and a fresh SMC capability snapshot, including the expected transport, fan count, key metadata and plausible runtime values.

The current verified `MacBookPro16,1` schema includes:

```text
FNum  ui8   1 byte
F0Mx  flt   4 bytes
F1Mx  flt   4 bytes
F0Ac  flt   4 bytes
F1Ac  flt   4 bytes
F0Md  ui8   1 byte
F1Md  ui8   1 byte
F0Tg  flt   4 bytes
F1Tg  flt   4 bytes
```

BCPC does not hard-code the final target RPM. Maximum Safe RPM is derived from the fresh verified `F0Mx` / `F1Mx` values.

## Ownership rules

Observed Manual mode is not proof that BCPC owns the fans.

If Manual mode is detected without BCPC's persisted ownership context, BCPC treats the state as externally controlled and does not silently take over.

A new BCPC Maximum Safe RPM transaction requires both fans to be in verified Apple Auto before ownership is taken.

BCPC saves its ownership marker before the first fan hardware write.

## Gaming Optimised transaction

On `MacBookPro16,1`, Gaming Optimised is an atomic two-phase operation:

1. validate the requested processor profile,
2. read the expected current processor state,
3. open one short exclusive AppleSMC execution session,
4. obtain a fresh fan capability snapshot,
5. derive the Maximum Safe RPM plan,
6. persist BCPC fan ownership,
7. apply and verify Maximum Safe RPM,
8. apply and verify the processor profile.

If the fan phase fails, processor settings remain untouched.

If the processor phase fails after BCPC has taken fan ownership, BCPC attempts non-cancellable Apple Auto compensation and preserves processor rollback semantics.

## Restore ordering

For the verified fan-control path, Restore is intentionally ordered:

```text
FANS -> Apple Auto verified
then
POWER -> exact saved processor state
```

BCPC never re-applies Maximum Safe RPM merely because processor Restore fails afterward.

## Crash recovery

The ownership marker is durable across process termination.

If BCPC is killed while its verified override is active:

- the fans may remain in Manual / Maximum Safe RPM,
- the ownership marker remains,
- the processor restore snapshot remains.

On the next startup, BCPC does not infer the physical state from the marker alone. It re-reads the hardware and uses the existing recovery policy.

When recovery is safe and AppleSMC is already available, BCPC restores **fans only** to Apple Auto and clears the ownership marker only after verified read-back.

BCPC does not automatically restore the saved processor profile at startup. The user retains explicit control through **Restore Original Settings**.

If AppleSMC is stopped, startup recovery does not silently elevate or start it. Recovery remains pending until the user explicitly enables fan monitoring.

## Physical validation

The complete production path was physically validated on a real `MacBookPro16,1` / Apple T2 machine:

- Apple Auto baseline read-back,
- production WPF Gaming Optimised apply,
- both fans at Maximum Safe RPM in Manual mode,
- CPU Maximum State `95% / 95%`,
- processor boost disabled,
- normal Restore to Apple Auto + exact original processor state,
- forced BCPC process termination while Gaming Optimised was active,
- durable fan ownership and processor snapshot after the crash,
- automatic startup fan-only recovery,
- final explicit processor Restore.

## Future models

T1 and additional T2 Macs require separate physical validation.

Do not generalize the `MacBookPro16,1` fan mode keys, metadata, RPM ranges or transaction assumptions to another model without evidence.
