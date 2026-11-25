# SnapForge — Windows acceptance test (step-by-step guide)

This guide is written for someone who is **not a developer**. You need a Windows 10 or Windows 11 PC,
about 2–3 hours (Parts 1–8 cover the original features; Parts 9–12 the new Capture window, Library and privacy features), and a GitHub account that can see the SnapForge repository.

Record your results in **ACCEPTANCE-RESULTS-TEMPLATE.md** (it is in the same download). For every check,
write **Pass**, **Fail** or **Skipped**, and for a failure write one sentence about what you saw and attach a
photo or screenshot.

> Nothing in this test sends data anywhere. SnapForge has no internet features. Screenshots you take stay
> on your PC unless you save or copy them.

---

## Part 1 — Get the program

### 1.1 Download
1. Open the pull request page: `https://github.com/bazeocrisy/SnapForge/pull/1` (sign in to GitHub if asked).
2. Scroll to the bottom and click **Checks** (or the green tick ✓ next to the latest commit), then click **CI**.
3. On the run page, scroll down to **Artifacts**.
4. Click the item named **SnapForge-portable-win-x64-dev-…** — a ZIP file (about 55 MB) downloads.
5. Open your **Downloads** folder, right-click the ZIP → **Extract All…** → **Extract**.
   You now have a folder containing `SnapForge.exe`, five `.dll` files and some text files. Keep them together.

### 1.2 Check the download is genuine (checksum)
1. Open the extracted folder. Click the address bar at the top, type `powershell` and press **Enter**.
   A blue or black window opens in that folder.
2. Copy this whole line, paste it into that window (right-click pastes) and press **Enter**:

   ```
   Get-Content SHA256SUMS.txt | ForEach-Object { $h,$f = $_ -split '\s+',2; if ((Get-FileHash $f -Algorithm SHA256).Hash -eq $h) { "OK        $f" } else { "MISMATCH  $f" } }
   ```
3. **Expected:** every line starts with `OK`. If any line says `MISMATCH`, stop and report it — do not run the program.
4. Write the SHA-256 value shown for `SnapForge.exe` (first word of its line in `SHA256SUMS.txt`) in your results.

### 1.3 Quick automatic self-check (optional but useful)
In the same PowerShell window, paste and press **Enter**:
```
Start-Process .\SnapForge.exe -ArgumentList '--self-test','self-test.txt' -Wait; Get-Content self-test.txt
```
A SnapForge window flashes briefly. **Expected:** the last line says `RESULT: PASS`. Copy the whole output into your results.

### 1.4 Note your setup
* Windows version: press **Windows key + R**, type `winver`, press **Enter**. Write down the text (e.g. "Windows 11, Version 24H2, OS Build 26100.xxxx").
* For each monitor: **Settings → System → Display**. Click each numbered screen and note **Display resolution** and **Scale** (e.g. 2560 × 1440, 150 %).
* Note whether a second monitor is placed **left of** or **above** the main one (shown in the same Settings page).

**Please test these setups if you can** (each is a separate round of Parts 2–6):
* **A** — one monitor at 100 % scale.
* **B** — one monitor at 150 % or 175 % scale.
* **C** — two monitors with **different** scales, the second one placed **left of or above** the main one.

---

## Part 2 — Start the program

| ID | Do this | You should see |
|----|---------|----------------|
| S1 | Double-click `SnapForge.exe`. If a blue box "Windows protected your PC" appears, click **More info** → **Run anyway** (the development build is not signed yet). | Within a few seconds a dark window titled **SnapForge** with four large buttons: Region, Window, Display, Full screen. |
| S2 | Read the small grey line under the four buttons. | It lists your display(s) with resolution and scale. |
| S3 | Drag the window's corner to make it as small as possible. | Everything still fits; nothing overlaps. |
| S4 | Look at the title bar. | Windows 11: dark title bar. Windows 10: light title bar (expected). |

---

## Part 3 — Taking screenshots

Before this part, open two ordinary programs (for example Notepad and File Explorer) so there is something to capture.

