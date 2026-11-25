# Product requirements

> **Note (2026-11-25):** for new work see the proposed master list [PRODUCT-REQUIREMENTS.md](PRODUCT-REQUIREMENTS.md)
> (Gate A, awaiting approval). This file remains the record of milestones 1–2; its IDs map to BL-* there.

Version: milestone 2 (interface redesign and persistent Capture Library), 2026-11-25. Status column: **Done**
(implemented and covered by automated tests or the acceptance checklist), **Partial**, **Planned** (not implemented —
not shown as working in the UI).

## Goal

A fast, private, Windows-native screen capture and annotation tool with the familiar Capture window / Editor /
Library workflow of established tools (see COMPETITIVE-ANALYSIS.md), keeping every capture in a local, editable,
searchable library — without accounts, cloud or telemetry.

## Capture (milestone 1, preserved)

| ID | Requirement | Status |
|----|-------------|--------|
| R1 | Full screen (all displays), region (across monitors), window, single display | Done |
| R2 | Multi-monitor with negative coordinates and mixed DPI | Done (unit-tested; manual check pending) |
| R3 | Esc / right-click cancels; SnapForge's own windows never captured | Done |

## Workspace 1 — Capture window

| ID | Requirement | Status |
|----|-------------|--------|
| C1 | Compact controller with a prominent Capture button showing the hotkey | Done |
| C2 | Region / Window / Display / Full screen selection | Done |
| C3 | Preview in Editor, Copy to clipboard, Save to Library, Capture mouse pointer options | Done |
| C4 | Capture delay with visible countdown (Esc cancels) | Done |
| C5 | Configurable global hotkeys (capture + per preset); conflicts reported | Done |
| C6 | Notification-area icon with menu; closing keeps SnapForge running (configurable); single instance | Done |
| C7 | Capture presets: built-ins, save current, rename, delete, per-preset hotkey | Done (no export/import, no reordering) |
| C8 | Start with Windows | Planned |

## Workspace 2 — Editor

| ID | Requirement | Status |
|----|-------------|--------|
| E1 | Traditional menu bar (File, Edit, Image, View, Capture, Help) with access keys | Done |
| E2 | Horizontal annotation toolbar: Select, Arrow, Line, Rectangle, Ellipse, Highlight, Text, Step, Redact, Crop | Done |
| E3 | Large central canvas with zoom (fit, 1:1, Ctrl+wheel) | Done |
| E4 | Right panel: Quick Styles (built-in + saved, favorites first) and Tool Properties (color, hex, thickness, applies to selection) | Done (per-tool styles, opacity, shadow planned) |
| E5 | Bottom Recent Captures Tray | Done (see T1–T9) |
| E6 | Prominent Capture and Library navigation | Done |
| E7 | Status bar: status, save state, dimensions, zoom | Done |
| E8 | Light and dark themes (+ follow Windows) | Done |
| E9 | Resizable and collapsible panels (properties panel, tray, library sidebar) | Done |
| E10 | Accessible keyboard navigation: tool keys, menu access keys, Tab order, automation names | Partial (screen-reader pass not done) |
| E11 | Undo/redo for all edits including redaction and crop | Done |

## Workspace 3 — Library

| ID | Requirement | Status |
|----|-------------|--------|
| L1 | Persistent thumbnail grid, virtualized, lazily loaded thumbnails | Done |
| L2 | All Captures, Recent, Favorites, Tags, Folders, Trash views | Done |
| L3 | Date-based grouping | Done |
| L4 | Search and sorting | Done |
| L5 | Adjustable thumbnail size | Done |
| L6 | Multiple selection (click, Ctrl, Shift, Ctrl+A, arrows) | Done |
| L7 | Rename, duplicate, favorite, tags, folder, delete (Trash), restore, delete permanently, export PNG/JPEG (single or batch), export .sfcap | Done |
| L8 | Double-click / Enter reopens editable captures in the Editor | Done |
| L9 | Configurable storage location (verified move) | Done |

## Persistent storage

| ID | Requirement | Status |
|----|-------------|--------|
| S1 | New captures saved automatically by default | Done |
| S2 | Opt-out: capture without history (memory only) | Done |
| S3 | Documented, versioned editable format with image + editable annotations | Done (CAPTURE-FORMAT.md) |
| S4 | Edits persisted automatically with crash-resistant writes | Done |
| S5 | Library separate from exported PNG/JPEG | Done |
| S6 | Thumbnail caching, lazy loading, bounded memory | Done |
| S7 | Backup, restore, retention, storage usage | Done |
| S8 | No internet or cloud dependency | Done (no network code) |

## Recent Captures Tray

| ID | Requirement | Status |
|----|-------------|--------|
| T1 | Thumbnails along the bottom of the Editor | Done |
| T2 | Click to open | Done |
| T3 | Selected indicator | Done |
| T4 | Horizontal scrolling | Done |
| T5 | Right-click menu | Done |
| T6 | Pinning (persisted, Ctrl+Shift+P) | Done (no drag reorder) |
| T7 | Close without deleting | Done |
| T8 | Explicit deletion (to Trash) | Done |
| T9 | Automatic thumbnail updates after edits; persistence across restarts; show/hide | Done |

## Privacy

| ID | Requirement | Status |
|----|-------------|--------|
| P1 | Threat model updated for persistent storage | Done (SECURITY.md) |
| P2 | Storage location disclosed (first-run notice, Help, Settings, Library sidebar) | Done |
| P3 | Never upload or sync automatically | Done |
| P4 | Exports contain no original hidden image data | Done (tests) |
| P5 | Warning that editable copies/undo history keep originals | Done (banner, tile badge, Settings, .sfcap export warning) |
| P6 | Destructive sanitization of retained capture, thumbnail and history | Done |
| P7 | Limitations documented (backups, recovery, external copies) | Done |

## Additional editing (prioritized after the workspaces)

| ID | Feature | Status |
|----|---------|--------|
| X1 | Crop | Done |
| X2 | Highlight | Done |
| X3 | Quick Styles + favorites | Done (basic) |
| X4 | Resize image | Planned |
| X5 | Blur / pixelate (with "not secure" labelling) | Planned |
| X6 | Callouts | Planned |

## Out of scope (until MVP review)

Video/GIF recording, scrolling capture, OCR, automatic PII detection, sharing/cloud, installer/auto-update,
localization.

## Acceptance criteria

Milestone 1 criteria AC1–AC22 still apply. New:

| ID | Criterion | Verify |
|----|-----------|--------|
| AC23 | A new capture appears in the Library and tray without any save action; its edits persist after restart and remain editable | A (LibraryWorkflowTests) + M |
| AC24 | Capture without history writes nothing to the library folder | A + M |
| AC25 | An interrupted or disk-full save never corrupts the previous version; the user is told and edits stay open | A (core + app) |
| AC26 | Closing a tray thumbnail keeps the capture; Delete moves it to Trash; pins persist | A + M |
| AC27 | Search, views and sort return the expected captures; 1 500-capture libraries index and search quickly | A |
| AC28 | Moving the library copies, verifies, then removes the old copy; settings updated | A + M |
| AC29 | Sanitize removes original pixels and hidden annotation text from the stored files and thumbnail | A + M |
| AC30 | Global hotkey conflicts are reported (never silent) | A + M |
| AC31 | Light and dark themes render all three workspaces legibly | Screenshots + M |
