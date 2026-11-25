# Architecture

## Overview

```
SnapForge.sln
├─ src/SnapForge.Core        net10.0, no UI, no Windows APIs — fully unit-testable on any OS
│  ├─ Geometry/              PixelRect (screen/image ints), RectD/PointD (sub-pixel annotation space)
│  ├─ Imaging/               PixelBuffer (BGRA32), ArgbColor, ImageCompositor (exact source-over blend)
│  ├─ Capture/               MonitorInfo, ScreenLayout (virtual desktop math), WindowCandidate, WindowPicker
│  ├─ Annotations/           Immutable annotation records + hit testing, arrow geometry, step numbering
│  ├─ Editing/               EditorDocument, UndoRedoHistory, commands (add/remove/replace/redact)
│  └─ Export/                Format detection, save-target resolution, default file names
├─ src/SnapForge.App         net10.0-windows WPF executable ("SnapForge.exe")
│  ├─ Interop/               LibraryImport P/Invoke (user32, gdi32, dwmapi, shcore), dark title bar
│  ├─ Capture/               ScreenCaptureService (GDI), OverlaySession + SelectionOverlayWindow
│  ├─ Rendering/             AnnotationRenderer (shared by screen and export), ImageExporter, BitmapConversion
│  ├─ Editor/                AnnotationCanvas (tools, mouse gestures, inline text editing)
│  └─ Themes/                Dark theme, control styles, icon geometries
├─ tests/SnapForge.Core.Tests   xUnit, runs on Linux and Windows
└─ tests/SnapForge.App.Tests    xUnit, Windows only: rendering/export pixels, encoders, clipboard,
                                real GDI capture, main-window smoke test
```

## Key decisions

| # | Decision | Why |
|---|----------|-----|
| D1 | **WPF on .NET 10 LTS** | Native Windows desktop, hardware-accelerated vector rendering for annotations, built-in WIC encoders and clipboard, no third-party runtime. .NET 8 support ended 10 Nov 2026. |
| D2 | **Freeze-frame capture with GDI BitBlt** (see RESEARCH.md §1) | One synchronous call captures every monitor; selection happens on exactly what the user saw; no capture border or permission prompts; simplest reliable path for an MVP. |
| D3 | **Per-Monitor-V2 DPI awareness + physical-pixel coordinates everywhere in capture** | Mixed-DPI multi-monitor setups are the main source of screenshot-tool bugs. Overlays read the cursor with `GetCursorPos` (physical) rather than converting WPF units, so a drag across a 100 % and a 150 % monitor stays exact. |
| D4 | **One overlay window per monitor**, positioned with `SetWindowPos` in physical pixels and re-positioned on `DpiChanged` | A single window spanning monitors with different DPI cannot render crisply on both. |
| D5 | **Image space = WPF units at 96 DPI** | 1 WPF unit = 1 image pixel, so annotation coordinates, on-screen rendering and export agree; zoom is a `LayoutTransform`. |
| D6 | **Export = render annotations to a transparent layer, then blend in Core** | Untouched pixels are bit-identical to the capture and redacted pixels stay exactly the redaction color; the blend is unit-tested. |
| D7 | **Redaction writes into the pixel buffer** (not an overlay) | A saved or copied image cannot contain the original pixels. Original pixels are kept only inside the undo command (memory) for in-session undo. |
| D8 | **Immutable annotations + command pattern** | Undo/redo is a stack of small reversible commands; move/recolor/resize are `ReplaceAnnotationCommand`s that keep the annotation `Id`. History is bounded (200 steps). |
| D9 | **Core has zero platform dependencies** | Most logic (geometry, redaction, compositing, history, export rules) is tested on every CI run on Linux and Windows. |
| D10 | **No runtime NuGet packages** | Smaller attack surface, no licensing obligations beyond .NET, no paid components. |
| D11 | **Portable build = self-contained single-file win-x64 exe** | Runs on a clean Windows 10/11 machine without installing .NET; no installer, registry or admin rights. |

## Capture flow

1. User picks a mode (toolbar, start screen or Ctrl+1…4). Unsaved edits prompt for confirmation.
2. Main window hides (DWM transitions disabled, then a 180 ms settle delay).
3. `ScreenCaptureService.TakeSnapshot()` enumerates monitors and visible windows (z-order), then
   BitBlts the whole virtual desktop; gaps between monitors are filled black; alpha forced to 255.
4. *All screens*: the snapshot is the result. Otherwise `OverlaySession` opens one overlay per monitor
   showing the frozen frame, dimmed, with a highlight for the drag rectangle / hovered window / hovered display.
5. The selected physical rectangle is clipped to the virtual desktop and cropped from the snapshot.
6. The crop is loaded into a new `EditorDocument`; the main window returns.

## Coordinate systems

| Space | Units | Used by |
|-------|-------|---------|
| Virtual desktop | physical px, origin may be negative | monitors, windows, cursor, selection |
| Snapshot bitmap | physical px, origin (0,0) = virtual-desktop top-left | `ScreenLayout.ToBitmapSpace` |
| Overlay window | WPF DIPs of that monitor | drawing the mask only (`ToLocal`) |
| Image / annotations | image px == WPF units at 96 DPI | editor, renderer, exporter |

## Error handling

* Capture failures restore the main window and show a message; the previous screenshot stays loaded.
* Save writes to a temporary file in the target folder and renames it; failures leave nothing behind.
* Clipboard copy retries when another application holds the clipboard.
* Unhandled UI exceptions show a message, write a local log to `%LOCALAPPDATA%\SnapForge\logs`, and keep
  the app running so work can still be saved.