| ID | Do this | You should see |
|----|---------|----------------|
| C1 | Click **Full screen**. | SnapForge disappears for a moment, then shows a picture of your **whole** screen (all monitors side by side). The bottom-right corner shows the size, e.g. "1920 × 1080 px". SnapForge's own window is **not** in the picture. |
| C2 | (Setup C only) Look at areas where the monitors don't line up. | Those empty areas are plain black. |
| C3 | Click **Region**. Press and hold the left mouse button, drag a box, release. | The screen freezes and darkens, a dashed cross-hair follows the mouse, a box with a size label (e.g. "412 × 237") appears while dragging. After releasing, the editor shows exactly what was inside the box, and the size at the bottom right matches the label. |
| C4 | (Setup C) **Region**: drag a box that starts on one monitor and ends on the other. | The box is drawn continuously across both monitors; the result contains both parts with no gap, shift or stretching. |
| C5 | (Setup C) **Region** entirely on the monitor that is left of / above the main one. | Correct content and size. |
| C6 | Click **Region**, then press **Esc**. Repeat, and this time **right-click** instead. | The darkened screen closes; the previous screenshot (if any) is unchanged; the bottom bar says "Capture cancelled." |
| C7 | Click **Region** and just click once without dragging. | Nothing happens — the darkened screen stays until you drag a box or press Esc. |
| C8 | Click **Window** and move the mouse over different windows (also the taskbar and the desktop). | Each window under the mouse is highlighted with its size. |
| C9 | Click a Notepad window. | The result is that window, including its title bar, with no transparent or extra border. |
| C10 | Maximise a window, then **Window** → click it. | The result is exactly the screen area of that window (no extra strip at the edges). |
| C11 | Let one window partly cover another, then **Window** → click the covered window. | The covering window appears on top in the result. *This is a known limitation, not a failure — mark Pass if it looks like this.* |
| C12 | Click **Display**, then click each monitor in turn (one capture per click). | The result size equals that monitor's resolution. |
| C13 | (Setups B and C) Repeat C3 and C12. | Results are sharp (not blurry) and not cut off. |
| C14 | With SnapForge in front, press **Ctrl+1**, **Ctrl+2**, **Ctrl+3**, **Ctrl+4** (one at a time; press Esc to cancel each). | They start Region, Window, Display and Full screen. |

---

## Part 4 — Viewing

| ID | Do this | You should see |
|----|---------|----------------|
| V1 | After a full-screen capture, look at the zoom number next to the magnifier buttons. | The whole picture fits the window; the number is below 100 %. |
| V2 | Click **1:1**. | Shows 100 %, picture is sharp; scroll bars let you move around. |
| V3 | Hold **Ctrl** and turn the mouse wheel; then press **Ctrl+0**. | Zooms in/out; Ctrl+0 fits it back. |

---

## Part 5 — Marking up

Use a capture that has some text on it. Tools are on the left; colour and size are along the top.

| ID | Do this | You should see |
|----|---------|----------------|
| M1 | Choose the **Arrow** tool and drag. | An arrow appears with a solid head at the point where you released. |
| M2 | Draw another arrow while holding **Shift**. | It snaps to straight horizontal, vertical or diagonal. |
| M3 | Try **Line**, **Rectangle**, **Ellipse** (hold Shift for a square / circle). | Each shape matches what you saw while dragging. |
| M4 | With a shape tool, click once without dragging. | Nothing is added. |
| M5 | **Text** tool: click, type "Hello", press **Shift+Enter**, type "world", press **Enter**. | Two lines of text with a light outline appear; the typing box disappears. |
| M6 | **Text** tool: click, type something, press **Esc**. | Nothing is added. |
| M7 | **Step** tool (circle with "1"): click three places. | Numbered circles 1, 2, 3. |
| M8 | Choose a different colour and size, then draw something. | The new item uses them. |
| M9 | **Select** tool (arrow pointer): click on a shape's outline and drag it. | A dashed orange outline shows it is selected; it moves. |
| M10 | With a shape selected, click another colour, then **L** size. | The selected shape changes colour, then thickness. |
| M11 | With a shape selected, click **Delete** (top bar) — then select another and press the **Delete** key. | Each one disappears. |
| M12 | **Select** tool: double-click a text item, change the words, press **Enter**. | The text updates. |
| M13 | **Step** tool after deleting step "2": click once more. | The new step is "4" (numbers are never reused). |

---

## Part 6 — Hiding sensitive information (redaction)

