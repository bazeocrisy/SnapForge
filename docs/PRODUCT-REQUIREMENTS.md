# SnapForge master product requirements (Gate A)

Status: **Proposed — awaiting owner approval.** This document becomes the master requirements list once approved.
It supersedes [REQUIREMENTS.md](REQUIREMENTS.md) for new work; REQUIREMENTS.md stays as the record of what M1/M2
delivered (its IDs C*, E*, L*, AC* remain valid and are mapped below as BL-*).

Every requirement has: **ID · Source** (registry ID or owner instruction) **· Status · Priority · Milestone ·
Acceptance criteria · Evidence · Approval**. Sources: [SOURCE-REGISTRY.md](SOURCE-REGISTRY.md). Parity rows:
[FEATURE-PARITY-MATRIX.md](FEATURE-PARITY-MATRIX.md).

* **Status**: Implemented (baseline, automated tests) · Partial · Not started · Not planned
* **Priority**: P1 must · P2 should · P3 could
* **Approval**: _Proposed_ (all rows today) → Approved / Rejected / Deferred by the owner
* "OWNER" in Source = the owner's milestone-3 instructions (I-05, `CLAUDE-PROMPT.md`).

## 0. Global rules (apply to every milestone)

| ID | Requirement | Source | Acceptance criteria | Approval |
|---|---|---|---|---|
| G-01 | Preserve all existing functionality and user data; no destructive migration | OWNER | All baseline tests pass; existing libraries/settings open unchanged; settings schema changes are additive with version bump and tested upgrade | Proposed |
| G-02 | No fake controls: unavailable features are absent or disabled and labelled "Not available yet" | OWNER, M1 rule | UI test enumerates every command; each is either functional (has a test) or disabled with that label | Proposed |
| G-03 | Offline and local: no accounts, subscription, ads, automatic upload, sync or telemetry | OWNER | No network API in code (CI grep gate), no outbound connections observed in a manual network-monitor check | Proposed |
| G-04 | Original branding; Snagit used as functional reference only | OWNER | Review: no TechSmith names/icons/art/text in UI or assets | Proposed |
| G-05 | No paid dependencies; licence-compatible (MIT-compatible) only | OWNER | THIRD-PARTY-NOTICES lists every dependency + licence; CI fails on unknown licences (M9 tooling) | Proposed |
| G-06 | No merge to `main`, no release publication without explicit approval | OWNER | — | Proposed |
| G-07 | Evidence honesty: CI results are labelled CI; manual Windows results only from a real PC | OWNER | Handoffs separate CI and manual evidence | Proposed |

## 1. Baseline (implemented at 3018841) — preserved, regression-tested

| ID | Requirement (summary) | Maps to | Status | Evidence |
|---|---|---|---|---|
| BL-CAP-01…07 | Region, Window, Display, Full screen; Preview in Editor; Copy to clipboard; presets with hotkeys; hide own windows | C1–C7, AC1–AC6 | Implemented | CI real-input tests (InteractiveUiTests), I-03 |
| BL-ED-01…07 | Arrow, line, rectangle, ellipse, highlight, text, steps, crop; zoom; undo/redo; restore last tool/colour/width | E1–E9 | Implemented | Core + App tests, I-03 |
| BL-PRIV-01/02 | Opaque redaction irreversible on export; Sanitize; JPEG metadata check; warnings for editable originals | AC9–AC12, docs/REDACTION.md | Implemented | ExportTests, CaptureLibraryTests |
| BL-LIB-01…09 | Autosave library (opt-out), tags, favorites, virtual folders, date groups, trash/restore, retention, backup/restore, thumbnail size | L1–L20, AC23–AC31 | Implemented | CaptureLibraryTests, LibraryWorkflowTests |
| BL-TR-01…04 | Persistent tray, pin, close ≠ delete, show/hide | AC27 | Implemented | TrayAndSettingsTests, LibraryWorkflowTests |
| BL-EXP-01…03 | PNG/JPEG export, `.sfcap` editable package, clipboard | AC7, AC8 | Implemented | ExportTests |
| BL-SET-01/02 | Light/Dark/System theme; close to notification area; single instance | — | Implemented | UiSmokeTests |
| BL-SEC-01/02 | No network/telemetry; supply-chain controls (pinned actions, lock files, signed packages, checksums) | docs/SECURITY.md | Implemented | OBS grep, CI |

## 2. Milestone 3 (Gate B proposal) — detailed

### 2.1 Capture control panel (CAP-UI)

