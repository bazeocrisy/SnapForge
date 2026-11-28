# SnapForge 1.0.0-rc.1 — release notes (release candidate, not a public release)

**Channel:** release candidate, not approved for public release. **Platform:** Windows 11 24H2 or later, x64.
**Licence:** MIT; free; no ads, accounts, subscriptions or telemetry. **Packages:** unsigned portable ZIP and per-user
installer (CI artifact `SnapForge-1.0.0-rc.1-packages-<commit>`), CycloneDX SBOM, SHA-256 checksums.

## New since the V1 recording milestone

### Capture
- **Active window** capture (Ctrl+5) — captures the foreground window of another program without clicking.
- **Scrolling capture** (Ctrl+6) — select an area, then Auto-scroll or scroll by hand; frames are aligned exactly
  (sticky headers/footers kept once) or refused, never guessed.
- **Repeat last capture** (Ctrl+R) and **New from clipboard** (Ctrl+Shift+V).
- Region selection **magnifier** (8×, pixel position and colour) and **arrow-key nudging** (1 px; Shift 10 px).

### Editor
- New tools: **Pen** (D), **Callout** (K), **Stamp** (S: check, cross, warning, info, question, star),
  **Blur** (B), **Pixelate** (P).
- **Image** menu: rotate, flip, **Resize image** (px or %), **Canvas size** (with anchor), **Add border**,
  **Add drop shadow**, **Add watermark** — annotations follow transforms; every change can be undone.
- **Copy / paste / duplicate annotations** (Ctrl+Shift+C / Ctrl+V / Ctrl+D), **Print** (Ctrl+P), **BMP and TIFF** export.
- **Extract text (OCR)** (Ctrl+Shift+T) — offline, uses the OCR languages installed in Windows; review, edit, copy.
- The toolbar wraps on narrow windows so every button stays reachable.

### Video
- **Video editor**: trim start/end, remove sections, frame-by-frame stepping, save a frame to the Library,
  save the edited video to the Library, export MP4, export **GIF** (frame rate, width, dithering, loop).
- Import **MP4** files into the Library (validated; non-H.264 or damaged files are rejected).

### Library
- **Combine images** side by side or stacked.
- Drag-and-drop copies: removed at once when a drag is cancelled; after a drop they are kept for up to 24 hours
  (2 GB cap) so slow targets such as browser upload fields still work; **Settings → Delete drag-and-drop copies now**.

### App
- **First-launch welcome** (Help → Getting started), **Reset all preferences** (keeps captures).
- New **aperture app icon**; **Help → About** shows version, build number, channel, architecture and commit.
- **Per-user installer** (no administrator rights; upgrade keeps data; uninstall asks twice before deleting
  captures) alongside the portable ZIP.

## Fixed in this candidate
See [DEFECT-REGISTER.md](DEFECT-REGISTER.md): DEF-RC-01 (drag copies deleted after 2 minutes — too early for
browser uploads), DEF-RC-02 (CI not running on release branches), DEF-RC-03…06 (test and script defects),
DEF-RC-07 (toolbar buttons clipped at narrow widths).

## Known limitations and pending verification
- **Unsigned** — SmartScreen may warn. Signing is prepared but needs an owner decision ([SIGNING.md](SIGNING.md)).
- Physical-PC acceptance (audio devices, GPUs/hardware encoders, multi-monitor, HDR, real drag targets, Narrator/NVDA)
  is **pending** ([MANUAL-ACCEPTANCE-1.0.md](MANUAL-ACCEPTANCE-1.0.md)).
- Legal review of H.264/AAC distribution and the name is **pending** ([LICENSING-REVIEW.md](LICENSING-REVIEW.md)).
- Videos cannot be redacted; blur/pixelate are not redaction. Full list: [KNOWN-LIMITATIONS.md](KNOWN-LIMITATIONS.md).

## Upgrading from development builds
Captures, settings and recordings in `%LOCALAPPDATA%\SnapForge` are kept. Back the folder up first. Captures that use
the new annotation types cannot be opened by older builds.
