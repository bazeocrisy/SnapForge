# Windows Graphics Capture (WGC) vs. GDI for single-window capture

Status: **investigation only — not implemented.** Date: 2026-11-25.

learn.microsoft.com is not reachable from the build environment, so the Microsoft documentation was read
from its official source repositories on GitHub (MicrosoftDocs/winrt-api, MicrosoftDocs/sdk-api,
MicrosoftDocs/windows-dev-docs). Statements marked **(unverified)** are not found in those sources and must be
confirmed with a prototype before any implementation decision.

## What SnapForge does today (GDI freeze-frame)

The whole virtual desktop is copied once with `BitBlt` (`SRCCOPY | CAPTUREBLT`); window capture then crops
the picked window's `DWMWA_EXTENDED_FRAME_BOUNDS` rectangle from that frozen frame.

## Documented WGC facts

| Fact | Source |
|------|--------|
| `IGraphicsCaptureItemInterop::CreateForWindow` (create a capture item from an HWND, the way a Win32 app captures a specific window without the picker) requires **Windows 10 version 1903 (build 18362)**. | sdk-api: `nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow.md` (`req.target-min-winverclnt`) |
| `GraphicsCaptureSession.IsCursorCaptureEnabled` was added in **Windows 10 2004 (SDK 19041)**. | winrt-api: `graphicscapturesession.md` (version history) |
| The system draws a **yellow border** around every item being captured. | windows-dev-docs: `screen-capture.md` |
| Disabling the border (`IsBorderRequired = false`) only takes effect after the user grants consent via `GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless)`, which shows a prompt, and calling it requires the **`graphicsCaptureWithoutBorder` capability in the app's package manifest**. If another app requires the border for the same item, it is shown anyway. | winrt-api: `graphicscapturesession_isborderrequired.md` |
| Screen capture APIs are supported only on Windows desktop devices and Mixed Reality headsets; apps should check `GraphicsCaptureSession.IsSupported()`. | windows-dev-docs: `screen-capture.md` |
| Frames arrive through a `Direct3D11CaptureFramePool` as Direct3D surfaces with a `ContentSize`; with HDR enabled the format may not be BGRA8 and content can look washed out unless an FP16 pipeline/tone mapping is used. | windows-dev-docs: `screen-capture.md` |
| In a desktop app, `GraphicsCapturePicker` must be associated with an owner HWND before showing UI. | winrt-api: `graphicscapturepicker.md` |
| Minimum OS for `IsBorderRequired` / `GraphicsCaptureAccess` | Not stated in the fetched sources **(unverified)** |

## Comparison for SnapForge

| | GDI freeze-frame (current) | WGC per-window |
|--|---------------------------|----------------|
| Covered/overlapped windows | Shows whatever covers the window | Captures the window's own content **(unverified for all window types; expected from DWM redirection)** |
| Minimized windows | Not offered (filtered out) | No content to capture **(unverified)** |
| Capture border | None | Yellow border while capturing; removable only with consent **and** a packaged-app capability |
| Portable, unpackaged exe | Works | Works, but border cannot be removed without package identity (MSIX or sparse package) |
| "What you see is what you get" selection | Exact (selection happens on the frozen frame) | Asynchronous: window content can change between hover and capture |
| Multi-monitor / mixed DPI | One physical-pixel coordinate system, tested by unit + UI tests | Per-item; window may span monitors; needs its own DPI handling **(unverified)** |
| HDR | Tone-mapped by GDI | Needs FP16 handling for correct HDR |
| Protected content (`SetWindowDisplayAffinity`, DRM) | Black | Excluded/black **(unverified)**; neither path may bypass it, and SnapForge must not try |
| OS minimum | Windows 10 1703 (Per-Monitor V2), well below .NET 10's floor | Windows 10 1903 for `CreateForWindow` |
| Code/dependencies | ~150 lines of P/Invoke, no packages | D3D11 device + WinRT interop. Either target `net10.0-windows10.0.19041.0` (adds the Microsoft-published C#/WinRT projection assembly to the build) or hand-written COM interop; GPU→CPU readback via a staging texture |
| Failure modes | GDI errors (rare) | No D3D device (some VMs/RDP), `IsSupported()` false, capture item closed when window closes, frame timeouts |

## Compatibility implications

* .NET 10's own supported Windows versions (see [COMPATIBILITY.md](COMPATIBILITY.md)) are all ≥ 1903 except
  Windows 10 1607 LTSB and 1809 LTSC (1809 = build 17763 < 18362). A WGC path would therefore need the
  existing GDI path as a **fallback**, not a replacement.
* Remote Desktop sessions and VMs without a GPU should be tested specifically **(unverified behavior)**.
* The border is a deliberate privacy signal from Windows; SnapForge must not attempt to hide it by other means.

## Recommendation

Keep GDI freeze-frame as the default (it matches the user's mental model of "freeze, then pick").
After MVP review, prototype an **optional** "capture window content even if covered" mode using WGC
`CreateForWindow`, with automatic fallback to GDI, an explicit note that Windows shows a yellow border,
and acceptance tests on Windows 10 22H2, Windows 11 24H2/25H2, RDP and a GPU-less VM. Do not take a dependency
on package identity for the MVP.
