# MØKSH — Remediation Record

Changes applied to this fork of Bulk Crap Uninstaller, in response to [SECURITY_REVIEW.md](SECURITY_REVIEW.md).

**Date:** 2026-08-13
**Applied by:** Binesh Balan
**Base:** Bulk Crap Uninstaller v6.2 © Marcin Szeniak (Klocman), Apache-2.0

> **Build status: green.** Solution builds clean in Release/AnyCPU with MSBuild 18.6.3 (VS 2022 toolchain, .NET 8 SDK), zero errors. Native launcher builds clean for Win32. Unit tests: **55 passed, 1 skipped, 0 failed**. NuGet lock files generated and committed for all 18 projects.
>
> See §5 for the six compile/test failures found during the build and how each was resolved, and §3 for what remains outstanding.

---

## 1. Security fixes

### Critical

| ID | Fix | Files |
|---|---|---|
| **BCU-C01** | Added `PathTools.IsProtectedSystemPath()` — a single canonicalising gate that rejects drive roots, every well-known system and profile folder, anything inside `%WINDIR%`, and any ancestor of those. Unparseable paths fail closed. Enforced at three points: `SimpleDelete` is no longer assigned to an entry whose `InstallLocation` is protected; the uninstall-string generator re-checks independently; and `UniversalUninstaller` refuses a protected target before showing UI or deleting anything. | `PathTools.cs`, `UninstallerTypeAdder.cs`, `SimpleDeleteUninstallStringGenerator.cs`, `UniversalUninstaller/Program.cs` |
| **BCU-C02** | `RecursiveDelete` now checks `FileAttributes.ReparsePoint` before descending. Junctions and symlinks are unlinked, never traversed. The read-only-attribute clear was moved *after* the check so it can no longer be applied through a link to its target. | `UniversalUninstaller/Program.cs`, `PathTools.cs` (`IsReparsePoint`) |

### High