| ID | Do this | You should see |
|----|---------|----------------|
| R1 | **Redact** tool (black bar icon): drag over some text. | The area becomes solid black. |
| R2 | Type some text with the **Text** tool, then redact over that text. | The black box covers your text too. |
| R3 | After R2, draw an arrow pointing into the black box. | The arrow is visible on top of the black box. |
| R4 | Click **Save**, save as PNG. Open the file in **Paint**, zoom in on the black box's edges. | Completely black, including the edges; no trace of the text underneath. |
| R5 | Click **Copy**, open Paint, press **Ctrl+V**. | Same as R4. |
| R6 | Save again choosing **JPEG** in "Save as type"; open it. | The black area shows no readable trace of the original. |
| R7 | In SnapForge press **Ctrl+Z** until the redaction is gone. | The original text reappears **inside SnapForge** (undo works while editing). The files from R4/R6 are still redacted. |
| R8 | After R5, redact something new. | The bottom bar warns that anything already copied or saved still has the earlier version. |
| R9 | Right-click the saved PNG → **Properties** → **Details**. | No author, camera, location or program information. |

---

## Part 7 — Undo, saving and copying

| ID | Do this | You should see |
|----|---------|----------------|
| U1 | Make five different changes, press **Ctrl+Z** five times, then **Ctrl+Y** five times. | Each change is undone one by one, then redone one by one. Hovering the undo/redo buttons names the step. |
| U2 | Undo twice, then make a new change. | Redo is no longer available (greyed out). |
| O1 | Press **Ctrl+S**. | The Save window opens in Pictures\Screenshots (or Pictures), named like "SnapForge 2026-11-25 143012.png". |
| O2 | Save as PNG and open it. | Looks the same as SnapForge at 1:1. |
| O3 | Save to a file name that already exists. | Windows asks whether to replace it; Yes replaces it. |
| O4 | Try saving into `C:\Windows`. | A clear error message; SnapForge keeps running; no file is created. |
| O5 | **Copy**, then paste (Ctrl+V) into: Paint, Word (or WordPad/Google Docs), a web form or chat (e.g. a GitHub comment box, Teams, Slack, Outlook). | The picture pastes correctly in each. |
| O6 | Make a change. Look at the window title. Save. Look again. | Title says "unsaved changes" after the change, and not after saving. |
| O7 | Make a change without saving, then click **Region** (and later close the window with ✕). | SnapForge asks before throwing away the changes. Choosing **No** keeps your work. |

---

## Part 8 — Robustness (if possible)

| ID | Do this | You should see |
|----|---------|----------------|
| H1 | With three monitors or a very wide desktop: **Full screen**, then zoom and save. | Works; save succeeds. |
| H2 | Change the display **Scale** in Settings while SnapForge is open, then capture. | Correct, sharp result. |
| H3 | Unplug a monitor, then capture. | Works with the remaining monitor(s). |
| H4 | Open **Task Manager**, note SnapForge's memory, take ten captures, check again. | Memory does not keep growing with every capture. |

---

## Part 9 — Capture window, hotkeys and notification area (milestone 2)

| ID | Do this | You should see |
|----|---------|----------------|
| W1 | Look at the bottom-right of the taskbar (click ^ if needed). | A SnapForge icon. Left-click it: the SnapForge window comes to the front. Right-click: a menu with Capture, Capture window, Editor, Library, Settings, Exit. |
| W2 | In SnapForge click the ⚙ next to the orange **Capture** button (or Capture menu → Capture window). | A small "Capture" window with a big Capture button, four selection buttons, options, Delay and Presets. |
| W3 | Choose **Region**, turn **Preview in Editor** on, click **Capture**, drag a box. | The capture opens in the Editor. |
| W4 | Turn **Preview in Editor** off and **Copy to clipboard** on, capture again, then paste into Paint. | The Editor does not appear; a Windows notification says it was saved and copied; Paint shows the image. |
| W5 | Set **Delay** to 3 s and capture. | A round countdown 3-2-1 appears, then the selection screen. Press Esc during the countdown on another try: nothing is captured. |
| W6 | Turn on **Capture mouse pointer**, capture full screen. | The mouse pointer is visible in the image. |
| W7 | Close SnapForge's main window with ✕. | A notification says SnapForge is still running. Pressing the capture hotkey still works. Exit from the icon's right-click menu really quits. |
| W8 | Press **Print Screen** (default hotkey). | Capture starts. **If nothing happens or SnapForge says the key is in use**, Windows is using it for the Snipping Tool: Settings → Accessibility → Keyboard → turn off "Use the Print screen key to open screen capture", restart SnapForge, try again. Record which case you saw. |
| W9 | Settings → Hotkeys: click the box, press **Ctrl+Shift+F9**. Press Ctrl+Shift+F9 anywhere. | The box shows the new hotkey with a ✓; pressing it starts a capture. |
| W10 | In the Capture window, right-click a preset → Set hotkey… → enter the same Ctrl+Shift+F9. | SnapForge reports the conflict ("Already used by another SnapForge action" or similar) instead of silently ignoring it. |
| W11 | Click "+ Save current as preset", name it. Use it via its **Capture** button. Right-click → Rename, then Delete. | Each step works; the preset list updates. |
| W12 | Start SnapForge a second time (double-click the exe again). | No second copy appears; the existing window comes to the front. |

