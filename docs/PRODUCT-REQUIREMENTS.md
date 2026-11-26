# SnapForge master product requirements

Status: **master list** (Gate A corrected 2026-11-25; Gate B approved with owner decisions). Generated from one
structured list, so every row has a **unique ID** (checked mechanically: 234 requirements, 0 duplicates) and every
parity-matrix row maps to at least one requirement (checked: 183 matrix rows, 0 dangling references).
[REQUIREMENTS.md](REQUIREMENTS.md) stays as the record of milestones 1–2; its IDs map to the BL-* rows.

Columns: **ID · Requirement · Source** (registry ID, owner instruction, or audit finding) **· Ev** (evidence class of
the *basis* for the requirement: OWNER = owner instruction, DOC = vendor documentation seen only as a search
extract, OBS = auditor observation of SnapForge, INFER = assumption, UNVERIFIED = conflicting/weak) **· Status ·
Pri · MS · Acceptance · Verification** (named automated test, CI job, or "manual"; "gap" = not yet verified)
**· Approval · FP** (parity rows). Sources: [SOURCE-REGISTRY.md](SOURCE-REGISTRY.md). Parity:
[FEATURE-PARITY-MATRIX.md](FEATURE-PARITY-MATRIX.md).

Approval states: *Approved — global rule* · *Approved — product decision* · *Approved — Gate B* (authorised for
milestone 3) · *Delivered M1/M2 — under independent review* · *Proposed — not authorised* (later milestones; nothing
here authorises work beyond milestone 3).


## Global rules (8)

| ID | Requirement | Source | Ev | Status | Pri | MS | Acceptance | Verification | Approval | FP |
|---|---|---|---|---|---|---|---|---|---|---|
| G-01 | Preserve all existing functionality and user data; no destructive migration | OWNER | OWNER | In force | P1 | all | All baseline tests pass; existing libraries/settings open unchanged; settings changes additive with upgrade test | CI test suite each commit | Approved — global rule (owner) | — |
| G-02 | No fake controls: unavailable features absent or disabled and labelled "Not available yet" | OWNER | OWNER | In force | P1 | all | Every visible command is functional (has a test) or disabled with that label | UI tests per surface | Approved — global rule (owner) | — |
| G-03 | Offline and local; no accounts, subscription, ads, automatic upload, sync or telemetry | OWNER | OWNER | In force | P1 | all | No network API in src/ (grep gate); manual network check | OBS grep; CI grep step (M3) | Approved — global rule (owner) | — |
| G-04 | Original SnapForge branding; Snagit used as functional reference only | OWNER | OWNER | In force | P1 | all | No TechSmith names/icons/art/text in UI or assets | Review | Approved — global rule (owner) | — |
| G-05 | No paid dependencies; MIT-compatible licences only | OWNER | OWNER | In force | P1 | all | THIRD-PARTY-NOTICES lists all dependencies and licences | Review of lock files | Approved — global rule (owner) | — |
| G-06 | No merge to main, no production release without explicit approval; no scope beyond the approved milestone | OWNER | OWNER | In force | P1 | all | — | — | Approved — global rule (owner) | — |
| G-07 | Evidence honesty: CI labelled CI; manual results only from a real Windows PC; search extracts never described as direct source inspection | OWNER | OWNER | In force | P1 | all | Handoffs separate CI, manual and documentary evidence | Review | Approved — global rule (owner) | — |
| G-08 | Do not weaken crash protection (production disk flushing) to improve benchmark or test results | OWNER | OWNER | In force | P1 | all | Benchmarks run with production durability; any test bypass is confined to setup and disclosed | Code review; PERF report | Approved — global rule (owner) | — |

## Product decisions (5)

| ID | Requirement | Source | Ev | Status | Pri | MS | Acceptance | Verification | Approval | FP |
|---|---|---|---|---|---|---|---|---|---|---|
| PD-01 | Keep "SnapForge" as working name pending naming clearance | OWNER | OWNER | Decided | P1 | — | No rename without approval | — | Approved — product decision (owner, 2026-11-25) | — |
| PD-02 | Keep existing copyright line provisionally | OWNER | OWNER | Decided | P1 | — | LICENSE unchanged | — | Approved — product decision (owner, 2026-11-25) | — |
| PD-03 | Prioritise Windows 11; evaluate Windows 10 compatibility separately | OWNER | OWNER | Decided | P1 | M3+ | Acceptance runs on Windows 11 first; Windows 10 evaluated in its own report | docs/COMPATIBILITY.md | Approved — product decision (owner, 2026-11-25) | — |
| PD-04 | Trusted code signing required before any public release | OWNER | OWNER | Decided | P1 | M9 | Release checklist blocks unsigned builds | — | Approved — product decision (owner, 2026-11-25) | — |
| PD-05 | Free, offline core functionality without paid dependencies | OWNER | OWNER | Decided | P1 | all | = G-03, G-05 | — | Approved — product decision (owner, 2026-11-25) | — |

## Research (2)

| ID | Requirement | Source | Ev | Status | Pri | MS | Acceptance | Verification | Approval | FP |
|---|---|---|---|---|---|---|---|---|---|---|
| RES-01 | Revalidate the source registry against original TechSmith/Microsoft pages when access is granted | OWNER | OWNER | Blocked (hosts still denied 2026-11-25) | P1 | M3 (when access) | Each DOC row re-checked and marked DOC-direct or corrected | SOURCE-REGISTRY revalidation log | Approved — Gate B (owner, 2026-11-25) | — |
| RES-02 | Label search-engine extracts as such; never as direct inspection | OWNER | OWNER | In force | P1 | all | Evidence class column on every claim | Review | Approved — global rule (owner) | — |

## Baseline (M1/M2) (37)

