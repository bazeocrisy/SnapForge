# Feature parity matrix — Snagit for Windows vs SnapForge (Gate A)

Baseline: SnapForge commit `3018841` (I-01); rows marked "(M3)" updated after milestone 3 (Gate B). Reference: Snagit for Windows 2025.x–2026.4.0. Sources and evidence
classes: [SOURCE-REGISTRY.md](SOURCE-REGISTRY.md). **All Snagit facts are DOC (search extract) or weaker; none was
verified hands-on.** All SnapForge facts are OBS (code read) unless marked CI.

**Approval state of every row: _Proposed — awaiting owner approval_.** Nothing here authorises development.

Legend
* **SnapForge**: ✅ Done (implemented + automated tests) · ◐ Partial · ✗ Missing · ⊕ Exceeds reference — **confirmed** (both sides evidenced) · ⊕? Exceeds — **plausible only** (Snagit side rests on absence of documentation) · ≠ Different by design · n/a
* **Pri**: P1 must (next milestone) · P2 should · P3 could · — not planned / out of scope
* **MS**: target milestone (see [DEVELOPMENT-ROADMAP.md](DEVELOPMENT-ROADMAP.md)); "M3" = Gate B proposal
* Requirement IDs (Req) link to [PRODUCT-REQUIREMENTS.md](PRODUCT-REQUIREMENTS.md).

## 1. Capture

| ID | Capability | Snagit (Windows) | Src · Ev | SnapForge today | Pri | MS | Req |
|---|---|---|---|---|---|---|---|
| FP-CAP-01 | Capture modes All-in-One / Image / Video | Three tabs | S-CAP-01/02 DOC | ◐ mode rail present (M3): Image active; Video active on the V1 branch (under review); All-in-One disabled, labelled "Not available yet" | P1 (layout) | M3/V1 | CAP-UI-02, VID-01 |
| FP-CAP-02 | Region selection with crosshairs | Orange crosshairs, drag; 2026.0.0 "simplified crosshairs" | S-CAP-02, S-CAP-22 DOC | ✅ freeze-frame overlay per monitor, real-mouse tests (CI) | — | done | BL-CAP-01 |
| FP-CAP-03 | Window selection by hover | Dashed border, click | S-CAP-03/04 DOC | ✅ hover highlight + click (`WindowPicker`) | — | done | BL-CAP-02 |
| FP-CAP-04 | Full screen / single monitor | Fullscreen selection | S-CAP-02 DOC | ✅ Display (one monitor) and Full screen (all) — parity (exceeds claim withdrawn: Snagit monitor choice not researched) | — | done | BL-CAP-03 |
| FP-CAP-05 | Magnifier during selection (M toggles) | "Show magnifier" preference | S-CAP-06/07 DOC | ✗ | P2 | M5 | CAP-ADV-01 |
| FP-CAP-06 | Exact width/height entry during selection | Width/Height fields in All-in-One toolbar | S-CAP-02/26 DOC | ✗ (size shown, not editable) | P2 | M5 | CAP-ADV-02 |
| FP-CAP-07 | Fixed region (+ Set position) | Selection Properties → Fixed region | S-CAP-03/29 DOC (2021.4; current UNVERIFIED) | ✗ | P2 | M5 | CAP-ADV-03 |
| FP-CAP-08 | Arrow keys move crosshair 1 px; Enter/Shift+Enter/Esc | Documented hotkeys | S-CAP-07 DOC | ◐ Esc cancels; no arrow nudge / Enter | P2 | M5 | CAP-ADV-04 |
| FP-CAP-09 | Repeat last capture | Assignable hotkey (older default Ctrl+Shift+R, UNVERIFIED) | S-CAP-07/33 | ✗ | P2 | M5 | CAP-ADV-05 |
| FP-CAP-10 | Menu capture (cascading menus) | Menu selection type | S-CAP-03/20 DOC | ✗ — only a built-in preset *named* "Menu capture" that is Region + 3 s delay (DEF-03) | P2 | M5 | CAP-ADV-06 |
| FP-CAP-11 | Multiple area (several regions → one image) | Advanced → Multiple Area | S-CAP-03 DOC | ✗ | P3 | M5 | CAP-ADV-07 |
| FP-CAP-12 | Scrolling capture, automatic (vertical/horizontal/both) | Arrows when auto-scroll detected; many documented app limits | S-CAP-05/13/16 DOC | ✗ | P2 | M5 | CAP-ADV-08 |
| FP-CAP-13 | Scrolling capture, manual (panoramic) | User scrolls, Done | S-CAP-05 DOC | ✗ | P2 | M5 | CAP-ADV-09 |
| FP-CAP-14 | Interval (time-lapse) capture, discard identical | Min interval 3 s since 2026.3.1 | S-CAP-03/10 DOC | ✗ | P3 | M5 | CAP-ADV-10 |
| FP-CAP-15 | Clipboard as capture source | Selection type "Clipboard" | S-CAP-03 DOC | ✗ | P2 | M5 | CAP-ADV-11 |
| FP-CAP-16 | Printer capture (virtual printer) | Installs as printer (current status UNVERIFIED) | S-CAP-06 DOC/UNVERIFIED | ✗ | — | — | CAP-LEG-01 |
| FP-CAP-17 | Object capture | Only Snagit 10/11 | S-CAP-32 DOC (historical) | ✗ | — | — | CAP-LEG-02 |
| FP-CAP-18 | Cursor capture | Cursor kept as editable vector object | S-CAP-03/04 DOC | ◐ cursor drawn into pixels (not movable) | P2 | M4 | ED-OBJ-20 |
| FP-CAP-19 | Time delay with on-screen countdown | Countdown bottom-right | S-CAP-03 DOC | ✅ delays 1–60 s (picker + custom) with countdown (M3) | P1 (picker) | M3 | CAP-UI-09 |
| FP-CAP-20 | Preview in Editor toggle | Yes | S-CAP-04 DOC | ✅ | — | done | BL-CAP-04 |
| FP-CAP-21 | Copy to Clipboard toggle | Yes | S-CAP-04 DOC | ✅ | — | done | BL-CAP-05 |
| FP-CAP-22 | Save to Library / capture without history | Library autosave can be turned off (Editor Prefs → Library) | S-LIB-11/12 DOC | ✅ per-capture switch + presets — parity (Snagit also allows autosave off) | — | done | BL-LIB-01 |
| FP-CAP-23 | Selection / Effects / Share dropdowns | Present in Image tab | S-CAP-04/28 DOC; layout I-05 OBS | ◐ Selection dropdown works (M3); Effects and Share shown disabled "Not available yet" | P1 | M3 | CAP-UI-03/04/05 |
| FP-CAP-24 | Share → File with format/folder at capture time | Share = File + gear | S-LIB-33 DOC | ✗ | P2 | M3 (optional) / M4 | EXP-05 |
| FP-CAP-25 | Presets: create, hotkey per preset, rename, delete | Add Preset, Add Hotkey | S-CAP-09 DOC | ✅ | — | done | BL-CAP-06 |
| FP-CAP-26 | Preset export/import (.snagpresets equivalent) | File → Export All / Import | S-CAP-09/21 DOC | ✗ | P2 | M4 | SET-05 |
| FP-CAP-27 | Global hotkey, default Print Screen; conflict handling | Print Screen; Win11 Snipping Tool may own it; "override" option | S-CAP-07/19 DOC | ✅ Print Screen default; conflict shown under the Capture button with Windows 11 Snipping Tool guidance (M3); ◐ real Win11 behaviour = manual M3-P8 | P1 (verify) | M3 | CAP-HK-01 |
| FP-CAP-28 | Hide own windows during capture | Preference (2024.1.0+) | S-CAP-06/14 DOC | ✅ always hides (180 ms settle); not configurable ≠ | — | done | BL-CAP-07 |
| FP-CAP-29 | Capture window chrome: File/Edit/View/Help, reset, Open Editor | Menus, reset icon, Open Editor link (reference image) | I-05 OBS; menu contents UNVERIFIED | ✅ File/Edit/View/Help menus, reset with undo, Open Editor/Library (M3) | P1 | M3 | CAP-UI-01/10/12 |
| FP-CAP-30 | Compact horizontal capture panel | ≈740×240 single row (reference image) | I-05 OBS | ✅ compact horizontal panel ≈746×257 px incl. title bar (M3, real screenshot) | P1 | M3 | CAP-UI-01 |
| FP-CAP-31 | Large Capture button + shortcut text under it | Circular button, "Print Screen" beneath | I-05 OBS | ✅ round Capture button, hotkey text beneath (M3) | P1 | M3 | CAP-UI-06 |
| FP-CAP-32 | Windows Graphics Capture backend | n/a (implementation detail) | — | ✗ GDI BitBlt (documented decision, docs/WINDOWS-GRAPHICS-CAPTURE.md) | P3 | M5 | CAP-ADV-12 |
| FP-CAP-33 | Multi-monitor, mixed DPI | Supported | INFER | ✅ Per-Monitor V2, virtual desktop; ◐ only single-monitor CI; real mixed-DPI untested | P1 (verify) | M3 | NFR-DPI-01/02 |

