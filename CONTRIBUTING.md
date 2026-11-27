# Contributing to SnapForge

Thank you for helping. SnapForge is a free, open-source (MIT) screenshot, annotation and screen-recording app for
Windows 11, written in C# with .NET 10 and WPF. It is private by design: no network access, no telemetry, no
account. Please keep it that way.

The current version is **1.0.0-rc.1**, a release candidate that has not been released publicly.

## Building

### Requirements

* The **.NET 10 SDK** (`global.json` pins 10.0.100 and allows later feature bands).
* **Windows 11 x64** to run the app and the Windows tests. The projects also compile on Linux and macOS
  (`EnableWindowsTargeting`), but only `SnapForge.Core` and its tests run there.
* No other tools are needed for a normal build. The installer is built in CI with Inno Setup 6
  (`installer/SnapForge.iss`).

### Commands

From the repository root:

```powershell
dotnet build SnapForge.sln -c Release
dotnet test SnapForge.sln -c Release
dotnet run --project src/SnapForge.App
```

* On Linux or macOS, run only the core tests: `dotnet test tests/SnapForge.Core.Tests -c Release`.
* A portable, self-contained build:

  ```powershell
  dotnet publish src/SnapForge.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
  ```

* `SnapForge.exe --self-test report.txt` runs a non-interactive diagnostic of a built copy.

The Windows tests include real screen capture, clipboard and real-input UI tests. Run them on a desktop session
you are not using at the same time; they move the mouse and type.

Note that a development build uses the same settings and Library (`%LOCALAPPDATA%\SnapForge`) as any installed
SnapForge, and only one copy can run at a time. Back up your Library before running development builds; see
[docs/INSTALLATION.md](docs/INSTALLATION.md#running-a-development-build-next-to-your-normal-snapforge).

## Project layout

| Path | Contents |
|---|---|
| `src/SnapForge.Core` | UI-independent logic (`net10.0`): annotations, editing commands and undo, imaging, export rules, the Capture Library and its file format, settings, video timing and MP4 handling. No WPF. |
| `src/SnapForge.App` | The WPF app (`net10.0-windows`, assembly `SnapForge.exe`): `Capture` (screen capture, selection overlay, scrolling capture), `Editor`, `Views` (capture panel, widget, Library, Settings), `Video` (recording, player, editor, GIF), `Ocr`, `Dialogs`, `Services`, `Interop`, `Rendering`, `Themes`. |
| `tests/SnapForge.Core.Tests` | Unit tests for the core (xUnit; run on any OS). |
| `tests/SnapForge.App.Tests` | Windows tests: rendering, export, capture, clipboard, drag and drop, real-input UI tests. |
| `tools/SnapForge.Benchmarks` | Performance benchmarks. |
| `tools/sbom` | Software bill of materials generator used by CI. |
| `installer/SnapForge.iss` | Per-user Inno Setup installer script. |
| `website/` | The project website (static site generator in `build.py`). |
| `docs/` | User documentation ([USER-GUIDE.md](docs/USER-GUIDE.md), [INSTALLATION.md](docs/INSTALLATION.md)) and design, security and test documents. |

Good starting points: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), [docs/SECURITY.md](docs/SECURITY.md),
[docs/REDACTION.md](docs/REDACTION.md) and [docs/CAPTURE-FORMAT.md](docs/CAPTURE-FORMAT.md).

## Coding rules

* **Warnings are errors.** `Directory.Build.props` sets `TreatWarningsAsErrors` and `AnalysisLevel`
  `latest-recommended`, with nullable reference types enabled. Fix analyzer warnings instead of suppressing them.
  If a suppression is truly needed, keep it as narrow as possible (a `#pragma` around the line) and write why.
* **Follow `.editorconfig`** and the style of the surrounding code.
* **No new runtime dependencies without discussion.** SnapForge ships no runtime NuGet packages; it uses .NET,
  WPF and Windows APIs only. Open an issue first if you think a package is needed. Test-only packages also need a
  reason. Packages come only from nuget.org (signatures required, see `nuget.config`) and are pinned in
  `packages.lock.json`; update the lock files when you change a reference.