| ID | Requirement | Source | Ev | Status | Pri | MS | Acceptance | Verification | Approval | FP |
|---|---|---|---|---|---|---|---|---|---|---|
| BL-CAP-01 | Region capture by drag, freeze-frame, multi-monitor | REQUIREMENTS.md C1 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Region_drag_completes_with_exact_rectangle; Drag_across_monitors_with_negative_coordinates_is_exact | Delivered M1/M2 — under independent review | FP-CAP-02 |
| BL-CAP-02 | Window capture by hover + click | REQUIREMENTS.md C2 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Window_mode_click_returns_the_window_under_the_cursor; Window_picker_prefers_frontmost_window | Delivered M1/M2 — under independent review | FP-CAP-03 |
| BL-CAP-03 | Display (one monitor) and Full screen (all monitors) | REQUIREMENTS.md C3 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Display_mode_click_returns_the_monitor_bounds; Snapshot_image_matches_virtual_desktop_and_is_opaque | Delivered M1/M2 — under independent review | FP-CAP-04 |
| BL-CAP-04 | Preview in Editor option | REQUIREMENTS.md C4 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Main_window_loads_edits_and_exports | Delivered M1/M2 — under independent review | FP-CAP-20 |
| BL-CAP-05 | Copy to clipboard option | REQUIREMENTS.md C5 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Clipboard_receives_bitmap_and_png | Delivered M1/M2 — under independent review | FP-CAP-21 |
| BL-CAP-06 | Capture presets with per-preset hotkeys | REQUIREMENTS.md C6 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Settings_round_trip_and_defaults; Hotkey_conflicts_are_detected_within_snapforge | Delivered M1/M2 — under independent review | FP-CAP-25 |
| BL-CAP-07 | SnapForge windows hidden during capture | REQUIREMENTS.md C7 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | (no automated pixel test; CaptureController.cs) — gap | Delivered M1/M2 — under independent review | FP-CAP-28 |
| BL-ED-01 | Arrow, line, rectangle, ellipse, text, step tools | REQUIREMENTS.md E1 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Every_annotation_type_renders; Annotations_can_be_drawn_selected_moved_undone_and_deleted_with_the_mouse | Delivered M1/M2 — under independent review | FP-ED-03 |
| BL-ED-02 | Highlight tool | REQUIREMENTS.md E2 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Highlight_annotation_hit_testing_and_degenerate | Delivered M1/M2 — under independent review | FP-ED-08 |
| BL-ED-03 | Crop (reversible until sanitize) | REQUIREMENTS.md E3 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Crop_cuts_images_shifts_annotations_clips_masks_and_undoes | Delivered M1/M2 — under independent review | FP-ED-09 |
| BL-ED-04 | Zoom in/out/fit/actual | REQUIREMENTS.md E4 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | (manual; UI menu) — gap | Delivered M1/M2 — under independent review | FP-ED-31 |
| BL-ED-05 | Stroke width and colour | REQUIREMENTS.md E5 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Annotations_round_trip_with_ids_and_styles | Delivered M1/M2 — under independent review | FP-OBJ-03 |
| BL-ED-06 | Undo/redo | REQUIREMENTS.md E6 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Add_undo_redo_round_trip; History_capacity_drops_oldest_steps | Delivered M1/M2 — under independent review | FP-OBJ-19 |
| BL-ED-07 | Restore last tool/colour/width | REQUIREMENTS.md E7 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Settings_round_trip_and_defaults | Delivered M1/M2 — under independent review | FP-OBJ-24 |
| BL-PRIV-01 | Opaque redaction, flattened exports, Sanitize, warnings | REQUIREMENTS.md AC9–AC12 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Redacted_pixels_are_exactly_black_in_the_export; Sanitize_removes_original_pixels_from_the_library_copy | Delivered M1/M2 — under independent review | FP-SEC-03 |
| BL-PRIV-02 | No metadata in PNG/JPEG exports | REQUIREMENTS.md AC12 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Png_contains_no_text_or_exif_metadata; Jpeg_contains_no_exif_or_xmp_metadata | Delivered M1/M2 — under independent review | FP-SEC-05 |
| BL-LIB-01 | Automatic library (opt-out per capture), versioned documented format | REQUIREMENTS.md L1 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | New_capture_is_saved_automatically_and_survives_restart_with_editable_annotations; Capture_without_history_is_not_written_to_the_library | Delivered M1/M2 — under independent review | FP-CAP-22, FP-LIB-01 |
| BL-LIB-02 | Tags | REQUIREMENTS.md L5 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Views_search_and_sort | Delivered M1/M2 — under independent review | FP-LIB-03 |
| BL-LIB-03 | Favorites | REQUIREMENTS.md L6 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Views_search_and_sort | Delivered M1/M2 — under independent review | FP-LIB-04 |
| BL-LIB-04 | Virtual folders | REQUIREMENTS.md L7 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Views_search_and_sort | Delivered M1/M2 — under independent review | FP-LIB-05 |
| BL-LIB-05 | Date grouping | REQUIREMENTS.md L8 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Date_grouping_labels | Delivered M1/M2 — under independent review | FP-LIB-06 |
| BL-LIB-06 | Trash, restore, permanent delete (best-effort overwrite) | REQUIREMENTS.md L9 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Trash_restore_and_permanent_delete | Delivered M1/M2 — under independent review | FP-LIB-07 |
| BL-LIB-07 | Retention policy and usage report | REQUIREMENTS.md L10 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Usage_and_retention | Delivered M1/M2 — under independent review | FP-LIB-08 |
| BL-LIB-08 | Backup/restore with entry validation | REQUIREMENTS.md L11 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Backup_and_restore_round_trip_without_overwriting; Restore_rejects_path_traversal_and_foreign_zips | Delivered M1/M2 — under independent review | FP-LIB-10 |
| BL-LIB-09 | Adjustable thumbnails, lazy loading | REQUIREMENTS.md L12 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Large_library_view_loads_lazily | Delivered M1/M2 — under independent review | FP-LIB-16 |
| BL-LIB-10 | Move library location | REQUIREMENTS.md L13 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Move_library_to_new_location_and_reject_non_empty_target; Storage_location_change_moves_all_captures | Delivered M1/M2 — under independent review | — |
| BL-TR-01 | Persistent Recent Captures tray | REQUIREMENTS.md AC27 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Tray_orders_pinned_first_then_most_recent_and_persists | Delivered M1/M2 — under independent review | FP-TR-01 |
| BL-TR-02 | Pin to tray | REQUIREMENTS.md AC27 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Tray_close_pin_and_delete_behave_as_specified | Delivered M1/M2 — under independent review | FP-TR-02 |
| BL-TR-03 | Close from tray does not delete | REQUIREMENTS.md AC27 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Tray_close_does_not_delete_and_prune_drops_missing | Delivered M1/M2 — under independent review | FP-TR-03 |
| BL-TR-04 | Show/hide tray | REQUIREMENTS.md AC27 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Switching_between_editor_and_library_and_search | Delivered M1/M2 — under independent review | FP-TR-06 |
| BL-EXP-01 | PNG/JPEG export | REQUIREMENTS.md AC7 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Png_round_trip_is_lossless; Jpeg_encodes_with_correct_size_and_close_colors | Delivered M1/M2 — under independent review | FP-EXP-01 |
| BL-EXP-02 | Editable package .sfcap export/import | REQUIREMENTS.md AC8 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Package_export_import_gets_new_id_and_rejects_unknown_entries | Delivered M1/M2 — under independent review | FP-EXP-06 |
| BL-EXP-03 | Copy flattened image to clipboard | REQUIREMENTS.md AC7 | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | Clipboard_and_file_exports_share_the_redacted_flattened_image | Delivered M1/M2 — under independent review | FP-EXP-07 |
| BL-SET-01 | Light/Dark/System theme | REQUIREMENTS.md — | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | (manual) — gap | Delivered M1/M2 — under independent review | FP-SET-02 |
| BL-SET-02 | Close to notification area; single instance | REQUIREMENTS.md — | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | (manual) — gap | Delivered M1/M2 — under independent review | FP-SET-10 |
| BL-SEC-01 | No network code, no telemetry | REQUIREMENTS.md docs/SECURITY.md | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | OBS grep 2026-11-25 | Delivered M1/M2 — under independent review | FP-SET-06, FP-SEC-01, FP-SEC-02 |
| BL-SEC-02 | Supply chain: pinned actions, lock files, signed packages, checksums | REQUIREMENTS.md docs/SECURITY.md | OBS | Implemented | P1 | M1/M2 | Preserved; covered by named tests | CI workflow | Delivered M1/M2 — under independent review | FP-SEC-06 |

