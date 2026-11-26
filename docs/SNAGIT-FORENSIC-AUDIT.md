# Gate A forensic audit — SnapForge vs Snagit for Windows

Date: 2026-11-25 · Auditor: Claude Code (cloud session) · Baseline: `3018841` on `claude/pensive-euler-ulw6m4`
Status: **Complete for Gate A; awaiting owner approval. No development has started.**

Companion documents: [SOURCE-REGISTRY.md](SOURCE-REGISTRY.md) (sources, evidence classes) ·
[FEATURE-PARITY-MATRIX.md](FEATURE-PARITY-MATRIX.md) (183 rows) · [PRODUCT-REQUIREMENTS.md](PRODUCT-REQUIREMENTS.md) ·
[UI-SPECIFICATION.md](UI-SPECIFICATION.md) · [DEVELOPMENT-ROADMAP.md](DEVELOPMENT-ROADMAP.md) ·
[ACCEPTANCE-TESTING.md](ACCEPTANCE-TESTING.md) Part 13 · research notes in [research/](research/).

## 1. Scope, method and limits

* **Inputs reviewed:** the owner's handoff package (prompt, Snagit capture-panel reference image, milestone-2
  handoff marked "not independently verified"); all repository docs, code and tests at the baseline; PR #1 state and
  check runs; the full Windows CI log of run 45704874375.
* **Independent checks performed (OBS/CI):** branch head re-fetched and compared; clean Linux build and core test
  run; Windows CI log parsed for build warnings, test totals, vulnerability scan and checksums; code inventory by
  grep and reading (capture modes, tools, annotation properties, menus, context menus, keyboard handling, drag/drop,
  network APIs, accessibility attributes, autosave, start-up indexing, test seeding); review of all eight
  committed screenshots.
* **External research:** three parallel research passes covering capture/widget/settings, editor/effects and
  library/video/export/enterprise; 143 source records.
* **Limits (read before relying on any Snagit statement):**
  1. TechSmith and Microsoft Learn hosts are blocked from this environment. **Every Snagit fact is a search-engine
     extract (DOC) or weaker. Nothing was read on the live page.**
  2. **No hands-on Snagit inspection** was possible. The reference image is the only direct observation of Snagit.
  3. **No hands-on Windows testing** of SnapForge was performed. Windows evidence is CI on a Windows Server runner
     with a single 1024×768 virtual display, running as an administrator.
  4. The artifact download host is blocked; the artifact was not re-downloaded. Its checksums come from the CI log.

## 2. Verification of the prior (milestone 2) handoff

| # | Claim in handoff | Independent result | Class | Verdict |
|---|---|---|---|---|
| V-01 | Branch head is `301884176813935786292f1b28136d6281799868` | `git fetch` → FETCH_HEAD equals it; working tree clean | OBS | ✔ Confirmed |
| V-02 | PR #1 open, not merged | 6 check runs, all success; not merged | OBS | ✔ |
| V-03 | Windows build 0 warnings / 0 errors | CI log: `0 Warning(s)` `0 Error(s)` | CI | ✔ |
| V-04 | 135/135 core tests, 33/33 app tests | CI log: `Total tests: 135 Passed: 135`, `Total tests: 33 Passed: 33`; Linux re-run 135/135 | CI + OBS | ✔ |
| V-05 | No vulnerable packages | CI log: all four projects "no vulnerable packages" | CI | ✔ |
| V-06 | `SnapForge.exe` SHA-256 `4f0bf414…f9d1`; zip digest `5f0590c3…9efa` | Both present in CI log | CI | ✔ (artifact not re-downloaded) |
| V-07 | Portable build runs without .NET | Check run "Portable build runs without an installed .NET" success | CI | ✔ (CI only) |
| V-08 | "Library … tested to 1 500 captures" | Core index test seeds 1 500 captures **with `SkipDiskFlush` during seeding** and measures re-open (<15 s threshold); the **UI** library test uses **600** captures, also seeded with the bypass. Production save latency at scale was never measured | OBS | ⚠ **Overstated** (DEF-01) |
| V-09 | Screenshots of all three workspaces | Eight images exist; they are **off-screen renders** of real windows with seeded demo data produced by `--screenshots` on CI, not captures of a live desktop session | OBS | ⚠ Accurate but should be labelled renders (DEF-09) |
| V-10 | "No manual Windows testing performed" | Consistent with all evidence | — | ✔ Honest |
| V-11 | Resize, blur/pixelate, callouts not shown as working | Not in toolbar/menus | OBS | ✔ |
| V-12 | Built-in presets are truthful | Preset "Menu capture (3 s delay, cursor)" is a Region capture with delay; real menu capture does not exist | OBS | ⚠ Misleading label (DEF-03) |