## 2. Desktop capture widget

| ID | Capability | Snagit (Windows) | Src · Ev | SnapForge today | Pri | MS | Req |
|---|---|---|---|---|---|---|---|
| FP-WID-01 | Widget instead of / alongside capture window | Choose Window vs Widget in Capture Prefs → General | S-CAP-06 DOC | ✅ widget alongside the panel, enabled by default (M3) | P1 | M3 | WID-01 |
| FP-WID-02 | Edge-docked tab, expand on hover | "Hover over the blue bar to display the Widget" | S-CAP-06 DOC | ✅ edge tab, hover or click to expand (M3, real-input test) | P1 | M3 | WID-02/03 |
| FP-WID-03 | Dock top or sides, drag along edge | "reposition along the top or side" | S-CAP-06 DOC | ✅ dock top/left/right by dragging the grip (M3) | P1 | M3 | WID-04 |
| FP-WID-04 | Auto-hide / completely disappear | Snagit 10 had it; current UNVERIFIED | S-CAP-32 DOC (historical) | ✅ auto-hide option (M3; visual check manual) | P1 | M3 | WID-06 |
| FP-WID-05 | Topmost option | UNVERIFIED | — | ✅ topmost by default, configurable (M3) | P1 | M3 | WID-07 |
| FP-WID-06 | Multi-monitor / DPI placement | UNVERIFIED | — | ◐ placement per monitor/edge/offset with DPI-scaled size, fallback to primary (unit-tested); real multi-monitor/mixed-DPI = manual | P1 | M3 | WID-08 |
| FP-WID-07 | Start with Windows | "Run Snagit when Windows starts" | S-CAP-06 DOC | ✅ launch with Windows (HKCU Run, --background) (M3) | P1 | M3 | SET-01 |

## 3. Editor tools