## M3 Capture panel (15)

| ID | Requirement | Source | Ev | Status | Pri | MS | Acceptance | Verification | Approval | FP |
|---|---|---|---|---|---|---|---|---|---|---|
| CAP-UI-01 | Compact horizontal panel (≤ 760×260 logical px at 100 %) with File/Edit/View/Help menus | OWNER; I-05 | OWNER+OBS | Implemented | P1 | M3 | Fits bounds at 100/150/200 %; menus via Alt | Capture_panel_is_compact_horizontal_and_marks_unavailable_modes; real screenshot | Approved — Gate B (owner, 2026-11-25) | FP-CAP-29, FP-CAP-30 |
| CAP-UI-02 | Mode rail All-in-One / Image / Video; only Image enabled; others disabled "Not available yet" | OWNER; S-CAP-01 | OWNER+DOC | Implemented | P1 | M3 | Image selected; other two disabled with label in tooltip + automation name | Capture_panel_is_compact_horizontal_and_marks_unavailable_modes | Approved — Gate B (owner, 2026-11-25) | FP-CAP-01 |
| CAP-UI-03 | Selection dropdown: Region, Window, Display, Full screen | OWNER; FP-CAP-23 | OWNER | Implemented | P1 | M3 | Each item runs the existing capture mode | Capture_panel_selection_delay_reset_and_presets_work | Approved — Gate B (owner, 2026-11-25) | FP-CAP-23 |
| CAP-UI-04 | Effects dropdown disabled ("Not available yet") until effects exist | OWNER; S-CAP-28 | OWNER+DOC | Implemented | P1 | M3 | Disabled + labelled | Capture_panel_is_compact_horizontal_and_marks_unavailable_modes | Approved — Gate B (owner, 2026-11-25) | FP-CAP-23 |
| CAP-UI-05 | Share dropdown shown; disabled "Not available yet" until destinations exist (Library/clipboard remain switches — see UI spec as-built note) | OWNER; S-CAP-04 | OWNER+DOC | Implemented (deviation documented) | P1 | M3 | Disabled + labelled; switches unchanged | Capture_panel_is_compact_horizontal_and_marks_unavailable_modes | Approved — Gate B (owner, 2026-11-25) | FP-CAP-23 |
| CAP-UI-06 | Large circular Capture button; assigned hotkey text beneath; warning when hotkey not registered | OWNER; I-05 | OWNER+OBS | Implemented | P1 | M3 | Enter triggers; text equals gesture or "No hotkey"; conflict state shown | Capture_panel_is_compact_horizontal_and_marks_unavailable_modes | Approved — Gate B (owner, 2026-11-25) | FP-CAP-31 |
| CAP-UI-07 | Switches Preview in Editor, Copy to Clipboard, Cursor, Time Delay | OWNER; S-CAP-04 | OWNER+DOC | Implemented | P1 | M3 | Persist and honoured | Capture_panel_selection_delay_reset_and_presets_work | Approved — Gate B (owner, 2026-11-25) | — |
| CAP-UI-08 | Save to Library switch preserved | OWNER | OWNER | Implemented | P1 | M3 | Off → no library files | Capture_panel_is_compact_horizontal_and_marks_unavailable_modes; Capture_without_history_is_not_written_to_the_library | Approved — Gate B (owner, 2026-11-25) | — |
| CAP-UI-09 | Time-delay duration picker (1–60 s) | OWNER; S-CAP-03 | OWNER+DOC | Implemented | P1 | M3 | Values persist; countdown shown; Esc cancels | Capture_panel_selection_delay_reset_and_presets_work; Old_settings_files_get_new_defaults… | Approved — Gate B (owner, 2026-11-25) | FP-CAP-19 |
| CAP-UI-10 | Reset button restores defaults with Undo | OWNER; I-05 | OWNER+OBS | Implemented | P2 | M3 | Reset then Undo restores previous options | Capture_panel_selection_delay_reset_and_presets_work | Approved — Gate B (owner, 2026-11-25) | FP-CAP-29 |
| CAP-UI-11 | Remembers position per monitor; clamps on-screen | INFER | INFER | Implemented | P1 | M3 | Clamp logic unit-tested | Window_position_is_kept_when_visible_and_clamped_when_not; Capture_panel_remembers_its_position… | Approved — Gate B (owner, 2026-11-25) | FP-SET-12 |
| CAP-UI-12 | Footer: Presets flyout, + save preset, Open SnapForge Editor, Open Library | OWNER; S-CAP-09 | OWNER+DOC | Implemented | P1 | M3 | Commands work by keyboard and mouse | Capture_panel_selection_delay_reset_and_presets_work | Approved — Gate B (owner, 2026-11-25) | FP-CAP-29 |
| CAP-UI-13 | Keyboard: Enter captures, Esc closes, access keys, Space toggles | NFR-A11Y-01 | OWNER | Implemented — manual check pending | P1 | M3 | Keyboard-only test | IsDefault/automation names tested; keyboard walk-through is manual M3-P6 (CI cannot own the foreground) | Approved — Gate B (owner, 2026-11-25) | — |
| CAP-UI-14 | Rename misleading preset "Menu capture (3 s delay, cursor)" (unchanged defaults migrated) | Audit DEF-03 | OBS | Implemented | P1 | M3 | New name; migration test | Old_settings_files_get_new_defaults_and_the_misleading_preset_is_renamed_only_if_unchanged | Approved — Gate B (owner, 2026-11-25) | — |
| CAP-HK-01 | Print Screen conflict (Windows 11 Snipping Tool) detected and explained | S-CAP-07/19 | DOC | Implemented — manual check pending | P1 | M3 | Message names the Windows setting; manual both ways | Capture_panel_remembers_its_position_and_explains_print_screen_conflicts; real Windows 11 behaviour = manual M3-P8 | Approved — Gate B (owner, 2026-11-25) | FP-CAP-27 |

## M3 Desktop widget (13)

