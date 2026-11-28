# SnapForge UI specification (proposed, Gate A)

Status: **Sections 1, 2, 4 (drag-out), 5 (drag-out, import) and 6 implemented in milestone 3 (Gate B)**; the
Editor proposals in section 3 remain proposals for M4. "As built" notes below record where the build differs from
the original proposal and why. Requirement IDs refer to [PRODUCT-REQUIREMENTS.md](PRODUCT-REQUIREMENTS.md).

Design rules that apply to every surface:

* **Original branding.** SnapForge's own name, orange accent, icon set and wording. The supplied Snagit image
  (`references/snagit-capture-panel.png` in the owner's handoff package, not committed) is used only as *functional
  layout guidance*: which controls exist and roughly where. No TechSmith artwork, icons, colours, names or text.
* **No fake controls.** A function that is not implemented is either absent or shown disabled with the label
  "Not available yet" in its tooltip and automation name (G-02). It never looks or behaves as if it worked.
* **Light, dark and Windows high-contrast themes**, all colours from theme resources (CAP-UI-12, NFR-A11Y-03).
* **Keyboard first.** Every command reachable by keyboard; visible focus; access keys on menus; automation names on
  every icon-only control (NFR-A11Y-01…03).
* **Per-monitor DPI** (Per-Monitor V2 manifest already present) — layouts tested at 100/125/150/200 % (NFR-DPI-1).

## 1. Capture control panel (Gate B item 1; requirements CAP-UI-01…CAP-UI-14)

### 1.1 Functional layout derived from the reference image (direct observation of the supplied PNG, 742×237 px)

The reference shows a **compact, horizontal, single-row panel** (approximately 740×240 logical px):

| Zone | What the reference shows (observation) | SnapForge proposal |
|---|---|---|
| Title bar | App icon, **File / Edit / View / Help** menus, minimise and close | Same menu set, SnapForge icon; window chrome follows theme |
| Left rail | Vertical mode switcher: **All-in-One**, **Image** (selected), **Video**, icon above label | Same three modes. **Video** and **All-in-One** shown disabled "Not available yet" until milestones 5/8 (CAP-UI-02). Image is the only enabled mode in M3 |
| Column 1 | Three labelled dropdowns: **Selection** (value "Fullscreen"), **Effects** ("None"), **Share** ("None") | **Selection**: Region / Window / Display / Full screen (implemented modes only). **Effects**: disabled, "None", "Not available yet". **Share**: *as built* disabled, "None", "Not available yet" — Library and clipboard stay as the Save to Library and Copy to Clipboard switches (two controls for one setting would be confusing); destinations such as "save to folder" come with EXP-05 |
| Column 2 | Four switches: **Preview in Editor** (on), **Copy to Clipboard**, **Cursor**, **Time Delay** with a chevron for duration | Same four, plus **Save to Library** (existing, must be preserved — CAP-UI-07) as a fifth row or inside Share. Delay chevron opens a duration picker |
| Right | Reset (circular arrow) icon top-right; **large circular Capture button**; assigned shortcut text beneath ("Print Screen") | Circular SnapForge-orange Capture button (≥ 96 px, keyboard default button); live hotkey text beneath, "No hotkey" if none, warning glyph if the hotkey failed to register (CAP-UI-05) |
| Footer | **Presets** (gear), **+▾** (add preset), **Open … Editor** link on the right | **Presets** flyout (list, capture, rename, hotkey, delete), **+** "Save current settings as preset", **Open SnapForge Editor** and **Open Library** links |

Today (baseline 3018841): the Capture window exists but is a **tall vertical panel** (≈360×710) with Region/Window/
Display/Full tiles, five switches, a delay segmented control and an inline preset list. All of its *functions* are
implemented; only the arrangement differs from the reference. M3 rearranges; it must not remove any function.

### 1.2 Behaviour

* Opens from: notification-area icon, widget, global hotkey for "show capture panel" (new, optional), Capture menu.
* Remembers position per monitor; clamps on-screen after monitor changes (CAP-UI-10).
* **Reset** restores the panel's current options to defaults after confirmation-free undoable action (shows "Reset"
  toast with Undo) (CAP-UI-09).
* Esc closes; Enter captures; Alt+S/E/H focus Selection/Effects/Share; switches are toggled with Space.
* Hidden during capture (already implemented for all SnapForge windows).
* Minimum size enforced; at 200 % scaling the panel must still fit a 1366×768 screen.

## 2. Persistent capture widget (Gate B item 2; WID-01…WID-12)