## 3. Implementation findings (defects, risks, gaps)

Severity: **High** = could mislead the owner or lose data · **Medium** = user-visible gap or untested risk · **Low**.

| ID | Finding | Evidence | Severity | Proposed fix (needs approval) |
|---|---|---|---|---|
| DEF-01 | Scale claim overstated; benchmarks bypass production durability during seeding; no production-path scale numbers | `CaptureLibraryTests.cs:398-422`, `LibraryWorkflowTests.cs:239-251`, `AtomicFile.cs:18,35` | High (evidence quality) | PERF-01/02 benchmark set |
| DEF-02 | Library opens **synchronously on the UI thread** at start-up and reads every capture's manifest; no persisted index — start-up time grows linearly | `AppServices.cs:50` → `CaptureLibrary.Open` → `Reindex` (`CaptureLibrary.cs:85-110`) | Medium (High at 10 000+) | Measure first (PERF-01); then background index or cached index file (PERF-03) |
| DEF-03 | Built-in preset name "Menu capture (3 s delay, cursor)" implies a menu-capture mode that does not exist | `AppSettings.cs` `CapturePreset.Defaults`; screenshot `capture-window-dark.jpg` (baseline 3018841; replaced in M3) | Medium (G-02 "no fake features") | CAP-UI-14 rename |
| DEF-04 | Capture window layout is a tall vertical panel; owner's reference is a compact horizontal panel | `capture-window-dark.jpg` (baseline) vs reference image | Medium (requirement gap) | CAP-UI-01…13 |
| DEF-05 | Library sidebar storage path wraps/clips | `library-light.jpg` | Low | Ellipsis + tooltip |
| DEF-06 | No context menu on the editor canvas or on annotations; no multi-select, resize handles, nudge, object clipboard | `MainWindow.xaml` context menus only on tray, quick styles; `AnnotationCanvas.cs` move-only | Medium | M4 (ED-OBJ-*, ED-UI-03) |
| DEF-07 | Accessibility not verified with a screen reader; no high-contrast handling verified | 58 `AutomationProperties` uses; no HC resources | Medium | NFR-A11Y-02/03 manual pass |
| DEF-08 | Default Print Screen hotkey not verified on real Windows 11 with the Snipping Tool keyboard setting on | Conflict detection relies on `RegisterHotKey` failure (`ShellIntegration.cs:14`); CI runner is Windows Server | Medium | CAP-HK-01 manual test |
| DEF-09 | Screenshot evidence is rendered off-screen at 1024×768; not a human-observed session | `ScreenshotTour.cs` | Low | Gate C: real-window captures + label renders |
| DEF-10 | No drag/drop and no external image import anywhere | grep: no `DragDrop`/`AllowDrop`/`FileDrop` in `src/`; only `.sfcap` import (`MainWindow.xaml.cs:1230`) | Medium (owner priority) | DND-01…12 |
| DEF-11 | Runs only tested as administrator (CI); standard-user operation unverified | CI runner context | Medium | NFR-SEC-04 manual |
| DEF-12 | Autosave debounce 1.2 s — a crash inside the window loses the last edits; undo history not persisted | `MainWindow.xaml.cs:68`; documented in AUDIT.md | Low (documented) | Keep; document |