| ID | Requirement | Source | Ev | Status | Pri | MS | Acceptance | Verification | Approval | FP |
|---|---|---|---|---|---|---|---|---|---|---|
| WID-01 | Widget enabled by default; can be disabled in Settings | OWNER (decision 2026-11-25) | OWNER | Implemented | P1 | M3 | Visible on first start; toggle persists | Widget_is_enabled_topmost_non_activating_and_docked_top_centre_by_default | Approved — Gate B (owner, 2026-11-25) | FP-WID-01 |
| WID-02 | Collapsed tab docked top-centre of primary monitor by default | OWNER | OWNER | Implemented | P1 | M3 | Default placement unit-tested | Widget_defaults_to_top_centre_of_primary_and_is_enabled_and_topmost; Widget_is_enabled_topmost… | Approved — Gate B (owner, 2026-11-25) | FP-WID-02 |
| WID-03 | Expands on hover or click (configurable); collapses when not in use | OWNER; S-CAP-06 | OWNER+DOC | Implemented | P1 | M3 | Hover/leave and click behaviour | Widget_expands_on_hover_without_taking_focus_and_collapses_when_left (real input) | Approved — Gate B (owner, 2026-11-25) | FP-WID-02 |
| WID-04 | Dock to top, left or right edge of any monitor by dragging | OWNER; S-CAP-06 | OWNER+DOC | Implemented | P1 | M3 | Dock model tests; drag test | Dropping_the_widget_picks_monitor_nearest_edge_and_offset; Widget_docks_to_left_and_right_edges_and_remembers_the_place | Approved — Gate B (owner, 2026-11-25) | FP-WID-03 |
| WID-05 | Buttons Capture, Editor, Library, Presets | OWNER | OWNER | Implemented | P1 | M3 | Each works | Screenshots widget-expanded-*; buttons call existing commands | Approved — Gate B (owner, 2026-11-25) | — |
| WID-06 | Auto-hide option (off by default) | OWNER | OWNER | Implemented — manual check pending | P2 | M3 | Hidden until edge hover | Setting persisted (Old_settings_files…); visual behaviour manual M3-W5 | Approved — Gate B (owner, 2026-11-25) | FP-WID-04 |
| WID-07 | Always on top by default; configurable | OWNER (decision 2026-11-25) | OWNER | Implemented | P1 | M3 | Topmost flag on by default; setting toggles | Widget_is_enabled_topmost_non_activating_and_docked_top_centre_by_default | Approved — Gate B (owner, 2026-11-25) | FP-WID-05 |
| WID-08 | Remembers monitor + edge + offset; survives monitor removal and mixed DPI | OWNER | OWNER | Implemented — mixed-DPI manual check pending | P1 | M3 | Placement tests incl. removed monitor | Widget_stays_on_its_monitor…; Widget_on_a_removed_monitor_falls_back…; Widget_docks…remembers_the_place; real mixed DPI = manual M3-W4 | Approved — Gate B (owner, 2026-11-25) | FP-WID-06 |
| WID-09 | Never steals focus (non-activating, no taskbar button) | OWNER | OWNER | Implemented | P1 | M3 | Foreground unchanged after hover/expand | Widget_is_enabled_topmost_non_activating… (WS_EX_NOACTIVATE); Widget_expands_on_hover_without_taking_focus… (foreground unchanged) | Approved — Gate B (owner, 2026-11-25) | — |
| WID-10 | Hidden automatically during capture; never in captured pixels | OWNER | OWNER | Implemented | P1 | M3 | Pixel test | Widget_is_hidden_during_capture_and_never_in_the_captured_pixels (pixel control + test) | Approved — Gate B (owner, 2026-11-25) | — |
| WID-11 | Keyboard/assistive access (notification menu entry, automation names) | OWNER | OWNER | Implemented — screen-reader check pending | P1 | M3 | Automation tree test | Automation names in XAML; notification-menu toggle; Narrator = manual M3-W6 | Approved — Gate B (owner, 2026-11-25) | — |
| WID-12 | Configurable through Settings (enable, topmost, hover/click, auto-hide, reset position) | OWNER | OWNER | Implemented | P1 | M3 | Each setting persists and applies live | Settings UI; Old_settings_files… round trip | Approved — Gate B (owner, 2026-11-25) | — |
| SET-01 | Optional launch with Windows (per-user Run key), off by default | OWNER; S-CAP-06 | OWNER+DOC | Implemented | P1 | M3 | Toggle writes/removes HKCU Run value; starts in background | Launch_with_windows_writes_and_removes_only_the_per_user_run_value; sign-in = manual M3-S1 | Approved — Gate B (owner, 2026-11-25) | FP-WID-07, FP-SET-01 |

## M3 Drag and drop + import (18)

| ID | Requirement | Source | Ev | Status | Pri | MS | Acceptance | Verification | Approval | FP |
|---|---|---|---|---|---|---|---|---|---|---|
| DND-01 | External drag from Recent Captures tray offers CF_HDROP files | OWNER; S-LIB-03; S-LIB-55 | OWNER+DOC | Implemented | P1 | M3 | Drop target receives file list | Real_mouse_drag_from_tray_delivers_png_files_through_OLE (real CF_HDROP via OLE, in-process target) | Approved — Gate B (owner, 2026-11-25) | FP-TR-09 |
| DND-02 | External drag from Library (single and multiple selection) | OWNER | OWNER | Implemented | P1 | M3 | N items → N files | Real_mouse_drag_of_a_library_multi_selection_delivers_one_file_per_capture | Approved — Gate B (owner, 2026-11-25) | FP-TR-09, FP-LIB-12 |
| DND-03 | Multi-selection in the tray (Ctrl/Shift-click) for drag | OWNER; S-LIB-16 | OWNER+DOC | Implemented | P1 | M3 | Selection drag yields all selected | Tray_ctrl_click_selects_several_without_opening | Approved — Gate B (owner, 2026-11-25) | FP-TR-08, FP-TR-09 |
| DND-04 | PNG by default; configurable JPEG (quality) in Settings | OWNER | OWNER | Implemented | P1 | M3 | Extension and encoder follow setting | Drag_data_offers_only_flattened_redacted_images_named_by_title_and_time (JPEG part) | Approved — Gate B (owner, 2026-11-25) | FP-TR-09, FP-SET-09 |
| DND-05 | Meaningful, unique, timestamped, filesystem-safe filenames | OWNER | OWNER | Implemented | P1 | M3 | Reserved names, invalid chars, long titles, duplicates | Export_file_names_are_meaningful_timestamped_safe_and_unique; Drag_data_offers… | Approved — Gate B (owner, 2026-11-25) | FP-TR-09, FP-EXP-09 |
| DND-06 | Copy semantics only; library never modified by a drop | OWNER | OWNER | Implemented | P1 | M3 | Library unchanged after drop | Copy-only effect; Drag_data_offers… (library unchanged); multi-selection test | Approved — Gate B (owner, 2026-11-25) | FP-TR-09 |
| DND-07 | Never offers editable packages, base images or unredacted data | OWNER | OWNER | Implemented | P1 | M3 | Data object formats inspected | Drag_data_offers… (formats, single file, no .sfcap) | Approved — Gate B (owner, 2026-11-25) | FP-TR-09 |
| DND-08 | Cancellation safe; temp files cleaned (delay, exit, start-up sweep) | OWNER | OWNER | Implemented | P1 | M3 | Fake-clock cleanup tests | Temp_export_sessions_are_private_folders_and_swept_after_the_grace_period; Reordering_inside_the_tray_creates_no_files_and_cancelled_drags_leave_nothing | Approved — Gate B (owner, 2026-11-25) | FP-TR-09 |
| DND-09 | Reordering inside the tray distinct from export | OWNER; S-LIB-01 | OWNER+DOC | Implemented | P1 | M3 | Reorder persists; no export files | Tray_reorder_keeps_sections_and_persists; Reordering_inside_the_tray_creates_no_files… | Approved — Gate B (owner, 2026-11-25) | FP-TR-04, FP-TR-09 |
| DND-10 | PNG and JPEG import (button and drop) into the Library | OWNER | OWNER | Implemented | P1 | M3 | Imported capture appears; invalid rejected | Png_and_jpeg_import_keeps_pixels_only_and_rejects_bad_files; drop-in = manual M3-D7 | Approved — Gate B (owner, 2026-11-25) | FP-TR-09, FP-LIB-11 |
| DND-11 | Compatibility table: Explorer, desktop, Outlook, Teams, Word, PowerPoint, OneNote, browser — Tested (manual) vs Expected | OWNER | OWNER | Manual testing pending | P1 | M3 | No "Tested" without recorded manual result | No real-app target tested yet: all of Explorer/desktop/Outlook/Teams/Word/PowerPoint/OneNote/browser are EXPECTED until the owner records M3-D1…D3 | Approved — Gate B (owner, 2026-11-25) | FP-TR-09, FP-EXP-10 |
| DND-12 | Drag shows a drag image | UI spec | INFER | Not done (P3) | P3 | M3 | Visual | Windows' default drag cursor only | Approved — Gate B (owner, 2026-11-25) | FP-TR-09 |
| PRIV-DND-01 | Drag is an export: same flattening/redaction path as File → Export | OWNER | OWNER | Implemented | P1 | M3 | Shared code path | Drag_data_offers… compares with ImageExporter.Flatten | Approved — Gate B (owner, 2026-11-25) | FP-TR-09 |
| PRIV-DND-02 | Pixel-level regression: redacted area equals mask colour in dragged file | OWNER | OWNER | Implemented | P1 | M3 | Pixel test | Drag_data_offers… (redacted area exactly black); Real_mouse_drag_from_tray… (black at 5,5) | Approved — Gate B (owner, 2026-11-25) | FP-TR-09 |
| PRIV-DND-03 | No metadata in dragged files | OWNER | OWNER | Implemented | P1 | M3 | Chunk/segment inspection | Drag_data_offers… (no tEXt/eXIf) | Approved — Gate B (owner, 2026-11-25) | FP-TR-09 |
| PRIV-DND-04 | Temp files in per-user %LOCALAPPDATA%\SnapForge\Temp\Drag, random subfolder per drag | OWNER | OWNER | Implemented | P1 | M3 | Location asserted | Drag_data_offers… (file under the store root); TempExportStore random session names | Approved — Gate B (owner, 2026-11-25) | FP-TR-09 |
| PRIV-DND-05 | Blur/pixelate never presented as irreversible redaction | OWNER; S-ED-08 | OWNER+DOC | In force (no blur tool yet) | P1 | M3+ | Label review when blur added | Review | Approved — Gate B (owner, 2026-11-25) | FP-ED-11 |
| PRIV-DND-06 | Imported images: only pixels kept (metadata dropped); size limits | OWNER | OWNER | Implemented | P1 | M3 | Import of image with metadata stores none; oversize rejected | Png_and_jpeg_import_keeps_pixels_only_and_rejects_bad_files; Import_rejects_bad_sizes_and_types | Approved — Gate B (owner, 2026-11-25) | — |

