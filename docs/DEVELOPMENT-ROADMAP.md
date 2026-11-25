# SnapForge development roadmap (proposed, Gate A)

Status: **M3 (Gate B) approved by the owner on 2026-11-25 with decisions: widget enabled and topmost by default; Windows 11 first. Every later milestone still needs explicit owner approval.** Approval of one
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
| M9 — enterprise/release readiness | Code signing, MSI/MSIX, silent install/uninstall, policy settings, SBOM, accessibility conformance review, controlled updates | Owner decisions Q-12 (audit §7) | Release candidate (still no publication without approval) |

## M3 Gate B work breakdown (for approval)

1. **Capture panel** (CAP-UI-01…14): new horizontal layout reusing the existing `CaptureOptions`/presets model;
   unavailable modes disabled with "Not available yet"; existing vertical window retired only after the new panel
   passes the same UI tests. Rename the misleading built-in preset "Menu capture (3 s delay, cursor)" (DEF-03).
2. **Widget** (WID-01…12): separate non-activating tool window; dock model in Core (unit-tested); settings page.
3. **Drag-out** (DND-01…12): `DataObject` with `CF_HDROP` (`DataFormats.FileDrop`) pointing to flattened exports in
   a private per-user temp folder; delayed cleanup; JPEG option; filename pattern; reorder-vs-export distinction.
4. **Drag privacy** (PRIV-DND-01…06): exports only flattened, redacted pixels; no `.sfcap`, no base image, metadata
   stripped; temp files ACL'd to the user, deleted on a schedule and on exit; pixel-level regression tests.
5. **Image import** (DND-10): drop or open PNG/JPEG/BMP into the Library.
6. **Scale benchmarks** (PERF-01…07): production durability (no `SkipDiskFlush`) at 100/1 000/5 000/10 000/25 000
   captures where CI time permits; report startup, memory, search, thumbnails, autosave latency, UI responsiveness;
   fix the synchronous start-up index if measurements confirm the risk (DEF-02).
7. **Regression**: all 168 existing tests stay green; new tests for each item above.

Estimated size: large (comparable to M2). Highest-risk items: drag-drop behaviour in Office/Teams/Outlook/browsers
(needs real-Windows manual testing; CI can only test the data object and Explorer-style drop targets), and the
widget's focus/topmost behaviour across monitors.

## Backlog (kept, not planned)

Capabilities identified in the audit but not scheduled are **kept** as requirements so nothing is lost: printer
capture (CAP-LEG-01), object capture (CAP-LEG-02), niche export formats (EXP-07), optional opt-in cloud destinations
(EXP-08), HEIF (EXP-02, investigate), Snagit `.snagx` migration (MIG-01, investigate).

## Coverage appendix — every parity row mapped to requirement and milestone

Generated from FEATURE-PARITY-MATRIX.md and PRODUCT-REQUIREMENTS.md (183 rows, 0 unmapped).

