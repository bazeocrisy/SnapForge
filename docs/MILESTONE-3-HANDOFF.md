# Milestone 3 handoff (Gate C) — for independent review

Date: 2026-11-26 · Branch `claude/pensive-euler-ulw6m4` · PR #1 (open, **not merged**) · No release published.

**Evidence labels:** *CI* = automated on GitHub's Windows Server runner (single 1024×768 display, administrator
account) — *Manual* = on a real Windows PC by a person; **none has been performed yet** — *DOC* = vendor
documentation seen only as search-engine extracts.

## 1. What was delivered

| Owner item | Delivered | Where |
|---|---|---|
| Audit corrections | 183 rows reconciled (n/a column); 223 requirements with unique IDs, evidence class and verification; "exceeds" claims revalidated (4 confirmed, 5 plausible, 6 withdrawn); every capability mapped to a milestone or backlog | SNAGIT-FORENSIC-AUDIT §3a–3b, PRODUCT-REQUIREMENTS, FEATURE-PARITY-MATRIX, DEVELOPMENT-ROADMAP |
| Capture control panel | Compact horizontal panel: File/Edit/View/Help; rail All-in-One / Image / Video (only Image active; others disabled "Not available yet"); Selection dropdown; Effects and Share disabled "Not available yet"; switches Preview in Editor, Copy to Clipboard, **Save to Library**, Cursor, Time Delay (1–60 s picker); round Capture button with hotkey and conflict help; reset with undo; presets flyout; Open Library / Open SnapForge Editor; remembered position | `Views/CaptureWindow.*`, screenshots capture-panel-* |
| Desktop widget | Enabled and always on top by default; collapsed tab at top centre; expands on hover or click (setting); Capture / Editor / Library / Presets; dock top/left/right on any monitor by dragging; remembers monitor/edge/offset (falls back to primary); auto-hide option; non-activating; hidden during capture; settings page; optional launch with Windows | `Views/CaptureWidget.*`, `Core/Settings/WidgetSettings.cs`, screenshots widget-* |
| Drag and drop | Native drag-out (`CF_HDROP`) from tray and Library, single and multiple; PNG default, JPEG option with quality; names "title yyyy-MM-dd HHmmss.png", unique and Windows-safe; copy-only; only flattened, redacted, metadata-free images; files written only when a target asks; private per-drag temp folder removed after 2 min, on exit and at next start; tray reorder; PNG/JPEG/BMP import (button and drop) storing pixels only | `Services/CaptureDragDrop.cs`, `MainWindow.DragDrop.cs`, `Core/Export/*` |
| Performance | Benchmarks 100 / 1 000 / 5 000 / 10 000 / 25 000 captures, production flushing, none skipped; synchronous library start-up fixed (background index) | BENCHMARKS.md |
| Research | Source registry revalidation **blocked** — hosts still denied (re-checked 2026-11-25); nothing described as direct inspection | SOURCE-REGISTRY "Revalidation status" |
| Product decisions | Recorded as PD-01…PD-05 (name, copyright, Windows 11 first, signing before public release, free/offline) | PRODUCT-REQUIREMENTS |
| QA fixes | Misleading preset renamed (with migration); Library storage path trimmed with tooltip; high-contrast palette | tests listed below |

## 2. Automated results (CI)

Final code commit `bd6ca394d92b63f85104c36e3fdea2a90d7be5dc`, run https://github.com/bazeocrisy/SnapForge/actions/runs/45724805609
(later commits on the branch change documentation only).

* Build: 0 warnings, 0 errors (warnings are errors). No vulnerable packages (5 projects).
* **Core tests: 150 / 150** (Windows and Linux) — 135 existing + 15 new.
* **Windows app tests: 50 / 50** — 33 existing + 17 new (real mouse input, real OLE drag-and-drop, real screen capture).
* Portable build without .NET: checksums verified, self-test PASS, screenshot tour PASS.
* Benchmarks: see BENCHMARKS.md (run 45722027176 on `ddabbbc`, same product code as `bd6ca39`).

