# Product requirements — MVP (v0.1)

## Goal

A fast, private, Windows-native screenshot tool: capture any part of the screen, explain it with
arrows, shapes, text and numbered steps, hide sensitive data for real, then copy or save.

## Users and core jobs

* Support/IT staff and technical writers documenting steps ("click 1, then 2").
* Developers and testers filing bug reports.
* Anyone sharing a screenshot who must hide personal data before sending it.

## In scope (MVP)

| ID | Requirement |
|----|-------------|
| R1 | Capture all displays as one image. |
| R2 | Capture a dragged region, including regions spanning monitors. |
| R3 | Capture a window by clicking it (visible area). |
| R4 | Capture one display by clicking it. |
| R5 | Correct results on multi-monitor setups with negative coordinates and mixed DPI scaling. |
| R6 | Preview the capture in an editor with fit-to-window, actual-pixels and zoom. |
| R7 | Annotate with arrow, line, rectangle, ellipse and text; choose color and size; Shift constrains. |
| R8 | Numbered step markers that auto-increment. |
| R9 | Select, move, recolor, resize and delete annotations; edit text. |
| R10 | Permanent redaction: covered pixels are replaced, never recoverable from output. |
| R11 | Undo and redo for every edit, including redaction. |
| R12 | Save as PNG (lossless) or JPEG (quality 92). |
| R13 | Copy the finished image to the clipboard. |
| R14 | Warn before discarding unsaved changes. |
| R15 | No network access, telemetry, elevation or background services. |
| R16 | Portable build that runs without installing .NET. |

## Out of scope for MVP (deliberately deferred)

Video/GIF recording, scrolling capture, global hotkeys and tray icon, delayed/timed capture, crop/resize
of the captured image, blur/pixelate (non-permanent-looking) redaction, OCR, cloud upload/sharing, settings
persistence, auto-save, installer/auto-update, localization, capture of occluded window content,
HDR-accurate capture.

## Acceptance criteria

Each criterion is verified either automatically (A) in CI or manually (M) on Windows using
`docs/ACCEPTANCE-TESTING.md`.

| ID | Criterion | Verify |
|----|-----------|--------|
| AC1 | Full-screen capture produces an image whose size equals the virtual-desktop bounding box. | A (capture test) + M |
| AC2 | Areas of the bounding box not covered by any monitor are black, never garbage. | A (geometry) + M |
| AC3 | Region selection result size equals the size shown in the overlay label, within 0 px. | M |
| AC4 | A region dragged across two monitors with different scale factors contains both parts without gaps, offsets or scaling. | M |
| AC5 | Window capture returns the window's visible frame without the invisible resize border; maximized-window overhang is clipped. | A (geometry) + M |
| AC6 | Display capture returns exactly that monitor's resolution. | M |
| AC7 | Esc or right-click cancels any selection and restores the previous screenshot unchanged. | M |
| AC8 | SnapForge's own window never appears in a capture. | M |
| AC9 | Each annotation tool creates the shape it previews; strays < 3 px are ignored. | A (model) + M |
| AC10 | Step markers number 1, 2, 3…; deleting a middle step does not cause a duplicate number. | A |
| AC11 | After redaction, the saved PNG, saved JPEG source and clipboard image contain only the fill color in the redacted area. | A (pixel test) + M |
| AC12 | Undo/redo restores the exact previous state, including original pixels after undoing a redaction. | A |
| AC13 | PNG export is lossless (pixel-identical round trip) and contains no text/EXIF metadata. | A |
| AC14 | Saving never leaves a partial file on failure; existing files are replaced only after confirmation. | A + M |
| AC15 | Clipboard contains both a bitmap and PNG; pasting into Paint/Word/browser works. | A (formats) + M (paste) |
| AC16 | Closing or recapturing with unsaved changes asks first. | M |
| AC17 | The portable exe starts on a clean Windows 10 22H2 / Windows 11 machine without .NET installed. | M |
| AC18 | Build has zero compiler/analyzer warnings; all automated tests pass on Windows CI. | A |
