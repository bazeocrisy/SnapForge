# Research: capture APIs, dependencies, licensing and naming

Date: 2026-11-25. Scope: what SnapForge's MVP needs on Windows 10/11.

## 1. Windows screen-capture APIs

| API | What it captures | Strengths | Weaknesses for a screenshot tool |
|-----|------------------|-----------|----------------------------------|
| **GDI `BitBlt` from the screen DC** (`GetDC(NULL)`, `SRCCOPY \| CAPTUREBLT`) | The DWM-composed desktop, any rectangle of the virtual screen | Available on every Windows version; synchronous; no permission prompt, no yellow capture border; one call covers all monitors; trivially exact pixel coordinates in a per-monitor-DPI-aware process | Cannot capture occluded parts of a window; HDR content is tone-mapped to SDR; content protected with `SetWindowDisplayAffinity` appears black (intended) |
| `PrintWindow` (+ `PW_RENDERFULLCONTENT`) | A single window, even if covered | Gets occluded content | Asks the target app to paint itself, so it can block or hang on unresponsive apps; inconsistent for GPU/Chromium/UWP content; DPI behavior is not documented |
| DXGI Desktop Duplication (`IDXGIOutputDuplication`) | One monitor (output) per duplication | Fast, designed for streaming | Per-output and per-adapter plumbing; fails on some hybrid-GPU laptops and when outputs are on different adapters; needs D3D11 interop; overkill for still images |
| **Windows.Graphics.Capture** (WGC, WinRT) | A window or monitor via `GraphicsCaptureItem` | Captures occluded windows; modern; used by Snipping Tool/OBS | Requires WinRT projections (`Microsoft.Windows.SDK.NET`, ~25 MB) and D3D11 interop; yellow border on Windows 10 (removable only on Windows 11 with `IsBorderRequired` + capability); async frame pool; extra failure modes |

**Decision (MVP):** GDI `BitBlt` of the whole virtual desktop into a 32-bpp top-down DIB, taken once
("freeze frame"), then region/window/display selection is done on that frozen image. This is the same
model as the Windows Snipping Tool's "freeze" behavior: what the user selects is exactly what they saw,
overlays never appear in the capture, and multi-monitor/mixed-DPI setups are handled by one coordinate
system (physical virtual-desktop pixels).

Window capture crops the window's DWM extended frame bounds (`DWMWA_EXTENDED_FRAME_BOUNDS`, which excludes
the invisible resize border) from the frozen frame. Consequence: parts of the window covered by other
windows show the covering content. Capturing occluded content (via WGC) is a candidate for a later
milestone; it is listed as a limitation.

Supporting Win32 APIs: `EnumDisplayMonitors`/`GetMonitorInfoW` (monitor rectangles and work areas),
`GetDpiForMonitor` (scale, informational), `GetTopWindow`/`GetWindow(GW_HWNDNEXT)` (z-order walk),
`DwmGetWindowAttribute(DWMWA_CLOAKED)` (skip windows on other virtual desktops and suspended UWP frames),
`GetCursorPos` (physical cursor position). The process is Per-Monitor-V2 DPI aware via `app.manifest`, so
all of these return physical pixels.

Sources consulted: Microsoft Learn — [Capturing an image (GDI)](https://learn.microsoft.com/en-us/windows/win32/gdi/capturing-an-image),
[Screen capture (Windows.Graphics.Capture)](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture),
[PrintWindow DPI Q&A](https://learn.microsoft.com/en-us/answers/questions/1609805/does-the-printwindow-functions-behavior-depend-on);
[Magpie: comparison of capture methods](https://github.com/Blinue/Magpie/wiki/Comparison-of-capture-methods);
[OBS PR #2208 (WGC vs BitBlt cost)](https://github.com/obsproject/obs-studio/pull/2208).

## 2. Framework and dependencies

* **.NET 10 (LTS, supported to November 2028) + WPF.** .NET 8 reached end of support on 10 November 2026,
  so a new product should not start on it.
* **Runtime NuGet dependencies: none.** Imaging (PNG/JPEG via WIC), clipboard, dialogs and rendering are
  all in WPF. Native calls use source-generated `LibraryImport` (no runtime marshalling stubs, trimming-safe).
* **Test-only packages:** xUnit 2.9.3 (Apache-2.0), xunit.runner.visualstudio (Apache-2.0),
  Microsoft.NET.Test.Sdk (MIT), coverlet.collector (MIT). Not shipped.
* **No paid dependencies, no telemetry SDKs, no auto-updater.**

## 3. Licensing

* .NET runtime and WPF are MIT-licensed (verified in the `Microsoft.WindowsDesktop.App.Runtime.win-x64`
  10.0.x package metadata). Redistributing them in a self-contained build is allowed; attribution is in
  `THIRD-PARTY-NOTICES.md`.
* SnapForge is licensed under the **MIT License** (`LICENSE`, copyright holder "bazeocrisy" — the repository
  owner's GitHub account; to be confirmed by the owner). MIT is compatible with the MIT-licensed .NET runtime
  redistributed in the portable build.
* Code signing: the development build is unsigned, so Windows SmartScreen will warn. A production release
  needs an Authenticode certificate (a cost decision for the owner; not a code dependency).

## 4. Product name check: "SnapForge"

Web searches on 2026-11-25 found existing uses of the name:

| Use | Category | Conflict level |
|-----|----------|----------------|
| "SnapForge – Screenshot Editor and Design Tool" (Chrome Web Store, site snapforge.app) | **Screenshot editing** | **High — same product category** |
| "SnapForge" on Product Hunt, described as an AI-powered screenshot tool | **Screenshot tool** | **High** |
| "SnapForge" Windows utility on itch.io (Nexum Software), block-based game logic tool | Developer tool | Medium (same name, Windows) |
| "SnapForge UI" on Product Hunt, UI component library | UI components | Low |

No USPTO registration for "SnapForge" appeared in the searches, but web search is **not** a trademark
clearance. Five alternative names with preliminary searches are in [NAMING.md](NAMING.md).
Recommendation: treat SnapForge as a working name; before any public release, run a formal
trademark search (USPTO, EUIPO, UKIPO) and strongly consider a distinct name. To keep a rename cheap, the
name lives in `SnapForge.Core/ProductInfo.cs`, `Directory.Build.props`, the window title/XAML text and
the icon only.

Sources: [Chrome Web Store listing](https://chromewebstore.google.com/detail/snapforge-%E2%80%93-screenshot-ed/jddlbdehkgmdcgmmjinaplmfdaogelin),
[Product Hunt: SnapForge](https://www.producthunt.com/products/snapforge),
[itch.io: SnapForge](https://nexumsoftware.itch.io/snapforge),
[Product Hunt: SnapForge UI](https://www.producthunt.com/products/snapforge-ui/makers).
