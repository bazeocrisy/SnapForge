# Architecture

## Overview

```
SnapForge.sln
├─ src/SnapForge.Core        net10.0, no UI, no Windows APIs — fully unit-testable on any OS
│  ├─ Geometry/              PixelRect (screen/image ints), RectD/PointD (sub-pixel annotation space)
│  ├─ Imaging/               PixelBuffer (BGRA32), ArgbColor, ImageCompositor (exact source-over blend)
│  ├─ Capture/               MonitorInfo, ScreenLayout (virtual desktop math), WindowCandidate, WindowPicker
│  ├─ Annotations/           Immutable annotation records (+ highlight, redaction mask), hit testing, step numbering
│  ├─ Editing/               EditorDocument (base + visible image), UndoRedoHistory, commands (add/remove/replace/redact/crop), Sanitize
│  ├─ Export/                Format detection, save-target resolution, default file names
│  ├─ Storage/               AtomicFile (crash-resistant writes), PixelCodec (SFPX), AnnotationSerializer, CaptureFormat v1
│  ├─ Library/               CaptureLibrary (index, CRUD, trash, search, retention, backup, move, sanitize), RecentTray, queries, date grouping
│  └─ Settings/              AppSettings (capture options, presets, hotkeys, quick styles, theme), HotkeyGesture
├─ src/SnapForge.App         net10.0-windows WPF executable ("SnapForge.exe")
│  ├─ Interop/               LibraryImport P/Invoke (user32, gdi32, dwmapi, shcore), dark title bar
│  ├─ Capture/               ScreenCaptureService (GDI), OverlaySession + SelectionOverlayWindow
│  ├─ Rendering/             AnnotationRenderer (shared by screen and export), ImageExporter, BitmapConversion
│  ├─ Editor/                AnnotationCanvas (tools, mouse gestures, inline text editing, crop preview)
│  ├─ Services/              AppServices (settings, library, tray), LibraryWriter (serialized background writes),
│  │                         ThumbnailCache (LRU, lazy), EditorSession, CaptureController (delay, hide, snapshot, overlay)
│  ├─ Views/                 LibraryView, CaptureWindow, SettingsWindow, view models
│  ├─ Interop/               … + ShellIntegration (notification icon, RegisterHotKey)
│  ├─ Themes/                Palette.Dark / Palette.Light (swapped at run time), Controls (styles, icons), ThemeManager
│  ├─ MainWindow             Shell: menu, Capture button, Editor/Library tabs, toolbar, canvas, properties panel, tray, status bar
│  ├─ SelfTest / ScreenshotTour   --self-test and --screenshots diagnostics (temporary library only)
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

## Milestone 2 decisions

| # | Decision | Why |
|---|----------|-----|
| D12 | **Folder-per-capture storage** (manifest + base pixels + annotations + meta + thumbnail) instead of one container file | Small files (annotations, metadata) can be rewritten atomically on every autosave without rewriting a large image; a damaged file affects one capture. A single-file `.sfcap` ZIP exists for export/import. |
| D13 | **Base image + redaction masks** in the document | Redactions stay reversible in the editable copy (Snagit-like editing) while exports are always burned in; Sanitize is the explicit destructive step. |
| D14 | **Own pixel container (SFPX: zlib BGRA)** instead of PNG inside the library | Lossless, trivially verifiable, encodable in the platform-independent Core (tested on Linux). |
| D15 | **Manifest written last with SHA-256 of content files** | Commit point for crash consistency; detects partial or tampered files. |
| D16 | **Serialized background writer** | Saves never overlap or block the UI; disk errors surface as a banner while edits stay in memory. |
| D17 | **Virtualized row-of-tiles library grid + LRU thumbnail cache** | WPF has no virtualizing wrap panel; rows of tiles in a VirtualizingStackPanel keep thousands of captures responsive and load thumbnails only when visible. |
| D18 | **Notification icon and hotkeys via Win32 (Shell_NotifyIcon, RegisterHotKey)** | No WinForms dependency, no keyboard hooks; conflicts are detectable. |
| D19 | **Runtime theme switching via a swapped palette dictionary** | All control styles use DynamicResource; light, dark or follow Windows. |
| D20 | **Default library in %LOCALAPPDATA%** | Not Documents/Pictures, which are frequently redirected to OneDrive (automatic upload). |
| D21 | **Deferred library index** (`CaptureLibrary.OpenDeferred` + `CompleteOpen` on the serialized writer) | Start-up no longer reads every manifest before the window appears (audit DEF-02). Indexing runs on the same background queue as saves, so nothing races it; captures created meanwhile are kept by a merge-safe `Reindex`; the tray checks capture folders on disk until the index is ready. |
| D22 | **Drag-out as a lazy `IDataObject`** wrapped by WPF's `DataObject` | Native targets receive `CF_HDROP` (WPF converts `string[]`), the format Explorer, Office, mail clients and browsers accept for files. Files are rendered only when a target calls `GetData(FileDrop)`, so in-app reorders never write files. Copy is the only allowed effect. |
| D23 | **Drag files = File → Export output** | Same flatten (`ImageExporter.Flatten`) and encoder (no metadata) as Export; a dragged open capture uses its live editor state, a closed one is loaded from the Library and its redaction masks re-applied before flattening. |
| D24 | **Per-drag private temp folder** `%LOCALAPPDATA%\SnapForge\Temp\Drag\<UTC time>-<random>` | Unique per drag (no collisions or reuse), under the user profile (inherits the user-only ACL), deleted 2 minutes after the drag (targets may copy late), on exit, and by a start-up sweep (crash). |
| D25 | **Widget as a non-activating tool window** (`WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`) positioned in physical pixels | Never steals focus from the app being captured; no taskbar button; placement stored as monitor device name + edge + fraction (survives resolution/DPI changes; falls back to the primary monitor). Pure placement math lives in Core (`WidgetDock`) and is unit-tested with mixed-DPI layouts. |
| D26 | **Capture panel dropdowns as buttons opening context menus** | Themed, keyboard- and screen-reader-accessible without a custom ComboBox template; unavailable dropdowns are disabled buttons whose name and tooltip say "Not available yet". |
| D27 | **High contrast palette built from `SystemColors`** | When Windows high contrast is on, every DynamicResource brush maps to the user's contrast colours; switching is live (`SystemParameters.HighContrast` change). |
| D28 | **Benchmarks outside the test suite** (`tools/SnapForge.Benchmarks`, `SnapForge.exe --benchmark-startup`) | Seeding and measurements use the production write path with production durability; no test-only bypass. Runs as its own CI job so slow disks never hide behind test timeouts. |
| D29 | **Launch with Windows via HKCU Run** with `--background` | Per-user, no admin rights, removable from Windows Settings → Startup; portable copies that move show a "points to a different copy" hint. |
| D30 | **Screen recording on Windows APIs only** (Windows.Graphics.Capture, Direct3D 11, Media Foundation, WASAPI) via explicit COM/WinRT interop | No codecs or packages added; vtable slots checked against SDK IDL; projection package rejected because it could not be restored under signature-verified locked restore (VIDEO-ARCHITECTURE.md). |
| D31 | **One recording clock** (QPC) for frames and audio; pauses removed in `RecordingClock` | Audio and video shift identically on pause; sync measured on CI at ≤ 0.1 ms. |
| D32 | **Constant frame rate** by slot scheduling: new frames go to their nearest slot, ended slots without a new frame repeat the previous frame | WGC delivers frames only on change; CFR files behave predictably in players and editors. |
| D33 | **Fragmented MP4 while recording, progressive MP4 when finished** (`Mp4Defragmenter`, byte-for-byte sample copy) | Fragments bound crash loss; Media Foundation writes no `tfdt`, which the Windows reader mis-times (measured 233 ms), so the final file is progressive. |
| D34 | **Videos in `videos/<id>/`**, manifest written last, in-progress marker | Older builds never read `videos/` (cannot corrupt videos); crash detection without scanning MP4s. |
| D35 | **Encoder thread owns the Sink Writer**; capture and audio feed bounded queues | No cross-thread MF calls; memory bounded (256 MB frame budget, 4 s audio per source); drops counted. |
| D36 | **Low-latency H.264 (no B-frames)** | Exact presentation order in the file; removed a 2-frame shift and out-of-order timestamps seen on CI. |

## Capture flow

1. User picks a mode (toolbar, start screen or Ctrl+1…4). Unsaved edits prompt for confirmation.
2. Main window hides (DWM transitions disabled, then a 180 ms settle delay).
3. `ScreenCaptureService.TakeSnapshot()` enumerates monitors and visible windows (z-order), then
   BitBlts the whole virtual desktop; gaps between monitors are filled black; alpha forced to 255.
4. *All screens*: the snapshot is the result. Otherwise `OverlaySession` opens one overlay per monitor
   showing the frozen frame, dimmed, with a highlight for the drag rectangle / hovered window / hovered display.
5. The selected physical rectangle is clipped to the virtual desktop and cropped from the snapshot.
6. The crop becomes a new `EditorSession`: by default it is written to the Library in the background and added to
   the Recent Captures Tray; optionally copied to the clipboard; shown in the Editor if "Preview in Editor" is on.
7. Every edit schedules a debounced autosave (1.2 s) through the LibraryWriter.

## Drag-out flow (milestone 3)

1. Mouse down on a tray tile or Library tile records the start point; a move beyond the system drag threshold
   starts the drag with the tile's multi-selection (or just that tile).
2. `CaptureDragDrop.StartDrag` wraps a `LazyCaptureFiles` (formats: `FileDrop`, `SnapForge.CaptureKeys`,
   `Preferred DropEffect` = copy) in a WPF `DataObject` and calls `DragDrop.DoDragDrop(…, Copy)`.
3. When a target asks for `FileDrop`, each capture is flattened and encoded (PNG, or JPEG at the configured quality)
   into a new private temp folder with names from `ExportFileNames` ("title yyyy-MM-dd HHmmss.png", unique, safe).
4. Drops onto the tray carry `SnapForge.CaptureKeys`; the tray reorders without asking for files.
5. After the drag the folder is scheduled for deletion; exit and the next start-up sweep anything left.

## Recording flow (Milestone V1)

1. Panel Record / widget Record / Shift+F9 → `RecordingController.StartAsync`: own windows hidden, frozen snapshot,
   the same selection overlay as screenshots (Region / Window / Display), snapshot pixels wiped.
2. Optional countdown → `Library.BeginVideo` (folder + `recording.inprogress`) → `ScreenRecorder.Create`
   (single session; hardware encoder allowed, software fallback) → outline + toolbar (excluded from capture).
3. Threads: capture (WGC poll, GPU crop, CFR slots, disk check), audio (WASAPI → clock → mixer), encoder (Sink Writer).
4. Stop (toolbar / Shift+F10 / disk / source ended / sleep): queues drained, `Finalize`, then on the library writer
   `CompleteVideo` converts `recording.mp4` → progressive `video.mp4`, writes thumbnail, meta, manifest (last).
5. Crash: next start → `RecordingRecovery` cuts to the last complete fragment, decodes to verify, completes as "Recovered".

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
