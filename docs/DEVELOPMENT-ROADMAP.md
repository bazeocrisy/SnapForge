# SnapForge development roadmap (proposed, Gate A)

Status: **Proposed — every milestone below needs explicit owner approval before work starts.** Approval of one
milestone does not authorise the next. Security, privacy, accessibility and the "no fake controls" rule apply to
every milestone. Requirement IDs are defined in [PRODUCT-REQUIREMENTS.md](PRODUCT-REQUIREMENTS.md); parity status in
[FEATURE-PARITY-MATRIX.md](FEATURE-PARITY-MATRIX.md).

| Milestone | Scope | Entry gate | Exit evidence |
|---|---|---|---|
| **M1** (done) | MVP capture, annotate, redact, export | — | PR #1 rounds 1–2 |
| **M2** (done, under review) | Capture window, Editor, Library, tray, persistent storage, privacy | — | PR #1 commit `3018841`, CI run 45704874375 |
| **M3 — Gate A** (this document set) | Forensic audit, parity matrix, requirements, UI spec, roadmap | Owner instruction | Docs committed; **stop for approval** |
| **M3 — Gate B** | Horizontal capture panel; persistent widget; native drag-out from tray/Library; privacy of drag-out; image import; library scale benchmarks and fixes found by them | Owner approves Gate A | Gate C handoff |
| **M3 — Gate C** | Handoff: clean build, test counts, CI evidence, real screenshots, artifact + SHA-256, manual checklist | Gate B complete | Independent review |
| M4 — professional editor | Resize/rotate handles, multi-select, grouping, layering, copy/paste between captures, per-tool Quick Styles, full tool properties, callouts, blur/pixelate (labelled not secure), resize image/canvas, rotate/flip, auto trim, pen, magnify, stamps (original artwork), spotlight, borders/shadow/edge effects, toolbar customisation, canvas context menu, eyedropper, custom palettes | M3 accepted | — |
| M5 — advanced capture | Scrolling capture (automatic where UI Automation permits; manual panoramic stitching), fixed/exact size, repeat last, multi-region, menu capture, crosshair magnifier, interval capture, clipboard capture; Windows Graphics Capture with GDI fallback | M4 accepted | — |
| M6 — offline intelligent tools | Offline OCR (Windows.Media.Ocr), grab text, OCR library search, smart redaction of detected text patterns (offline only), background removal only if an offline, licence-compatible model exists | M5 accepted | Licence review of every model |
| M7 — documentation/templates | Combine images, step templates, captions, reusable templates, PDF output | M6 accepted | — |
| M8 — video/audio | Screen recording (MP4 via Media Foundation), mic/system audio, webcam, pause/resume, trim, GIF, still extraction | M7 accepted | Codec licensing review |
| M9 — enterprise/release readiness | Code signing, MSI/MSIX, silent install/uninstall, policy settings, SBOM, accessibility conformance review, controlled updates | Owner decisions D-1…D-5 | Release candidate (still no publication without approval) |

## M3 Gate B work breakdown (for approval)

1. **Capture panel** (CAP-UI-01…14): new horizontal layout reusing the existing `CaptureOptions`/presets model;
   unavailable modes disabled with "Not available yet"; existing vertical window retired only after the new panel
   passes the same UI tests. Rename the misleading built-in preset "Menu capture (3 s delay, cursor)" (DEF-03).
2. **Widget** (WID-01…16): separate non-activating tool window; dock model in Core (unit-tested); settings page.
3. **Drag-out** (DND-01…12): `DataObject` with `CF_HDROP` (`DataFormats.FileDrop`) pointing to flattened exports in
   a private per-user temp folder; delayed cleanup; JPEG option; filename pattern; reorder-vs-export distinction.
4. **Drag privacy** (PRIV-DND-01…08): exports only flattened, redacted pixels; no `.sfcap`, no base image, metadata
   stripped; temp files ACL'd to the user, deleted on a schedule and on exit; pixel-level regression tests.
5. **Image import** (LIB-IMP-01): drop or open PNG/JPEG/BMP into the Library.
6. **Scale benchmarks** (PERF-01…06): production durability (no `SkipDiskFlush`) at 100/1 000/5 000/10 000/25 000
   captures where CI time permits; report startup, memory, search, thumbnails, autosave latency, UI responsiveness;
   fix the synchronous start-up index if measurements confirm the risk (DEF-02).
7. **Regression**: all 168 existing tests stay green; new tests for each item above.

Estimated size: large (comparable to M2). Highest-risk items: drag-drop behaviour in Office/Teams/Outlook/browsers
(needs real-Windows manual testing; CI can only test the data object and Explorer-style drop targets), and the
widget's focus/topmost behaviour across monitors.