## M3 Performance (7)

| ID | Requirement | Source | Ev | Status | Pri | MS | Acceptance | Verification | Approval | FP |
|---|---|---|---|---|---|---|---|---|---|---|
| PERF-01 | Benchmarks at 100, 1 000, 5 000, 10 000, 25 000 captures with production flushing | OWNER | OWNER | Implemented — see benchmark report | P1 | M3 | Report per size: startup, memory, search, thumbnails, autosave, responsiveness | CI job 'Library scale benchmarks'; docs/BENCHMARKS.md | Approved — Gate B (owner, 2026-11-25) | FP-LIB-18 |
| PERF-02 | Seeding uses production durability too (no flush bypass anywhere in benchmarks) | OWNER; DEF-01 | OWNER+OBS | Implemented | P1 | M3 | Code review | tools/SnapForge.Benchmarks uses only Create/SaveContent (no SkipDiskFlush) | Approved — Gate B (owner, 2026-11-25) | FP-LIB-18 |
| PERF-03 | Library initialisation must not block the UI thread | OWNER; DEF-02 | OWNER+OBS | Implemented | P1 | M3 | Window responsive while 25 000-capture library indexes | Deferred_open_indexes_in_background_and_keeps_captures_written_meanwhile; Library_opens_instantly_and_indexes_in_the_background_without_freezing_the_ui; benchmark LongestUiFreezeMs | Approved — Gate B (owner, 2026-11-25) | FP-LIB-18 |
| PERF-04 | Search latency reported; target < 100 ms at 10 000 | INFER | INFER | Measured | P2 | M3 | Measured | docs/BENCHMARKS.md | Approved — Gate B (owner, 2026-11-25) | FP-LIB-18 |
| PERF-05 | Memory reported; target < 400 MB at 10 000 | INFER | INFER | Measured | P2 | M3 | Measured | docs/BENCHMARKS.md | Approved — Gate B (owner, 2026-11-25) | FP-LIB-18 |
| PERF-06 | Autosave latency reported per size | OWNER | OWNER | Measured | P1 | M3 | Measured | docs/BENCHMARKS.md | Approved — Gate B (owner, 2026-11-25) | FP-LIB-18 |
| PERF-07 | Thumbnail loading time reported | OWNER | OWNER | Measured | P1 | M3 | Measured | docs/BENCHMARKS.md | Approved — Gate B (owner, 2026-11-25) | FP-LIB-18 |

## M3 Quality / NFR (8)

| ID | Requirement | Source | Ev | Status | Pri | MS | Acceptance | Verification | Approval | FP |
|---|---|---|---|---|---|---|---|---|---|---|
| NFR-DPI-01 | Multi-monitor operation (capture, panel, widget) | OWNER | OWNER | Partial — manual check pending | P1 | M3 | Manual on real multi-monitor PC; unit tests for coordinate math | Core layout tests with two monitors; CI has one monitor | Approved — Gate B (owner, 2026-11-25) | FP-CAP-33 |
| NFR-DPI-02 | Mixed DPI scaling correct | OWNER | OWNER | Partial — manual check pending | P1 | M3 | Manual at 100/150/200 % mixed | Mixed-DPI placement unit tests; real mixed DPI = manual | Approved — Gate B (owner, 2026-11-25) | FP-CAP-33 |
| NFR-A11Y-01 | Keyboard access to every new command | OWNER | OWNER | Partial — manual check pending | P1 | M3 | Keyboard-only UI tests | Access keys, IsDefault, automation names; keyboard walk-through manual | Approved — Gate B (owner, 2026-11-25) | FP-SEC-11 |
| NFR-A11Y-02 | Screen reader names/roles/states for new controls | OWNER; S-CAP-24 | OWNER+DOC | Partial — manual check pending | P1 | M3 | Automation-tree test + manual Narrator | Automation names asserted in tests; Narrator pass manual | Approved — Gate B (owner, 2026-11-25) | FP-SEC-11 |
| NFR-A11Y-03 | Windows high-contrast themes respected | OWNER; S-CAP-24 | OWNER+DOC | Implemented — manual check pending | P1 | M3 | HC palette from system colours; manual | High_contrast_palette_covers_every_theme_colour_from_system_colours; live switch manual M3-S3 | Approved — Gate B (owner, 2026-11-25) | FP-SET-03, FP-SEC-11 |
| NFR-SEC-04 | Runs as a standard (non-admin) user | OWNER | OWNER | Unverified — manual check pending | P1 | M3 | CI test as standard user where feasible + manual | CI runs as administrator; no admin APIs used (HKCU only) | Approved — Gate B (owner, 2026-11-25) | FP-SEC-10 |
| LIB-UI-01 | Library storage path never clips (ellipsis + tooltip) | Audit DEF-05 | OBS | Implemented | P2 | M3 | Long path renders trimmed with tooltip | Library_storage_path_is_trimmed_with_full_path_in_tooltip | Approved — Gate B (owner, 2026-11-25) | — |
| TR-01 | Tray tooltips show title, date and size | S-LIB-01 (UNVERIFIED in Snagit) | INFER | Implemented | P2 | M3 | Tooltip text test | Tray_ctrl_click_selects_several_without_opening (tooltip text) | Approved — Gate B (owner, 2026-11-25) | FP-TR-11 |

