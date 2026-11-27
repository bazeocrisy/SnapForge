# Known limitations — SnapForge 1.0.0-rc.2

Honest list of what does not work, is not verified, or is deliberately out of scope. Recording-specific limitations
KL-V01…KL-V13 are detailed in [VIDEO-ACCEPTANCE.md](VIDEO-ACCEPTANCE.md).

## Not yet verified (pending physical-PC tests or external action)
- Real-application drag-and-drop compatibility (Outlook, Word, PowerPoint, Teams, browsers) — tested with Windows
  drag-and-drop and a separate-process receiver on CI only.
- Audio devices, hardware encoders, GPUs, displays above 1080p, multi-monitor/mixed-DPI, HDR, Remote Desktop, sleep,
  lock, power loss — CI has no audio, GPU or second monitor.
- Accessibility with Narrator/NVDA — not claimed compliant from automated checks.
- Code signing — packages are **unsigned** (SmartScreen may warn); see SIGNING.md. No bypass advice is given.
- Camera recording (SnapForge Studio) — tested on CI only with a synthetic camera; real drivers, formats, privacy
  error codes and the camera light need a physical PC.
- Interactive UAC with separate administrator credentials, fast user switching and Remote Desktop with the
  system-wide installer — scripted on CI, interactive check pending.
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
- Library backups (format 2, from rc.2) include recordings, the Trash, metadata and damaged items. Backups made with
  rc.1 contain images only (they still restore, labelled as such). A backup with any corrupt entry is refused as a
  whole (nothing is restored partially).
- Older SnapForge builds (rc.1 and earlier) show captures saved by rc.2 (capture format 2) — and captures with the new
  annotation types — as damaged and leave them unchanged; they cannot open them. Downgrading is not supported; keep a
  backup if you might go back.
- Repeat last capture is remembered only while SnapForge runs.
- Without `--profile`, development builds and installed builds share the same data folder
  (`%LOCALAPPDATA%\SnapForge`); use `SnapForge.exe --profile <name>` (rc.2) to test with separate data.
- The system-wide uninstaller never deletes user data; each user deletes their own data in Settings (or by hand).
- Drag-and-drop copies are temporary (up to 24 hours, 2 GB): for a permanent file use Export.
- Directory entries are not flushed after a rename (no portable API); after a power cut at the exact moment of a save,
  some file systems may keep the previous version of that capture (never a mixed or corrupt one).
- Windows 10, Windows on Arm and other operating systems are not supported in 1.0.