| ID | Capability | Snagit (Windows) | Src · Ev | SnapForge today | Pri | MS | Req |
|---|---|---|---|---|---|---|---|
| FP-ED-01 | Select/Move objects | Move tool; Smart Move (AI object detection) | S-ED-15 DOC | ✅ select + move; ✗ Smart Move | P1 / P3 | M4 / M6 | ED-OBJ-01, AI-03 |
| FP-ED-02 | Arrow (end styles, sizes, Bezier, shadow) | Rich properties | S-ED-25 DOC | ◐ straight arrow, colour + width | P1 | M4 | ED-OBJ-06 |
| FP-ED-03 | Line | Windows-only tool | S-ED-26 DOC | ✅ | — | done | BL-ED-01 |
| FP-ED-04 | Shape (rect, ellipse, rotate, shadow) | Shape tool, rotation handle | S-ED-29 DOC | ◐ rectangle + ellipse outline; no fill/rotate/shadow | P1 | M4 | ED-OBJ-02/04/07 |
| FP-ED-05 | Text (fill, outline, font) | Text tool | S-ED-28 DOC | ◐ text, colour, size; no font family/outline | P1 | M4 | ED-OBJ-08 |
| FP-ED-06 | Callout | Box + tail, padding, shaded | S-ED-27 DOC | ✗ (X6 Planned) | P1 | M4 | ED-TOOL-01 |
| FP-ED-07 | Step (numbers/letters, restart, edit value) | Step tool | S-ED-11 DOC | ◐ numeric steps auto-increment; no letters/restart/edit value | P1 | M4 | ED-TOOL-02 |
| FP-ED-08 | Highlighter | Highlighter tool | S-ED-30 DOC | ✅ rectangle highlight (fixed opacity) | — | done | BL-ED-02 |
| FP-ED-09 | Crop | Crop tool | S-ED-33 DOC | ✅ reversible until sanitize | — | done | BL-ED-03 |
| FP-ED-10 | Cut Out (remove band) | Cut Out tool | S-ED-34 DOC | ✗ | P2 | M4 | ED-TOOL-03 |
| FP-ED-11 | Blur / Pixelate / Solid | Blur tool; TechSmith warns blur/pixelate reversible | S-ED-08/09 DOC | ◐ solid redaction only (opaque, flattened on export, Sanitize); ✗ blur/pixelate | P1 | M4 | ED-TOOL-04, PRIV-DND-05 |
| FP-ED-12 | Smart Redact (detect emails, cards, faces…) | 2025.0.0+, on-device per TechSmith | S-ED-08/10 DOC | ✗ | P2 | M6 | AI-02 |
| FP-ED-13 | Grab Text (OCR) / Edit Text | ABBYY → Microsoft OCR default 2026.4.0 | S-ED-12/14 DOC | ✗ | P2 | M6 | AI-01 |
| FP-ED-14 | Simplify | 2026.1.0 Auto Simplify | S-ED-20 DOC | ✗ | P3 | M6 | AI-04 |
| FP-ED-15 | Magnify tool | Source area + enlarged copy | S-ED-31 DOC | ✗ | P2 | M4 | ED-TOOL-05 |
| FP-ED-16 | Spotlight (effect on Windows) | Image → Effects → Spotlight and Magnify | S-ED-21/32 DOC | ✗ | P2 | M4 | ED-FX-05 |
| FP-ED-17 | Pen (vector option) | Pen, Shift constrains | S-ED-37 DOC | ✗ | P2 | M4 | ED-TOOL-06 |
| FP-ED-18 | Eraser | Eraser, reveals canvas colour | S-ED-39 DOC | ✗ | P3 | M4 | ED-TOOL-07 |
| FP-ED-19 | Stamps | Built-in + downloadable | S-ED-36 DOC | ✗ (needs original artwork) | P3 | M4 | ED-TOOL-08 |
| FP-ED-20 | Fill (tolerance, transparent) | Fill tool | S-ED-38 DOC | ✗ | P3 | M4 | ED-TOOL-09 |
| FP-ED-21 | Selection: rectangle, ellipse, polygon, freehand; Auto-Fill | Selection tool | S-ED-16 DOC | ✗ (pixel selection) | P2 | M4 | ED-TOOL-10 |
| FP-ED-22 | Magic Wand / global colour select | Magic Wand | S-ED-17 DOC | ✗ | P3 | M6 | ED-TOOL-11 |
| FP-ED-23 | Background removal | Fill/Wand/Lasso on Windows; one-click AI on Mac (Windows UNVERIFIED) | S-ED-18/19 DOC | ✗ | P3 | M6 | AI-05 |
| FP-ED-24 | Resize image (%, px, in, cm; aspect lock) | Image → Resize Image | S-ED-35 DOC | ✗ (X4 Planned) | P1 | M4 | ED-IMG-01 |
| FP-ED-25 | Resize canvas | Separate command (legacy wording) | S-ED-46 UNVERIFIED | ✗ | P2 | M4 | ED-IMG-02 |
| FP-ED-26 | Rotate (90/180/custom) / Flip | Image → Rotate; Arrange → Flip | S-ED-35 DOC | ✗ | P1 | M4 | ED-IMG-03 |
| FP-ED-27 | Auto Trim | Label renamed 2026.0.0; behaviour legacy | S-ED-01 DOC / S-ED-46 UNVERIFIED | ✗ | P2 | M4 | ED-IMG-04 |
| FP-ED-28 | Rounded image corners | Yellow diamond handle | S-ED-29 DOC | ✗ | P3 | M4 | ED-IMG-05 |
| FP-ED-29 | Toolbar customisation (More, drag items, labels on/off) | More → Customize Toolbar | S-ED-24 DOC | ✗ fixed labelled toolbar | P2 | M4 | ED-UI-01 |
| FP-ED-30 | Ctrl+1–9 by toolbar position | Position-based | S-ED-22 DOC | ≠ single-letter tool keys (V A L R E H T N X C) | P3 | M4 | ED-UI-02 |
| FP-ED-31 | Zoom in/out/fit/actual | Ctrl+Plus/Minus/0 | S-ED-22 DOC | ✅ (Ctrl+= / Ctrl+− / Ctrl+0 / Ctrl+Alt+0) | — | done | BL-ED-04 |