| ID | Requirement | Source | Status | Pri | Acceptance criteria | Evidence today | Approval |
|---|---|---|---|---|---|---|---|
| CAP-UI-01 | Compact horizontal panel (≤ 760×260 logical px at 100 %) with File/Edit/View/Help menus | OWNER; I-05 OBS | Not started (vertical panel exists) | P1 | Window renders within bounds at 100/150/200 %; menus reachable with Alt; screenshot at Gate C | I-06 (vertical) | Proposed |
| CAP-UI-02 | Left rail: All-in-One, Image, Video; only Image enabled, others disabled "Not available yet" | OWNER; S-CAP-01 DOC | Not started | P1 | UI test: Image selected by default; other two `IsEnabled=false`, tooltip + automation name contain "Not available yet" | — | Proposed |
| CAP-UI-03 | Selection dropdown: Region, Window, Display, Full screen | OWNER; FP-CAP-23 | Partial (tiles) | P1 | Each item triggers the existing capture mode (reuse real-input tests) | InteractiveUiTests | Proposed |
| CAP-UI-04 | Effects and Share dropdowns; Share offers only working destinations (None, Library, Clipboard, Folder if EXP-05 approved); Effects disabled until M4 | OWNER; FP-CAP-23 | Not started | P1 | Selecting each Share item produces the documented result in a test; Effects dropdown disabled + labelled | — | Proposed |
| CAP-UI-05 | Large circular Capture button with assigned hotkey text beneath; warning state when hotkey failed to register | OWNER; I-05 | Partial | P1 | Button is default (Enter); text equals current gesture or "No hotkey"; conflict state shown in test with a pre-registered hotkey | ShellIntegration conflict reporting | Proposed |
| CAP-UI-06 | Switches: Preview in Editor, Copy to Clipboard, Cursor, Time Delay | OWNER; S-CAP-04 DOC | Implemented (vertical) | P1 | Each switch persists in settings and is honoured by a capture (existing tests extended) | CaptureWindow | Proposed |
| CAP-UI-07 | Save to Library switch preserved | OWNER | Implemented | P1 | Off → capture produces no library files (existing test) | LibraryWorkflowTests | Proposed |
| CAP-UI-08 | Time Delay chevron opens duration picker (1–60 s, presets 1/3/5/10) | OWNER; S-CAP-03 DOC | Partial (0–10 fixed) | P1 | Values persist; countdown shown; Esc cancels during countdown | CountdownWindow | Proposed |
| CAP-UI-09 | Reset button restores panel defaults, with Undo toast | OWNER; I-05 | Not started | P2 | Reset then Undo returns previous options exactly | — | Proposed |
| CAP-UI-10 | Remembers position per monitor; clamps on-screen | INFER | Not started | P1 | Unit test of clamp logic with monitor-layout fixtures | — | Proposed |
| CAP-UI-11 | Footer: Presets flyout, "+" save preset, Open SnapForge Editor, Open Library | OWNER; S-CAP-09 DOC | Partial | P1 | Each command works via keyboard and mouse in UI test | CaptureWindow | Proposed |
| CAP-UI-12 | Light/dark/high-contrast themes | OWNER | Partial (no HC) | P1 | Renders with each theme; HC uses system colours (manual check) | ThemeManager | Proposed |
| CAP-UI-13 | Keyboard: Enter captures, Esc closes, access keys for dropdowns, Space toggles | NFR-A11Y | Partial | P1 | Keyboard-only UI test covers every control | — | Proposed |
| CAP-UI-14 | Rename misleading preset "Menu capture (3 s delay, cursor)" to "Region with 3 s delay and cursor" (DEF-03) | Audit | Not started | P1 | Existing users' preset renamed only if unchanged from default; test | AppSettings.Defaults | Proposed |
| CAP-HK-01 | Print Screen default verified on Windows 11 with Snipping Tool setting on/off | S-CAP-07/19 DOC | Partial | P1 | Manual result recorded both ways; UI explains conflict and how to fix | DEF-08 | Proposed |

### 2.2 Persistent capture widget (WID)