Positive findings (verified): no network APIs in `src/` (no `HttpClient`, `WebRequest`, sockets, URLs) — offline
claim holds; crash-safe write path with tests for interrupted saves and sanitize; exports flattened with metadata
checks; supply-chain controls present in CI; per-monitor V2 DPI manifest present.

## 3a. Revalidation of "SnapForge exceeds Snagit" claims (Gate A correction, 2026-11-25)

A claim is **Confirmed** only when both sides are evidenced (Snagit: DOC extract stating the behaviour; SnapForge:
OBS/CI). **Plausible** means the Snagit side rests on *absence* of documentation in search extracts, which is not
proof. **Withdrawn** means the evidence shows parity or merely a different choice.

| Row | Claim | Snagit evidence | SnapForge evidence | Verdict |
|---|---|---|---|---|
| FP-SEC-01 | Works fully offline, no account or licence check | DOC S-LIB-39/42: online licence check at least every 90 days | OBS: no network API in `src/` | **Confirmed** |
| FP-SEC-02, FP-SET-06 | No telemetry | DOC S-CAP-06: usage-data choices | OBS: no network code | **Confirmed** |
| FP-LIB-04 | Favorites on Windows | DOC S-LIB-04: favorites "Mac only" | CI: `Views_search_and_sort` | **Confirmed** (DOC is an extract; re-verify under RES-01) |
| FP-LIB-01, FP-EXP-06 | Editable format is documented | No SNAGX specification found; non-official ZIP claim (S-LIB-13) | OBS: docs/CAPTURE-FORMAT.md | **Plausible** |
| FP-LIB-05 | Folders | No Snagit folder feature found | CI tests | **Plausible** |
| FP-LIB-07 | Trash with restore | No in-app trash found; Recycle Bin behaviour unknown | CI: `Trash_restore_and_permanent_delete`; permanent delete is best effort on SSDs | **Plausible** |
| FP-LIB-10 | Backup/restore | Current backup appears to be a folder copy (INFER) | CI: backup tests | **Plausible** |
| FP-LIB-08 | Retention policy | DOC S-LIB-14: captures kept until the user deletes them | CI: `Usage_and_retention` | **Withdrawn** → different by design |
| FP-CAP-04 | Display + Full screen | Snagit monitor selection not researched | CI | **Withdrawn** → parity |
| FP-CAP-22 | Capture without history | DOC S-LIB-12: autosave can be turned off | CI | **Withdrawn** → parity |
| FP-ED-11, FP-SEC-03 | Safer redaction | DOC S-ED-08/09: solid shapes + flatten recommended; exports flattened | CI redaction tests | **Withdrawn** → parity |
| FP-OBJ-18 | Sanitize better than flatten | DOC S-ED-22: Flatten All is permanent | CI | **Withdrawn** → different |

Corrected headline: only SnapForge's **offline / no-telemetry posture** (and favorites on Windows) is confirmed to
exceed the reference. Library and storage are **at parity**; some advantages are plausible but unproven.

## 3b. Gate A correction log (owner review, 2026-11-25)

| Owner item | Correction |
|---|---|
| 183 rows vs category counts totalling 182 | FP-SEC-12 ("n/a") had been excluded from the count. Summary now has an n/a column; columns total 183 |
| Unique IDs and traceable evidence | Requirements regenerated from one structured list: 223 requirements, duplicate check passes, every ranged ID (e.g. "ED-TOOL-01…11") expanded into individual rows, each with Source, Ev and Verification columns; every matrix row's references resolve |
| Documented vs assumed vs verified | Ev column (OWNER/DOC/OBS/INFER/UNVERIFIED) on every requirement; Verification column names the test/CI job or says "gap"/"manual" |
| Revalidate "exceeds" claims | §3a: 4 confirmed, 5 plausible, 6 withdrawn |
| Preserve all capabilities in roadmap | Every one of the 183 matrix rows is mapped to a requirement and milestone; "not planned" rows kept as **Backlog** requirements (CAP-LEG-01/02, EXP-07/08) — DEVELOPMENT-ROADMAP.md coverage appendix |
| Owner decisions | Widget enabled and topmost by default (WID-01/07); Windows 11 first (PD-03); signing before release (PD-04) |

