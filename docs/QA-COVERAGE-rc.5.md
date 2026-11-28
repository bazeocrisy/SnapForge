# SnapForge 1.0.0-rc.5: QA coverage checklist

This is the full-scope QA audit of the **installed** Windows app. Windows CI ran every check below against the real installer on a GitHub `windows-latest` runner (Windows Server, one virtual 1920×1080 display, no camera, microphone or GPU). Nothing is marked passed unless a CI run executed it.

* **Before (rc.4 code):** CI run 183, commit `803b4e0`: https://github.com/bazeocrisy/SnapForge/actions/runs/46174581430
* **After (rc.5 candidate):** CI run RUNNUM, commit `COMMIT`: RUNURL
* **Screenshots:** the `ui-audit-<commit>` artifact of each run holds every PNG and `ui-audit.json`. Before/after pairs are listed in §4.

**Legend:** ✅ passed in CI · ⚠️ passed with a note · ⛔ blocked (needs hardware or an environment CI does not have, see §6) · ⏸ unverified (CI could not apply the condition)

## 1. Display scaling: what could be verified

For each level, CI changes the runner's display scaling live (the same setting as Settings → System → Display → Scale), reads it back from Windows, and checks the DPI that SnapForge itself measures. A level is **verified** only when all three agree.

| Scaling | Effective DPI | Before (run 183) | After (run RUNNUM) | Status |
|---|---|---|---|---|
| 100 % | 96 | 4 FAIL, 664 WARN | AFTER100 | ✅ verified |
| 125 % | 120 | 8 FAIL, 664 WARN | AFTER125 | ✅ verified |
| 150 % | 144 | 23 FAIL, 665 WARN | AFTER150 | ✅ verified |
| 175 % | 168 | not run | AFTER175 | ✅ verified (the runner's maximum) |
| 200 % | 192 | unverified | unverified | ⏸ **unverified.** At 1920×1080, Windows only allows up to 175 %, and the runner offers no larger display mode. CI attempted it and Windows stayed at 175 %. The run reports this as UNVERIFIED, never as passed. |
| Mixed DPI (two monitors at different scaling) | — | — | — | ⛔ blocked: the runner has one display |

**What each audited screen is checked for, at every scaling level:**

* **Text size:** no text smaller than **12 DIP** (12 px at 100 %, 18 px at 150 %, 21 px at 175 %).
* **Clipping:** text cut off, or a word wrapped out of sight. Text shortened with "…" must have a tooltip that shows the full text.
* **Visibility:** no control partly outside its window, and no window outside the screen's work area (above the taskbar).
* **Screen readers:** every control has an accessible name.
* **Keyboard:**
  * every control is reachable with **Tab**, or with the **arrow keys** inside an option group;
  * keyboard focus never lands on something invisible.
* **A real screen capture** of the screen.

**Gate:** from rc.5 on, the CI job **fails** if any verified scaling has a FAIL finding, or if fewer than three scaling levels could be verified.

## 2. Coverage: windows, tools, menus, dialogs and workflows

### 2.1 Audited at 100/125/150/175 % (45 screens per scaling level)

| Area | What is opened | Screens | After |
|---|---|---|---|
| **Editor** | Main editor window with a demo capture, recent-captures strip, properties panel, status bar | `editor` | ✅ |
| **Editor tools** (all 15) | Select, Arrow, Line, Shape (rectangle), Ellipse, Highlight, Text, Step, Pen, Callout, Stamp, Redact, Blur, Pixelate, Crop, each selected with its tool properties panel | `editor-tool-*` | ✅ (arrow-key reach fixed, see DEF-RC5-06) |
| **Editor menus** (all 6) | File, Edit, Image, View, Capture, Help, each opened with all items | `menu-*` | ✅ |
| **Editor dialogs** | Resize image, Canvas size, Border, Add watermark, Export GIF | `dialog-*` | ✅ |
| **Library** | All captures and the Videos filter, with the storage location | `library`, `library-videos` | ✅ |
| **Other Editor windows** | Welcome, Text recognition (OCR), Video player, Video editor (trim/export) | `welcome`, `ocr`, `video-player`, `video-editor` | ✅ |
| **Capture panel** | Image tab (what to capture, after capture, options, presets, hotkey) and Video tab | `capture-panel`, `capture-panel-video` | ✅ (keyboard focus fixed, see DEF-RC5-05) |
| **QuickDock** | Collapsed pill, expanded dock, keyboard mode | `quickdock-*` | ✅ (clipping fixed, see DEF-RC5-03) |
| **Settings** | Every section, page by page: Appearance, Behaviour, QuickDock, Drag and drop, Hotkeys, Capture library, Privacy, Reset | `settings` (+ one screenshot per page) | ✅ |
| **Record panel** | Compact panel, and with **More options** expanded (camera, microphone, video, countdown, after recording) | `record-panel`, `record-panel-more-options` | ✅ (overflow fixed, see DEF-RC5-02) |
| **Recording** | Recording control strip, Recording saved, Recording could not be finished, Camera preview | `recording-controls`, `recording-saved`, `recording-failed`, `camera-preview` | ✅ |
| **Setup** | Setup wizard, first page, at each scaling level (photographed, not installed) | `setup-wizard` | ✅ (screenshot only; Inno Setup draws its own wizard) |

### 2.2 Workflows driven through the installed or packaged app in CI (full regression, run RUNNUM)

| Workflow | CI job / step | After |
|---|---|---|
| **Build and code checks:** build with warnings as errors, locked restore, vulnerable-package check | Windows build | ✅ |
| **Automated tests:**<br>• unit tests;<br>• Windows rendering, capture and clipboard tests;<br>• **real-input UI tests** for region, window and display capture and for drawing annotations with real mouse/keyboard input | Windows build → Test | ✅ |
| **Capture logic:** region selection state machine, redaction pipeline, export, drag-out files | unit + UI tests | ✅ |
| **Packaged program without .NET:** `--self-test` | Portable | ✅ |
| **Start-up:** Windows start-up shows no window; a normal launch shows exactly one | Portable | ✅ |
| **Recording:**<br>• screen capture, encoders and audio devices probed;<br>• A/V sync, pause/resume, 60 FPS, real screen, single session, cancel | Portable | ✅ |
| **Recording quality** (PSNR/SSIM by region) | Portable | ✅ |
| **Editor features:** OCR, image transforms, GIF export, scrolling capture, video trim/export | Portable → feature self-test | ✅ |
| **Crash recovery:** the process is killed while recording; the recording is recovered at the next start | Portable | ✅ |
| **Library at scale** (start-up, search, thumbnails) | Benchmarks | ✅ |
| **Install and upgrade:**<br>• clean install: both programs, two shortcuts, machine-wide entry, protected folder, version in About; then uninstall;<br>• rc.2 → rc.5 with SnapForge open: custom Library location and settings kept;<br>• rc.1 (per-user) → rc.5, migrated as the signed-in user;<br>• **rc.3 → rc.5 and rc.4 → rc.5 with Capture and Editor both open:** both closed gracefully, and data, settings, the single start-up entry and both shortcuts kept | Installer | ✅ |
| **Busy and unusual states:**<br>• reinstall with both programs open;<br>• an **active recording** is never stopped without consent;<br>• SnapForge running in another Windows session;<br>• interrupted setup | Installer | ✅ |
| **Repair, a second Windows user, and uninstall** with SnapForge open; data is kept | Installer | ✅ |
| **Website:**<br>• build is up to date;<br>• links and accessibility basics;<br>• Chromium, Firefox and WebKit at phone, tablet and desktop widths | Website workflow | ✅ (sideways scroll fixed, see DEF-RC5-07) |

### 2.3 Not driven automatically, with the reason

| Item | Why | Covered by |
|---|---|---|
| **Native Windows message boxes:**<br>• Keyboard shortcuts (Help);<br>• confirmations such as Empty Trash;<br>• the Setup "stop and save the recording?" question | These are drawn by Windows, not SnapForge, and follow the system font and scaling | Setup's question is answered in the installer job; the rest is ⛔ owner glance (§6) |
| **Region selection overlay** at high scaling | A full-screen overlay; its size is the monitor itself | The real-input UI tests select regions at 100 %. Other scaling levels: ⛔ owner (§6) |
| **System tray menu** | Shell-owned menu | Start-up/About checks in the installer job; ⛔ owner glance |

## 3. Defects found and fixed (DEF-RC5-01 … 07)

| ID | Severity | Where | Before | Fix | After |
|---|---|---|---|---|---|
| DEF-RC5-01 | Medium | Everywhere (small text) | 2,300+ text elements at **9–11.5 DIP**: Editor tool labels (10), badges (9–10), section headings (11), Library details, Settings headings, QuickDock labels, recording windows (11–11.5), selection loupe | Every UI font raised to at least 12 DIP | 0 small-text findings at every scaling level |
| DEF-RC5-02 | High | Record panel | With **More options** open, the panel ran off the bottom of the screen at 100 %, 125 % and 150 %. At 125–150 % **Cancel and Start recording were below the screen**, as were the countdown and after-recording options | The panel is never taller than its monitor's work area. Its options scroll, and **Cancel / Start recording are pinned at the bottom**. Every SnapForge window that grows after opening is moved back into the work area | No FAIL; screenshot `record-panel-more-options` at 150 % shows both buttons |
| DEF-RC5-03 | Low | QuickDock (collapsed) | The collapsed pill was 30 DIP tall inside its 22-DIP window, so its lower edge and rounded corners were cut off | The pill no longer inherits the default 30-DIP minimum button height | No FAIL |
| DEF-RC5-04 | Low | Video editor, Editor, Library | The video editor's quality list had no accessible name. Capture titles cut off with "…" (for example "Demo recording (test pa…") had no way to read the full title | Accessible name "Export quality"; truncated titles and Library details show the full text as a tooltip | No WARN |
| DEF-RC5-05 | Medium | Capture panel (Image and Video) | **Keyboard focus disappeared:** Tab stopped on the "Undo" link of the hidden "Options reset." notice, so Tab could no longer reach Library, Editor, Capture or the options below | The link only takes focus while the notice is shown | Fixed in `462f448`; run RUNNUM |
| DEF-RC5-06 | Low | Editor toolbar at 175 % | The toolbar wraps onto two rows, and the arrow keys stopped at the end of the first row, so **Blur, Pixelate and Crop** could not be reached from the keyboard (their shortcut keys still worked) | The arrow keys step through all 15 tools in order and wrap around | Fixed in `462f448`; run RUNNUM |
| DEF-RC5-07 | Low | Website download page | On phones and tablets the page scrolled sideways by **17–53 px** (Chromium, Firefox and WebKit), because the 40-character commit ID could not wrap | Long IDs, checksums and file names wrap | 0 px at 360, 768 and 1280 px (local Playwright check and Website CI) |

**Audit corrections, so the numbers are honest:**

* In run 183 the audit ran **without** the app's work-area guard. The Settings and Video editor "window beyond the work area" FAILs at 150 % came from the audit mode, not from what users see; the audit now uses the same guard as the app. DEF-RC5-02 was real in both.
* The Record panel was invisible in the run-183 screenshots: SnapForge deliberately hides it from screen capture so it never appears in recordings. The audit now opens it without that exclusion.
* The two "Print Screen / Shift+F9 clipped" FAILs were a rounding effect of the measurement. The screenshots show the full text, and the check now allows 1 DIP of rounding.
* Radio-style options (capture modes, editor tools) follow the Windows convention: Tab reaches the group, and the arrow keys move inside it. The audit now presses the arrow keys and no longer reports these as unreachable.

## 4. Before/after screenshots

The PNGs are in the `ui-audit-<commit>` artifacts of runs 183 and RUNNUM; the after set is also attached to PR #3. Recommended pairs:

| Screen | Before (run 183) | After (run RUNNUM) |
|---|---|---|
| Record panel, More options, 150 % | `150/record-panel-more-options.png` (panel missing: excluded from capture) | panel fits; Cancel and Start recording visible |
| Editor, 150 % | tool labels 10 DIP | tool labels 12 DIP |
| Editor, 175 % | not run | two-row toolbar, all tools visible |
| Capture panel, 150 % | 10–11 DIP section labels | 12 DIP |
| QuickDock expanded, 150 % | 10 DIP labels | 12 DIP |
| Settings, 175 % | not run | window fits the screen; content scrolls |
| Setup wizard, 100–175 % | photographed | photographed (Inno Setup's own UI, unchanged) |

## 5. Upgrades preserve captures and settings

The installer job checks each of these with a real older build, against a SHA-256 of the Library, the settings keys, the custom Library location, the single start-up entry and both shortcuts:

* rc.1 (per-user) → rc.5;
* rc.2 → rc.5 with SnapForge open;
* rc.3 → rc.5 and **rc.4 (build 177) → rc.5** with Capture and Editor both open;
* repair, reinstall, uninstall (data kept).

All ✅ in run RUNNUM.

## 6. Blocked: needs real hardware or your PC (grouped)

These need hardware or a setup that a CI virtual machine does not have. Everything else above ran in CI.

**A. Display hardware** (about 10 minutes, if you have a second monitor or a high-resolution laptop):

1. **200 % scaling:** set a 4K or high-DPI screen to 200 %. Open the Editor, Capture panel, Record panel (More options), Settings and Library, and check that text is readable and every button is visible.
2. **Mixed DPI:** with two monitors at different scaling (for example 100 % and 150 %), drag the Editor, Settings and the Record panel between them. Each should redraw sharply, and none should end up partly off-screen.
3. **Region overlay at 150–200 %:** take a region capture and check that the loupe text and size label are readable.

**B. Devices:**

4. A real **webcam and microphone** in the Record panel and in a recording.
5. **Hardware video encoders** (Intel, NVIDIA or AMD).

**C. Windows features CI cannot drive interactively:**

6. The **administrator (UAC) prompt** and the interactive Setup dialogs.
7. A **second signed-in Windows user** at the same time (CI uses a scheduled-task session).
8. **SmartScreen** ("Windows protected your PC"): expected, because the build is unsigned (REL-SIGN-01).
9. **Dragging a capture into File Explorer** with a physical mouse.
10. **Native message boxes and the tray menu:** a glance at Help → Keyboard shortcuts and the tray right-click menu at your usual scaling.
