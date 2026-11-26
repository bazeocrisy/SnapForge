<p align="center"><img src="docs/snapforge-icon.png" width="72" alt="SnapForge icon"></p>

<p align="center"><img src="docs/screenshots/editor-dark.jpg" width="640" alt="SnapForge Editor"></p>
<p align="center"><img src="docs/screenshots/capture-panel-dark.jpg" width="560" alt="SnapForge capture panel"></p>

# SnapForge

A fast, private screenshot and annotation tool for Windows 10 and 11, built with C#, .NET 10 and WPF.

> **Status: development build (v0.1.0-dev), not released.** Automated tests run on Windows in CI;
> interactive acceptance testing on physical Windows hardware is still pending
> (see [docs/ACCEPTANCE-TESTING.md](docs/ACCEPTANCE-TESTING.md)).
> "SnapForge" is a working name — see [naming conflicts](docs/RESEARCH.md#4-product-name-check-snapforge) and
> [alternative names](docs/NAMING.md).

## Features

Three workspaces modelled on a familiar capture/edit/organize workflow (see [competitive analysis](docs/COMPETITIVE-ANALYSIS.md)):

* **Capture window** — big Capture button, Region / Window / Display / Full screen, Preview in Editor, Copy to
  clipboard, Save to Library (off = capture without history), capture mouse pointer, delay countdown, presets with
  their own global hotkeys; notification-area icon; configurable global hotkey (default Print Screen, conflicts reported).
* **Editor** — menu bar, horizontal toolbar, canvas, Quick Styles & Tool Properties panel, Recent Captures Tray
  (pin, close vs delete, unsaved indicator, persists across restarts), light/dark themes.
* **Library** — every capture saved automatically and stays editable; All / Recent / Favorites / Tags / Folders /
  Trash; search, sort, date groups, adjustable thumbnails, multi-select, rename, duplicate, export, sanitize;
  backup/restore, retention, storage usage, movable location ([format](docs/CAPTURE-FORMAT.md)).

### Screen recording (Milestone V1 — feature branch, under review)

* **Video mode** in the capture panel and a **Record** button on the widget: record a region, a window or a display
  to **MP4** (H.264 + AAC) with the encoders built into Windows; 30 FPS (60 optional), Standard / High quality.
* **Audio**: system sound (on by default) and microphone (off by default), toggled live with level meters.
* **Floating toolbar**: elapsed time, Pause/Resume, Stop and save, Cancel and discard; it and the dashed outline are
  kept out of the recording. Countdown before recording; cursor on/off; hotkeys **Shift+F9** record/pause/resume,
  **Shift+F10** stop.
* **Library**: videos with play badge, duration and size; Videos view; player; rename, tags, favourites, folders,
  search, Trash; export or drag out as MP4. Interrupted recordings are recovered at the next start.
* Videos cannot be redacted. Details: [video plan](docs/VIDEO-PLAN.md), [acceptance and known limitations](docs/VIDEO-ACCEPTANCE.md).

### Capture and annotation (MVP)

* **Capture**: full screen (all displays), region (can span monitors), window, single display.
  Multi-monitor and mixed-DPI aware; the screen is frozen while you select.
* **Annotate**: arrow, line, rectangle, ellipse, highlight, text, numbered step markers, crop; colors, thickness,
  Quick Styles; Shift to constrain; select, move, recolor, resize, delete, edit text.
* **Redact**: replaces pixels with solid black and covers earlier annotations. Undo works while editing; saved or
  copied images contain only the black box — nothing to un-blur, nothing hidden under a layer ([details](docs/REDACTION.md)).
* **Undo / redo** for every edit.
* **Output**: save PNG or JPEG, copy to clipboard (bitmap + PNG).
* **Private by design**: no network access, no telemetry, no account, no admin rights. Captures are kept in a
  **local** library (`%LOCALAPPDATA%\SnapForge\Library`) unless you turn that off; nothing is ever uploaded.
  See the [threat model](docs/SECURITY.md).

## Keyboard shortcuts

| Action | Keys | Action | Keys |
|--------|------|--------|------|
| Capture (current settings) / Region / Window / Display / Full screen | Ctrl+N / Ctrl+1 / 2 / 3 / 4 | Undo / Redo | Ctrl+Z / Ctrl+Y (Ctrl+Shift+Z) |
| Select / Arrow / Line / Rectangle / Ellipse | V / A / L / R / E | Copy / Export | Ctrl+C / Ctrl+S |
| Highlight / Text / Step / Redact / Crop | H / T / N / X / C | Delete selection | Del |
| Editor / Library / Settings | Ctrl+E / Ctrl+L / Ctrl+, | Zoom in / out / fit / 1:1 | Ctrl+= / Ctrl+− / Ctrl+0 / Ctrl+Alt+0 |
| Pin current capture in tray / Close capture | Ctrl+Shift+P / Ctrl+W | Global capture hotkey | Print Screen (configurable) |
| Cancel | Esc (or right-click during capture) | Zoom with wheel | Ctrl+wheel |

## Download a development build

Every CI run publishes a portable, self-contained build (`SnapForge.exe` plus five WPF native DLLs; no .NET
install needed — CI verifies this on a runner with .NET removed) as the artifact
`SnapForge-portable-win-x64-dev-<commit>` on the **Actions** run page, with `SHA256SUMS.txt`. It is unsigned, so
SmartScreen will ask for confirmation. `SnapForge.exe --self-test report.txt` runs a non-interactive diagnostic.

## Build from source

Requires the .NET 10 SDK.

```powershell
dotnet build SnapForge.sln -c Release
dotnet test SnapForge.sln -c Release          # Windows: all tests; Linux/macOS: run tests/SnapForge.Core.Tests
dotnet run --project src/SnapForge.App        # Windows only
dotnet publish src/SnapForge.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

The Windows projects compile on Linux/macOS (`EnableWindowsTargeting`) but run only on Windows.

## Documentation

* **Milestone V1 screen recording (branch `claude/video-v1`, awaiting independent review):** [handoff](docs/V1-HANDOFF.md) · [plan and decisions](docs/VIDEO-PLAN.md) · [architecture and API research](docs/VIDEO-ARCHITECTURE.md) · [threat model](docs/VIDEO-THREAT-MODEL.md) · [Library video format](docs/LIBRARY-VIDEO-FORMAT.md) · [benchmarks](docs/VIDEO-BENCHMARKS.md) · [test results](docs/VIDEO-TEST-RESULTS.md) · [acceptance and known limitations](docs/VIDEO-ACCEPTANCE.md)
* **Milestone 3 handoff (awaiting independent review):** [handoff](docs/MILESTONE-3-HANDOFF.md) · [benchmarks](docs/BENCHMARKS.md)
* **Milestone 3 planning:** [forensic audit](docs/SNAGIT-FORENSIC-AUDIT.md) · [feature parity matrix](docs/FEATURE-PARITY-MATRIX.md) · [master requirements](docs/PRODUCT-REQUIREMENTS.md) · [UI specification](docs/UI-SPECIFICATION.md) · [roadmap](docs/DEVELOPMENT-ROADMAP.md) · [source registry](docs/SOURCE-REGISTRY.md)
* [Architecture and decisions](docs/ARCHITECTURE.md)
* [Requirements and acceptance criteria](docs/REQUIREMENTS.md)
* [Security and privacy model](docs/SECURITY.md)
* [Research: capture APIs, dependencies, licensing, naming](docs/RESEARCH.md)
* [Screenshots of the three workspaces](docs/screenshots/README.md)
* [Competitive analysis (Snagit as usability reference)](docs/COMPETITIVE-ANALYSIS.md)
* [Editable capture format v1](docs/CAPTURE-FORMAT.md)
* [Redaction pipeline review](docs/REDACTION.md)
* [Windows 10/11 compatibility](docs/COMPATIBILITY.md)
* [Windows Graphics Capture investigation](docs/WINDOWS-GRAPHICS-CAPTURE.md)
* [Automated UI testing](docs/UI-TESTING.md)
* [Alternative product names](docs/NAMING.md)
* [Windows acceptance test guide](docs/ACCEPTANCE-TESTING.md) and [results form](docs/ACCEPTANCE-RESULTS-TEMPLATE.md)
* [Self-audit](docs/AUDIT.md)
* [Third-party notices](THIRD-PARTY-NOTICES.md)

## License

[MIT](LICENSE) — Copyright (c) 2026 bazeocrisy.