| ID | Fix | Files |
|---|---|---|
| **BCU-H01** | `GetFullPathOfExecutable` no longer searches the current directory, no longer reads the user `PATH`, and no longer falls back to **HKCU** `App Paths` — machine `PATH` and HKLM only. It also rejects arguments that aren't bare filenames. Discovered third-party binaries are additionally screened with a new `FilesystemTools.IsWritableByNonAdministrators()` ACL check before being executed. | `PathTools.cs`, `FilesystemTools.cs`, `ChocolateyFactory.cs`, `ScoopFactory.cs` |
| **BCU-H02** | `TakeOwnership` no longer builds a `cmd.exe` command line by interpolation. `takeown.exe` and `icacls.exe` are resolved from `%SystemRoot%\System32` and invoked directly with `ArgumentList`, so quotes and `& \| < > ^` are passed as literal argument text and can never be parsed as commands. | `MainWindow.cs` |
| **BCU-H03** | `GetOldSimpleDeleteString` — the `cmd.exe /C del …` fallback — deleted outright. When the bundled deleter is missing, entries are now simply reported as having no uninstall method. Remaining quotes are stripped from the path before it reaches the deleter's command line. | `SimpleDeleteUninstallStringGenerator.cs` |
| **BCU-H04** | PowerShell is resolved from `%SystemRoot%\System32\WindowsPowerShell\v1.0\`, not `PATH`. Scoop is ignored entirely if `scoop.ps1` sits anywhere a non-administrator can write. The invocation switched from a bare script path to `-File`, so trailing tokens are script *parameters* rather than a second statement — `;` in an app name no longer starts a new command. App names are additionally allowlisted to `[A-Za-z0-9-_.+]`. | `ScoopFactory.cs` |
| **BCU-H05** | `.github/workflows/winget.yml` **deleted**. It pinned `vedantmgoyal9/winget-releaser` to the mutable `main` branch and handed it `secrets.WINGET_TOKEN`, and it published under the upstream `Klocman.BulkCrapUninstaller` identifier — wrong for this fork on both counts. | *(removed)* |

### Medium

| ID | Fix | Files |
|---|---|---|
| **BCU-M01** | **All telemetry removed.** `Program.ConnectionString` is now `null` and a new `HomeServerAvailable` flag gates every caller. The NBug crash destination is not registered, statistics upload returns early, and ratings fetch/upload return early. No request is made to `bugsklocman.ddns.net` or anywhere else. | `Program.cs`, `NBugConfigurator.cs`, `DatabaseStatSender.cs`, `UninstallerRatingManager.cs` |
| **BCU-M02** | The NSIS uninstaller copy now goes into a freshly created randomly-named directory under `%TEMP%`, written with `FileMode.CreateNew` instead of `File.Copy(overwrite: true)` — so a planted hardlink or a pre-created file causes a failure rather than an arbitrary overwrite, and the predictable filename race is gone. | `UninstallManager.cs` |
| **BCU-M03** | The automation daemon's pipe is created with `PipeOptions.CurrentUserOnly`. `ProcessCanBeAutomatized` now rejects any process whose main module lives under `%WINDIR%` — `consent.exe` (the UAC prompt), credential dialogs and security UI can no longer be driven — and rejects processes it cannot inspect. | `UninstallHandler.cs` |
| **BCU-M04** | Added `AuthenticodeTools.IsTrusted()`, a real `WinVerifyTrust` P/Invoke. Certificate validity is now determined by verifying the signature over the file, not by `X509Certificate2.Verify()` (which only validates the chain and says nothing about the bytes). Certificates not sourced from a file on disk report *unknown* rather than *valid*. | `AuthenticodeTools.cs` *(new)*, `CertificateGetter.cs`, `ApplicationUninstallerEntry.cs` |
| **BCU-M05** | The installer no longer skips a download when a file of the expected name already exists in `{tmp}` — it deletes any such file and always downloads. Closes the pre-seed path where a local user could plant a binary in a shared temp directory and have setup execute it elevated. **See §3 — the checksum itself is still outstanding.** | `CodeDependencies.iss` |
| **BCU-M06** | `cmd.exe`, `regedit.exe` and `CleanLogs.bat` are all launched by absolute path now. Previously they were resolved by bare name with the working directory set to the application folder — user-writable for portable installs, and this process is elevated. | `Program.cs`, `RegistryTools.cs` |
| **BCU-M07** | `RestorePackagesWithLockFile` enabled; the three floating `[8.*,9)` ranges pinned to `8.0.0`; added a `nuget.config` that clears all sources, declares `nuget.org` only, and enables package source mapping. | `Directory.Build.props`, 4 × `.csproj`, `nuget.config` *(new)* |

### Low

| ID | Fix | Files |
|---|---|---|
| **BCU-L01** | Windows-directory guard switched to `StringComparison.OrdinalIgnoreCase` (it was a case-*sensitive* `Contains`, so `c:\windows\…` walked straight past it) and now also calls the shared `IsProtectedSystemPath`. | `JunkCreatorBase.cs` |
| **BCU-L02** | `CheckIfDirIsStillUsed` uses the boundary-aware `PathTools.SubPathIsInsideBasePath` instead of a bare `StartsWith`, which treated `…\App` as a prefix of `…\AppData`. | `JunkCreatorBase.cs` |
| **BCU-L03** | Uninstall-list regex filters get a 2-second match timeout. The existing handler treats a timeout as "no result", which is the correct outcome. | `FilterCondition.cs` |
| **BCU-L04** | `WaitForDirEmpty` is bounded to ~10 seconds and throws instead of spinning forever on a locked or continuously-recreated file. | `UniversalUninstaller/Program.cs` |
| **BCU-L05** | The install identifier is now a random 64-bit value from `RandomNumberGenerator`, replacing the MD5 fold of Windows SID + all identity claims + every NIC MAC address. | `Program.cs` |
| **BCU-L06** | CI actions pinned to commit SHAs with version comments; explicit least-privilege `permissions: contents: read` added at workflow and job level. **See §3 — verify the SHAs.** | `ci.yaml` |
| **BCU-L07** | Deleted the three unsigned, unreferenced NBug developer binaries (`NBug.Configurator.exe`, `NBug.Examples.WinForms.exe`, `NBug.dll`). | *(removed)* |
| **BCU-L08** | `/U` combined with a junk confidence level below `VeryGood` is refused unless the new `/FORCELOWCONFIDENCE` flag is also passed. Help text updated. | `BCU-console/Program.cs` |

---

## 2. Rebranding

Product identity changed to **Moksh** (internal) / **MØKSH** (display).

| Area | Change |
|---|---|
| Executable | `BCUninstaller.exe` → `Moksh.exe` (`AssemblyName`, launcher target, publish script, installer, `CleanLogs.bat`, resources DLL lookup) |
| Assembly metadata | `Product`/`AssemblyTitle` = `MØKSH`; `Authors`/`Company` = `Binesh Balan`; `Copyright` credits both parties |
| Installer | `MyAppName` = `MØKSH`, publisher = `Binesh Balan`, settings file `Moksh.settings` |
| **Installer AppId** | **New GUID `edf1b036-2b58-45ab-a933-88b908e026f8`** (was `f4fef76c-…`), kept in sync with `Program.GetInstalledRegKey()`. A separate product must not share an uninstall registry key with upstream, or each would offer to uninstall the other |
| Install directory | `{commonpf}\Moksh` — the ASCII short name. `Ø` in a filesystem path breaks scripts, log parsers and deployment tooling; the display name keeps the stylised form |
| Mutex | `Global\BCU-singleinstance` → `Global\MOKSH-singleinstance` |
| Settings directory | `%LOCALAPPDATA%\Marcin_Szeniak` → `%LOCALAPPDATA%\Binesh_Balan` |
| App manifest | `assemblyIdentity name` = `Moksh.app` |
| CLI | Help text now reads `moksh-cli` |
| UI strings | 535 occurrences across 128 localisation files in 24 languages, plus dialog titles, export filenames, batch-script headers and the window title |
| Docs | `README.md`, `NOTICE`, `PrivacyPolicy.txt` rewritten |

### Brand assets

The upstream artwork is gone. Upstream's editable source file `bigImage.pdn` was deleted as well.

| Asset | Was | Now |
|---|---|---|
| `Resources/3.png` (wizard) | Octagon "B" | 256×256 MØKSH badge |
| `Resources/logo.ico`, `installer/assets/logo.ico` | BCU octagon | Multi-resolution 16/24/32/48/64/128/256 badge |
| `installer/assets/bigImage.bmp`, `Resources/bigImage.bmp` | BCU banner | 164×314 gradient panel, mark + MØKSH wordmark |
| `installer/assets/smallImage.bmp` | BCU mark | 55×55 badge on white |
| `doc/BCU_manual.html` / `.odt` | — | Renamed `Moksh_manual.html` / `.odt`, rebranded, upstream support link replaced with an attribution note |

**The mark** is the Ø from MØKSH: a ring with a diagonal stroke. That glyph is also the universal "no / remove" symbol, so the letterform and the product's function are the same shape. It is generated from primitives (see `make_logo.py` in the session scratchpad) rather than traced from a font, so it is identical at every size and has no font dependency. Verified legible at 16 px on both light and dark backgrounds.

The wizard previously split the name between image and text — `3.png` drew the "B" and `label2` supplied "ulk crap / uninstaller". That label now reads `MØKSH` in all 21 locale files. **This is why a plain search for "Bulk Crap Uninstaller" missed it:** the string in the resources was `ulk crap`, with no leading B.

### Outbound links removed

MØKSH has no homepage, contact form, donation page, social account or issue tracker. Every upstream destination is blanked in `Resources.resx`, and a new `BrandLinks` helper hides any control whose URL is empty, so nothing dead-links:

| Removed | Was |
|---|---|
| Homepage | `https://www.bcuninstaller.com/` |
| Contact form | `klocmansoftware.weebly.com` (also an embedded legacy `WebBrowser` control over plain HTTP — the whole `FeedbackWindow` form was dead code and has been deleted, 23 files) |
| Donate | `http://klocmansoftware.weebly.com/donate.html` |
| Review / Twitter / Translate / GitHub issues + releases | upstream and third-party destinations |
| Self-uninstall farewell | asked users to explain themselves on the author's website, in 21 locales — replaced with a neutral sentence |