New tests (Windows): `Capture_panel_is_compact_horizontal_and_marks_unavailable_modes`,
`Capture_panel_selection_delay_reset_and_presets_work`, `Capture_panel_remembers_its_position_and_explains_print_screen_conflicts`,
`Widget_is_enabled_topmost_non_activating_and_docked_top_centre_by_default`, `Widget_expands_on_hover_without_taking_focus_and_collapses_when_left`,
`Widget_docks_to_left_and_right_edges_and_remembers_the_place`, `Widget_is_hidden_during_capture_and_never_in_the_captured_pixels`,
`Drag_data_offers_only_flattened_redacted_images_named_by_title_and_time`, `Real_mouse_drag_from_tray_delivers_png_files_through_OLE`,
`Real_mouse_drag_of_a_library_multi_selection_delivers_one_file_per_capture`, `Reordering_inside_the_tray_creates_no_files_and_cancelled_drags_leave_nothing`,
`Tray_ctrl_click_selects_several_without_opening`, `Png_and_jpeg_import_keeps_pixels_only_and_rejects_bad_files`,
`Launch_with_windows_writes_and_removes_only_the_per_user_run_value`, `High_contrast_palette_covers_every_theme_colour_from_system_colours`,
`Library_opens_instantly_and_indexes_in_the_background_without_freezing_the_ui`, `Library_storage_path_is_trimmed_with_full_path_in_tooltip`.
New core tests: widget docking (incl. mixed-DPI two-monitor layouts, removed monitor), window-position clamping,
file names, temp cleanup, tray reorder, settings migration, deferred index, image import.

## 3. Drag-and-drop compatibility

| Target | Status | Basis |
|---|---|---|
| WPF drop target in the same process via OLE (`CF_HDROP` → file list) | **Tested (CI)** | real mouse drag tests |
| Windows desktop | Expected | `CF_HDROP` is the standard shell file format (S-LIB-55, DOC) |
| Explorer folder | Expected | same |
| Outlook (attachment) | Expected | same |
| Teams chat | Expected | same |
| Word / PowerPoint / OneNote | Expected | same |
| Browser upload (Edge/Chrome/Firefox) | Expected | same |

No real application has been tested by a person yet. Please use ACCEPTANCE-TESTING Part 13 (M3-D1…D3) and the
compatibility table in ACCEPTANCE-RESULTS-TEMPLATE.md; only rows you record become "Tested".

## 4. Requested QA areas

| Area | Automated (CI) | Manual (real PC) |
|---|---|---|
| Print Screen conflicts | Conflict detection/explanation tested | **Pending** M3-P8 (Windows 11 Snipping Tool setting on/off) |
| Standard-user operation | Only per-user APIs (HKCU, %LOCALAPPDATA%); CI runs as admin | **Pending** M3-S2 |
| High contrast | Palette covers every colour from system colours | **Pending** M3-S3 |
| Accessibility (keyboard, screen reader) | Automation names, default button, access keys asserted | **Pending** M3-P6, M3-W6 |
| Multi-monitor | Placement math unit-tested with two-monitor layouts | **Pending** M3-W4 (CI has one display) |
| Mixed DPI | Unit-tested with 100 % + 150 % layouts | **Pending** M3-W4 |

## 5. Build to test

* Portable build artifact: `SnapForge-portable-win-x64-dev-bd6ca394d92b63f85104c36e3fdea2a90d7be5dc` in run 45724805609
  (Actions → run → Artifacts; 30-day retention). Contents are listed with SHA-256 in `SHA256SUMS.txt`.
* `SnapForge.exe` SHA-256: `2c8e7b04367afd3f3632ff9547b5a0e39d7da11c6f57a1191a9d794af8fbbbbc`
* Not code-signed: Windows SmartScreen will warn (PD-04: trusted signing before any public release).

## 6. Known limitations and open defects

* KL-PERF-1: up to ≈260 ms UI pause when opening the Library grid or searching a 25 000-capture library.
* KL-DND-1: SnapForge timers pause during a drag (Windows' modal drag loop).
* KL-WID-1: the widget cannot take keyboard focus (by design); use the notification-area menu or panel.
* KL-UI-1: Share dropdown disabled instead of offering Library/Clipboard (deviation documented in UI-SPECIFICATION).
* DND-12 (P3) not done: no custom drag image.
* Library index not persisted: ≈6 s "Loading the library…" after start-up at 25 000 captures (UI usable).
* Open from earlier audit: DEF-06 (editor object features, M4), DEF-11 (standard user, manual), DEF-12 (autosave window).
* Source revalidation (RES-01) waiting for network access to TechSmith/Microsoft documentation.
* Windows 10 not yet evaluated (PD-03).

## 7. Not done on purpose (scope)

No video, effects, editor-object work, cloud destinations or other later-milestone items; no merge to `main`; no
release; no paid dependencies.
