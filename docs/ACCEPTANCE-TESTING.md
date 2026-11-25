# Windows acceptance test script (MVP)

Automated CI covers model logic, rendering/export pixels, encoders, clipboard formats, the GDI capture
path and a main-window smoke test. **Interactive behavior (mouse selection, overlays, DPI placement,
real pasting) has not been tested on a physical Windows machine yet.** This script is that test.

## Setup

1. Download the latest `SnapForge-portable-win-x64-dev-<commit>` artifact from the pull request's
   **Checks → CI → Artifacts** (or the Actions run page) and unzip it.
2. Optional: verify the checksum — `Get-FileHash .\SnapForge.exe -Algorithm SHA256` must match `SHA256SUMS.txt`.
3. Run `SnapForge.exe`. It is unsigned: on the SmartScreen prompt choose **More info → Run anyway**.
4. Record: Windows version (`winver`), each monitor's resolution, scale (%) and arrangement.

Recommended configurations: (a) single monitor 100 %; (b) single monitor 150 % or 175 %;
(c) two monitors with different scales, one placed **left of or above** the primary (negative coordinates);
(d) a laptop with an external display at a different scale, if available.

For each step mark Pass / Fail and attach a screenshot of failures.

## A. Start-up

| # | Steps | Expected |
|---|-------|----------|
| A1 | Launch the exe on a machine without .NET installed | Window opens within a few seconds, dark theme, dark title bar, start screen with 4 capture cards |
| A2 | Look at the line under the cards | Lists every display with resolution and scale |
| A3 | Resize the window to the minimum size | Layout stays usable, nothing overlaps |

## B. Capture

| # | Steps | Expected |
|---|-------|----------|
| B1 | Click **Full screen** | Window disappears, then returns with an image covering all monitors; status bar size = total desktop size (e.g. 1920+2560 wide); SnapForge itself is not in the image |
| B2 | Check the gap areas (config c with different heights) | Black, not garbage or stale pixels |
| B3 | Click **Region**, drag a rectangle on the primary monitor | Screen freezes and dims; dashed crosshair before drag; live size label; result matches selection exactly and its size equals the label |
| B4 | **Region** dragged from one monitor across to another with a different scale | Selection rectangle is continuous on both monitors; result contains both parts with no offset, gap or stretch |
| B5 | **Region** on the secondary monitor with negative coordinates | Correct content and size |
| B6 | **Region**, then press Esc; repeat with right-click | Overlay closes, previous screenshot (if any) unchanged, status "Capture cancelled." |
| B7 | **Region**, click without dragging | Nothing happens; overlay stays until you drag or cancel |
| B8 | **Window**, hover different windows | Each window highlights with its size; taskbar and desktop also highlight |
| B9 | **Window**, click a normal window | Result is the window with no transparent/extra border; title bar included |
| B10 | **Window** on a maximized window | Result equals the monitor area of that window, no overhang |
| B11 | **Window** on a partly covered window | Result shows the covering window over it (documented limitation) |
| B12 | **Display**, click each monitor | Result size equals that monitor's resolution |
| B13 | Repeat B3 and B12 at 150 % / 175 % scale | Results remain pixel-accurate and sharp (not blurry, not cropped) |
| B14 | Ctrl+1 / Ctrl+2 / Ctrl+3 / Ctrl+4 while SnapForge is focused | Start region / window / display / full-screen capture |

## C. Preview and zoom

| # | Steps | Expected |
|---|-------|----------|
| C1 | After a large capture | Image fits the window; zoom shows < 100 % |
| C2 | Click **1:1** | Image shown at actual pixels (zoom 100 %), sharp |
| C3 | Ctrl+mouse wheel, Ctrl+= / Ctrl+−, Ctrl+0 | Zoom in / out / fit |

## D. Annotation

| # | Steps | Expected |
|---|-------|----------|
| D1 | Arrow tool (A): drag | Arrow with solid head at the end point; preview while dragging matches result |
| D2 | Hold Shift while dragging arrow/line | Snaps to 0/45/90° |
| D3 | Line (L), Rectangle (R), Ellipse (E); Shift makes square/circle | Shapes as previewed |
| D4 | Click without dragging with a shape tool | Nothing is added |
| D5 | Text (T): click, type two lines using Shift+Enter, press Enter | Text appears with outline halo; editing box disappears |
| D6 | Text: click, press Esc | Nothing added |
| D7 | Step (N): click 3 times | Markers 1, 2, 3; delete "2" then click again → "4" |
| D8 | Change color and size, then draw | New annotations use them |
| D9 | Select (V): click an annotation, drag | Dashed selection outline; moves; Delete removes it |
| D10 | With an annotation selected, change color / size | The selected annotation updates; one undo step each |
| D11 | Select tool: double-click a text annotation | Opens it for editing; changes apply on Enter |

## E. Redaction

| # | Steps | Expected |
|---|-------|----------|
| E1 | Redact tool (X): drag over text | Area becomes solid black |
| E2 | Save as PNG, open in Paint, zoom in on the edge | Fully black including edge pixels; no trace of the text |
| E3 | Copy, paste into Paint | Same as E2 |
| E4 | Draw an arrow over the redacted area, save | Arrow visible over black; area under the arrow still black |
| E5 | Undo the redaction | Original pixels return (in-session only) |

## F. Undo/redo

| # | Steps | Expected |
|---|-------|----------|
| F1 | Make 5 mixed edits, Ctrl+Z ×5, Ctrl+Y ×5 | Exact step-by-step reversal and replay; buttons and tooltips name the step |
| F2 | Undo twice, make a new edit | Redo becomes unavailable |

## G. Output

| # | Steps | Expected |
|---|-------|----------|
| G1 | Save (Ctrl+S), default name | Dialog opens in Pictures\Screenshots (or Pictures) with name "SnapForge YYYY-MM-DD HHMMSS.png" |
| G2 | Save as PNG, reopen | Identical to editor view at 1:1; properties show no camera/author metadata |
| G3 | Save choosing the JPEG type | `.jpg` file; good quality; opens everywhere |
| G4 | Save over an existing file | Overwrite confirmation; file replaced |
| G5 | Save into a folder without write permission (e.g. `C:\Windows`) | Clear error message, app keeps running, no partial file |
| G6 | Copy (Ctrl+C), paste into Paint, Word, a browser text box (e.g. GitHub comment), Teams/Slack | Image pastes correctly in each |
| G7 | Title shows "unsaved changes" after edits; disappears after saving | As described |
| G8 | With unsaved changes: start a new capture; close the window | Confirmation each time; "No" keeps the current work |

## H. Robustness

| # | Steps | Expected |
|---|-------|----------|
| H1 | Capture on a 3-monitor or > 8K-wide desktop, if available | Works; editor scrolls and zooms; save succeeds |
| H2 | Change display scale while SnapForge is open, then capture | Correct result |
| H3 | Disconnect a monitor, then capture | Correct result for the remaining layout |
| H4 | Task Manager: memory after 10 captures | No unbounded growth beyond the current image + history |