* **No network code.** No uploads, sync, update checks, web requests, crash reporting or online services of any
  kind.
* **No telemetry.** No usage statistics, analytics or tracking, not even "anonymous" or opt-in.
* **Privacy and redaction rules are not optional.** Anything that leaves the app (export, clipboard, drag and
  drop, print, OCR input) must be the flattened image with redactions burned in and no metadata. Never offer
  original pixels or editable data to other apps. Read [docs/REDACTION.md](docs/REDACTION.md) before touching
  that code.
* **Never lose user data.** Library writes are atomic; keep them that way. Do not delete captures without the
  user's explicit action.
* **UI text:** short, plain English. Use the names that the UI already uses (for example "Library", "capture
  panel", "Sanitize"). Anything not implemented must not look like it works.
* **Accessibility:** controls need an accessible name (`AutomationProperties.Name`) and must be usable with the
  keyboard where possible.
* Put logic that does not need WPF in `SnapForge.Core` so it can be unit tested.

## Reporting bugs

Report bugs on the project's **GitHub Issues** page: <https://github.com/bazeocrisy/snapforge/issues>.

Please include:

1. **What you did**, step by step.
2. **What you expected** and **what happened instead** (copy the exact text of any message).
3. **Version information** from **Help → About SnapForge** (version, build, commit, status).
4. **Windows version** (Windows **Settings → System → About**: edition, version and OS build), and whether you
   use the installer or the portable ZIP.
5. **Your setup if relevant:** number of displays and their scaling (for example 150 %), HDR on or off, the app
   you were capturing or dragging into, audio devices for recording problems.
6. **Error log** if SnapForge said "Something went wrong": the text file in `%LOCALAPPDATA%\SnapForge\logs`.
   It contains error text and the Windows version, never images. Read it before attaching it.

**Never attach private screenshots or recordings.** Captures often contain passwords, names, messages or
company data. If a picture helps, make one of a harmless test window, or redact it with SnapForge, export a PNG
(exports contain only the black boxes) and check it before uploading. Never attach `.sfcap` files or Library
backups: they can contain original pixels under unsanitized redactions.

## Reporting security issues

**Do not report security problems in public issues.** Use GitHub's private vulnerability reporting for this
repository (**Security** tab → **Report a vulnerability**). If that option is not available, open an issue that
only asks a maintainer for a private contact, without any details.

Examples of security issues: a way to recover redacted pixels from an export, clipboard or drag; data written
outside the expected folders; a crafted `.sfcap`, backup or image file that writes files elsewhere or runs code;
any network traffic from SnapForge. The threat model is in [docs/SECURITY.md](docs/SECURITY.md).

## Pull requests

1. For anything larger than a small fix, open an issue first so we can agree on the approach.
2. Keep each pull request focused on one change.
3. **Add or update tests.** Logic in `SnapForge.Core` needs unit tests; UI behaviour should have a test in
   `SnapForge.App.Tests` where practical.
4. **Update the documentation** when you change behaviour the user can see: [docs/USER-GUIDE.md](docs/USER-GUIDE.md)
   (menu names, buttons, shortcuts, settings), [docs/INSTALLATION.md](docs/INSTALLATION.md), and the security and
   redaction documents when relevant. Keep the in-app help (Help → Keyboard shortcuts, tool hints) consistent.
5. Make sure `dotnet build SnapForge.sln -c Release` passes without warnings and `dotnet test SnapForge.sln -c
   Release` passes. CI runs the same build and tests on Windows, plus packaging checks.
6. Describe what you changed and why, and how you tested it (including manual testing on Windows 11 for UI
   changes). Include screenshots only of harmless test content.

## Licence of contributions

SnapForge is licensed under the [MIT License](LICENSE). By submitting a contribution you agree that it is licensed
under the MIT License as part of SnapForge, and you confirm that you have the right to submit it. Do not copy code
from projects with incompatible licences. If you add third-party material, list it in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