`FeedbackBox` (the nag that opened itself three minutes after launch) and `NewsPopup` now return early when none of their destinations are configured, so neither appears at all. The About box's "Official webpage" link became **"Original project (Bulk Crap Uninstaller)"** and points at upstream — attribution belongs there, support does not. The installer no longer sets `AppPublisherURL`/`AppSupportURL`/`AppUpdatesURL`, so Add/Remove Programs won't send MØKSH support requests to Marcin Szeniak.

Filling a value back into `Resources.resx` restores the corresponding link and its UI control with no code change.

### Attribution

Credit to the original author is carried in six places:

1. **`NOTICE`** — full Apache-2.0 attribution, plus a statement that MØKSH is not endorsed by or supported by the upstream project.
2. **`README.md`** — a dedicated Attribution section at the top.
3. **Assembly copyright** — `Copyright © 2017-2026 Marcin Szeniak (Klocman). Modifications copyright © 2026 Binesh Balan. Licensed under Apache-2.0.`, visible in file properties.
4. **About box** — an explicit derivative-work notice naming the original author and licence.
5. **Installer** — `VersionInfoCopyright` carries the same combined notice.
6. **Per-file copyright headers** — **left untouched in all ~500 source files.**

> **On the untouched headers and the `Klocman.*` namespaces:** these were deliberately not renamed. Apache-2.0 §4(c) requires retaining all copyright, patent, trademark and attribution notices in derivative works — stripping them would breach the licence this fork depends on. Renaming the namespaces would also be a ~500-file churn with no user-visible benefit. The rebrand deliberately targets *product identity* only: what users see, what the binary is called, and where it installs.