## 4. Menu-by-menu and surface-by-surface gap analysis

### 4.1 Capture panel

| Reference (image, OBS) | SnapForge today (OBS) | Gap |
|---|---|---|
| Menus File / Edit / View / Help | None (links at bottom: Open Editor, Open Library, Settings) | Add menus (contents UNVERIFIED in Snagit: known items File → Capture Preferences, Import Presets, Export All Presets [S-CAP-06/21]) |
| Mode rail All-in-One / Image / Video | None (image only) | Add rail; disable unavailable modes |
| Selection ▾ / Effects ▾ / Share ▾ | Selection tiles; no Effects/Share | Dropdowns; Effects disabled; Share limited to working destinations |
| Preview in Editor, Copy to Clipboard, Cursor, Time Delay › | Same four + Save to Library; delay as fixed segments | Rearrange; delay picker |
| Circular Capture + "Print Screen" | Rectangular Capture + "Global hotkey: PrintScreen" | Shape/placement |
| Reset ↺ | None | Add |
| Presets ⚙, + ▾ | Inline preset list, "+ Save current as preset" | Flyout |
| Open … Editor | "Open Editor" link | Keep |

### 4.2 Editor menus (SnapForge today vs documented Snagit commands)

| Menu | SnapForge items (OBS) | Documented Snagit equivalents missing in SnapForge (DOC unless noted) |
|---|---|---|
| File | New capture (Ctrl+N), Capture window…, Save to Library, Export PNG/JPEG (Ctrl+S), Copy image, Export/Import `.sfcap`, Sanitize, Close capture (Ctrl+W), Settings (Ctrl+,), Exit | Print (Ctrl+P) [S-ED-22]; Save As other formats [S-LIB-28]; New from Clipboard [S-ED-24]; open image file (UNVERIFIED wording) |
| Edit | Undo, Redo, Delete annotation, Copy image | Cut/Copy/Paste objects, Copy All, Select All, Group/Ungroup, Bring Forward/Send Backward, Flatten [S-ED-22]; Editor Preferences [S-CAP-06] |
| Image | Crop…, Redact…, Sanitize capture… | Resize Image, Rotate, Flip, Canvas colour, Effects [S-ED-35, S-ED-39, S-ED-21] |
| View | Editor, Library, Capture window, Recent Captures, Quick Styles & Properties, Zoom in/out/fit/actual, Theme | Thumbnail size (tray) [S-LIB-01] |
| Capture | Region (Ctrl+1), Window (Ctrl+2), Display (Ctrl+3), Full screen (Ctrl+4), Presets, Capture window… | ≠ Snagit uses Ctrl+1–9 for toolbar positions [S-ED-22] — conflict to resolve if ED-UI-02 is approved (Q-08) |
| Help | Keyboard shortcuts (F1), Where are my captures? (privacy), About | — |

### 4.3 Context menus