| ID | Requirement | Source | Status | Pri | Acceptance criteria | Approval |
|---|---|---|---|---|---|---|
| WID-01 | Widget can be enabled/disabled (Settings → Widget); default **off** pending Q-04 | OWNER; S-CAP-06 DOC | Not started | P1 | Toggle shows/hides immediately and persists | Proposed |
| WID-02 | Collapsed tab docked top-centre of primary monitor by default | OWNER | Not started | P1 | Position test on fixture layouts; screenshot | Proposed |
| WID-03 | Expands on hover (≥ 300 ms intent) or click (configurable); collapses on leave | OWNER; S-CAP-06 DOC | Not started | P1 | Real-input UI test: hover expands, leave collapses; click mode ignores hover | Proposed |
| WID-04 | Dock to top, left or right edge of any monitor by dragging; snaps | OWNER; S-CAP-06 DOC | Not started | P1 | Core dock-model unit tests; drag UI test | Proposed |
| WID-05 | Buttons: Capture, Editor, Library, Presets ▾ | OWNER | Not started | P1 | Each button test | Proposed |
| WID-06 | Auto-hide option | OWNER | Not started | P1 | Hidden until edge touched; test | Proposed |
| WID-07 | Topmost option | OWNER | Not started | P1 | Window style flag test | Proposed |
| WID-08 | Remembers monitor + edge + offset; survives monitor removal and mixed DPI | OWNER | Not started | P1 | Unit tests with layout changes; manual mixed-DPI check | Proposed |
| WID-09 | Never steals focus (non-activating tool window, no taskbar button) | OWNER | Not started | P1 | Test: foreground window unchanged after hover/expand | Proposed |
| WID-10 | Hidden during capture; never appears in captured pixels | OWNER | Not started | P1 | Capture test asserts widget region pixels ≠ widget colours | Proposed |
| WID-11 | Accessible: keyboard reachable from notification-area menu, automation names | NFR-A11Y | Not started | P1 | Automation tree test | Proposed |

### 2.3 Native drag and drop (DND)

| ID | Requirement | Source | Status | Pri | Acceptance criteria | Approval |
|---|---|---|---|---|---|---|
| DND-01 | Drag from Recent Captures tray and Library to external apps offers `CF_HDROP` file list | OWNER; S-LIB-03 DOC; S-LIB-55 DOC | Not started | P1 | Unit test inspects `DataObject` formats; Explorer drop (CI desktop) creates files | Proposed |
| DND-02 | Single and multi-selection (Ctrl/Shift-click in tray and Library) | OWNER; S-LIB-16 DOC | Not started (tray) / Implemented (Library multi-select) | P1 | Drag of N items yields N files | Proposed |
| DND-03 | Files are **flattened** renders (annotations applied, redactions burned in) — PNG by default | OWNER | Not started | P1 | Pixel test: dropped PNG equals export of same capture | Proposed |
| DND-04 | Optional JPEG (quality setting) in Settings → Drag and drop | OWNER; S-CAP-06 DOC | Not started | P1 | Setting changes extension and encoder; test | Proposed |
| DND-05 | Meaningful, unique, filesystem-safe filenames (title + date; collision suffix) | OWNER; S-LIB-31 DOC | Not started | P1 | Unit tests: reserved names, invalid chars, long titles, duplicates | Proposed |
| DND-06 | Copy semantics only (`DragDropEffects.Copy`); library originals never moved or deleted | OWNER | Not started | P1 | Test: library unchanged after drop with Move requested by target | Proposed |
| DND-07 | Editable originals preserved; `.sfcap`/base images never offered | OWNER | Not started | P1 | Test: data object contains no `.sfcap`, no unredacted base | Proposed |
| DND-08 | Safe cancellation (Esc, drop outside) and delayed temp cleanup (≥ 60 s after drop, on exit, and stale sweep at start) | OWNER | Not started | P1 | Tests with fake clock; temp folder empty after sweep | Proposed |
| DND-09 | Reordering inside the tray distinguished from export (drop target = tray → reorder, no files) | OWNER; S-LIB-01 DOC | Not started | P1 | UI test: reorder persists, no temp files created | Proposed |
| DND-10 | External image import: drop or open PNG/JPEG/BMP into Library or Editor | OWNER | Not started | P1 | Imported image appears in Library; invalid files rejected with message | Proposed |
| DND-11 | Compatibility evidence table for desktop, Explorer, Outlook, Teams, Word, PowerPoint, OneNote, browser upload — each marked **Tested (manual, real PC)** or **Expected** | OWNER | Not started | P1 | Table in Gate C handoff; no "Tested" without a recorded manual result | Proposed |
| DND-12 | Drag image shows thumbnail + count badge | UI spec | Not started | P2 | Visual check | Proposed |

### 2.4 Privacy of drag-out (PRIV-DND)