## Part 10 — Editor redesign

| ID | Do this | You should see |
|----|---------|----------------|
| N1 | Look at the Editor. | Menu bar at the top, orange Capture button with Editor/Library tabs, a row of tools with labels, the image in the middle, Quick Styles and Tool Properties on the right, Recent Captures along the bottom, zoom and size in the bottom bar. |
| N2 | Drag the divider between image and right panel; then click the panel button (top-right of the toolbar). | The panel resizes; then hides; click again to show. View menu → Show Recent Captures hides/shows the tray. |
| N3 | **Highlight** tool (H): drag over text. | A translucent yellow/colored box. |
| N4 | **Crop** tool (C): drag around part of the image. | Outside area dims while dragging; after release only the selected part remains. Ctrl+Z restores it. |
| N5 | Click a Quick Style, draw an arrow. Click "+ Save current", name it; right-click it → Add to favorites. | The arrow uses that style; the new style appears; favorites move to the front with a star. |
| N6 | Select an arrow (Select tool) and change color and thickness in Tool Properties. | The selected arrow changes; each change can be undone. |
| N7 | View → Theme → Light, then Dark, then "Use Windows setting". | All windows switch theme immediately and stay legible (menus, tray, Library, Capture window, Settings). |
| N8 | Use only the keyboard: Alt opens the menus, arrow keys move, Enter chooses; Tab moves through toolbar, panel and tray; letter keys (V A L R E H T N X C) choose tools. | Everything is reachable without a mouse (report anything that is not). |

## Part 11 — Capture Library and Recent Captures Tray

| ID | Do this | You should see |
|----|---------|----------------|
| LB1 | Take 5 captures. Click the **Library** tab. | All 5 appear as thumbnails under "Today", newest first. The sidebar shows counts and where the library is stored. |
| LB2 | Close SnapForge completely (icon → Exit), start it again, open the Library. | All captures are still there. |
| LB3 | Double-click one. | It opens in the Editor with its annotations still editable (move an arrow). |
| LB4 | Library: drag the **Size** slider; use **Sort**. | Thumbnails resize; order changes. |
| LB5 | Right-click a capture → Rename…, Tags… ("bug, ui"), Move to folder… ("Project"), Toggle favorite. | The sidebar lists the tag and folder; clicking them, or Favorites, filters the grid. |
| LB6 | Type part of a name or tag in **Search**. | Only matching captures remain. |
| LB7 | Ctrl+click and Shift+click to select several; Export… | Asked for a folder; one PNG per capture is written there. |
| LB8 | Select one → Move to Trash. Open **Trash** → Restore. Move it to Trash again → Delete permanently… | Restore brings it back; permanent delete asks for confirmation and removes it. |
| LB9 | Duplicate a capture. | A "(copy)" appears and can be edited independently. |
| TR1 | In the Editor, look at the tray. Click different thumbnails. | Each opens on the canvas; the current one has an orange border. |
| TR2 | Make an edit and watch the thumbnail. | A dot appears briefly while saving, then the thumbnail updates with your edit. |
| TR3 | Right-click a thumbnail → Pin to tray. Restart SnapForge. | It stays pinned at the left after restart. |
| TR4 | Right-click a thumbnail → Close (keep in Library). | It leaves the tray but is still in the Library. |
| TR5 | Right-click a thumbnail → Delete capture… | It goes to the Library's Trash and leaves the tray. |
| TR6 | Take more than 10 captures; scroll the tray with the mouse wheel. | The tray scrolls sideways. |

## Part 12 — Privacy, storage and sanitize

