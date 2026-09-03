# Security Policy

## Supported Versions

Security fixes are provided for the latest released version of BootCamp Performance Control and, where practical, the current release candidate.

## Reporting a Vulnerability

Please do not report security-sensitive vulnerabilities in a public GitHub issue.

Use GitHub private vulnerability reporting when available.

If private vulnerability reporting is unavailable, report security-sensitive issues by email to security@mkus.co.uk.

Security reports should include:

- the affected BCPC version,
- the affected Mac model and Windows version when relevant,
- whether the AppleSMC compatibility backend was installed/running,
- steps to reproduce the issue,
- the expected and observed behaviour,
- relevant logs with personal or sensitive information removed.

## Hardware Safety

BootCamp Performance Control can interact with:

- Windows processor power-management settings, and
- on explicitly verified hardware, Apple SMC fan-control state through a separately installed compatibility driver.

Reports involving any of the following are treated as safety-relevant:

- unexpected processor power-setting changes,
- failed or incomplete Restore operations,
- unexpected fan-mode or fan-target changes,
- failure to return verified hardware to Apple Auto,
- crash-recovery failures,
- fan writes occurring on an unverified model,
- recovery ownership markers being cleared without verified hardware recovery.

## Fan-control safety boundary

Production fan writes are model-gated and fail closed. The current verified production write path is limited to `MacBookPro16,1`.

BCPC does not infer ownership from observed Manual mode alone. A Manual state without BCPC ownership context is treated as externally controlled and is not silently taken over.

BCPC does not bundle or redistribute the third-party AppleSMC compatibility driver. See [THIRD_PARTY.md](THIRD_PARTY.md) and [docs/FAN-CONTROL.md](docs/FAN-CONTROL.md).