## M4 Editor (39)

| ID | Requirement | Source | Ev | Status | Pri | MS | Acceptance | Verification | Approval | FP |
|---|---|---|---|---|---|---|---|---|---|---|
| ED-OBJ-01 | Resize handles for all objects | S-ED-31 | DOC | Partial (move only) | P1 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-01, FP-OBJ-10 |
| ED-OBJ-02 | Fill vs outline | S-ED-28/29 | DOC | Not started | P1 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-04, FP-OBJ-02 |
| ED-OBJ-03 | Colour picker HEX/RGB + eyedropper | S-ED-38 | DOC | Not started | P1 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-OBJ-01 |
| ED-OBJ-04 | Object rotation | S-ED-29 | DOC | Not started | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-04, FP-OBJ-09 |
| ED-OBJ-05 | Opacity per object | S-ED-25 | DOC | Not started | P1 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-OBJ-04 |
| ED-OBJ-06 | Arrowheads / end styles | S-ED-25 | DOC | Not started | P1 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-02, FP-OBJ-06 |
| ED-OBJ-07 | Shadow | S-ED-29 | DOC | Not started | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-04, FP-OBJ-08 |
| ED-OBJ-08 | Font family/style/alignment | S-ED-28 | DOC | Not started | P1 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-05, FP-OBJ-05 |
| ED-OBJ-09 | Dash styles | S-ED-46 (UNVERIFIED) | UNVERIFIED | Not started | P3 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-OBJ-07 |
| ED-OBJ-10 | Multi-select, marquee, Ctrl+A, select all of a kind | S-ED-22; S-ED-01 | DOC | Not started | P1 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-OBJ-11 |
| ED-OBJ-11 | Group/ungroup | S-ED-22 | DOC | Not started | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-OBJ-12 |
| ED-OBJ-12 | Layering | S-ED-22 | DOC | Not started | P1 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-OBJ-13 |
| ED-OBJ-13 | Object cut/copy/paste/duplicate | S-ED-22/24 | DOC | Not started | P1 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-OBJ-14, FP-OBJ-15 |
| ED-OBJ-14 | Paste objects between captures | INFER | INFER | Not started | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-OBJ-16 |
| ED-OBJ-15 | Per-object flatten | S-ED-22 | DOC | Not started | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-OBJ-18 |
| ED-OBJ-16 | Arrow-key nudge | INFER | INFER | Not started | P1 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-OBJ-20 |
| ED-OBJ-20 | Cursor captured as a movable object | S-CAP-03 | DOC | Partial (pixels) | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-CAP-18 |
| ED-TOOL-01 | Callout | S-ED-27 | DOC | Not started | P1 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-06 |
| ED-TOOL-02 | Step options (letters, restart, edit value, renumber) | S-ED-11 | DOC | Not started | P1 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-07, FP-OBJ-17 |
| ED-TOOL-03 | Cut Out | S-ED-34 | DOC | Not started | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-10 |
| ED-TOOL-04 | Blur / pixelate labelled "not secure" | S-ED-08 | DOC | Not started | P1 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-11 |
| ED-TOOL-05 | Magnify tool | S-ED-31 | DOC | Not started | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-15 |
| ED-TOOL-06 | Pen (vector) | S-ED-37 | DOC | Not started | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-17 |
| ED-TOOL-07 | Eraser | S-ED-39 | DOC | Not started | P3 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-18 |
| ED-TOOL-08 | Stamps (original artwork) | S-ED-36 | DOC | Not started | P3 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-19 |
| ED-TOOL-09 | Fill tool | S-ED-38 | DOC | Not started | P3 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-20 |
| ED-TOOL-10 | Pixel selection (rect/ellipse/polygon/freehand, auto-fill) | S-ED-16 | DOC | Not started | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-21 |
| ED-TOOL-11 | Magic wand / global colour select | S-ED-17 | DOC | Not started | P3 | M6 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-22 |
| ED-IMG-01 | Resize image | S-ED-35 | DOC | Not started | P1 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-24 |
| ED-IMG-02 | Resize canvas | S-ED-46 (UNVERIFIED) | UNVERIFIED | Not started | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-25 |
| ED-IMG-03 | Rotate / flip | S-ED-35 | DOC | Not started | P1 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-26 |
| ED-IMG-04 | Auto trim | S-ED-01 | DOC | Not started | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-27 |
| ED-IMG-05 | Rounded image corners | S-ED-29 | DOC | Not started | P3 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-28 |
| ED-UI-01 | Toolbar customisation | S-ED-24 | DOC | Not started | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-29 |
| ED-UI-02 | Ctrl+1–9 by toolbar position (aliases) | S-ED-22 | DOC | Not started | P3 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-30 |
| ED-UI-03 | Canvas / object context menu | S-ED-08/11/12 | DOC | Not started | P1 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-OBJ-25 |
| ED-QS-01 | Per-tool Quick Styles | S-ED-05/06 | DOC | Partial (shared list) | P1 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-OBJ-21 |
| ED-QS-02 | Favorites across tools | S-ED-06 | DOC | Partial | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-OBJ-22 |
| ED-QS-03 | Themes / custom palettes | S-ED-07 | DOC | Not started | P3 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-OBJ-23 |

## M4 Effects (10)

| ID | Requirement | Source | Ev | Status | Pri | MS | Acceptance | Verification | Approval | FP |
|---|---|---|---|---|---|---|---|---|---|---|
| ED-FX-01 | Border | S-ED-21 | DOC | Not started | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-FX-01 |
| ED-FX-02 | Edges | S-ED-21 | DOC | Not started | P3 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-FX-02 |
| ED-FX-03 | Shadow effect | S-ED-21 | DOC | Not started | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-FX-03 |
| ED-FX-04 | Perspective / page curl | S-ED-21 | DOC | Not started | P3 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-FX-04 |
| ED-FX-05 | Spotlight and magnify effect | S-ED-21/32 | DOC | Not started | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-16 |
| ED-FX-06 | Watermark | S-ED-21 | DOC | Not started | P3 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-FX-05 |
| ED-FX-07 | Filters | S-ED-21 | DOC | Not started | P3 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-FX-06 |
| ED-FX-08 | Colour adjustment | S-ED-21 | DOC | Not started | P3 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-FX-07 |
| ED-FX-09 | Capture info | S-ED-21 | DOC | Not started | P3 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-FX-08 |
| ED-FX-10 | Effects in capture presets | S-ED-44 | DOC | Not started | P3 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-FX-09 |

## M5 Advanced capture (12)

