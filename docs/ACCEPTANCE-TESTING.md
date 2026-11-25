# SnapForge — Windows acceptance test (step-by-step guide)

This guide is written for someone who is **not a developer**. You need a Windows 10 or Windows 11 PC,
about 60–90 minutes, and a GitHub account that can see the SnapForge repository.

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

## When you're done

Fill in the summary at the top of ACCEPTANCE-RESULTS-TEMPLATE.md and send it (with any screenshots of failures)
to the reviewer. Thank you!
