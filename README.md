<p align="center"><img src="docs/snapforge-icon.png" width="72" alt="SnapForge icon"></p>

# SnapForge

A fast, private screenshot and annotation tool for Windows 10 and 11, built with C#, .NET 10 and WPF.

> **Status: development build (v0.1.0-dev), not released.** Automated tests run on Windows in CI;
> interactive acceptance testing on physical Windows hardware is still pending
> (see [docs/ACCEPTANCE-TESTING.md](docs/ACCEPTANCE-TESTING.md)).
> "SnapForge" is a working name — see [naming conflicts](docs/RESEARCH.md#4-product-name-check-snapforge).

## Features (MVP)

* **Capture**: full screen (all displays), region (can span monitors), window, single display.
  Multi-monitor and mixed-DPI aware; the screen is frozen while you select.
* **Annotate**: arrow, line, rectangle, ellipse, text, numbered step markers; 8 colors, 4 sizes; Shift to
  constrain; select, move, recolor, resize, delete, edit text.
* **Redact permanently**: replaces pixels with solid black — nothing to un-blur, nothing hidden under a layer.
* **Undo / redo** for every edit.
* **Output**: save PNG or JPEG, copy to clipboard (bitmap + PNG).
* **Private by design**: no network access, no telemetry, no admin rights, nothing written to disk unless you save.

## Keyboard shortcuts

| Action | Keys | Action | Keys |
|--------|------|--------|------|
| Region / Window / Display / Full screen | Ctrl+1 / 2 / 3 / 4 | Undo / Redo | Ctrl+Z / Ctrl+Y (Ctrl+Shift+Z) |
| Select / Arrow / Line | V / A / L | Copy / Save | Ctrl+C / Ctrl+S |
| Rectangle / Ellipse / Text | R / E / T | Delete selection | Del |
| Step marker / Redact | N / X | Zoom in / out / fit / 1:1 | Ctrl+= / Ctrl+− / Ctrl+0 / Ctrl+Alt+0 |
| Cancel | Esc (or right-click during capture) | Zoom with wheel | Ctrl+wheel |

## Download a development build

Every CI run publishes a portable, self-contained `SnapForge.exe` (no .NET install needed) as the artifact
`SnapForge-portable-win-x64-dev-<commit>` on the **Actions** run page. It is unsigned, so SmartScreen
will ask for confirmation.

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

* [Architecture and decisions](docs/ARCHITECTURE.md)
* [Requirements and acceptance criteria](docs/REQUIREMENTS.md)
* [Security and privacy model](docs/SECURITY.md)
* [Research: capture APIs, dependencies, licensing, naming](docs/RESEARCH.md)
* [Windows acceptance test script](docs/ACCEPTANCE-TESTING.md)
* [Self-audit](docs/AUDIT.md)
* [Third-party notices](THIRD-PARTY-NOTICES.md)

## License

Not yet chosen by the project owner. Until a `LICENSE` file is added, all rights are reserved.
