# Self-audit — MVP development build

Date: 2026-11-25 · Branch: `claude/pensive-euler-ulw6m4`

## Method

1. Line-by-line review of all code against the requirements in `REQUIREMENTS.md` by the author.
2. A separate reviewer pass (a second agent given only the code, not the author's conclusions) looking for
   runtime bugs: P/Invoke layouts, GDI handle lifetime, DPI/multi-monitor math, XAML resource order,
   input/focus handling, undo/redo integrity, redaction leakage, re-entrancy.
3. Automated: compiler + .NET analyzers (`latest-recommended`, warnings as errors), unit tests on Linux
   and Windows, Windows-only rendering/capture/clipboard/UI tests, NuGet vulnerability audit.

## Findings from the review and their resolution

| # | Severity | Finding | Status |
|---|----------|---------|--------|
| 1 | **High** | Region selection could never complete: the overlay released mouse capture *before* evaluating mouse-up, and the synchronous `LostMouseCapture` handler discarded the drag. | Fixed — mouse-up is evaluated first, then capture is released. |
| 2 | Medium | Del or Ctrl+Z while dragging an annotation could commit a move for an annotation no longer in the document (exception dialog). | Fixed — undo/redo/delete cancel any gesture first; move commit verifies the annotation still exists. |
| 3 | Medium | After more than 200 edits, undoing every remaining step reported "no unsaved changes" although evicted edits remained. | Fixed — history keeps a base state id; regression test added. |
| 4 | Low-Med | Changing color/size while editing existing text was ignored on commit. | Fixed. |
| 5 | Low | "1:1" zoom / percentage stale after moving the window to a monitor with another scale. | Fixed — recomputed on `DpiChanged`. |
| 6 | Low | If opening an overlay failed on one monitor, overlays already shown on others stayed open. | Fixed — closed before rethrowing. |
| 7 | Low | Closing the app mid-save could leave a hidden temp file. | Fixed — closing is blocked while a capture/save is in progress. |
| 8 | Low | `GetWindowLongPtrW` does not exist on 32-bit Windows. | Fixed — falls back to `GetWindowLongW` (the published build is x64 anyway). |
| 9 | Note | A redaction does not hide *annotations* drawn earlier over the same area (annotations render above pixels). It never exposes original screen pixels. | Documented limitation (see below). |

Verified correct by the reviewer: struct sizes (`MONITORINFOEXW` 104 bytes, `BITMAPINFOHEADER`, `RECT`),
GDI cleanup in `finally`, no exceptions crossing the native monitor-enumeration callback, physical-pixel
cursor handling, StaticResource ordering, event handlers during `InitializeComponent`, re-entrancy guard.

## Automated results

See the pull request checks for the latest run. First CI run (commit `377343d`, before the fixes above):
Windows job — build 0 warnings/0 errors; **75/75 core tests and 15/15 Windows tests passed** on
`windows-2025` (GDI capture, window enumeration, export pixels, encoders, clipboard, main-window smoke test);
no vulnerable packages; portable exe published. Linux job — 75/75 core tests passed; Windows projects compile.

## What has NOT been verified

* **No interactive testing on a physical Windows machine.** The CI runner has a desktop session, so real
  capture APIs and a real WPF window were exercised, but nobody has dragged a selection, clicked a window
  in the overlay, used a mixed-DPI multi-monitor setup, or pasted into another application.
* The overlay windows (`SelectionOverlayWindow`, `OverlaySession`) and mouse-driven editing
  (`AnnotationCanvas` gestures) have no automated tests.
* Behavior on Windows 10 (CI uses Windows Server 2025), on ARM64, on HDR displays and with > 8K-wide desktops.

## Known limitations (by design for MVP)

* Window capture shows overlapping windows on top of the picked window (freeze-frame model).
* HDR content is captured tone-mapped to SDR by GDI.
* Annotations created before a redaction remain visible over it; delete them if they contain sensitive text.
* Redaction is black only; no blur/pixelate (intentionally — those can be reversible).
* No global hotkeys/tray, delay timer, crop, settings persistence, installer, code signing or localization.
* Unsigned executable → SmartScreen warning.