| ID | Requirement | Source | Ev | Status | Pri | MS | Acceptance | Verification | Approval | FP |
|---|---|---|---|---|---|---|---|---|---|---|
| CAP-ADV-01 | Magnifier during selection | S-CAP-06/07 | DOC | Not started | P2 | M5 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-CAP-05 |
| CAP-ADV-02 | Exact width/height entry | S-CAP-02 | DOC | Not started | P2 | M5 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-CAP-06 |
| CAP-ADV-03 | Fixed region (+ set position) | S-CAP-03/29 | DOC | Not started | P2 | M5 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-CAP-07 |
| CAP-ADV-04 | Crosshair keys (arrows, Enter, Shift+Enter) | S-CAP-07 | DOC | Not started | P2 | M5 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-CAP-08 |
| CAP-ADV-05 | Repeat last capture | S-CAP-07 | DOC | Not started | P2 | M5 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-CAP-09 |
| CAP-ADV-06 | Menu capture | S-CAP-03/20 | DOC | Not started | P2 | M5 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-CAP-10 |
| CAP-ADV-07 | Multiple area | S-CAP-03 | DOC | Not started | P3 | M5 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-CAP-11 |
| CAP-ADV-08 | Scrolling capture — automatic | S-CAP-05/13 | DOC | Not started | P2 | M5 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-CAP-12 |
| CAP-ADV-09 | Scrolling capture — manual/panoramic | S-CAP-05 | DOC | Not started | P2 | M5 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-CAP-13 |
| CAP-ADV-10 | Interval capture | S-CAP-03/10 | DOC | Not started | P3 | M5 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-CAP-14 |
| CAP-ADV-11 | Clipboard as capture source | S-CAP-03 | DOC | Not started | P2 | M5 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-CAP-15 |
| CAP-ADV-12 | Windows Graphics Capture backend with GDI fallback | docs/WINDOWS-GRAPHICS-CAPTURE.md | OBS | Not started | P3 | M5 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-CAP-32 |

## M6 Offline intelligent tools (7)

| ID | Requirement | Source | Ev | Status | Pri | MS | Acceptance | Verification | Approval | FP |
|---|---|---|---|---|---|---|---|---|---|---|
| AI-01 | Offline OCR / grab text | S-ED-12/14 | DOC | Not started | P2 | M6 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-13 |
| AI-02 | Smart redaction (offline detection) | S-ED-08/10 | DOC | Not started | P2 | M6 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-12 |
| AI-03 | Smart move | S-ED-15 | DOC | Not started | P3 | M6 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-01 |
| AI-04 | Simplify | S-ED-20 | DOC | Not started | P3 | M6 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-14 |
| AI-05 | Background removal (offline, licence-compatible) | S-ED-18/19 | DOC | Not started | P3 | M6 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-ED-23 |
| AI-06 | OCR search in Library | S-LIB-04 (absent in Snagit) | DOC | Not started | P3 | M6 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-LIB-14 |
| AI-07 | Admin switch to disable intelligent tools | S-LIB-46 | DOC | Not started | P3 | M6 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-SEC-12 |

## M7 Documentation (2)

| ID | Requirement | Source | Ev | Status | Pri | MS | Acceptance | Verification | Approval | FP |
|---|---|---|---|---|---|---|---|---|---|---|
| DOC-01 | Combine images / templates | S-LIB-16 | DOC | Not started | P3 | M7 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-DOC-01 |
| DOC-02 | Step recording guide | S-LIB-19 | DOC | Not started | P3 | M7 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-DOC-02 |

## Export (8)

| ID | Requirement | Source | Ev | Status | Pri | MS | Acceptance | Verification | Approval | FP |
|---|---|---|---|---|---|---|---|---|---|---|
| EXP-01 | BMP, GIF, TIFF, WebP export | S-LIB-28/30 | DOC | Not started | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-EXP-02 |
| EXP-02 | HEIF investigation (Windows support unverified) | S-LIB-29 | UNVERIFIED | Not started | P3 | investigate | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-EXP-04 |
| EXP-03 | PDF export | S-LIB-28 | DOC | Not started | P2 | M7 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-DOC-04, FP-EXP-05 |
| EXP-04 | Print | S-LIB-31; S-ED-22 | DOC | Not started | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-EXP-08 |
| EXP-05 | Share → save to folder at capture time | S-LIB-33 | DOC | Not started | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-CAP-24 |
| EXP-06 | Configurable filename patterns for exports | S-LIB-02/31 | DOC | Not started | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-EXP-09 |
| EXP-07 | Niche formats ICO/PSD/TGA/WMF/CUR/EPS | S-LIB-28 | DOC | Not started | — | Backlog (not planned) | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-EXP-03 |
| EXP-08 | Optional cloud share destinations (opt-in only) | S-LIB-32/37 | DOC | Not started | — | Backlog (owner decision; conflicts with G-03 unless opt-in) | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-EXP-11 |

## Library (later) (5)

| ID | Requirement | Source | Ev | Status | Pri | MS | Acceptance | Verification | Approval | FP |
|---|---|---|---|---|---|---|---|---|---|---|
| LIB-05 | Merge libraries when moving onto an existing library | S-LIB-05 | DOC | Not started | P3 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-LIB-09 |
| LIB-06 | Details / metadata view | S-LIB-04 | DOC | Not started | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-LIB-15 |
| LIB-META-01 | Record source application / website | S-LIB-04 | DOC | Not started | P2 | M5 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-LIB-02 |
| LIB-BATCH-01 | Batch export / convert / effects | S-LIB-15 | DOC | Not started | P2 | M7 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-FX-10, FP-LIB-13 |
| MIG-01 | Investigate Snagit .snagx migration (format unpublished) | S-LIB-11/13 | UNVERIFIED | Not started | P3 | investigate | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-LIB-17 |

## Tray (later) (3)

| ID | Requirement | Source | Ev | Status | Pri | MS | Acceptance | Verification | Approval | FP |
|---|---|---|---|---|---|---|---|---|---|---|
| TR-02 | Thumbnail sizes S/M/L | S-LIB-01 | DOC | Not started | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-TR-05 |
| TR-03 | Clear tray on close preference | S-LIB-01 | DOC | Partial (command) | P3 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-TR-07 |
| TR-04 | Drag tray image onto canvas to combine | S-LIB-01 | DOC | Not started | P3 | M7 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-TR-10 |

## Settings (later) (4)

| ID | Requirement | Source | Ev | Status | Pri | MS | Acceptance | Verification | Approval | FP |
|---|---|---|---|---|---|---|---|---|---|---|
| SET-03 | Restore default hotkeys | S-CAP-07 | DOC | Not started | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-SET-04 |
| SET-04 | Reset all settings | S-CAP-06 | DOC | Not started | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-SET-07 |
| SET-05 | Settings / preset import-export | S-CAP-09/21 | DOC | Not started | P2 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-CAP-26, FP-SET-08 |
| SET-06 | Capture sounds | UNVERIFIED | UNVERIFIED | Not started | P3 | M4 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-SET-11 |

## Capture (not planned) (2)

