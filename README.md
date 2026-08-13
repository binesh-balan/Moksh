# MØKSH

[![Licence](https://img.shields.io/badge/licence-Apache--2.0-blue.svg)](Licence.txt)
[![Based on](https://img.shields.io/badge/based%20on-Bulk%20Crap%20Uninstaller-lightgrey.svg)](https://github.com/Klocman/Bulk-Crap-Uninstaller)

MØKSH is a free program uninstaller for Windows. It excels at removing large amounts of applications with minimal user input. It can clean up leftovers, detect orphaned applications, run uninstallers according to premade lists, and much more.

MØKSH is fully compatible with Windows Store Apps, Steam, Windows Features and has special support for many uninstalling systems (NSIS, InnoSetup, Msiexec, and many others).

---

## Attribution

**MØKSH is a modified distribution of [Bulk Crap Uninstaller](https://github.com/Klocman/Bulk-Crap-Uninstaller) by Marcin Szeniak (Klocman), used under the Apache License 2.0.**

All credit for the original application belongs to Marcin Szeniak and the Bulk Crap Uninstaller contributors. This fork exists to add security hardening and is maintained separately.

- Original work: © 2017–2026 Marcin Szeniak (Klocman) — https://github.com/Klocman/Bulk-Crap-Uninstaller
- This fork: https://github.com/binesh-balan/Moksh — maintained by **Binesh Balan**
- Modifications: © 2026 Binesh Balan
- Licence: Apache-2.0 (unchanged). See [Licence.txt](Licence.txt) and [NOTICE](NOTICE).

**MØKSH is not produced, endorsed or supported by the Bulk Crap Uninstaller project. Do not report MØKSH issues upstream** — use [MØKSH issues](https://github.com/binesh-balan/Moksh/issues) instead.

---

## Differences from upstream

MØKSH is functionally the same application. The changes are security and identity only — see [REMEDIATION.md](REMEDIATION.md) for the full list and [SECURITY_REVIEW.md](SECURITY_REVIEW.md) for the audit that produced it.

**Security fixes**

- Registry-supplied install locations can no longer drive an elevated recursive delete of system directories.
- Recursive deletion no longer follows junctions and symlinks out of the target directory.
- Helper executables are no longer resolved from the current directory or from `HKCU\...\App Paths`, closing a one-registry-value route to elevated code execution.
- Two `cmd.exe` command-injection sinks fed from registry values were removed.
- Scoop integration no longer runs a user-writable PowerShell script with the execution policy disabled.
- Application signatures are verified with `WinVerifyTrust` instead of a certificate-chain check that never validated the signature.
- The uninstaller automation daemon's control pipe is restricted to the current user and refuses to drive Windows system processes.

**Privacy**

- **All telemetry is removed.** Upstream posted crash reports, usage statistics and application ratings to a third-party server over cleartext HTTP. MØKSH ships with no reporting backend and makes no such requests.
- The per-install identifier is a random value rather than a fingerprint derived from your Windows SID and network adapter MAC addresses.
- Usage statistics are still collected locally into `UsageStatistics.xml` where you can read or delete them. Nothing is uploaded.

Network access that remains is user-initiated only: the "look up online" menu opens your browser, and the update check performs a single request to GitHub if you enable it.

## System requirements

- Earliest supported OS: Windows 10
- Requirements: .NET 8 desktop runtime (not needed for the portable build)

## Compiling

Development is done on Visual Studio 2022. The solution should load and build without extra steps, provided the necessary VS features are installed.

The installer is compiled with InnoSetup v6.4. To make a release, run `publish.bat` first.

Restore uses a committed lock file. Generate it once with a normal restore, commit the resulting `packages.lock.json` files, then build CI with `-p:RestoreLockedMode=true`.

## Support

- Issues: https://github.com/binesh-balan/Moksh/issues
- Discussions: https://github.com/binesh-balan/Moksh/discussions
- Releases: https://github.com/binesh-balan/Moksh/releases

Security issues should use GitHub's private vulnerability reporting rather than a public issue —
this application performs elevated file deletion. See [CONTRIBUTING.md](CONTRIBUTING.md).

## Licence

Apache License 2.0 — the same licence as the original work. You may use MØKSH in private and commercial settings for free and with no obligations, as long as no conditions of the licence are broken. See [Licence.txt](Licence.txt).