| Surface | SnapForge (OBS) | Snagit (DOC) | Gap |
|---|---|---|---|
| Tray tile | Open, Pin/Unpin, Close (keep in Library), Close all unpinned, Show in Library, Delete… | Delete, Thumbnail Size, Combine…, (drag-out) [S-LIB-01/16] | Thumbnail size; drag-out |
| Library item | Open, Rename, Duplicate, Favorite, Tags, Move to folder, Export, Export `.sfcap`, Sanitize, Trash/Restore/Delete permanently | Details, Delete, Combine in Template [S-LIB-04/16] | Details view |
| Canvas / object | **None** | Flatten, Flatten All, Grab Text, Edit Text, Edit Value (step) [S-ED-08/11/12] | Canvas menu (M4) |
| Quick Style | Apply, Favorite, Delete style | Delete Quick Style, Favorite [S-ED-06] | ✓ parity |
| Toolbar | None | Icons+text vs icons only [S-ED-24] | M4 |
| Notification icon | Capture, Capture window, Editor, Library, Settings, Exit | Similar (UNVERIFIED detail) | ✓ |
| Capture preset | Capture with preset, Use settings, Set hotkey, Rename, Delete | Export preset [S-CAP-09] | Preset export |

### 4.4 Keyboard shortcuts

| Action | SnapForge (OBS) | Snagit (DOC S-CAP-07, S-ED-22) | Note |
|---|---|---|---|
| Global capture | Print Screen | Print Screen | Same |
| Capture: Enter/Esc/arrows/M | Esc only | Enter, Shift+Enter, Esc, arrows 1 px, M magnifier | M5 |
| Tool select | V A L R E H T N X C | Ctrl+1–9 by position | Different by design (Q-08) |
| Undo/Redo | Ctrl+Z, Ctrl+Shift+Z, Ctrl+Y | Ctrl+Z, Ctrl+Y | Same |
| Copy flattened | Ctrl+C | Ctrl+Shift+C (Copy All); Ctrl+C copies object | **Conflict** if object clipboard is added (Q-09) |
| Save/Export | Ctrl+S (export dialog) | Ctrl+S | Same key, different semantics (SnapForge autosaves) |
| Zoom | Ctrl+= / Ctrl+− / Ctrl+0 fit / Ctrl+Alt+0 actual | Ctrl+Plus / Minus / Ctrl+0 actual | Ctrl+0 meaning differs (Q-09) |
| Select all / group / layer / flatten | — | Ctrl+A, Ctrl+Shift+G/U/F/B, Ctrl+T | M4 |
| Print | — | Ctrl+P | M4 |
| Workspaces | Ctrl+E Editor, Ctrl+L Library, Ctrl+N new capture, Ctrl+W close, Ctrl+, settings, F1 help | Ctrl+N new image | SnapForge extra |

### 4.5 Preferences

| Snagit (DOC S-CAP-06) | SnapForge Settings (OBS) | Gap |
|---|---|---|
| Run at Windows start | — | SET-01 (M3) |
| Theme Light/Dark/System | ✓ | — |
| Automatic update check | — (no network) | ≠ by design; M9 controlled updates |
| Capture Window vs Widget | — | WID-01 (M3) |
| Hide during capture (option) | Always on | ≠ fine |
| Show magnifier | — | M5 |
| Hotkeys tab + Restore Defaults | Hotkey recorder, conflict status | Restore defaults (M4) |
| Data & Privacy (usage data) | No telemetry | ⊕ |
| Advanced: keep running, delete cropped content, **drag-and-drop file type**, Reset to defaults | Close to notification area; — ; — ; — | DND-04 (M3); reset (M4) |
| Library: location, autosave on/off, import backup | Location/move, usage, retention, backup/restore, sanitize-all | ⊕ |

### 4.6 Accessibility

Snagit's own 2025 VPAT (S-CAP-24, extract) rates use without vision and assistive-technology exposure as **"Does Not
Support"** and reports a keyboard trap between toolbar and properties (S-ED-42). SnapForge has keyboard shortcuts for
every tool, access keys on menus and automation names on icon buttons (OBS), but **no screen-reader or
high-contrast verification** (DEF-07). Opportunity: exceed the reference by making the new panel and widget fully
keyboard- and Narrator-operable (NFR-A11Y-01…03).

## 5. Reference facts that change design decisions