| ID | Requirement | Source | Ev | Status | Pri | MS | Acceptance | Verification | Approval | FP |
|---|---|---|---|---|---|---|---|---|---|---|
| CAP-LEG-01 | Printer capture (virtual printer driver) | S-CAP-06 | UNVERIFIED | Not started | — | Backlog (not planned: driver/admin) | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-CAP-16 |
| CAP-LEG-02 | Object capture | S-CAP-32 | DOC (historical) | Not started | — | Backlog (not planned) | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-CAP-17 |

## M8 Video (11)

| ID | Requirement | Source | Ev | Status | Pri | MS | Acceptance | Verification | Approval | FP |
|---|---|---|---|---|---|---|---|---|---|---|
| VID-01 | Screen recording region/window/screen to MP4 | S-LIB-21 | DOC | Not started | — | M8 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-VID-01 |
| VID-02 | Microphone + system audio | S-LIB-21 | DOC | Not started | — | M8 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-VID-02 |
| VID-03 | Webcam picture-in-picture | S-LIB-21 | DOC | Not started | — | M8 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-VID-02 |
| VID-04 | Pause/resume, stop hotkeys | S-LIB-21; S-CAP-07 | DOC | Not started | — | M8 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-VID-03 |
| VID-05 | Countdown before recording | UNVERIFIED | UNVERIFIED | Not started | — | M8 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-VID-04 |
| VID-06 | Quality presets up to 4K60 | S-LIB-27 | DOC | Not started | — | M8 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-VID-05 |
| VID-07 | Cursor highlight / click animation | S-LIB-02 | DOC | Not started | — | M8 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-VID-06 |
| VID-08 | Draw on screen while recording | S-LIB-24 | DOC | Not started | — | M8 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-VID-07 |
| VID-09 | Trim, save frame, combine clips | S-LIB-22/23 | DOC | Not started | — | M8 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-VID-08 |
| VID-10 | Video from images with narration | S-LIB-20 | DOC | Not started | — | M8 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-DOC-03 |
| VID-11 | Animated GIF export | S-LIB-25 | DOC | Not started | — | M8 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-VID-09 |

## M9 Enterprise (7)

| ID | Requirement | Source | Ev | Status | Pri | MS | Acceptance | Verification | Approval | FP |
|---|---|---|---|---|---|---|---|---|---|---|
| ENT-01 | MSI/MSIX installer, silent install/uninstall | S-LIB-34 | DOC | Not started | — | M9 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-SEC-09 |
| ENT-02 | Trusted code signing (= PD-04) | OWNER | OWNER | Not started | — | M9 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-SEC-08 |
| ENT-03 | SBOM | INFER | INFER | Not started | — | M9 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-SEC-07 |
| ENT-04 | Intune/SCCM deployment guide | S-LIB-40/41 | DOC | Not started | — | M9 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-SEC-09 |
| ENT-05 | Optional encryption at rest | INFER | INFER | Not started | — | M9 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-SEC-04 |
| ENT-06 | Policy settings for administrators | S-LIB-37 | DOC | Not started | — | M9 | Defined at that milestone's Gate A | — | Proposed — not authorised | — |
| ENT-07 | Controlled updates (no automatic network by default) | S-CAP-06 | DOC | Not started | — | M9 | Defined at that milestone's Gate A | — | Proposed — not authorised | FP-SET-05 |

## M3.1 Capture-first (11)

| ID | Requirement | Source | Ev | Status | Pri | MS | Acceptance | Verification | Approval | FP |
|---|---|---|---|---|---|---|---|---|---|---|
| CF-01 | Capture-first start-up: icon, widget and (if enabled) panel only; Editor not created at start | OWNER (option A, 2026-11-26); S-CAP-22/23 | OWNER | Not started | P1 | M3.1 | See docs/CAPTURE-FIRST-PLAN.md | Planned tests | Proposed — M3.1, awaiting approval | — |
| CF-02 | Editor/Library window created on demand from every entry point | OWNER (option A, 2026-11-26); S-CAP-22/23 | OWNER | Not started | P1 | M3.1 | See docs/CAPTURE-FIRST-PLAN.md | Planned tests | Proposed — M3.1, awaiting approval | — |
| CF-03 | Closing the Editor saves, releases documents and destroys it; capture keeps working | OWNER (option A, 2026-11-26); S-CAP-22/23 | OWNER | Not started | P1 | M3.1 | See docs/CAPTURE-FIRST-PLAN.md | Planned tests | Proposed — M3.1, awaiting approval | — |
| CF-04 | Captures with Preview off never create the Editor | OWNER (option A, 2026-11-26); S-CAP-22/23 | OWNER | Not started | P1 | M3.1 | See docs/CAPTURE-FIRST-PLAN.md | Planned tests | Proposed — M3.1, awaiting approval | — |
| CF-05 | Separate taskbar identities for capture panel and Editor | OWNER (option A, 2026-11-26); S-CAP-22/23 | OWNER | Not started | P1 | M3.1 | See docs/CAPTURE-FIRST-PLAN.md | Planned tests | Proposed — M3.1, awaiting approval | — |
| CF-06 | Editor errors are isolated; capture keeps working; Editor can be reopened | OWNER (option A, 2026-11-26); S-CAP-22/23 | OWNER | Not started | P1 | M3.1 | See docs/CAPTURE-FIRST-PLAN.md | Planned tests | Proposed — M3.1, awaiting approval | — |
| CF-07 | Notification-icon left-click opens the capture panel | OWNER (option A, 2026-11-26); S-CAP-22/23 | OWNER | Not started | P1 | M3.1 | See docs/CAPTURE-FIRST-PLAN.md | Planned tests | Proposed — M3.1, awaiting approval | — |
| CP-01 | Capture panel shown at start by default (setting) | OWNER (option A, 2026-11-26); S-CAP-22/23 | OWNER | Not started | P1 | M3.1 | See docs/CAPTURE-FIRST-PLAN.md | Planned tests | Proposed — M3.1, awaiting approval | — |
| CP-02 | Menus in the capture panel's title bar (accessible custom title bar) | OWNER (option A, 2026-11-26); S-CAP-22/23 | OWNER | Not started | P1 | M3.1 | See docs/CAPTURE-FIRST-PLAN.md | Planned tests | Proposed — M3.1, awaiting approval | — |
| CP-03 | Capture panel always-on-top option | OWNER (option A, 2026-11-26); S-CAP-22/23 | OWNER | Not started | P1 | M3.1 | See docs/CAPTURE-FIRST-PLAN.md | Planned tests | Proposed — M3.1, awaiting approval | — |
| CP-04 | Show/hide capture panel hotkey (unassigned by default) | OWNER (option A, 2026-11-26); S-CAP-22/23 | OWNER | Not started | P1 | M3.1 | See docs/CAPTURE-FIRST-PLAN.md | Planned tests | Proposed — M3.1, awaiting approval | — |

## Totals

| Approval state | Requirements |
|---|---|
| Approved — global rule (owner) | 9 |
| Approved — product decision (owner, 2026-11-25) | 5 |
| Approved — Gate B (owner, 2026-11-25) | 62 |
| Delivered M1/M2 — under independent review | 37 |
| Proposed — not authorised | 110 |
| Proposed — M3.1, awaiting approval | 11 |
| **Total** | **234** |