## 4. Object properties and management

| ID | Capability | Snagit (Windows) | Src · Ev | SnapForge today | Pri | MS | Req |
|---|---|---|---|---|---|---|---|
| FP-OBJ-01 | Colour (HEX/RGB, eyedropper) | Colour dropdown, Mode, Eyedropper | S-ED-38/27/30 DOC | ◐ fixed swatches via Quick Styles + colour; ✗ HEX entry/eyedropper | P1 | M4 | ED-OBJ-03 |
| FP-OBJ-02 | Fill vs outline | Text/Shape fill + outline | S-ED-28/29 DOC | ✗ | P1 | M4 | ED-OBJ-02 |
| FP-OBJ-03 | Thickness | Yes | S-ED-25 DOC | ✅ stroke width | — | done | BL-ED-05 |
| FP-OBJ-04 | Opacity | Arrow, eraser etc. | S-ED-25/39 DOC | ✗ (highlight fixed) | P1 | M4 | ED-OBJ-05 |
| FP-OBJ-05 | Font family/style/alignment | Text tool | S-ED-28 DOC | ✗ | P1 | M4 | ED-OBJ-08 |
| FP-OBJ-06 | Arrowheads / end styles | End Style dropdown | S-ED-25 DOC | ✗ | P1 | M4 | ED-OBJ-06 |
| FP-OBJ-07 | Dash styles | Not in current docs (legacy yes) | S-ED-46 UNVERIFIED | ✗ | P3 | M4 | ED-OBJ-09 |
| FP-OBJ-08 | Shadow | Colour/angle/distance/opacity/blur | S-ED-29 DOC | ✗ | P2 | M4 | ED-OBJ-07 |
| FP-OBJ-09 | Rotation of objects | Green handle | S-ED-29 DOC | ✗ | P2 | M4 | ED-OBJ-04 |
| FP-OBJ-10 | Resize handles | Yes | S-ED-31 DOC | ✗ move only | P1 | M4 | ED-OBJ-01 |
| FP-OBJ-11 | Multi-select, Ctrl+A, Select All of a kind (2026.1.0) | Yes | S-ED-22, S-ED-01 DOC | ✗ single selection | P1 | M4 | ED-OBJ-10 |
| FP-OBJ-12 | Group/ungroup | Ctrl+Shift+G/U | S-ED-22 DOC | ✗ | P2 | M4 | ED-OBJ-11 |
| FP-OBJ-13 | Layering (forward/backward) | Ctrl+Shift+F/B | S-ED-22 DOC | ✗ | P1 | M4 | ED-OBJ-12 |
| FP-OBJ-14 | Cut/Copy/Paste objects; Copy All (flattened) | Ctrl+X/C/V, Ctrl+Shift+C | S-ED-22 DOC | ◐ Ctrl+C copies flattened image; ✗ object clipboard | P1 | M4 | ED-OBJ-13 |
| FP-OBJ-15 | Duplicate object | Customisable item | S-ED-24 DOC | ✗ | P1 | M4 | ED-OBJ-13 |
| FP-OBJ-16 | Copy objects between captures | UNVERIFIED | — | ✗ | P2 | M4 | ED-OBJ-14 |
| FP-OBJ-17 | Step renumbering on delete | UNVERIFIED in Snagit | — | ◐ `StepNumbering` gives next number; renumbering behaviour to be specified | P2 | M4 | ED-TOOL-02 |
| FP-OBJ-18 | Flatten / Flatten All | Ctrl+T / Ctrl+Shift+T (conflicting) | S-ED-22/23 DOC | ≠ Sanitize capture (whole-capture, irreversible, explicit); ✗ per-object flatten | P2 | M4 | ED-OBJ-15 |
| FP-OBJ-19 | Undo/redo | Ctrl+Z/Y | S-ED-22 DOC | ✅ (not persisted across reopen) | — | done | BL-ED-06 |
| FP-OBJ-20 | Arrow-key nudge | UNVERIFIED for editor | — | ✗ | P1 | M4 | ED-OBJ-16 |
| FP-OBJ-21 | Quick Styles (per tool), save "+", delete | Per tool | S-ED-05/06 DOC | ◐ one shared style list (colour+width), save, favourite, delete | P1 | M4 | ED-QS-01 |
| FP-OBJ-22 | Favorites across tools | Favorites tool | S-ED-06 DOC | ◐ favourite styles listed first | P2 | M4 | ED-QS-02 |
| FP-OBJ-23 | Themes / custom palettes (.snagtheme) | 4–8 colours generate styles | S-ED-07 DOC | ✗ | P3 | M4 | ED-QS-03 |
| FP-OBJ-24 | Default tool properties | Saved Quick Styles act as defaults | S-ED-05 DOC (wording UNVERIFIED) | ✅ last tool/colour/width restored on start | — | done | BL-ED-07 |
| FP-OBJ-25 | Canvas context menu | Flatten, Grab Text, Edit Text, Edit Value | S-ED-08/11/12 DOC | ✗ no canvas context menu (DEF-06) | P1 | M4 | ED-UI-03 |

## 5. Effects