1. **Drag-out is a documented Snagit behaviour with a user-selectable file type** (2024.1.2, Advanced tab)
   [S-LIB-03, S-LIB-02]. The Windows data format Snagit uses is undocumented; Microsoft's guidance is `CF_HDROP` for
   existing files [S-LIB-55]. → DND-01/04 adopt temp-file + `CF_HDROP`, PNG default, JPEG option.
2. **The widget docks on top or side edges and opens on hover** [S-CAP-06]. Auto-hide/topmost/multi-monitor are
   undocumented today → SnapForge defines them itself (WID-06…08).
3. **TechSmith states blur and pixelate can be reversed** [S-ED-08/09]. → PRIV-DND-05: never label blur as redaction.
4. **Snagit Library: Favorites are Mac-only; no trash or retention found** [S-LIB-04/14]. SnapForge already exceeds
   these; keep.
5. **Snagit's cursor is an editable object** [S-CAP-03]; SnapForge burns it into pixels → ED-OBJ-20 (M4).
6. **Snagit 2026 is subscription-only with a 90-day online licence check and optional usage data** [S-LIB-39/44,
   S-CAP-06]. SnapForge's offline, account-free posture is a differentiator (G-03).
7. One extract says Snagit 2026.0.0+ requires Windows 11 24H2+ (S-ED-01 family, UNVERIFIED). SnapForge's
   position is in docs/COMPATIBILITY.md (Windows 11 supported by .NET 10; Windows 10 22H2 Home/Pro not listed by
   .NET 10 — open owner decision Q-12); no change proposed.

## 6. Risks for milestone 3

| Risk | Impact | Mitigation |
|---|---|---|
| Drag-drop behaviour differs per target app (Office, Teams, Outlook, browsers) and cannot be exercised on CI | Owner sees failures CI did not catch | DND-11 evidence table; owner manual test on a real PC with Office |
| Non-activating topmost widget interacts badly with full-screen apps / games / UAC prompts | Annoyance, focus theft | WID-07 option off by default for topmost? (Q-05); manual checks |
| Production-durability benchmarks at 25 000 captures exceed CI time limits | Missing data point | Run up to the largest size that fits (report skipped sizes); optional local run by owner |
| Settings schema additions break older settings files | Data loss | G-01 additive schema + upgrade tests |
| Search-extract research is wrong in details | Wrong parity decisions | Q-01 re-verification before M4 |

## 7. Unresolved questions and owner decisions

| ID | Question | Why it matters | Recommendation |
|---|---|---|---|
| Q-01 | Allow `www.techsmith.com`, `support.techsmith.com`, `assets.techsmith.com`, `learn.microsoft.com` in this environment's network policy (or supply PDFs/screenshots) so DOC items can be re-verified? | All Snagit facts are extracts | Yes, before M4 planning; not blocking M3 |
| Q-02 | Approve the M3 scope in DEVELOPMENT-ROADMAP.md (panel, widget, drag-out, import, privacy, benchmarks)? | Gate B start | Approve as written |
| Q-03 | Video and All-in-One shown **disabled** in the rail (labelled "Not available yet"), or hidden until built? | G-02 | Disabled + labelled, matching the reference layout |
| Q-04 | Widget enabled by default for new installs? | First-run experience | **Decided by owner: enabled** |
| Q-05 | Widget topmost by default? | Focus/full-screen apps | **Decided by owner: on** |
| Q-06 | Drag-out default PNG; filename pattern `<title> <yyyy-MM-dd HHmmss>.png`? | DND-04/05 | Yes |
| Q-07 | Any cloud share destinations ever (opt-in), or strictly none? | G-03 | Strictly none until M9 decision |
| Q-08 | Keep single-letter tool keys or switch to Ctrl+1–9 by toolbar position (Snagit style)? | Muscle memory vs current tests | Keep letters; add Ctrl+1–9 as aliases in M4 if Capture shortcuts move |
| Q-09 | Ctrl+C semantics when object clipboard arrives (copy object vs copy flattened image) | Shortcut conflict | Snagit-style: Ctrl+C object, Ctrl+Shift+C flattened |
| Q-10 | Who performs manual acceptance on a real Windows 10/11 PC with Office, Teams, a second monitor at different scaling, and a standard-user account? | DND-11, NFR-DPI-1, NFR-SEC-04 | Owner, using ACCEPTANCE-TESTING Part 13 |
| Q-11 | Should the owner's Snagit reference image be committed? | Contains TechSmith branding | **No**; keep outside the repo (not committed) |
| Q-12 | Carried over: product name, copyright line, code signing, Windows 10 edition support | Release readiness | Unchanged; decide before M9 |

