# Support

BootCamp Performance Control is an open-source project for Intel Macs running Windows through Boot Camp.

Before reporting a problem, check the current [Hardware Compatibility](docs/HARDWARE-COMPATIBILITY.md), [Fan Control](docs/FAN-CONTROL.md), [Third-Party Software](THIRD_PARTY.md), and [latest release](https://github.com/mksolutionsuniversal/BootCampPerformanceControl/releases/latest) documentation.

## Bug reports and compatibility issues

Use GitHub Issues for normal bugs, compatibility reports and feature requests.

For hardware-related reports, include as much of the following as possible:

- BCPC version,
- Mac model identifier, for example `MacBookPro16,1`,
- Windows version,
- CPU and GPU,
- active Windows power plan when relevant,
- whether the problem involves Apply, Restore, startup recovery, clean exit, tray behaviour or fan control,
- whether Macs Fan Control / the AppleSMC compatibility service is separately installed,
- Macs Fan Control version and AppleSMC driver file version when known,
- the expected behaviour,
- the observed behaviour,
- reproducible steps,
- relevant BCPC logs or compatibility/diagnostic reports with personal information removed.

For fan-control problems, also report the observed fan mode and RPM state before and after the issue if this can be collected safely without performing additional writes.

## Unsupported hardware

A request for support on a different Intel Mac does **not** imply that BCPC fan writes should be enabled for that model.

Current production fan writes are physically verified only for `MacBookPro16,1`.

Other models remain disabled until they have independent read/write/read-back/Restore evidence and are deliberately whitelisted.

Do not test speculative SMC writes merely to produce a bug report.

## Processor profile guidance

The Gaming Optimised `95%` processor limit is an empirically validated result for `MacBookPro16,1`.

It is not a universal recommendation for every Intel Mac. Reports from other models are useful evidence, but new defaults require separate validation.

## Fan-control dependency

Stable `0.4.0` does not ship a production AppleSMC kernel driver.

The verified production fan-control environment uses a **separately installed** copy of Macs Fan Control `1.5.16` (Build `693`) with AppleSMC driver file version `1.0.7.0`.

BCPC does not redistribute these third-party binaries.

If AppleSMC is installed but stopped, use BCPC's explicit **Enable Fan Monitoring** action rather than manually copying driver files or forcing undocumented service changes.

## Native BootCampSmc research driver

The `drivers/BootCampSmc/` tree is experimental research and is not the production dependency for stable `0.4.0`.

Its current physically completed checkpoint is Gate 5D-B on `MacBookPro16,1`.

Do not install or modify the research driver as a troubleshooting step for a normal BCPC support issue unless you are deliberately participating in a separately defined research gate.

## Security vulnerabilities

Do **not** report security-sensitive vulnerabilities in a public issue.

Follow [`SECURITY.md`](SECURITY.md). Use GitHub private vulnerability reporting when available; otherwise use the security contact listed there.

## Safety first

If BCPC reports an unsupported model, unavailable capability, ambiguous ownership state or failed verification, treat the fail-closed result as intentional.

Do not bypass model gates, edit SMC keys manually, or disable Restore/recovery safeguards to make a support scenario proceed.