| ID | Capability | Snagit (Windows) | Src · Ev | SnapForge today | Pri | MS | Req |
|---|---|---|---|---|---|---|---|
| FP-FX-01 | Border | Colour + width | S-ED-21 DOC | ✗ | P2 | M4 | ED-FX-01 |
| FP-FX-02 | Edges (torn, faded, wave…) | Style list (current list UNVERIFIED) | S-ED-21 DOC | ✗ | P3 | M4 | ED-FX-02 |
| FP-FX-03 | Shadow | Colour, position, size | S-ED-21 DOC | ✗ | P2 | M4 | ED-FX-03 |
| FP-FX-04 | Perspective, Page Curl | Windows | S-ED-21 DOC | ✗ | P3 | M4 | ED-FX-04 |
| FP-FX-05 | Watermark (text/image, underlay/overlay) | Windows | S-ED-21 DOC | ✗ | P3 | M4 | ED-FX-06 |
| FP-FX-06 | Filters (grayscale, invert, sharpen, soften, monochrome, opacity) | Windows | S-ED-21 DOC | ✗ | P3 | M4 | ED-FX-07 |
| FP-FX-07 | Colour adjustment (brightness/contrast, replace) | Listed; controls UNVERIFIED | S-ED-21 DOC | ✗ | P3 | M4 | ED-FX-08 |
| FP-FX-08 | Capture info | Listed; fields UNVERIFIED | S-ED-21 DOC | ✗ | P3 | M4 | ED-FX-09 |
| FP-FX-09 | Effects in capture presets | "automatically add effects" | S-ED-44 DOC | ✗ | P3 | M4 | ED-FX-10 |
| FP-FX-10 | Batch effects / batch convert | Batch Export wizard (2024 tutorial) | S-LIB-15 DOC | ✗ | P3 | M7 | LIB-BATCH-01 |

## 6. Recent Captures tray

| ID | Capability | Snagit (Windows) | Src · Ev | SnapForge today | Pri | MS | Req |
|---|---|---|---|---|---|---|---|
| FP-TR-01 | Persistent thumbnails | Yes | S-LIB-01 DOC | ✅ `tray.json` (CI tests) | — | done | BL-TR-01 |
| FP-TR-02 | Pin to left | Pin icon | S-LIB-01 DOC | ✅ Ctrl+Shift+P | — | done | BL-TR-02 |
| FP-TR-03 | Close ≠ delete | Close vs right-click Delete | S-LIB-01 DOC | ✅ | — | done | BL-TR-03 |
| FP-TR-04 | Drag to reorder | Yes | S-LIB-01 DOC | ✅ drag to reorder within sections (M3, real-input test) | P1 | M3 | DND-09 |
| FP-TR-05 | Thumbnail size S/M/L | Right-click → Thumbnail Size | S-LIB-01 DOC | ✗ | P2 | M4 | TR-02 |
| FP-TR-06 | Show/hide tray | Show/Hide Recent | S-LIB-01 DOC | ✅ | — | done | BL-TR-04 |
| FP-TR-07 | Clear tray on editor close (pref) | Windows preference | S-LIB-01 DOC | ◐ "Close all unpinned" command; no preference | P3 | M4 | TR-03 |
| FP-TR-08 | Multi-select (Ctrl+click) | Yes | S-LIB-16 DOC | ✅ Ctrl/Shift-click multi-select (M3) | P1 | M3 | DND-03 |
| FP-TR-09 | **Drag out to other apps** with chosen file type | 2024.1.2: preference for drag-out file type | S-LIB-03, S-LIB-02 DOC; data format UNVERIFIED | ✅ drag-out CF_HDROP, PNG default / JPEG option, flattened, private temp files (M3, real OLE drag in CI to an in-process target); ◐ real apps (Explorer, Office, Teams, browsers) = manual, Expected only | **P1** | **M3** | DND-01…12, PRIV-DND-01…04 |
| FP-TR-10 | Drag tray image onto canvas (combine) | Yes | S-LIB-01 DOC | ✗ | P3 | M7 | TR-04 |
| FP-TR-11 | Tooltips | UNVERIFIED | — | ✅ tooltip with title, date, size and drag hint (M3) | P2 | M3 | TR-01 |

## 7. Library