| ID | Requirement | Source | Status | Pri | Acceptance criteria | Approval |
|---|---|---|---|---|---|---|
| PRIV-DND-01 | Drag is an export: same privacy guarantees as File → Export | OWNER | Not started | P1 | Shared code path with export; test | Proposed |
| PRIV-DND-02 | Never expose hidden original pixels (pixel-level regression: redacted area identical to mask colour) | OWNER | Not started | P1 | Test on redacted capture with known underlying pattern | Proposed |
| PRIV-DND-03 | Strip metadata (no EXIF/XMP/text chunks beyond required) | OWNER | Not started | P1 | PNG chunk and JPEG segment inspection test | Proposed |
| PRIV-DND-04 | Temp files in per-user folder under `%LOCALAPPDATA%\SnapForge\Temp\Drag`, user-only ACL inherited, random subfolder per drag | OWNER | Not started | P1 | Test asserts location; ACL check on Windows CI | Proposed |
| PRIV-DND-05 | Blur/pixelate (when added in M4) never described as irreversible redaction | OWNER; S-ED-08 DOC | Not started | P1 | UI text review; test that blur tool label contains "not secure" | Proposed |
| PRIV-DND-06 | Cleanup on cancel, on exit, and crash recovery sweep at next start | OWNER | Not started | P1 | Tests | Proposed |

### 2.5 Scale and performance (PERF)

| ID | Requirement | Source | Status | Pri | Acceptance criteria | Approval |
|---|---|---|---|---|---|---|
| PERF-01 | Benchmarks with **production durability** (no `SkipDiskFlush`) at 100, 1 000, 5 000, 10 000, 25 000 captures where CI time allows; report startup, memory, search, thumbnail, autosave latency, UI responsiveness | OWNER | Not started | P1 | Benchmark report with machine description; skipped sizes explained | Proposed |
| PERF-02 | Seeding may bypass flush only if the measured operations do not, and the report says so | OWNER; DEF-01 | Partial | P1 | Code review + report wording | Proposed |
| PERF-03 | Startup does not block UI for library indexing at 10 000 captures (target: first window < 2 s on CI) | DEF-02 | Not started | P1 | Benchmark | Proposed |
| PERF-04 | Search over 10 000 records < 100 ms | INFER | Unknown | P2 | Benchmark | Proposed |
| PERF-05 | Memory: Library at 10 000 records < 400 MB working set | INFER | Unknown | P2 | Benchmark | Proposed |
| PERF-06 | Autosave latency at 10 000 captures within 2× of 100-capture latency | INFER | Unknown | P2 | Benchmark | Proposed |

### 2.6 Settings and NFRs touched in M3

| ID | Requirement | Source | Status | Pri | Acceptance criteria | Approval |
|---|---|---|---|---|---|---|
| SET-01 | Launch with Windows (per-user Run key), off by default | S-CAP-06 DOC; OWNER | Not started | P1 | Toggle writes/removes `HKCU\…\Run` value; test on Windows CI | Proposed |
| NFR-DPI-1 | Per-monitor DPI correct at 100/125/150/200 % and mixed | OWNER | Partial | P1 | Manual on real multi-monitor PC; CI unit tests for coordinate math | Proposed |
| NFR-A11Y-01 | Keyboard access to every command | OWNER | Partial | P1 | Keyboard-only UI tests per new surface | Proposed |
| NFR-A11Y-02 | Narrator/NVDA announce names/roles/states of new controls | OWNER; S-CAP-24 DOC | Not verified | P1 | Manual screen-reader pass recorded | Proposed |
| NFR-A11Y-03 | Windows high-contrast themes respected | S-CAP-24 DOC | Not verified | P1 | Manual check, screenshots | Proposed |
| NFR-SEC-04 | Runs as standard (non-admin) user | OWNER | Unverified | P1 | Manual run under standard account | Proposed |
| DEF-05 | Library sidebar storage path must not clip | Audit | Defect | P2 | Long path renders with ellipsis + tooltip | Proposed |

## 3. Later milestones (summary; detailed criteria written at that milestone's Gate A)