| State | Proposal |
|---|---|
| Reference | Snagit docks a widget on the top or side edges and opens it on hover (S-CAP-06, DOC search extract); auto-hide, topmost and multi-monitor behaviour are undocumented, so SnapForge defines them here |
| Defaults (owner decision 2026-11-25) | **Enabled** and **always on top** for new installs; hover to open; auto-hide off |
| Collapsed | Small tab (72×12 logical px as built) docked to the **top centre** of the primary monitor by default; translucent, theme coloured; no taskbar button; never steals focus (`WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`) |
| Expanded | On hover (300 ms intent delay) or click: a small strip with **Capture**, **Editor**, **Library**, **Presets ▾** buttons and a tooltip showing the capture hotkey |
| Docking | Drag along the top, left or right edge of any monitor; snaps to edge; position stored as (monitor device name, edge, offset fraction) so mixed DPI and monitor reordering restore sensibly |
| Options (Settings → Widget) | Enable/disable; topmost on/off; auto-hide (collapse after leaving, or hide completely until the screen edge is touched); expand on hover vs click only; launch with Windows (shared with app startup setting) |
| Capture | Widget hides itself before the frame is grabbed (same mechanism as other windows) and is never in the captured image |
| Accessibility | Reachable from keyboard via the notification-area menu "Focus widget"; automation names on every button; respects "reduce animations" |

## 3. Editor (existing; M4 proposals)

Today: menu (File/Edit/Image/View/Capture/Help), labelled horizontal toolbar (10 tools), canvas, right Quick Styles/
Tool Properties panel (resizable, collapsible), Recent Captures tray, status bar with save state, size and zoom,
redaction / no-history / save-error banners. Single-letter tool keys and Ctrl shortcuts (see F1 "Keyboard
shortcuts" dialog).

Proposed additions (M4, not M3): canvas context menu (Cut/Copy/Paste/Duplicate/Delete, Arrange ▸, Edit style…),
resize/rotate handles, multi-select (Shift/Ctrl-click, marquee, Ctrl+A), arrow-key nudge (1 px, Shift = 10 px),
per-tool Quick Styles and full Tool Properties (fill, outline, opacity, dash, arrowheads, font, shadow), toolbar
customisation (More ▸ overflow).

## 4. Recent Captures tray (existing; M3 adds drag-out)

Today: horizontal scrolling thumbnails, selected indicator, pin, close-without-delete, delete, context menu,
show/hide, persisted. M3 adds **native drag-out** (DND-01…DND-12): dragging a tile shows a drag image of the
thumbnail with a count badge for multi-selection; dropping outside the app produces flattened PNG files (copy
effect — Snagit offers a user-selected drag-out file type since 2024.1.2, S-LIB-03); dragging within the tray reorders (only when the drop target is the tray itself). Esc cancels.

## 5. Library (existing; M3 adds drag-out and image import)

Today: sidebar views (All, Recent, Favorites, Trash, Tags, Folders, Storage), search, sort, size slider, date
groups, multi-select, context menu. M3 adds drag-out (same rules as the tray), drop-in import of PNG/JPEG/BMP files
(DND-10) and fixes the truncated storage path in the sidebar (DEF-05).

## 6. Settings (existing; M3 additions)

Add pages/sections: **Widget** (section 2), **Drag and drop** (default PNG/JPEG, JPEG quality, filename pattern),
**Startup** (launch with Windows — per-user `Run` key, off by default). Existing pages unchanged.

## 6a. Screen recording (Milestone V1)

* **Capture panel — Video mode**: rail button *Video* swaps the options card: Record (Region / Window / Display),
  Frame rate (30 / 60 FPS), Quality (Standard / High), switches System audio (on), Microphone (off), Cursor (on),
  Countdown (on, 3 s; menu 0/3/5/10). The round button reads **Record** with the recording hotkey (Shift+F9) beneath.
  Image mode is unchanged. Screenshot: `capture-panel-video-*`.
* **Widget**: *Record* button next to Capture; reads *Stop* while recording.
* **Recording toolbar** (floating, does not take focus, excluded from capture, draggable): blinking red dot, elapsed
  time `h:mm:ss`, `W×H · FPS`, Pause/Resume, **Stop** (save), Cancel (asks before discarding; Esc), toggles *System
  audio* / *Microphone* with level meters; a status line for notices (device changes, low disk). Placed below the
  recorded area, else above, else inside it. Screenshots: `recording-*`, `recording-toolbar-*`.
* **Outline**: dashed red frame 4 px outside the recorded area; click-through; excluded from capture.
* **Library**: video tiles with play badge, duration, resolution and size; *Videos* view; context menu *Play*,
  *Export MP4 video…*; image-only commands hidden for videos; tooltips for recovered / fragmented videos.
  Screenshot: `library-videos-*`.
* **Player** (own window): Play/Pause (Space), seek bar (←/→ 5 s), time, Mute, volume, info line. Screenshot: `player-*`.
* **Settings → Recording**: microphone picker, Record/Stop/Cancel hotkeys, Save recordings to the Library, Open the
  recording when it is saved, minimum free space, privacy disclosure. Screenshot: `settings-recording-*`.

## 7. Screenshot evidence required at Gate C

Real-window captures (not only off-screen renders) of: capture panel (light/dark), widget collapsed and expanded on
top/left/right, tray during drag, Library with multi-selection, Settings → Widget. Off-screen renders from
`--screenshots` are labelled as renders.