| ID | Capability | Snagit (Windows) | Src · Ev | SnapForge today | Pri | MS | Req |
|---|---|---|---|---|---|---|---|
| FP-LIB-01 | Automatic library of all captures | SNAGX in Documents\Snagit | S-LIB-11, S-CAP-06 DOC | ✅ `%LOCALAPPDATA%\SnapForge\Library`, versioned format documented in docs/CAPTURE-FORMAT.md — ⊕? openness plausible (no published SNAGX specification found; absence, not proof) | — | done | BL-LIB-01 |
| FP-LIB-02 | Search | File name + filters date/tags/app/website | S-LIB-04 DOC | ◐ title/tag search; ✗ app/website metadata | P2 | M5 | LIB-META-01 |
| FP-LIB-03 | Tags | Manual, visible only in Snagit | S-LIB-04 DOC | ✅ | — | done | BL-LIB-02 |
| FP-LIB-04 | Favorites | **Mac only** | S-LIB-04 DOC | ✅ ⊕ on Windows (DOC: Snagit favorites are Mac-only) | — | done | BL-LIB-03 |
| FP-LIB-05 | Folders | Not found (UNVERIFIED) | S-LIB-04 | ✅ virtual folders — ⊕? plausible (no Snagit folder feature found; absence, not proof) | — | done | BL-LIB-04 |
| FP-LIB-06 | Date browsing | Year/month/day (restored 2022.1.7) | S-LIB-04 DOC | ✅ date groups | — | done | BL-LIB-05 |
| FP-LIB-07 | Trash / recovery | Not found; "cannot recover deleted files" | S-LIB-07 DOC (limited) | ✅ Trash + restore; permanent delete overwrites before removal but is best effort on SSDs — ⊕? plausible (no Snagit trash found; Windows Recycle Bin behaviour unknown) | — | done | BL-LIB-06 |
| FP-LIB-08 | Retention / auto-delete | None found | S-LIB-14 DOC | ≠ optional retention policy (Snagit keeps captures until the user deletes them, S-LIB-14) — a different choice, not a superiority claim | — | done | BL-LIB-07 |
| FP-LIB-09 | Move library location (merge if exists) | Choose location; merge | S-LIB-05 DOC | ✅ move; ≠ refuses non-empty target (no merge) | P3 | M4 | LIB-05 |
| FP-LIB-10 | Backup / restore | Legacy .snagarchive; now folder copy | S-LIB-07 DOC | ✅ ZIP backup/restore with validation — ⊕? plausible (current Snagit backup appears to be a folder copy: INFER) | — | done | BL-LIB-08 |
| FP-LIB-11 | Import images (PNG/JPEG) | Not documented on Windows | S-LIB-06 DOC (SNAG/MP4/GIF only) | ✅ PNG/JPEG/BMP import, button and drop, pixels only (M3) | P1 | M3 | DND-10 |
| FP-LIB-12 | Drag out of Library | Same preference as tray | S-LIB-02/03 DOC | ✅ drag-out of single/multi selection (M3, real OLE drag in CI) | P1 | M3 | DND-02 |
| FP-LIB-13 | Batch export/convert | Batch Export wizard | S-LIB-15 DOC | ◐ multi-select export | P2 | M7 | LIB-BATCH-01 |
| FP-LIB-14 | OCR / full-text search | Not documented | S-LIB-04 DOC | ✗ | P3 | M6 | AI-06 |
| FP-LIB-15 | Details (metadata view) | Right-click → Details | S-LIB-04 DOC | ◐ size/date shown on tile | P2 | M4 | LIB-06 |
| FP-LIB-16 | Thumbnail size | — | — | ✅ slider | — | done | BL-LIB-09 |
| FP-LIB-17 | Migration of Snagit `.snagx` | Format unpublished (ZIP claim non-official) | S-LIB-11 DOC, S-LIB-13 UNVERIFIED | ✗ | P3 | investigate | MIG-01 |
| FP-LIB-18 | Scale (10k+ captures) | n/a | — | ✅ background indexing (UI not blocked); benchmarks 100–25 000 with production durability — see BENCHMARKS.md | **P1** | **M3** | PERF-01…07 |

## 8. Documentation / templates

| ID | Capability | Snagit (Windows) | Src · Ev | SnapForge today | Pri | MS | Req |
|---|---|---|---|---|---|---|---|
| FP-DOC-01 | Combine in Template / Apply Template / Image from Template | 2025 templates | S-LIB-16 DOC | ✗ | P3 | M7 | DOC-01 |
| FP-DOC-02 | Step Capture (auto screenshot per click → guide) | 2025.0.0; Word/PowerPoint export | S-LIB-18/19 DOC | ✗ | P3 | M7 | DOC-02 |
| FP-DOC-03 | Video from Images | Narration per image | S-LIB-20 DOC | ✗ | — | V3 | VID-V3-05 |
| FP-DOC-04 | PDF export | Export only | S-LIB-28 DOC | ✗ | P2 | M7 | EXP-03 |

## 9. Video

| ID | Capability | Snagit (Windows) | Src · Ev | SnapForge today | Pri | MS | Req |
|---|---|---|---|---|---|---|---|
| FP-VID-01 | Record region/window/screen to MP4 | Video tab | S-LIB-21 DOC | ◐ V1 branch — under review | CI self-test | V1 | VID-01/02 |
| FP-VID-02 | Mic + system audio, webcam PiP | Yes | S-LIB-21 DOC | ◐ mic + system audio on V1 branch (devices Pending-PC); webcam ✗ | Mixer tests | V1/V3 | VID-03, VID-V3-01 |
| FP-VID-03 | Pause/resume (Shift+F9), stop (Shift+F10) | Yes | S-LIB-21, S-CAP-07 DOC | ◐ V1 branch — under review | CI pause-resume | V1 | VID-04, VID-06 |
| FP-VID-04 | Countdown before recording | UNVERIFIED | — | ◐ V1 branch — under review | settings test | V1 | VID-05 |
| FP-VID-05 | Quality up to 4K60 (VFR) | 2024.2.0 | S-LIB-27 DOC | ◐ 30/60 FPS CFR, Standard/High on V1 branch; 4K Pending-PC; VFR ✗ | CI 60fps | V1/V3 | VID-10, VID-V3-06 |
| FP-VID-06 | Cursor highlight / click animation | Yes | S-LIB-02 DOC | ✗ (cursor on/off only: VID-07) | — | V3 | VID-V3-03 |
| FP-VID-07 | Screen Draw while recording | Ctrl+Shift+D | S-LIB-24 DOC | ✗ | — | V3 | VID-V3-02 |
| FP-VID-08 | Trim/cut, save frame, combine clips | Yes | S-LIB-22/23 DOC | ✗ | — | V2/V3 | VID-V2-01/02, VID-V3-04 |
| FP-VID-09 | Animated GIF (fps, size, dithering, loop) | Yes | S-LIB-25 DOC | ✗ | — | V2 | VID-V2-03 |

## 10. Export / share

