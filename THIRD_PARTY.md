# Third-Party Software

BootCamp Performance Control (BCPC) is an independent open-source project by MK Universal Solutions LTD.

## Current release relationship

The same third-party distribution boundary applies to:

- stable `0.4.0`, and
- release candidate `0.5.0-rc.1`.

Neither release line bundles a production AppleSMC driver. `0.5.0-rc.1` broadens runtime fan-write eligibility through a guarded live capability-family policy, but that does not change the legal/distribution boundary described below.

## Macs Fan Control / CrystalIDEA

BCPC's optional AppleSMC compatibility backend can interoperate with a **separately and lawfully installed** compatible copy of Macs Fan Control for Windows.

The currently physically verified interoperability environment uses:

- Macs Fan Control `1.5.16`
- application build `693`
- AppleSMC driver file version `1.0.7.0`

Official upstream release page:

https://github.com/crystalidea/macs-fan-control/releases/tag/v1.5.16

BCPC does **not** distribute, modify, bundle, mirror or install Macs Fan Control or its AppleSMC driver.

Users are responsible for obtaining third-party software from the applicable vendor/upstream source and for complying with the vendor's terms.

Known-compatible third-party versions are listed only for interoperability, reproducibility and support purposes.

Macs Fan Control and CrystalIDEA are independent third-party products and are not affiliated with, endorsed by, sponsored by, or associated with BootCamp Performance Control or MK Universal Solutions LTD.

## Experimental native BootCampSmc research driver

The repository contains an independently authored experimental KMDF research driver under `drivers/BootCampSmc/`.

That research code is part of the BCPC source repository, but `BootCampSmc.sys` is **not** the production fan-control dependency for stable `0.4.0` or release candidate `0.5.0-rc.1` and is not included in their published ZIP artifacts.

Its current closed physical research checkpoint is Gate 5D-B fixed-key `GET_KEY_INFO(F0Mx/F1Mx)` metadata discovery on `MacBookPro16,1`.

See [`drivers/BootCampSmc/README.md`](drivers/BootCampSmc/README.md).

## No third-party binary redistribution

BCPC release artifacts must not contain:

- `macsfancontrol_setup.exe`,
- `MacsFanControl.exe`,
- `applesmc.sys`,
- other proprietary Macs Fan Control binaries,
- experimental `BootCampSmc.sys`, `.inf` or `.cat` driver-package files.

The release-packaging safety scan rejects prohibited driver/package content before a release ZIP is accepted.

BCPC also does not copy proprietary implementation code, bypass licensing/activation/DRM, or claim ownership of third-party components.

## Apple and Microsoft

BCPC is not affiliated with, endorsed by, or sponsored by Apple Inc. or Microsoft Corporation.

Apple, Mac, MacBook Pro, Boot Camp, Windows, Microsoft, Macs Fan Control, CrystalIDEA, and other product names are trademarks or registered trademarks of their respective owners.
