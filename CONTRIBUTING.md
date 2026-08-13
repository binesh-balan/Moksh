# Contributing to MØKSH

Thanks for taking an interest. MØKSH is a security-hardened, de-telemetried distribution of
[Bulk Crap Uninstaller](https://github.com/Klocman/Bulk-Crap-Uninstaller).

---

## Credits

### Original work

**MØKSH exists because of [Marcin Szeniak (Klocman)](https://github.com/Klocman)**, who wrote Bulk
Crap Uninstaller and released it under Apache-2.0. Essentially all of the application's
functionality — the uninstaller detection, the junk scanner, the bulk engine, the Store/Steam/Scoop
integrations, twenty years of Windows edge cases — is his work and that of the BCU contributors.

- Original project: https://github.com/Klocman/Bulk-Crap-Uninstaller
- Original author: Marcin Szeniak (Klocman)
- Licence: Apache License 2.0

Please **do not** report MØKSH issues to the upstream project. They did not ship this binary and
should not be asked to support it.

### Translations

The interface translations were contributed to the original project by:

Arabic — MFM Dawdeh · Czech — Richard Kahl · Dutch — Jaap Kramer · French — Thierry Delaunay,
Orphée V. · German — Dieter Hummel, Thomas Werk · Hungarian — Phoenix (Döbröntei Sándor) ·
Italian — Luca Carrabba · Japanese — KKbion · Polish — Marcin Szeniak · Portuguese — Artur Álvaro
Pereira, Silvio Corral · Russian — wvxwxvw, Kommprog · Simplified Chinese — cc713 · Slovenian —
Jadran Rudec · Spanish — MS-PC2, Freddynic159, Emilio J. Grao · Swedish — @glecas · Traditional
Chinese — Henryliu880922 · Turkish — Harun Güngör, @DogancanYr · Ukrainian — Serhii Horoshko ·
Vietnamese — wanwanvxt / Vũ Xuân Trường

### Third-party components

ObjectListView (Phillip Piper) · NBug (Teoman Soygul) · es.exe / Everything (voidtools) ·
TaskScheduler (David Hall) · FlaUI · Newtonsoft.Json (James Newton-King) ·
Windows Icons (Templarian)

### This fork

**Binesh Balan** — MØKSH maintainer. Security audit and remediation, telemetry removal, rebranding.
See [SECURITY_REVIEW.md](SECURITY_REVIEW.md) and [REMEDIATION.md](REMEDIATION.md) for the full
record of what was changed and why.

---

## Reporting a bug

Open an issue at https://github.com/binesh-balan/Moksh/issues

Please include:

- MØKSH version (Help → About) and whether it is the installed or portable build
- Windows version and architecture
- What you did, what happened, what you expected
- The contents of `Moksh.log` from the application directory, if relevant

**Security issues:** please do not open a public issue. This application performs elevated file
deletion, so a report that turns out to be exploitable should not be public before it is fixed.
Use GitHub's private vulnerability reporting on the repository instead.

## Submitting changes

1. Fork, branch off `main`.
2. Keep the change focused — one concern per pull request.
3. Match the surrounding style. The codebase is inherited; consistency beats personal preference.
4. Build and run the tests before opening the PR:

```bash
msbuild source\BulkCrapUninstaller.sln /t:Build /p:Configuration=Release /p:Platform="Any CPU"
```

Note that `dotnet build` **cannot** build this solution — `ResolveComReference` is unsupported on
the .NET Core MSBuild. Use the .NET Framework MSBuild that ships with Visual Studio.

### Things to be careful about

This application deletes files and runs external commands with administrator rights. Changes in
these areas get extra scrutiny, and a PR that weakens one of them needs to say so explicitly:

- **`PathTools.IsProtectedSystemPath`** is the single gate protecting system directories from
  deletion. Do not add a second, parallel path check somewhere else — inconsistent guards between
  call sites is the exact bug class this replaced.
- **Never traverse a reparse point** in a recursive delete. Junctions enumerate their target.
- **Never build a command line by string interpolation.** Use `ProcessStartInfo.ArgumentList`.
- **Never resolve an executable from the current directory, the user PATH, or HKCU.** The process
  is elevated; those locations are user-writable.
- **No telemetry.** MØKSH does not phone home, and pull requests that add analytics, crash
  reporting or "anonymous usage" endpoints will be declined.

## Translations

Translation files live in `source/BulkCrapUninstaller/Properties/Localisable.*.resx` and the
per-form `*.resx` files. The product name renders as `MØKSH`; leave it untranslated.

## Licence

By contributing you agree that your contributions are licensed under the Apache License 2.0, the
same licence as the original work.