| ID | Capability | Snagit (Windows) | Src · Ev | SnapForge today | Pri | MS | Req |
|---|---|---|---|---|---|---|---|
| FP-EXP-01 | PNG, JPEG | Yes | S-LIB-28 DOC | ✅ (JPEG quality fixed 92) | — | done | BL-EXP-01 |
| FP-EXP-02 | BMP, GIF, TIFF, WebP | BMP/GIF/TIF; WebP since 2022.1.1 | S-LIB-28/30 DOC | ✗ | P2 | M4 | EXP-01 |
| FP-EXP-03 | ICO, PSD, TGA, WMF, CUR, EPS | Yes | S-LIB-28 DOC | ✗ | — | — | EXP-07 |
| FP-EXP-04 | HEIF | Mac only (Windows UNVERIFIED) | S-LIB-29 DOC | ✗ | — | investigate | EXP-02 |
| FP-EXP-05 | PDF | Export only | S-LIB-28 DOC | ✗ | P2 | M7 | EXP-03 |
| FP-EXP-06 | Native editable format | SNAGX (unpublished) | S-LIB-11 DOC | ✅ `.sfcap` documented — ⊕? plausible (no SNAGX specification found) | — | done | BL-EXP-02 |
| FP-EXP-07 | Clipboard | Yes | S-LIB-31 DOC | ✅ | — | done | BL-EXP-03 |
| FP-EXP-08 | Print | Printer destination, Ctrl+P | S-LIB-31, S-ED-22 DOC | ✗ | P2 | M4 | EXP-04 |
| FP-EXP-09 | Descriptive automatic filenames | From source context; can be turned off | S-LIB-02/31 DOC | ✅ drag-out names "title yyyy-MM-dd HHmmss", unique and Windows-safe (M3); ◐ export dialog still uses title | P1 | M3 | DND-05, EXP-06 |
| FP-EXP-10 | Office / email / browser interop | Share destinations | S-LIB-32/37 DOC | ◐ drag-out is the offline route into Office/mail/chat/browsers (M3); real-app compatibility = manual | P1 | M3 | DND-11 |
| FP-EXP-11 | Cloud destinations (Drive, Dropbox, Slack, Teams, YouTube…) | Many; vary by version | S-LIB-32/37/49 DOC | ✗ ≠ offline-first; only optional, opt-in later | — | owner decision | EXP-08 |

## 11. Settings

| ID | Capability | Snagit (Windows) | Src · Ev | SnapForge today | Pri | MS | Req |
|---|---|---|---|---|---|---|---|
| FP-SET-01 | Run at Windows start | General tab | S-CAP-06 DOC | ✅ launch with Windows option (M3) | P1 | M3 | SET-01 |
| FP-SET-02 | Theme Light/Dark/System | System added 2026.2.0 | S-CAP-06/10 DOC | ✅ | — | done | BL-SET-01 |
| FP-SET-03 | High contrast | VPAT: poor in Snagit | S-CAP-24 DOC | ◐ high-contrast palette from system colours (M3, unit-tested); live visual check = manual | P1 | M3 | NFR-A11Y-03 |
| FP-SET-04 | Hotkeys tab with Restore Defaults | Yes | S-CAP-07 DOC | ◐ hotkey recorder; no restore-defaults | P2 | M4 | SET-03 |
| FP-SET-05 | Update checks (Standard/Delayed) | Yes | S-CAP-06 DOC | ✗ ≠ no network; controlled updates later (M9) | — | M9 | ENT-07 |
| FP-SET-06 | Usage data | Send usage data / anonymous / none | S-CAP-06 DOC | ⊕ no telemetry at all — confirmed: Snagit usage-data choices (DOC) vs no network code in SnapForge (OBS) | — | done | BL-SEC-01 |
| FP-SET-07 | Reset to defaults | Advanced tab | S-CAP-06 DOC | ✗ | P2 | M4 | SET-04 |
| FP-SET-08 | Settings/preset export/import | Presets only; admin `/e` registry export | S-CAP-21/23 DOC | ✗ | P2 | M4 | SET-05 |
| FP-SET-09 | Drag-and-drop file type | Advanced tab | S-CAP-06, S-LIB-03 DOC | ✅ drag-and-drop file type and JPEG quality (M3) | P1 | M3 | DND-04 |
| FP-SET-10 | Keep editor running in background / close to tray | Advanced tab | S-CAP-06 DOC | ✅ close to notification area | — | done | BL-SET-02 |
| FP-SET-11 | Capture sounds | UNVERIFIED | — | ✗ | P3 | M4 | SET-06 |
| FP-SET-12 | Window position memory | UNVERIFIED | — | ✅ capture panel position remembered per monitor, clamped on-screen (M3) | P1 | M3 | CAP-UI-11 |

## 12. Security, privacy, enterprise, accessibility