| ID | Do this | You should see |
|----|---------|----------------|
| PV1 | First start (or Help → Where are my captures?). | A notice explains captures are saved locally, shows the folder, and how to turn it off. |
| PV2 | Capture window: turn **Save to Library** off; capture with Preview on. | A banner says "Captured without history"; the Library count does not change; closing that capture asks before discarding it. |
| PV3 | Redact text in a capture saved to the Library. | A yellow banner warns the Library copy still keeps the originals; the Library tile shows "Unsanitized redactions". Exported PNG (R4 in Part 6) still shows only black. |
| PV4 | Close SnapForge, restart, reopen that capture, press Ctrl+Z repeatedly / select and remove nothing — instead use File → Sanitize… and confirm. | The banner disappears; the Library tile badge disappears; Ctrl+Z can no longer bring back the text. |
| PV5 | Settings → Library → **Back up library…**, then on another PC (or after deleting a capture) **Restore from backup…** | The captures come back; existing captures are not duplicated. |
| PV6 | Settings → Library → **Change location…** → choose an empty folder. | After the move, the Library still lists everything and the old folder is empty. Avoid a OneDrive folder unless you want them uploaded. |
| PV7 | Settings → Retention: tick "Permanently delete captures in the Trash for more than 30 days". | Saved; "Apply retention now" reports what it did. |
| PV8 | Unplug the network / turn on airplane mode and use SnapForge normally. | Everything works (SnapForge never needs the internet). |

## Part 13 — Milestone 3: capture panel, widget, drag and drop, import, start-up

> Requirement IDs refer to [PRODUCT-REQUIREMENTS.md](PRODUCT-REQUIREMENTS.md). Do these on a **real Windows PC**
> (Windows 11 first; then Windows 10 if you want to evaluate it). Automated CI results are reported separately in the
> handoff and do not replace these checks. For drag and drop, write down exactly which app you tested and whether it
> worked — anything you did not try stays "Expected", not "Tested".

