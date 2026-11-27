# Known limitations — SnapForge 1.0.0-rc.1

Honest list of what does not work, is not verified, or is deliberately out of scope. Recording-specific limitations
KL-V01…KL-V13 are detailed in [VIDEO-ACCEPTANCE.md](VIDEO-ACCEPTANCE.md).

## Not yet verified (pending physical-PC tests or external action)
- Real-application drag-and-drop compatibility (Outlook, Word, PowerPoint, Teams, browsers) — tested with Windows
  drag-and-drop and a separate-process receiver on CI only.
- Audio devices, hardware encoders, GPUs, displays above 1080p, multi-monitor/mixed-DPI, HDR, Remote Desktop, sleep,
  lock, power loss — CI has no audio, GPU or second monitor.
- Accessibility with Narrator/NVDA — not claimed compliant from automated checks.
- Code signing — packages are **unsigned** (SmartScreen may warn); see SIGNING.md.
- Legal review of H.264/AAC distribution and the SnapForge name — see LICENSING-REVIEW.md.

## Functional limitations
- **Videos cannot be redacted or sanitized.** Blur and pixelate (images) are not redaction.
- The Library copy keeps original pixels under redactions until you choose **Sanitize**. Sanitize cannot reach exports,
  backups, synced copies, copies in other apps, or data remaining on SSDs after deletion. SnapForge does not encrypt
  the Library.
- **Scrolling capture** works only where content scrolls inside a fixed area; apps that animate, lazy-load or redraw
  while scrolling may not align (SnapForge refuses such frames rather than guessing). Large scroll steps may be
  refused — scroll less than about two thirds of the area. Horizontal scrolling is supported by the stitcher but the
  Auto-scroll button scrolls vertically.
- **OCR** depends on recognition languages installed in Windows; accuracy is not guaranteed; text is never saved.
- Text annotations keep their orientation when the image is rotated (their position follows).
- Printing prints the flattened image fitted to the page (no print preview or layout options).
- Annotation copy/paste uses an in-app clipboard (not shared with other programs).
- Alignment guides, grouping, snapping and templates are not included (deferred).
- Video editing: trim and removed sections re-encode the video (quality setting applies); combining clips, webcam
  overlay, recording annotations and cursor highlight are not included (deferred).
- GIF export: at most 256 colours, no sound; long ranges make large files.
- Library backups (.zip) contain images only; back up the `videos` folder separately.
- Older SnapForge builds show captures that use the new annotation types (pen, callout, stamp) as damaged and leave them
  unchanged; they cannot open them.
- Repeat last capture is remembered only while SnapForge runs.
- Development builds and installed builds share the same data folder (`%LOCALAPPDATA%\SnapForge`); back it up before
  testing another build.
- Windows 10, Windows on Arm and other operating systems are not supported in 1.0.