The `Licence.txt` file is unchanged, as required.

---

## 3. Outstanding — must be handled before shipping

These are **not** done and were not within reach of a static, offline pass.

| # | Item | Why it's still open |
|---|---|---|
| 1 | ~~Compile and test the solution.~~ | **Done.** Build green, 55/56 tests pass (1 pre-existing skip). See §5. |
| 2 | **Verify the fork against upstream.** | There is no `.git` directory in this tree — `git rev-parse` resolves to `C:\`. Clone `Klocman/Bulk-Crap-Uninstaller` at the v6.2 tag, diff it against a pre-remediation copy, and confirm the only differences are the ones in this document. Until then, "no tampering found" remains an absence of evidence. Then `git init` with a clean initial commit recording the upstream base. |
| 3 | **Verify the CI action SHAs.** | The three pinned SHAs in `ci.yaml` were written from memory and **have still not been verified** — the build was local and never exercised the workflow. Confirm each against the action's GitHub releases page before relying on CI; a wrong SHA fails the workflow rather than failing open, but it will fail. |
| 3b | **Clean builds are blocked on this machine.** | `/t:Rebuild` fails with `MSB4018 / 0x800711C7 — An Application Control policy has blocked this file` when MSBuild regenerates and loads `Interop.Scripting.dll` (the `Scripting` COM interop for `FileSystemObject`). Incremental `/t:Build` succeeds because it reuses the already-generated interop. This is a local WDAC/AppLocker/ThreatLocker policy, not a code defect — GitHub-hosted runners are unaffected — but it will bite anyone doing a clean local build. Allow the generated interop assembly in policy, or drop the COM reference in favour of a managed directory-size walk. |
| 4 | **Fill in the .NET runtime checksum.** | `CodeDependencies.iss` has a `TODO(security, BCU-M05)` marking the empty `Checksum` argument. Inno Setup skips integrity verification when it is blank. Get the published SHA-256 from the .NET download page for the exact build referenced. |
| 5 | ~~Replace branding assets.~~ | **Done.** All upstream artwork replaced with an original MØKSH mark; see §2. Upstream's editable source file `bigImage.pdn` was deleted too. |
| 6 | **Review translated strings.** | The 535 name substitutions were mechanical. A proper-noun swap is safe in most contexts, but a native speaker should check the inflected languages (Polish, Russian, Hungarian, Czech, Ukrainian) for grammar that no longer agrees. |
| 7 | **Update `doc/BCU_manual.html`.** | The bundled user manual is untouched and still describes BCUninstaller throughout. |
| 8 | **Sign the binaries.** | Unsigned binaries from an unknown publisher performing elevated deletion will be treated as hostile by SmartScreen and most EDR. |
| 9 | **Architectural: split the privilege boundary.** | Recommendation §15 item 22 of the review. Untrusted registry and filesystem parsing still happens in the same Administrator-token process that executes commands and deletes files. The fixes above close the specific holes found; this would structurally prevent the next one. Worth planning for a v2. |

---

## 4. Behaviour changes users will notice

- **No network traffic** except the user-initiated "look up online" (opens a browser) and the opt-in update check (a single request to GitHub).
- **Community ratings are gone.** The ratings column still exists but will always be empty — there is no backend to fetch from. Consider hiding the column, or repointing it at your own service.
- **Crash reports are shown and logged locally, never sent.**
- **Some entries may lose their uninstall method.** Entries whose `InstallLocation` points at a protected system location no longer get an auto-generated delete command — deliberately. Anything relying on that behaviour was relying on the BCU-C01 bug.
- **Scoop may be skipped** on installations where `scoop.ps1` sits in a user-writable location. This is the intended outcome; scoop installs to `%USERPROFILE%\scoop` by default, which is user-writable, so **expect Scoop detection to stop working for most users**. If Scoop support matters to you, the right answer is to run discovery unprivileged (item 9) rather than to relax this check.
- **Existing installs are not upgraded.** The new `AppId` means MØKSH installs alongside BCUninstaller rather than over it.

---

## 5. Issues found during the build

Six failures surfaced when the solution was actually compiled. Three were caused by the remediation; three were pre-existing defects in the fork that the build exposed.

| # | Failure | Cause | Fix |
|---|---|---|---|
| 1 | `NU1605` package downgrade — `System.Drawing.Common` 8.0.10 → 8.0.0 | **Mine.** Pinning the floating `[8.*,9)` range to `8.0.0` (BCU-M07) dropped below the `>= 8.0.10` that `FlaUI.Adapter.White 0.2.1 → FlaUI.Core 5.0.0` requires. | Pinned to `8.0.10` with a comment recording the constraint. |
| 2 | `CS0117` — `WellKnownSidType` has no `NtAuthoritySid` | **Mine.** Invented enum member in the new ACL check. | Removed it. The existing `S-1-5-80-` prefix test already covers service SIDs, so no coverage was lost. |
| 3 | `SerializeDeserializeCasheTest` — *"No items received"* | **Mine.** The test called `FetchRatings()` and asserted the server returned data. With telemetry removed (BCU-M01) it never can. | Rewrote it to seed ratings locally via `SetMyRating`. It now tests the serialize/deserialize round-trip it is named for, deterministically and without a network dependency — strictly better than before. |
| 4 | `CS1061` — `Icon` has no `GetHicon` | **Pre-existing.** `GetHicon()` is on `Bitmap`, not `Icon`; the test project didn't compile as received. | Route through `ToBitmap().GetHicon()`. This also fixes a latent bug: `CreateOwnedIconFromHandle` calls `DestroyIcon` on the handle it is given, so passing the shared `SystemIcons.Application.Handle` would have corrupted that icon process-wide. The bitmap round-trip yields a handle we own. |
| 5 | `CS0117` — `Assert` has no `ThrowsException` | **Pre-existing.** MSTest 4.x removed it; the project already pinned MSTest 4.1.0 before this work. | Switched to `Assert.Throws<T>`. |
| 6 | `MSB8041` — MFC libraries required | **Pre-existing / environment.** `BCU-launcher.vcxproj` declared `UseOfMfc=Static` in all four configurations, but `main.cpp` includes no MFC header and uses no MFC type — only `<Windows.h>` and plain Win32 (`MessageBox`, `CreateProcess`). | Set `UseOfMfc=false`. Removes an unused static dependency and a build-machine prerequisite, and shrinks the binary. Verified the launcher builds and links clean without it. |

**Toolchain used:** MSBuild 18.6.3 (`Microsoft Visual Studio\18\Community`), .NET SDK 8.0.419 / 10.0.300.

---

## 6. Runtime smoke test

`bin\Release\AnyCPU\Moksh.exe` launched, ran a full system scan, and exited cleanly.

| Check | Result |
|---|---|
| Process | Started, `Responding=True`, 13 threads, working set 147 → 212 MB during scan |
| Window | Handle valid, visible, 997 × 731, title **`MØKSH v6.2 Portable x64`** — rebrand confirmed live, and "Portable" confirms the new `AppId` lookup correctly reports not-installed |
| Applications discovered | **397** — 234 Msiexec, 118 StoreApp, 24 Unknown, 11 SimpleDelete, 6 Nsis, 4 InnoSetup |
| Factories | Registry, Chocolatey, Oculus, Predefined, Scoop, Script, Steam, Directory, StoreApp all ran; helper EXEs (`OculusHelper`, `ScriptHelper`, `SteamHelper`, `StoreAppHelper`) all invoked successfully |
| Unhandled exceptions | **None.** No NBug `Exception_*.zip` produced |
| Network | No telemetry attempt — BCU-M01 confirmed at runtime |
| Manifest | Embedded `requestedExecutionLevel` verified as `requireAdministrator` (the `asInvoker` visible in a raw string search is one of three commented-out examples in `app.manifest`, not the active setting) |

**BCU-C01 guard verified against live data.** All 11 `SimpleDelete` targets are legitimate application subdirectories — `C:\Program Files (x86)\dotnet`, `…\Google\Update`, `…\Docker\cli-plugins` and similar. None is a protected root, and none was wrongly suppressed. The guard is neither over-blocking (real apps still get uninstall commands) nor letting a system path through. Several targets arrived with non-matching case (`C:\Program files (x86)\Windows kits\…`) — precisely the variants that defeated the old case-sensitive check fixed in BCU-L01.

**Two non-errors in the log**, both pre-existing and both handled:

- `es.exe failed to connect to Everything: Error 8` — the Everything service is not running on this machine. `FastSizeGenerator` degrades to `Scripting.FileSystemObject` as designed.
- `DirectoryNotFoundException (CTL_E_PATHNOTFOUND)` at `FastSizeGenerator.cs:72` — a folder-size lookup on an `InstallLocation` that no longer resolves. Caught and logged by the existing handler, not thrown. Untouched by this work.

### Not covered by this smoke test

- **No visual confirmation of the rendered UI.** The app runs elevated, and Windows UIPI blocks a non-elevated session from `PrintWindow`-ing or driving its window, so no screenshot was obtainable. Launch is evidenced by the titled visible window plus 397 enumerated applications and a clean log — strong, but not the same as having seen it.
- **The Scoop changes (BCU-H04) are untested at runtime.** Scoop is not installed on this machine, so `ScoopFactory` short-circuited in 11 ms. The writable-location check and the `-File` invocation have never actually executed. Test on a machine with Scoop before relying on them — and note the expected outcome there is that Scoop detection *stops working*, per §4.
- **No uninstall was performed.** Only discovery ran. The deletion, junk-removal and automation paths are unexercised.

Note that `dotnet build`/`dotnet test` **cannot** build this solution — `ResolveComReference` is unsupported on the .NET Core MSBuild (`MSB4803`). Use the .NET Framework MSBuild, as `publish.bat` and `ci.yaml` already do.