## 8. Recommendation

Approve Gate A and authorise Gate B with the M3 scope defined in PRODUCT-REQUIREMENTS §2 (CAP-UI, CAP-HK, WID, DND,
PRIV-DND, PERF, SET-01, NFR items, DEF-03/05 fixes), answering Q-02…Q-06 and Q-10. Everything in §3 of the
requirements remains _Proposed_ for later milestones and is **not** authorised by approving M3.

## 9. Milestone 3 (Gate B) — status of the audit findings

| ID | Status after Gate B | Evidence |
|---|---|---|
| DEF-01 | **Fixed**: benchmarks use production durability end to end, 100–25 000 captures; the test-only flush bypass is not used | docs/BENCHMARKS.md (CI) |
| DEF-02 | **Fixed**: library indexed in the background; window shown in ≈1 s at every size | BENCHMARKS.md; `Library_opens_instantly_and_indexes_in_the_background_without_freezing_the_ui` |
| DEF-03 | **Fixed**: preset renamed "Region with 3 s delay and pointer" (migrated only if unchanged) | `Old_settings_files_get_new_defaults_and_the_misleading_preset_is_renamed_only_if_unchanged` |
| DEF-04 | **Fixed**: compact horizontal capture panel | screenshots capture-panel-*; panel tests |
| DEF-05 | **Fixed**: storage path trimmed with tooltip | `Library_storage_path_is_trimmed_with_full_path_in_tooltip` |
| DEF-06 | Open (M4 editor scope) | — |
| DEF-07 | **Partly**: high-contrast palette from system colours (tested); screen-reader pass still manual | `High_contrast_palette_covers_every_theme_colour_from_system_colours` |
| DEF-08 | **Partly**: conflict explained in the panel with the Windows 11 setting name; real Windows 11 check manual (M3-P8) | `Capture_panel_remembers_its_position_and_explains_print_screen_conflicts` |
| DEF-09 | **Fixed**: screenshots are real screen captures (CI desktop 1024×768) | docs/screenshots/README.md |
| DEF-10 | **Fixed**: drag-out (tray, Library, multi-select) and PNG/JPEG/BMP import | real-input OLE drag tests; import test |
| DEF-11 | Open: standard-user run is a manual check (CI runs as administrator); no admin-only API is used | — |
| DEF-12 | Unchanged (documented) | — |

New findings during Gate B:

| ID | Finding | Status |
|---|---|---|
| KL-PERF-1 | Opening the Library grid or searching blocks the UI thread for up to ≈260 ms at 25 000 captures (grid rebuild on the UI thread) | Known limitation; proposal: incremental grid / persisted index (later milestone, needs approval) |
| KL-DND-1 | During a drag the UI thread is inside the modal OLE drag loop; SnapForge's own timers (autosave) pause until the drop | By design of Windows drag and drop; autosave resumes immediately after |
| KL-WID-1 | The widget cannot take keyboard focus (non-activating by design); keyboard users use the notification-area menu or the panel | Documented in SECURITY/acceptance |
| KL-UI-1 | Share dropdown is disabled rather than offering Library/Clipboard (those stay switches) — deviation from the proposed CAP-UI-05, documented in UI-SPECIFICATION | Owner may decide otherwise |