| ID | Requirement | Source | Status | Pri | MS | Acceptance (summary) | Approval |
|---|---|---|---|---|---|---|---|
| ED-OBJ-01 | Resize handles + move for all objects | S-ED-31 DOC | Partial (move) | P1 | M4 | Handle drag tests | Proposed |
| ED-OBJ-02 | Fill vs outline for shapes/text | S-ED-28/29 | Not started | P1 | M4 | Property round-trip in format | Proposed |
| ED-OBJ-03 | Colour picker with HEX/RGB + eyedropper | S-ED-38 | Not started | P1 | M4 | Tests | Proposed |
| ED-OBJ-04 | Object rotation | S-ED-29 | Not started | P2 | M4 | Tests | Proposed |
| ED-OBJ-05 | Opacity per object | S-ED-25 | Not started | P1 | M4 | Tests | Proposed |
| ED-OBJ-06 | Arrowheads/end styles | S-ED-25 | Not started | P1 | M4 | Tests | Proposed |
| ED-OBJ-07 | Shadow | S-ED-29 | Not started | P2 | M4 | Tests | Proposed |
| ED-OBJ-08 | Font family/style/alignment | S-ED-28 | Not started | P1 | M4 | Tests | Proposed |
| ED-OBJ-09 | Dash styles | S-ED-46 UNVERIFIED | Not started | P3 | M4 | Tests | Proposed |
| ED-OBJ-10 | Multi-select, marquee, Ctrl+A, select all of a kind | S-ED-22, S-ED-01 | Not started | P1 | M4 | Tests | Proposed |
| ED-OBJ-11 | Group/ungroup | S-ED-22 | Not started | P2 | M4 | Tests | Proposed |
| ED-OBJ-12 | Layering | S-ED-22 | Not started | P1 | M4 | Tests | Proposed |
| ED-OBJ-13 | Object cut/copy/paste/duplicate | S-ED-22/24 | Not started | P1 | M4 | Tests | Proposed |
| ED-OBJ-14 | Paste objects between captures (editable) | INFER | Not started | P2 | M4 | Tests | Proposed |
| ED-OBJ-15 | Per-object flatten | S-ED-22 | Not started | P2 | M4 | Tests | Proposed |
| ED-OBJ-16 | Arrow-key nudge (1/10 px) | INFER | Not started | P1 | M4 | Tests | Proposed |
| ED-OBJ-20 | Cursor captured as movable object | S-CAP-03 DOC | Partial (pixels) | P2 | M4 | Format + tests | Proposed |
| ED-TOOL-01…11 | Callout, step options, cut out, blur/pixelate (labelled not secure), magnify, pen, eraser, stamps (original art), fill, pixel selection, magic wand | S-ED-* | Not started | P1–P3 | M4/M6 | Per-tool tests | Proposed |
| ED-IMG-01…05 | Resize image, resize canvas, rotate/flip, auto trim, rounded corners | S-ED-35, S-ED-01 | Not started | P1–P3 | M4 | Tests | Proposed |
| ED-UI-01…03 | Toolbar customisation; position shortcuts; canvas context menu | S-ED-24/22 | Not started | P1–P3 | M4 | Tests | Proposed |
| ED-QS-01…03 | Per-tool Quick Styles; cross-tool favourites; palettes/themes | S-ED-05/06/07 | Partial | P1–P3 | M4 | Tests | Proposed |
| ED-FX-01…10 | Border, edges, shadow, perspective/page curl, spotlight, watermark, filters, colour adjust, capture info, effects in presets | S-ED-21/44 | Not started | P2–P3 | M4 | Tests | Proposed |
| CAP-ADV-01…12 | Magnifier, exact size, fixed region, crosshair keys, repeat last, menu, multi-area, scrolling auto/manual, interval, clipboard source, WGC backend | S-CAP-* | Not started | P2–P3 | M5 | Tests + documented app limits | Proposed |
| AI-01…07 | Offline OCR/grab text, smart redaction, smart move, simplify, background removal, OCR search, admin disable | S-ED-08…20 | Not started | P2–P3 | M6 | Offline-only; licence review | Proposed |
| DOC-01/02 | Templates/combine; step recording | S-LIB-16/19 | Not started | P3 | M7 | Tests | Proposed |
| EXP-01…06 | BMP/GIF/TIFF/WebP; HEIF investigation; PDF; print; capture-time save to folder; filename patterns | S-LIB-28/30/33 | Not started | P2 | M4/M7 | Tests | Proposed |
| LIB-05/06, LIB-META-01, LIB-BATCH-01, MIG-01 | Library merge on move; details view; app/website metadata; batch export; Snagit `.snagx` migration investigation | S-LIB-04/05/15 | Not started | P2–P3 | M4–M7 | Tests | Proposed |
| TR-01…03 | Tooltips; thumbnail sizes; clear-on-close preference | S-LIB-01 | Partial | P2–P3 | M3/M4 | Tests | Proposed |
| SET-03…06 | Restore default hotkeys; reset all settings; settings/preset import-export; sounds | S-CAP-06/07/21 | Not started | P2–P3 | M4 | Tests | Proposed |
| VID-01…11 | Recording, audio, webcam, pause/resume, countdown, quality, cursor effects, screen draw, trim/combine, video from images, GIF | S-LIB-21…27 | Not started | — | M8 | Codec licensing review first | Proposed |
| ENT-01…07 | MSI/MSIX, signing, SBOM, silent deploy/uninstall + Intune, encryption option, policy settings, controlled updates | S-LIB-34…41 | Not started | — | M9 | Owner decisions | Proposed |