| FP row | Requirement(s) | Milestone(s) |
|---|---|---|
| FP-CAP-01 | CAP-UI-02 | M3 |
| FP-CAP-02 | BL-CAP-01 | M1/M2 |
| FP-CAP-03 | BL-CAP-02 | M1/M2 |
| FP-CAP-04 | BL-CAP-03 | M1/M2 |
| FP-CAP-05 | CAP-ADV-01 | M5 |
| FP-CAP-06 | CAP-ADV-02 | M5 |
| FP-CAP-07 | CAP-ADV-03 | M5 |
| FP-CAP-08 | CAP-ADV-04 | M5 |
| FP-CAP-09 | CAP-ADV-05 | M5 |
| FP-CAP-10 | CAP-ADV-06 | M5 |
| FP-CAP-11 | CAP-ADV-07 | M5 |
| FP-CAP-12 | CAP-ADV-08 | M5 |
| FP-CAP-13 | CAP-ADV-09 | M5 |
| FP-CAP-14 | CAP-ADV-10 | M5 |
| FP-CAP-15 | CAP-ADV-11 | M5 |
| FP-CAP-16 | CAP-LEG-01 | Backlog (not planned: driver/admin) |
| FP-CAP-17 | CAP-LEG-02 | Backlog (not planned) |
| FP-CAP-18 | ED-OBJ-20 | M4 |
| FP-CAP-19 | CAP-UI-09 | M3 |
| FP-CAP-20 | BL-CAP-04 | M1/M2 |
| FP-CAP-21 | BL-CAP-05 | M1/M2 |
| FP-CAP-22 | BL-LIB-01 | M1/M2 |
| FP-CAP-23 | CAP-UI-03, CAP-UI-04, CAP-UI-05 | M3 |
| FP-CAP-24 | EXP-05 | M4 |
| FP-CAP-25 | BL-CAP-06 | M1/M2 |
| FP-CAP-26 | SET-05 | M4 |
| FP-CAP-27 | CAP-HK-01 | M3 |
| FP-CAP-28 | BL-CAP-07 | M1/M2 |
| FP-CAP-29 | CAP-UI-01, CAP-UI-10, CAP-UI-12 | M3 |
| FP-CAP-30 | CAP-UI-01 | M3 |
| FP-CAP-31 | CAP-UI-06 | M3 |
| FP-CAP-32 | CAP-ADV-12 | M5 |
| FP-CAP-33 | NFR-DPI-01, NFR-DPI-02 | M3 |
| FP-WID-01 | WID-01 | M3 |
| FP-WID-02 | WID-02, WID-03 | M3 |
| FP-WID-03 | WID-04 | M3 |
| FP-WID-04 | WID-06 | M3 |
| FP-WID-05 | WID-07 | M3 |
| FP-WID-06 | WID-08 | M3 |
| FP-WID-07 | SET-01 | M3 |
| FP-ED-01 | ED-OBJ-01, AI-03 | M4, M6 |
| FP-ED-02 | ED-OBJ-06 | M4 |
| FP-ED-03 | BL-ED-01 | M1/M2 |
| FP-ED-04 | ED-OBJ-02, ED-OBJ-04, ED-OBJ-07 | M4 |
| FP-ED-05 | ED-OBJ-08 | M4 |
| FP-ED-06 | ED-TOOL-01 | M4 |
| FP-ED-07 | ED-TOOL-02 | M4 |
| FP-ED-08 | BL-ED-02 | M1/M2 |
| FP-ED-09 | BL-ED-03 | M1/M2 |
| FP-ED-10 | ED-TOOL-03 | M4 |
| FP-ED-11 | ED-TOOL-04, PRIV-DND-05 | M3+, M4 |
| FP-ED-12 | AI-02 | M6 |
| FP-ED-13 | AI-01 | M6 |
| FP-ED-14 | AI-04 | M6 |
| FP-ED-15 | ED-TOOL-05 | M4 |
| FP-ED-16 | ED-FX-05 | M4 |
| FP-ED-17 | ED-TOOL-06 | M4 |
| FP-ED-18 | ED-TOOL-07 | M4 |
| FP-ED-19 | ED-TOOL-08 | M4 |
| FP-ED-20 | ED-TOOL-09 | M4 |
| FP-ED-21 | ED-TOOL-10 | M4 |
| FP-ED-22 | ED-TOOL-11 | M6 |
| FP-ED-23 | AI-05 | M6 |
| FP-ED-24 | ED-IMG-01 | M4 |
| FP-ED-25 | ED-IMG-02 | M4 |
| FP-ED-26 | ED-IMG-03 | M4 |
| FP-ED-27 | ED-IMG-04 | M4 |
| FP-ED-28 | ED-IMG-05 | M4 |
| FP-ED-29 | ED-UI-01 | M4 |
| FP-ED-30 | ED-UI-02 | M4 |
| FP-ED-31 | BL-ED-04 | M1/M2 |
| FP-OBJ-01 | ED-OBJ-03 | M4 |
| FP-OBJ-02 | ED-OBJ-02 | M4 |
| FP-OBJ-03 | BL-ED-05 | M1/M2 |
| FP-OBJ-04 | ED-OBJ-05 | M4 |
| FP-OBJ-05 | ED-OBJ-08 | M4 |
| FP-OBJ-06 | ED-OBJ-06 | M4 |
| FP-OBJ-07 | ED-OBJ-09 | M4 |
| FP-OBJ-08 | ED-OBJ-07 | M4 |
| FP-OBJ-09 | ED-OBJ-04 | M4 |
| FP-OBJ-10 | ED-OBJ-01 | M4 |
| FP-OBJ-11 | ED-OBJ-10 | M4 |
| FP-OBJ-12 | ED-OBJ-11 | M4 |
| FP-OBJ-13 | ED-OBJ-12 | M4 |
| FP-OBJ-14 | ED-OBJ-13 | M4 |
| FP-OBJ-15 | ED-OBJ-13 | M4 |
| FP-OBJ-16 | ED-OBJ-14 | M4 |
| FP-OBJ-17 | ED-TOOL-02 | M4 |
| FP-OBJ-18 | ED-OBJ-15 | M4 |
| FP-OBJ-19 | BL-ED-06 | M1/M2 |
| FP-OBJ-20 | ED-OBJ-16 | M4 |
| FP-OBJ-21 | ED-QS-01 | M4 |
| FP-OBJ-22 | ED-QS-02 | M4 |
| FP-OBJ-23 | ED-QS-03 | M4 |
| FP-OBJ-24 | BL-ED-07 | M1/M2 |
| FP-OBJ-25 | ED-UI-03 | M4 |
| FP-FX-01 | ED-FX-01 | M4 |
| FP-FX-02 | ED-FX-02 | M4 |
| FP-FX-03 | ED-FX-03 | M4 |
| FP-FX-04 | ED-FX-04 | M4 |
| FP-FX-05 | ED-FX-06 | M4 |
| FP-FX-06 | ED-FX-07 | M4 |
| FP-FX-07 | ED-FX-08 | M4 |
| FP-FX-08 | ED-FX-09 | M4 |
| FP-FX-09 | ED-FX-10 | M4 |
| FP-FX-10 | LIB-BATCH-01 | M7 |
| FP-TR-01 | BL-TR-01 | M1/M2 |
| FP-TR-02 | BL-TR-02 | M1/M2 |
| FP-TR-03 | BL-TR-03 | M1/M2 |
| FP-TR-04 | DND-09 | M3 |
| FP-TR-05 | TR-02 | M4 |
| FP-TR-06 | BL-TR-04 | M1/M2 |
| FP-TR-07 | TR-03 | M4 |
| FP-TR-08 | DND-03 | M3 |
| FP-TR-09 | DND-01, DND-02, DND-03, DND-04, DND-05, DND-06, DND-07, DND-08, DND-09, DND-10, DND-11, DND-12, PRIV-DND-01, PRIV-DND-02, PRIV-DND-03, PRIV-DND-04 | M3 |
| FP-TR-10 | TR-04 | M7 |
| FP-TR-11 | TR-01 | M3 |
| FP-LIB-01 | BL-LIB-01 | M1/M2 |
| FP-LIB-02 | LIB-META-01 | M5 |
| FP-LIB-03 | BL-LIB-02 | M1/M2 |
| FP-LIB-04 | BL-LIB-03 | M1/M2 |
| FP-LIB-05 | BL-LIB-04 | M1/M2 |
| FP-LIB-06 | BL-LIB-05 | M1/M2 |
| FP-LIB-07 | BL-LIB-06 | M1/M2 |
| FP-LIB-08 | BL-LIB-07 | M1/M2 |
| FP-LIB-09 | LIB-05 | M4 |
| FP-LIB-10 | BL-LIB-08 | M1/M2 |
| FP-LIB-11 | DND-10 | M3 |
| FP-LIB-12 | DND-02 | M3 |
| FP-LIB-13 | LIB-BATCH-01 | M7 |
| FP-LIB-14 | AI-06 | M6 |
| FP-LIB-15 | LIB-06 | M4 |
| FP-LIB-16 | BL-LIB-09 | M1/M2 |
| FP-LIB-17 | MIG-01 | investigate |
| FP-LIB-18 | PERF-01, PERF-02, PERF-03, PERF-04, PERF-05, PERF-06, PERF-07 | M3 |
| FP-DOC-01 | DOC-01 | M7 |
| FP-DOC-02 | DOC-02 | M7 |
| FP-DOC-03 | VID-10 | M8 |
| FP-DOC-04 | EXP-03 | M7 |
| FP-VID-01 | VID-01 | M8 |
| FP-VID-02 | VID-02, VID-03 | M8 |
| FP-VID-03 | VID-04 | M8 |
| FP-VID-04 | VID-05 | M8 |
| FP-VID-05 | VID-06 | M8 |
| FP-VID-06 | VID-07 | M8 |
| FP-VID-07 | VID-08 | M8 |
| FP-VID-08 | VID-09 | M8 |
| FP-VID-09 | VID-11 | M8 |
| FP-EXP-01 | BL-EXP-01 | M1/M2 |
| FP-EXP-02 | EXP-01 | M4 |
| FP-EXP-03 | EXP-07 | Backlog (not planned) |
| FP-EXP-04 | EXP-02 | investigate |
| FP-EXP-05 | EXP-03 | M7 |
| FP-EXP-06 | BL-EXP-02 | M1/M2 |
| FP-EXP-07 | BL-EXP-03 | M1/M2 |
| FP-EXP-08 | EXP-04 | M4 |
| FP-EXP-09 | DND-05, EXP-06 | M3, M4 |
| FP-EXP-10 | DND-11 | M3 |
| FP-EXP-11 | EXP-08 | Backlog (owner decision; conflicts with G-03 unless opt-in) |
| FP-SET-01 | SET-01 | M3 |
| FP-SET-02 | BL-SET-01 | M1/M2 |
| FP-SET-03 | NFR-A11Y-03 | M3 |
| FP-SET-04 | SET-03 | M4 |
| FP-SET-05 | ENT-07 | M9 |
| FP-SET-06 | BL-SEC-01 | M1/M2 |
| FP-SET-07 | SET-04 | M4 |
| FP-SET-08 | SET-05 | M4 |
| FP-SET-09 | DND-04 | M3 |
| FP-SET-10 | BL-SET-02 | M1/M2 |
| FP-SET-11 | SET-06 | M4 |
| FP-SET-12 | CAP-UI-11 | M3 |
| FP-SEC-01 | BL-SEC-01 | M1/M2 |
| FP-SEC-02 | BL-SEC-01 | M1/M2 |
| FP-SEC-03 | BL-PRIV-01 | M1/M2 |
| FP-SEC-04 | ENT-05 | M9 |
| FP-SEC-05 | BL-PRIV-02 | M1/M2 |
| FP-SEC-06 | BL-SEC-02 | M1/M2 |
| FP-SEC-07 | ENT-03 | M9 |
| FP-SEC-08 | ENT-02 | M9 |
| FP-SEC-09 | ENT-01, ENT-04 | M9 |
| FP-SEC-10 | NFR-SEC-04 | M3 |
| FP-SEC-11 | NFR-A11Y-01, NFR-A11Y-02, NFR-A11Y-03 | M3 |
| FP-SEC-12 | AI-07 | M6 |