| ID | Capability | Snagit (Windows) | Src · Ev | SnapForge today | Pri | MS | Req |
|---|---|---|---|---|---|---|---|
| FP-SEC-01 | Offline operation, no account | Subscription; internet check every 90 days; account not needed for MSI | S-LIB-39/42/44 DOC | ✅ ⊕ confirmed: Snagit needs an online licence check every 90 days (DOC); SnapForge has no network code (OBS grep) | — | done | BL-SEC-01 |
| FP-SEC-02 | Telemetry | Opt-out choices | S-CAP-06 DOC | ✅ ⊕ none — confirmed (DOC vs OBS) | — | done | BL-SEC-01 |
| FP-SEC-03 | Irreversible redaction guidance | Blur reversible; flatten to remove | S-ED-08/09 DOC | ✅ parity — opaque masks, flattened exports, explicit Sanitize, warnings (Snagit also documents solid redaction + flatten; exceeds claim withdrawn) | — | done | BL-PRIV-01 |
| FP-SEC-04 | Encryption at rest | Not documented | S-LIB (none) | ✗ | P3 | M9 | ENT-05 |
| FP-SEC-05 | Metadata stripping on export | n/a | — | ✅ JPEG metadata test (CI) | — | done | BL-PRIV-02 |
| FP-SEC-06 | Supply chain (pinned actions, lock files, signatures, checksums) | n/a | — | ✅ CI audit (docs/SECURITY.md) | — | done | BL-SEC-02 |
| FP-SEC-07 | SBOM | n/a | — | ✗ | P2 | M9 | ENT-03 |
| FP-SEC-08 | Code signing | Signed vendor binaries (INFER) | — | ✗ owner decision D-3 | — | M9 | ENT-02 |
| FP-SEC-09 | MSI/MSIX, silent install/uninstall, transforms, Intune/SCCM | MSI + Deployment Tool; Intune Win32 | S-LIB-34/37/40 DOC | ✗ portable only | — | M9 | ENT-01/04 |
| FP-SEC-10 | Standard-user operation | INFER per-machine MSI | — | ✅ portable, per-user data, no admin (CI runs as admin; UNVERIFIED as standard user) | P1 (verify) | M3 | NFR-SEC-04 |
| FP-SEC-11 | Accessibility conformance | 2025 VPAT: "Does Not Support" for use without vision | S-CAP-24 DOC | ◐ automation names, keyboard access, high-contrast palette (M3); ✗ screen-reader pass (manual) | P1 | M3+ | NFR-A11Y-01…03 |
| FP-SEC-12 | Disable AI features (admin) | Article exists | S-LIB-46 DOC | n/a — SnapForge has no AI features yet (AI-07 when M6 adds them) | — | M6 | AI-07 |

## Summary counts (baseline 3018841, after Gate A corrections)

Computed mechanically from the first status symbol in each row's "SnapForge today" cell (⊕ and ⊕? counted as
done; "n/a" counted separately). The earlier table summed to 182 because FP-SEC-12 ("n/a") was silently excluded;
it now has its own column, so the columns add up to the 183 rows.

| Area | Rows | ✅/⊕ done | ◐ partial | ✗ missing | ≠ different | n/a |
|---|---|---|---|---|---|---|
| Capture | 33 | 12 | 5 | 16 | 0 | 0 |
| Desktop capture widget | 7 | 0 | 0 | 7 | 0 | 0 |
| Editor tools | 31 | 5 | 5 | 20 | 1 | 0 |
| Object properties and management | 25 | 3 | 5 | 16 | 1 | 0 |
| Effects | 10 | 0 | 0 | 10 | 0 | 0 |
| Recent Captures tray | 11 | 4 | 2 | 5 | 0 | 0 |
| Library | 18 | 9 | 4 | 4 | 1 | 0 |
| Documentation / templates | 4 | 0 | 0 | 4 | 0 | 0 |
| Video | 9 | 0 | 0 | 9 | 0 | 0 |
| Export / share | 11 | 3 | 1 | 7 | 0 | 0 |
| Settings | 12 | 3 | 2 | 7 | 0 | 0 |
| Security, privacy, enterprise, accessibility | 12 | 6 | 1 | 4 | 0 | 1 |
| **Total** | **183** | **45** | **25** | **109** | **3** | **1** |

Of the rows counted as done, the "exceeds" claims were revalidated (see SNAGIT-FORENSIC-AUDIT.md §3a): **4 confirmed**
(favorites on Windows, no telemetry ×2, offline/no licence check), **5 plausible only** (rest on the absence of Snagit
documentation), **6 withdrawn** (parity or different, not superior).

Headline: SnapForge's **offline and no-telemetry posture is confirmed to exceed** the reference; its Library and
storage features are **at least at parity**, with some advantages plausible but unproven. **Capture breadth, editor
depth, effects, widget, drag-out and video** are the gaps. Rows marked "—" in Pri are kept in the roadmap backlog
(not dropped) — see DEVELOPMENT-ROADMAP.md coverage appendix.

## Summary counts after milestone 3 (Gate B, same method)

The "SnapForge today" cells of 28 rows were updated for milestone 3; the baseline table above is kept for comparison. "Done" for a row whose behaviour still needs a real-PC check is marked ◐ where the open check is material (multi-monitor, real-app drag targets, high contrast, screen readers).

| Area | Rows | ✅/⊕ done | ◐ partial | ✗ missing | ≠ different | n/a |
|---|---|---|---|---|---|---|
| Capture | 33 | 14 | 4 | 15 | 0 | 0 |
| Desktop capture widget | 7 | 6 | 1 | 0 | 0 | 0 |
| Editor tools | 31 | 5 | 5 | 20 | 1 | 0 |
| Object properties and management | 25 | 3 | 5 | 16 | 1 | 0 |
| Effects | 10 | 0 | 0 | 10 | 0 | 0 |
| Recent Captures tray | 11 | 8 | 1 | 2 | 0 | 0 |
| Library | 18 | 12 | 3 | 2 | 1 | 0 |
| Documentation / templates | 4 | 0 | 0 | 4 | 0 | 0 |
| Video | 9 | 0 | 0 | 9 | 0 | 0 |
| Export / share | 11 | 4 | 1 | 6 | 0 | 0 |
| Settings | 12 | 6 | 2 | 4 | 0 | 0 |
| Security, privacy, enterprise, accessibility | 12 | 6 | 1 | 4 | 0 | 1 |
| **Total** | **183** | **64** | **23** | **92** | **3** | **1** |