| ID | Do this | You should see | Req |
|----|---------|----------------|-----|
| M3-P1 | Open the capture panel (notification-area icon → Capture panel…, or the widget's ⚙ → Capture panel…). | One compact **horizontal** panel: menus File/Edit/View/Help; left rail All-in-One / **Image** / Video (only Image active; the others are greyed and their tooltip says "Not available yet"); Selection, Effects, Share (Effects and Share greyed, "Not available yet"); switches Preview in Editor, Copy to Clipboard, Save to Library, Cursor, Time Delay ›; a big round **Capture** button with your hotkey under it; Presets, +, Open Library, Open SnapForge Editor at the bottom. SnapForge's orange, not any other product's branding. | CAP-UI-01…08, CAP-UI-12 |
| M3-P2 | Selection ▾ → Window; then Capture. | The window picker appears (not the region drag). | CAP-UI-03 |
| M3-P3 | Time Delay › → 5 seconds; Capture. Then turn Time Delay off and on again. | A 5-second countdown, then capture. Turning it back on remembers 5 s. Custom… accepts 1–60. | CAP-UI-09 |
| M3-P4 | Click the circular ↺ (reset) at the top right, then "Undo" in the footer. | Options go back to defaults, then back to what you had. | CAP-UI-10 |
| M3-P5 | Move the panel to a corner (or another monitor), close it, open it again. | It reopens where you left it (fully on screen). | CAP-UI-11 |
| M3-P6 | Keyboard only: Tab through the panel, Space toggles switches and opens dropdowns, Alt+S jumps to Selection, Enter captures, Esc closes. | Every control is reachable and shows focus. | CAP-UI-13, NFR-A11Y-01 |
| M3-P7 | Presets → the list. | The built-in preset is called "Region with 3 s delay and pointer" (no "Menu capture"). | CAP-UI-14 |
| M3-P8 | Windows 11: Settings → Accessibility → Keyboard → "Use the Print screen key to open screen capture" **on**; restart SnapForge; look under the Capture button; press Print Screen. Then turn the Windows setting **off**, restart SnapForge, press Print Screen. | On: SnapForge shows "Print Screen unavailable" with a warning icon; its tooltip names that Windows setting; the Snipping Tool opens. Off: SnapForge captures. Record both results. | CAP-HK-01 |
| M3-W1 | First start (or Settings → Capture widget → on). | A small orange tab at the **top centre** of the main screen, always on top. Your current app keeps keyboard focus. | WID-01, WID-02, WID-07, WID-09 |
| M3-W2 | Point at the tab and wait; move away. | It opens (Capture, Editor, Library, Presets) after a moment and closes again when you leave. Typing still goes to the app you were in. | WID-03, WID-05, WID-09 |
| M3-W3 | Settings → Capture widget → Open the widget: **On click**. Point at it; then click it. | Pointing does nothing; clicking opens it. | WID-03, WID-12 |
| M3-W4 | Drag the widget by its grip (⋮⋮) to the left edge, then to the right edge, then onto a second monitor with a different scaling (e.g. 100 % and 150 %). Restart SnapForge. | It snaps to each edge, looks sharp on both monitors, and returns to the last place after restart. Unplug the second monitor: it comes back on the main screen. | WID-04, WID-08, NFR-DPI-01/02 |
| M3-W5 | Turn auto-hide on; turn "keep on top" off; take a capture with the widget open. | Auto-hide: a thin strip until you touch it. Not on top: other windows can cover it. The widget never appears in the captured image. | WID-06, WID-07, WID-10 |
| M3-W6 | Narrator on (Ctrl+Win+Enter); move to the widget with Narrator's scan mode / Caps Lock+arrow keys. | Its buttons are announced as "Capture", "Editor", "Library", "Presets". (The widget cannot take keyboard focus by design; keyboard users use the notification-area menu.) | WID-11, NFR-A11Y-02 |
| M3-D1 | Drag one tray thumbnail to the **desktop** and into an **Explorer** folder. | A PNG named like "Checkout error 2026-11-25 134905.png" appears. The capture is still in SnapForge. | DND-01, DND-05, DND-06 |
| M3-D2 | Ctrl+click two tray thumbnails, drag one of them out. Library: select 3 (Ctrl/Shift+click), drag them to Explorer. | 2 files, then 3 files, each with a different name. | DND-02, DND-03 |
| M3-D3 | Take a capture, redact some text, then drag it into **Word**, **PowerPoint**, **OneNote**, an **Outlook** email, a **Teams** chat and a **browser** upload page (e.g. a webmail attachment). Record each app's result. | Each app receives a picture/file; the redacted area is solid; no .sfcap or second "original" file. If an app refuses, write down the app and version. | DND-11, PRIV-DND-01/02 |
| M3-D4 | Settings → Drag and drop → JPEG (quality 80); drag again. | A .jpg is produced. | DND-04 |
| M3-D5 | Drag a tray thumbnail and drop it back on the tray further left; start another drag and press Esc. | Reorder: the order changes and stays after restart. Esc: nothing is created. | DND-08, DND-09 |
| M3-D6 | After a few drags, wait 2 minutes, then look in `%LOCALAPPDATA%\SnapForge\Temp\Drag`. Exit SnapForge and look again. | Old drag folders disappear after about 2 minutes; the folder is empty after exit. | DND-08, PRIV-DND-04/06 |
| M3-D7 | Drop a PNG and a JPEG from Explorer onto the Library; also File → Import images…. Drop a .txt renamed to .png. | The images appear as new captures named after the files; the broken file is reported, nothing else happens. | DND-10, PRIV-DND-06 |
| M3-S1 | Settings → Launch SnapForge when I sign in. Sign out and in. | SnapForge starts in the notification area (no window). Windows Settings → Apps → Startup lists it. Turn it off again. | SET-01 |
| M3-S2 | Run SnapForge from a **standard (non-administrator)** Windows account: capture, edit, drag, import, settings. | Everything works; no admin prompt. | NFR-SEC-04 |
| M3-S3 | Windows high contrast (Settings → Accessibility → Contrast themes → e.g. Night sky). Look at the panel, widget, Editor, Library, Settings. | All text readable in the contrast colours; turning contrast off returns to SnapForge's theme. | NFR-A11Y-03 |
| M3-S4 | Library with many captures (if you have them): start SnapForge. | The window appears immediately; the Library shows "Loading the library…" briefly and then all captures; the app stays responsive. | PERF-03 |
| M3-S5 | Library sidebar → STORAGE. | The path is shortened with "…" if long; hovering shows the full path. | LIB-UI-01 |

---

## When you're done

Fill in the summary at the top of ACCEPTANCE-RESULTS-TEMPLATE.md and send it (with any screenshots of failures)
to the reviewer. Thank you!
