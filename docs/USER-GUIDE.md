# SnapForge User Guide

For SnapForge **1.0.0-rc.1** (release candidate) on Windows 11, 64-bit (x64), version 24H2 or later.

SnapForge is a free, open-source app for taking screenshots, marking them up, hiding private details and
recording your screen. Everything stays on your PC: SnapForge has no account, no cloud service and no network
features.

> **Release candidate.** This version is being tested and has not been released to the public yet. Some details
> may still change before version 1.0. Download links will be published when the release is approved. How to
> install it is described in [INSTALLATION.md](INSTALLATION.md).

## Contents

1. [Getting started and first launch](#1-getting-started-and-first-launch)
2. [The capture panel](#2-the-capture-panel)
3. [The desktop widget](#3-the-desktop-widget)
4. [Taking screenshots](#4-taking-screenshots)
5. [Scrolling capture](#5-scrolling-capture)
6. [Editing images](#6-editing-images)
7. [Redaction and sanitization](#7-redaction-and-sanitization)
8. [Text recognition (OCR)](#8-text-recognition-ocr)
9. [Screen recording](#9-screen-recording)
10. [Video editing](#10-video-editing)
11. [The Library](#11-the-library)
12. [Drag and drop](#12-drag-and-drop)
13. [Keyboard shortcuts](#13-keyboard-shortcuts)
14. [Settings](#14-settings)
15. [Backup and restore](#15-backup-and-restore)
16. [Upgrading](#16-upgrading)
17. [Uninstalling](#17-uninstalling)
18. [Troubleshooting](#18-troubleshooting)
19. [Privacy summary](#19-privacy-summary)
20. [Known limitations](#20-known-limitations)

---

## 1. Getting started and first launch

### Starting SnapForge

* **Installed version:** open the Start menu and choose **SnapForge**.
* **Portable version:** open the folder you extracted and run `SnapForge.exe`. Keep the other files in that
  folder next to it.

### What you see the first time

1. The **Welcome to SnapForge** window opens. It explains how to take a screenshot, where the capture widget
   is, where your captures are saved, how to record and how redaction works. Click **Take a capture now** to
   try it, or **Get started** to close the window. You can open it again later with **Help → Getting
   started**.
2. The **main window** opens. It has two workspaces, the **Editor** and the **Library**. Switch between them
   with the **Editor** and **Library** buttons at the top right, or with **Ctrl+E** and **Ctrl+L**.
3. The **capture panel** opens (a small window with a big round **Capture** button). See
   [The capture panel](#2-the-capture-panel).
4. The **capture widget** opens briefly at the top centre of your screen, then shrinks to a small tab. See
   [The desktop widget](#3-the-desktop-widget).
5. A **SnapForge icon** appears in the notification area (next to the clock). You may need to click the
   **^** arrow to see it.

### The notification-area icon

* **Click** the icon to open the capture panel.
* **Right-click** it for a menu: **Capture**, **Capture panel…**, **Show capture widget**, **Hide capture
  widget** (only shown while the widget is on), **Open Editor**, **Open Library**, **Settings…** and **Exit
  SnapForge**.

### Closing and exiting

* Closing the main window does **not** exit SnapForge. It keeps running in the notification area so your
  hotkeys keep working. The first time, a notification tells you so. You can change this in **Settings →
  Behaviour**.
* To exit completely, choose **File → Exit SnapForge**, or right-click the notification-area icon and choose
  **Exit SnapForge**.
* Your captures are saved automatically, so you do not need to save before exiting. The only exception is a
  capture taken with **Save to Library** turned off: SnapForge asks before such a capture is lost.

### Where your captures go

Every screenshot is saved automatically to your **Library** on this PC, in
`%LOCALAPPDATA%\SnapForge\Library` (unless you chose another folder). To see the exact folder, choose **Help →
Where are my captures? (privacy)**. You can capture without keeping a copy; see
[Save to Library](#image-mode-options).

### Help inside the app

The **Help** menu works offline:

* **Getting started** opens the welcome window again.
* **Keyboard shortcuts** (**F1**) shows the main shortcuts. The complete list is in
  [Keyboard shortcuts](#13-keyboard-shortcuts).
* **Where are my captures? (privacy)** shows where captures are stored and how redaction works.
* **About SnapForge** shows the version, build, commit, the program location and the Library location.

---

## 2. The capture panel

The capture panel is where you choose what to capture and how. It opens when SnapForge starts (you can turn
that off in **Settings → Behaviour**). To open it at any time:

* click the notification-area icon,
* choose **View → Capture panel** or **File → Capture window…** in the main window,
* click the small settings button next to the orange **Capture** button in the main window,
* choose **Capture panel…** from the widget's **Presets** menu, or
* press the **Show / hide capture panel** hotkey, if you set one (**Settings → Hotkeys**).

Press **Esc** (or **File → Close panel**) to close the panel. Closing only hides it; SnapForge keeps running.
The panel remembers where you put it on the screen.

### Modes

The column on the left chooses the mode:

| Mode | What it does |
|---|---|
| **All-in-One** | Not available yet. The button is disabled in this version. |
| **Image** | Screenshots. The options below are shown and the round button says **Capture**. |
| **Video** | Screen recording to an MP4 file. Video options are shown and the round button says **Record**. See [Screen recording](#9-screen-recording). |

### Image mode options

| Option | What it does |
|---|---|
| **Selection** | What to capture: **Region** (drag to select an area), **Window** (click a window), **Active window** (the window you were using, no clicking), **Display** (click a display) or **Full screen** (every display). |
| **Effects** | Not available yet (always "None"). |
| **Share** | Not available yet (always "None"). Use the **Copy to Clipboard** and **Save to Library** switches instead. |
| **Preview in Editor** | Opens the capture in the Editor straight away. |
| **Copy to Clipboard** | Copies the finished image to the clipboard, ready to paste. |
| **Save to Library** | Saves the capture in your Library. **Off** means "capture without history": the capture exists only in memory and nothing is written to disk unless you export it. |
| **Cursor** | Includes the mouse pointer in the image. |
| **Time Delay** | Waits before capturing. Tick the box to turn it on. Use the small button next to it to choose **No delay**, 1, 2, 3, 5, 10, 15, 30 or 60 seconds, or **Custom…** (any whole number from 1 to 60). |

If you turn off **Preview in Editor**, SnapForge stays in the background and shows a short notification
instead, for example "Capture saved to the Library." If **Preview in Editor**, **Copy to Clipboard** and **Save
to Library** are all off, the capture opens in the Editor anyway, so it is not lost.

### Video mode options

| Option | What it does |
|---|---|
| **Record** | What to record: **Region**, **Window** or **Display**. |
| **Frame rate** | **30 FPS** (default) or **60 FPS**. |
| **Quality** | **Standard (smaller files)** (default) or **High (sharper, larger files)**. |
| **System audio** | Records the sound your PC plays. **On** by default. |
| **Microphone** | Records your microphone. **Off** by default. Choose the device in **Settings → Capture Library → Recording**. |
| **Cursor** | Shows the mouse pointer in the recording. On by default. |
| **Countdown** | Counts down before recording starts. On by default, 3 seconds. Use the small button next to it to choose **No countdown**, 3, 5 or 10 seconds. |

### Other parts of the panel

* **Capture / Record** (the round button): starts a capture or recording with the current options. **Enter**
  does the same, and so does **File → Capture**.
* **Hotkey line** under the button: shows the global hotkey for this mode, for example **Print Screen** for
  Image mode and **Shift+F9** for Video mode. "No hotkey" means none is set. A warning sign and "…unavailable"
  mean Windows or another program is already using that key; point at it to see why.
* **Presets** and **+** (Save current settings as a preset): see [Presets](#presets).
* **Reset capture options** (the reset button, or **Edit → Reset capture options**): puts the Image options
  back to their defaults. A note "Options reset." with an undo link appears for 15 seconds; you can also use
  **Edit → Undo reset**.
* **Open Library** and **Open SnapForge Editor**: bring the main window to the front.
* **View → Keep capture panel on top**: keeps the panel above other windows.
* **View → Show capture widget**: turns the widget on and shows it.
* **File → Settings…** opens Settings.

---

## 3. The desktop widget

The capture widget is a small tab docked to the edge of your screen (top centre by default). It gives you the
main commands without switching windows.

### Using the widget

1. Point at the tab (or click it, depending on **Settings → Capture widget → Open the widget**). It opens.
2. Choose a button:
   * **Capture** – captures with the current settings of the capture panel.
   * **Record** – starts a recording with the current video settings. While a recording runs, this button
     says **Stop** and stops and saves the recording.
   * **Editor** – opens the Editor.
   * **Library** – opens the Library.
   * **Presets** – a menu with your presets (click one to capture with it), **Capture panel…**, **Widget
     settings…** and **Hide widget**.
3. Move the pointer away. The widget shrinks back to a tab.

The widget never takes keyboard focus, so it does not interrupt the app you are working in. It is hidden while
a capture is taken, so it never appears in your screenshots. Keyboard users can reach the same commands from the
notification-area menu, the capture panel and the main window.

### Moving the widget

Drag the widget by its grip to the **top**, **left** or **right** edge of any display. It docks to the nearest
of those edges and remembers the place, even after a restart.

### Recovering a missing widget

If you cannot find the widget, use any of these:

* In the main window, choose **View → Show capture widget**.
* Right-click the notification-area icon and choose **Show capture widget**.
* In the capture panel, choose **View → Show capture widget**.
* In **Settings → Capture widget**, tick **Show the capture widget at the screen edge**, or click **Move widget
  to top centre**.

**Show capture widget** turns the widget on, moves it to the top centre of the display the main window is on
and opens it for a few seconds so you can see it. If **Auto-hide** is on (see [Settings](#capture-widget)), the
widget is only a thin strip until the pointer touches it.

---

## 4. Taking screenshots

### Quick start

1. Press **Print Screen**, or click **Capture** (in the capture panel, the widget, the main window or the
   notification-area menu).
2. For a **region**, drag a rectangle over the screen. For a **window** or **display**, click it.
3. The capture opens in the Editor (if **Preview in Editor** is on) and is saved to your Library.

The screen is "frozen" while you select: SnapForge first takes a picture of the whole desktop and you select on
that picture. SnapForge's own windows are hidden while it captures.

To cancel, press **Esc** (or right-click during a region selection).

### Region

1. Choose **Capture → Region** (**Ctrl+1**), or set **Selection** to **Region** in the capture panel.
2. Drag from one corner of the area to the opposite corner. The size in pixels is shown next to the selection.
3. Release the mouse button to capture.

Precise selection:

* A **magnifier** follows the pointer. It shows the pixels around the pointer enlarged, the screen position
  (**X**, **Y**) and the colour of the pixel under the pointer as a hex value (for example `#1E88E5`).
* Crosshair guide lines help you line up edges.
* The **arrow keys** move the pointer by one pixel. **Shift + arrow keys** move it by 10 pixels. You can use
  them before you start dragging and while you drag.
* A region can span several displays.
* A single click without dragging does nothing; the selection stays open so you can try again.

### Window

1. Choose **Capture → Window** (**Ctrl+2**), or set **Selection** to **Window**.
2. Move the pointer over the window you want. It is highlighted.
3. Click it.

Because the screen is frozen first, windows that overlap the chosen window also appear in the image.

### Active window

**Capture → Active window** (**Ctrl+5**), or **Selection → Active window**, captures the window you were working
in, without clicking. This is useful with a hotkey or a delay: SnapForge remembers which window was in front
before its own windows appeared. If it cannot tell, it lets you click a window instead.

### Display

**Capture → Display** (**Ctrl+3**), or **Selection → Display**: click the display (monitor) you want to capture.

### Full screen

**Capture → Full screen** (**Ctrl+4**), or **Selection → Full screen**: captures all displays at once, as one
image. There is no selection step.

### New capture with the current settings

**File → New capture** (**Ctrl+N**), the orange **Capture** button in the main window, the round button in the
capture panel and your global capture hotkey all capture with the capture panel's current options.

### Delay

Use a delay to capture menus or tooltips that close when you click elsewhere.

1. In the capture panel, tick **Time Delay** and choose the number of seconds.
2. Click **Capture**. A countdown appears.
3. Open the menu or tooltip you want to capture before the countdown ends.

Press **Esc** or click **Cancel** in the countdown to cancel.

### Cursor

Tick **Cursor** in the capture panel to include the mouse pointer in the image. It is off by default.

### Presets

A preset saves a combination of capture options under a name, with its own hotkey if you want.

SnapForge comes with four presets: **Region to Editor**, **Window to Editor**, **Full screen to Clipboard (no
history)** and **Region with 3 s delay and pointer**.

* **Capture with a preset:** in the capture panel, click **Presets**, point at a preset and choose **Capture
  now**. You can also use **Capture → Presets** in the main window or the widget's **Presets** menu.
* **Save a new preset:** set the options in the capture panel, then click **+** or choose **File → Save current
  settings as preset…**, and type a name.
* **Manage a preset:** in the capture panel, click **Presets**, point at the preset, then choose **Use these
  settings** (copies its options into the panel), **Set hotkey…**, **Rename…** or **Delete…**.
* **Preset hotkeys:** type a combination such as `Ctrl+Shift+F9`, or leave the box empty for none. If Windows
  or another program already uses it, SnapForge tells you.

### Repeat last capture

**Capture → Repeat last capture** (**Ctrl+R**) captures exactly the same screen area again with the same options,
without selecting and without delay. It only works after you have taken a capture since SnapForge started; the
last area is kept in memory only.

### New from clipboard

**Capture → New from clipboard** (**Ctrl+Shift+V**) opens the image that is on the clipboard (for example one
copied from a browser) as a new capture in the Editor. It is saved to the Library like any capture (if **Save to
Library** is on).

### Print Screen and the Windows Snipping Tool

SnapForge's default global capture hotkey is **Print Screen**. On Windows 11 the Print Screen key usually opens
the Windows Snipping Tool instead. If so, SnapForge shows "Print Screen unavailable" under the capture button
and lists the problem in **Settings → Hotkeys**.

To let SnapForge use Print Screen:

1. Open Windows **Settings → Accessibility → Keyboard**.
2. Turn off **Use the Print screen key to open screen capture**.
3. Exit SnapForge (notification-area icon → **Exit SnapForge**) and start it again.

Or choose a different hotkey in SnapForge: **Settings → Hotkeys → Capture (current settings)**. See
[Hotkeys](#hotkeys).

---

## 5. Scrolling capture

A scrolling capture joins several screenshots of an area that scrolls (a long web page, document or list) into
one tall image.

### How to take one

1. Choose **Capture → Scrolling capture…** (**Ctrl+6**).
2. Drag a rectangle over the part of the window that scrolls. Select only the moving content, not the window's
   toolbar or title.
3. A small **Scrolling capture — SnapForge** window appears next to the area. It shows the area's size.
4. Choose one of the two ways below.
5. Press **Finish**. The joined image opens in the Editor.
6. **Check the result** for gaps or repeated parts before you share it.

To stop without keeping anything, press **Cancel** (or **Esc**).

### Auto-scroll

1. Press **Auto-scroll**.
2. SnapForge moves the pointer to the middle of the area, scrolls with the mouse wheel and takes a frame after
   each step. The status line shows how many frames and pixels it has so far.
3. It stops by itself when the content no longer moves ("Reached the end"). To stop earlier, press **Stop**.
4. Press **Finish**.

While Auto-scroll runs, do not use the mouse. SnapForge puts the pointer back where it was when it stops.

### Manual

1. Scroll the area yourself, a bit less than one screenful (with the wheel, keyboard or scrollbar).
2. Press **Add frame** (or **Space**).
3. Repeat until you reach the end.
4. Press **Finish**.

### Tips

* Scroll **less than a screenful** each time. Each new frame must overlap the previous one so SnapForge can
  match them.
* Scroll **downwards only**. Scrolling back up is not supported.
* Fixed headers and footers that do not move are detected and kept only once.
* Close pop-ups, chat bubbles and cookie banners first; anything that changes while you scroll can stop
  frames from matching.
* Pause animations and videos in the area if you can.

### Messages you may see

* **"This frame could not be matched to the previous one (scrolled too far, or the content changed)."**
  The new frame was not added. Scroll back a little so it overlaps the last frame, then press **Add frame**
  again, or press **Finish** to keep what you have. SnapForge never guesses how frames fit together.
* **"Nothing new — scroll the area first…"** You pressed **Add frame** without scrolling.
* **"Maximum number of frames reached."** / **"Maximum length reached."** The limit is 400 frames or about
  30,000 pixels of length. Press **Finish**.

### Limitations

Scrolling capture only works where the content scrolls inside a fixed area. Apps that redraw, animate or change
their layout while scrolling may not match, and some apps ignore simulated mouse-wheel input (use **Manual**
then). It does not work in every app.

---

## 6. Editing images

Captures open in the **Editor**. Every change is saved to the Library automatically about a second after you
make it. The status bar at the bottom shows "Saving to Library…", "Saved to Library", "Unsaved changes" or "Not
in Library".

### The Editor at a glance

* **Toolbar** (top): the drawing tools, then **Undo**, **Redo**, **Delete annotation**, **Drag**, **Copy**,
  **Export** and the button that shows or hides the side panel.
* **Canvas** (middle): your capture.
* **Quick Styles & Properties panel** (right): colours, thickness and saved styles. Drag its edge to make it
  wider or narrower. Show or hide it with **View → Show Quick Styles & Properties**.
* **Recent Captures tray** (bottom): thumbnails of your recent captures. See
  [Recent Captures tray](#recent-captures-tray).
* **Status bar**: hints for the current tool, image size and zoom controls.

When you choose a tool, the status bar and the **Tool Properties** panel show a short hint about how to use it.

### Drawing tools

Press the letter key to choose a tool quickly (the Editor must be showing a capture and you must not be typing).

| Tool | Key | How to use it |
|---|---|---|
| **Select** | V | Click an annotation to select it; drag to move it. Double-click text to edit it. **Delete** (or **Backspace**) removes the selected annotation. |
| **Arrow** | A | Drag from the tail to the point. Hold **Shift** to snap to 45° steps. |
| **Line** | L | Drag. Hold **Shift** for 45° steps. |
| **Shape** (rectangle) | R | Drag. Hold **Shift** for a square. |
| **Ellipse** | E | Drag. Hold **Shift** for a circle. |
| **Highlight** | H | Drag over content to highlight it (thickness is not used). |
| **Text** | T | Click to place text and type. **Enter** finishes, **Shift+Enter** starts a new line, **Esc** cancels. Click existing text with the Text tool to edit it. The text size follows the thickness setting. |
| **Step** | N | Click to place the next numbered step (1, 2, 3…). The size follows the thickness setting. |
| **Pen** | D | Drag to draw freehand. |
| **Callout** | K | Drag from the thing you are pointing at to where the speech bubble should go, then type its text in the **Callout** box. If you just click, the bubble is placed above and to the right. |
| **Stamp** | S | Click to place a stamp. Click the **Stamp** button again to choose: **Check**, **Cross**, **Warning**, **Info**, **Question** or **Star**. The size follows the thickness setting. |
| **Redact** | X | Drag over sensitive content to black it out. See [Redaction and sanitization](#7-redaction-and-sanitization). |
| **Blur** | B | Drag over an area to blur it. **Not redaction.** |
| **Pixelate** | P | Drag over an area to turn it into coarse blocks. **Not redaction.** |
| **Crop** | C | Drag the area to keep. The rest is removed. |

**Esc** cancels what you are doing: first the text you are typing, then a drag in progress, then the selection.

### Colour, thickness and Quick Styles

* **Colour:** click one of the eight swatches (**Red**, **Orange**, **Yellow**, **Green**, **Blue**,
  **Purple**, **Black**, **White**) or type a colour in the **Hex** box as `#RRGGBB` (for example `#E53935`) and
  press **Enter**.
* **Thickness:** drag the slider (1 to 20 px). For Text it sets the text size, for Step and Stamp the size.
* **Changing an existing annotation:** select it with **Select** (V), then pick a colour or thickness. Each
  change is one undo step. There are no resize handles; use the thickness slider to make an annotation bigger or
  smaller.
* **Quick Styles:** click a style to use its colour and thickness. SnapForge includes a thin and a bold style for
  each colour. Click **+ Save current** to save your current colour and thickness under a name. Right-click a
  style for **Apply**, mark or unmark it as a favourite (favourites are listed first), or **Delete style** (your
  own styles only).

SnapForge remembers the last colour and thickness you used.

### Undo and redo

* **Undo:** **Ctrl+Z**, the **Undo** button or **Edit → Undo**. The button's tooltip says what will be undone.
* **Redo:** **Ctrl+Y** or **Ctrl+Shift+Z**, the **Redo** button or **Edit → Redo**.

Every edit can be undone while the capture is open, including crop, rotate, resize, border, shadow, watermark,
blur and pixelate.

### Copy, paste and duplicate annotations

* **Copy an annotation:** select it, then **Edit → Copy annotation** (**Ctrl+Shift+C**).
* **Paste:** **Edit → Paste** (**Ctrl+V**). The copy appears slightly offset from the original. You can also
  paste it into another capture.
* **Duplicate:** select an annotation, then **Edit → Duplicate annotation** (**Ctrl+D**).
* Redactions cannot be copied or duplicated.
* If you have not copied an annotation, **Ctrl+V** opens the image on the clipboard as a new capture (like
  **Capture → New from clipboard**).

### Image commands (Image menu)

| Command | Shortcut | What it does |
|---|---|---|
| **Crop…** | C | Chooses the Crop tool. |
| **Redact…** | X | Chooses the Redact tool. |
| **Sanitize capture…** | | See [Sanitize](#sanitize). |
| **Rotate right 90°** | Ctrl+] | Turns the image clockwise. |
| **Rotate left 90°** | Ctrl+[ | Turns the image anticlockwise. |
| **Rotate 180°** | | Turns the image upside down. |
| **Flip horizontal** / **Flip vertical** | | Mirrors the image. |
| **Resize image…** | Ctrl+Alt+I | Changes the image size. Enter **Width (px)**, **Height (px)** or **Percent**. **Keep aspect ratio** is on by default. |
| **Canvas size…** | Ctrl+Alt+C | Changes the size of the canvas around the image without stretching it. Choose where to keep the image (one of nine anchor positions, centre by default). New space is filled with white; a smaller canvas cuts off the edges. |
| **Add border…** | | Adds a border of the chosen thickness (1–500 px) around the image, in the current colour. The image grows. |
| **Add drop shadow** | | Adds a soft shadow below and to the right of the image, on a white background. |
| **Add watermark…** | | Type the text (default "Confidential"). It is placed in the bottom-right corner at 45 % opacity and becomes part of the image. |
| **Blur area** / **Pixelate area** | B / P | Choose the Blur or Pixelate tool. |
| **Extract text (OCR)…** | Ctrl+Shift+T | See [Text recognition](#8-text-recognition-ocr). |

The maximum image size for **Resize image** and **Canvas size** is 400 million pixels.

### Zoom

* **Zoom in:** **Ctrl+=** (or **Ctrl+Plus** on the number pad), **View → Zoom in**, or the **+** button in the
  status bar.
* **Zoom out:** **Ctrl+−**, **View → Zoom out**, or the **−** button.
* **Fit to window:** **Ctrl+0** or **View → Fit to window**.
* **Actual pixels (1:1):** **Ctrl+Alt+0**, **View → Actual pixels**, or the **1:1** button. One image pixel
  is then one screen pixel.
* **Ctrl + mouse wheel** zooms in and out.

The zoom percentage is shown in the status bar. Zoom ranges from 5 % to 800 %.

### Saving, copying, exporting and printing

* **Saving** happens automatically to the Library.
* **Copy image** (**Ctrl+C**, **Copy** button, **File → Copy image**): copies the finished image to the
  clipboard.
* **Export** (**Ctrl+S**, **Export** button, **File → Export as PNG/JPEG…**): saves the finished image as a
  **PNG**, **JPEG**, **BMP** or **TIFF** file. The default name is like `SnapForge 2026-11-25 134905.png`, and
  the default folder is **Pictures\Screenshots** (or **Pictures** if that folder does not exist). JPEG files
  are saved at quality 92.
* **Drag** button: drag it into another app. See [Drag and drop](#12-drag-and-drop).
* **Print…** (**Ctrl+P**, **File → Print…**): prints the finished image, fitted to the page with a margin.

The clipboard, exports, prints and drags always get the **finished** image: annotations drawn in, redactions
burned in, and no hidden data or metadata (no user name, location or app information).

### Captures without history

If **Save to Library** was off, a banner says "Captured without history". The capture exists only in
memory and is lost when you close it unless you export it. Click **Save to Library** in the banner (or **File →
Save to Library**) to keep it after all.

### Editable captures (.sfcap)

* **File → Export editable capture (.sfcap)…** saves a capture with its annotations still editable, so you can
  open it on another PC with SnapForge.
* **File → Import editable capture (.sfcap)…** adds such a file to your Library as a new capture.

**Warning:** a `.sfcap` file is an editable copy. If the capture has redactions that are not sanitized, the file
still contains the original pixels and anyone you give it to can undo the redactions. SnapForge warns you before
exporting it. Sanitize first, or share a PNG/JPEG instead.

### Closing a capture

**File → Close capture** (**Ctrl+W**) closes the capture in the Editor. It stays in the Library and in the
Recent Captures tray.

### Recent Captures tray

The tray at the bottom of the Editor shows your recent captures. It is kept when you restart SnapForge.

* **Click** a thumbnail to open it. **Ctrl+click** selects several; **Shift+click** selects a range.
* **Drag** a thumbnail (or several selected ones) into another app to copy them as images. Dragging a thumbnail
  within the tray changes the order.
* **Right-click** a thumbnail for **Open**, **Pin** / **Unpin** (**Ctrl+Shift+P** for the open capture; pinned
  thumbnails stay when you close unpinned ones), **Close (keep in Library)**, **Close all unpinned**, **Show in
  Library** and **Delete capture…** (moves it to the Library's Trash).
* A mark on a thumbnail means its changes are not saved yet; "Not in Library" marks a capture without history.
* Buttons: **Close unpinned**, **Open Library** and **Hide**. **View → Show Recent Captures** shows the tray
  again.

### Opening image files

To edit an existing PNG, JPEG or BMP file, import it into the Library: **File → Import images…**, or drag the
files from File Explorer onto the SnapForge window. When you drop files while the Editor is showing, the first
one opens. Only the pixels are imported; the file's metadata is not kept. The limit is 16,384 pixels per side and
100 megapixels.

---

## 7. Redaction and sanitization

### Redact: what it does

The **Redact** tool (**X**) replaces the pixels in the area you drag with **solid black**. It also covers any
annotations you drew before it. Annotations you add after the redaction are drawn on top of it (for example an
arrow pointing at the black box).

### What is removed, and where

| Where the image goes | Contains the original pixels under a redaction? |
|---|---|
| **Exported files** (PNG, JPEG, BMP, TIFF) | **No.** Only the black box. |
| **Clipboard** (Copy image) | **No.** Only the black box. |
| **Drag and drop** into other apps | **No.** Only the black box. |
| **Printed pages** | **No.** Only the black box. |
| **Text recognition (OCR)** | **No.** OCR reads the finished image, so redacted text is not recognised. |
| **Your editable Library copy** | **Yes, until you sanitize it.** |
| **Editable capture files (.sfcap)** | **Yes, if exported before sanitizing.** |
| **Library backups (.zip)** | **Yes, for captures not sanitized when the backup was made.** |

Why the Library copy keeps the originals: so you can undo a redaction later, even after restarting SnapForge.
While a Library capture is in this state:

* the Editor shows a banner: "Exports of this capture are redacted, but its editable Library copy still keeps
  the original pixels so redactions can be undone. Sanitize to remove them permanently." with a **Sanitize…**
  button,
* its Library tile is marked **Unsanitized redactions**, and
* **Settings → Privacy** counts how many captures are in this state.

If you change a redaction after you already copied or exported the image, the status bar reminds you: anything
you already copied or exported still contains the earlier version.

### Sanitize

Sanitize makes redactions permanent in the Library copy.

1. Open the capture and choose **File → Sanitize capture…** (also in the **Image** menu and in the banner).
   In the Library, select one or more captures and choose **Sanitize…** from the right-click menu or **More**.
   To do all at once: **Settings → Privacy → Sanitize all captures with redactions…**.
2. Read the confirmation and click **Yes**.

Sanitize:

* burns the redactions into the stored image and deletes the original pixels from the Library copy,
* flattens annotations that lie under a redaction into the image and removes them as editable objects,
* regenerates the thumbnail and clears the undo history.

**This cannot be undone.**

### What Sanitize cannot erase

Sanitize only changes the Library copy. It cannot reach:

* **earlier exports** – files, clipboard contents and drags you made before redacting (Windows clipboard history,
  **Win+V**, may also keep earlier copies; clear it yourself),
* **backups** – Library backups and `.sfcap` files you made before sanitizing,
* **copies other people or apps hold** – anything you sent, uploaded or pasted elsewhere,
* **remnants on the disk** – SnapForge overwrites the old files before deleting them, but on SSDs and with
  features like File History, Volume Shadow Copies or cloud-synced folders, old data can survive. This cannot be
  guaranteed away.

### Blur and pixelate are not redaction

**Blur** (**B**) and **Pixelate** (**P**) soften an area for looks. Blurred or pixelated text can often be
reconstructed. **Never use them for passwords, account numbers or personal data.** Use **Redact** (**X**) and
then **Sanitize**.

### Other things to know

* The size of the black box shows roughly how long the hidden text was.
* Screen recordings **cannot be redacted**. See [Screen recording](#9-screen-recording).

---

## 8. Text recognition (OCR)

SnapForge can read the text in a capture so you can copy it. It uses the text recognition built into Windows and
runs entirely on your PC. Nothing is uploaded or saved.

### How to use it

1. Open the capture in the Editor.
2. Choose **Image → Extract text (OCR)…** (**Ctrl+Shift+T**).
3. A window opens and shows the recognised text. The status line says how many lines were found and in which
   language.
4. If needed, choose another **Language:** from the list (only languages installed for text recognition on this
   PC are listed).
5. Correct the text if necessary. You can edit it in the window.
6. Click **Copy text** to copy it to the clipboard. Click **Close** when you are done.

The text exists only in this window until you copy it.

OCR reads the image as it would be exported: redacted areas are black, so redacted text is not recognised.

**Check the text.** Recognition is not always exact, especially with small, stylised, handwritten or
low-contrast text.

### Installing a text-recognition language in Windows

If SnapForge says "No text-recognition language is installed", add one in Windows:

1. Open Windows **Settings → Time & language → Language & region**.
2. Next to the language you want (add it with **Add a language** first if it is not listed), click **…** and
   choose **Language options**.
3. Under **Language features**, add **Optical character recognition** if it is offered.
4. Wait until Windows has installed it, then try **Extract text** again.

If SnapForge says that text recognition for a particular language is not installed, install that language's
**Optical character recognition** feature the same way.

---

## 9. Screen recording

SnapForge records a region, a window or a whole display to an **MP4** file (H.264 video, AAC sound) using the
video encoders built into Windows.

### Start a recording

1. Open the capture panel and choose **Video**.
2. Set **Record** (Region, Window or Display), **Frame rate**, **Quality**, **System audio**, **Microphone**,
   **Cursor** and **Countdown**.
3. Click **Record** (or press **Shift+F9** from any app, or click **Record** on the widget).
4. Select what to record: drag a region, or click a window or display. **Esc** cancels. The area must be at
   least 16 × 16 pixels.
5. If **Countdown** is on, a countdown runs (3 seconds by default). **Esc** cancels it.
6. Recording starts.

### Audio defaults

* **System audio** (what your PC plays) is **on** by default.
* **Microphone** is **off** by default. When you turn it on, SnapForge uses the Windows default microphone
  unless you choose another in **Settings → Capture Library → Recording → Microphone**.
* You can switch both on and off during the recording from the toolbar, as long as at least one of them was on
  when the recording started. If both were off at the start, the recording has no sound track.

### The recording toolbar

A small floating toolbar appears next to the recorded area. It shows:

* a recording dot and the **elapsed time**,
* **Pause** / **Resume**,
* **Stop** – stops and saves,
* **Cancel** – asks "Discard this recording? It will not be saved." and discards it if you click **Yes**,
* **System audio** and **Microphone** switches with level meters,
* the resolution and frame rate,
* a line for notices (for example a device change or low disk space).

Clicking the toolbar does not take focus away from the app you are recording.

### Recording hotkeys

These work from any app while SnapForge is running:

| Action | Default |
|---|---|
| Start a recording; while recording, pause and resume | **Shift+F9** |
| Stop and save | **Shift+F10** |
| Cancel and discard (without asking) | none (set one in Settings) |

Change them in **Settings → Capture Library → Recording**.

### Pause and resume

Press **Pause** on the toolbar or **Shift+F9**. Press **Resume** or **Shift+F9** again to continue. The paused time
is not in the video.

### When the recording ends

* SnapForge saves the MP4 to your Library and shows a notification such as "Recording saved: 0:01:23,
  1920×1080."
* The recording opens in the player (turn this off with **Settings → Capture Library → Recording → Open the
  recording when it is saved**).
* If **Save recordings to the Library** is off, SnapForge asks where to save the MP4 and does not keep it in the
  Library. If you cancel that dialog, the recording is kept in the Library.

### What is excluded from the video

* SnapForge's own windows (main window, capture panel, widget) are hidden while you record.
* The **recording toolbar** and the **dashed red outline** around the recorded area are kept out of the video. If
  Windows cannot hide the toolbar from the recording, the toolbar says so and is placed outside the recorded area
  where possible.
* The mouse pointer is included only if **Cursor** is on.

Everything else that is visible in the recorded area, and everything you hear (system audio) or say
(microphone) while recording, **is in the file**. Videos cannot be redacted or sanitized. Close private windows
and notifications before you record.

### Privacy indicators

* Windows draws its own **capture border** around what is being recorded. SnapForge never turns it off.
* When the microphone is on, Windows shows its **microphone indicator**. SnapForge opens the microphone only when
  **Microphone** is on; turning it off on the toolbar releases the device.

### Locking, sleep, low disk space and closing

* **Locking the PC** pauses the recording. The toolbar says "Paused because the PC was locked." Press **Resume**
  to continue.
* **Sleep** stops and saves the recording before the PC goes to sleep.
* **Signing out, shutting down or exiting SnapForge** stops and saves the recording.
* **Low disk space:** when the estimated remaining recording time falls below 10 minutes, the toolbar warns you.
  When free space on the Library's drive falls below 500 MB (change it in **Settings → Capture Library →
  Recording**), the recording stops and is saved.
* A recording of a **window** follows the window. If the window is minimised, no new frames arrive and the last
  frame repeats.
* A **region** that spans several displays is cut to the display that holds most of it.

### Crash recovery

SnapForge writes recordings in a crash-safe form while recording. If SnapForge, Windows or the power fails during
a recording, SnapForge recovers it the next time it starts and shows a notification, for example "Recovered 42.0
s of an interrupted recording." The last one or two seconds before the interruption may be missing. Recovered
recordings are marked **Recovered** in the Library.

### Playing a recording

Double-click a video in the Library (or choose **Play**). The player has **Play**/**Pause**, a position slider,
**Mute** and volume. Keys: **Space** play/pause, **Left**/**Right** arrow 5 seconds back/forward, **Esc** close.

---

## 10. Video editing

SnapForge can trim and cut your recordings, save single frames and export MP4 or GIF files. **The original
recording is never changed.**

### Open the video editor

In the Library, right-click a video and choose **Edit video (trim, frames, GIF)…**.

### Move through the video

* **Play** / **Pause** (**Space**).
* Drag the position slider.
* **◀ Frame** (**,**) and **Frame ▶** (**.**) move one frame back or forward.

The time display shows the current position and the total length. The status line says which parts are kept and
how long the result will be.

### Trim the start and end

1. Move to where the video should start and click **Start here [** (or press **[**).
2. Move to where it should end and click **End here ]** (or press **]**).

### Remove a section from the middle

1. Move to where the unwanted part begins and click **Mark section** (or press **M**).
2. Move to where it ends and click **Mark section** again. The section is removed from the export.
3. **Undo removal** puts back the last removed section. **Reset** keeps the whole video again.

### Save a frame as an image

Move to the frame you want and click **Save frame to Library**. It is saved as a new image capture named like
"<video title> — frame at 0:12.3", which you can then edit, redact and export like any screenshot.

### Export the edited video

1. Choose **Standard quality** or **High quality** in the list.
2. Click **Save edited video to Library** (a new recording named "<title> (edited)"), or **Export MP4…** to save
   a file.

You must trim or remove something first. To copy the whole video unchanged, use **Export** in the Library
instead.

### Export a GIF

1. Trim the start and end first (a GIF uses the range from **Start here** to **End here**; removed sections in the
   middle are ignored).
2. Click **Export GIF…**.
3. Choose the **Frame rate** (5, 10, 15, 20 or 25; default 10), the **Width (px)** (default up to 800), **Dither
   (smoother gradients, larger file)** and **Loop forever** (on by default).
4. Click **OK** and choose where to save.

GIFs have at most 256 colours and no sound. GIFs of long recordings become very large; SnapForge warns you when
the range is longer than 60 seconds.

**Esc** closes the video editor.

---

## 11. The Library

The Library holds every capture and recording you keep. Open it with the **Library** button, **View → Library**
or **Ctrl+L**.

### Views (left sidebar)

* **All Captures** – everything except the Trash.
* **Recent (7 days)** – captures created or opened in the last seven days.
* **Favorites** – captures you marked as favourite.
* **Videos** – screen recordings only.
* **Trash** – deleted captures.
* **Tags** and **Folders** – one entry per tag or folder you created.
* **Storage** – number of captures, space used, Trash size and the folder where the Library is stored, with a
  **Storage & privacy settings…** button.

Each view shows how many items it contains.

### Browsing

* Captures are grouped by date (Today, Yesterday, Earlier this week, Earlier this month, then by month) when
  sorted by date.
* **Size** slider: changes the thumbnail size.
* Video tiles show a play badge, the duration and the file size.
* Point at a tile to see details such as tags, folder, video information or warnings.
* Tiles can be marked **Damaged** (the capture cannot be opened), **Recovered** (a recording saved after an
  interruption) or **Unsanitized redactions**.

### Opening

* **Double-click** a tile or press **Enter** to open it: images open in the Editor, videos in the player.
* The **Open** button does the same.

### Selecting

* Click to select. **Ctrl+click** adds or removes; **Shift+click** selects a range.
* **Ctrl+A** selects all. **Esc** clears the selection.
* Arrow keys, **Home** and **End** move between tiles.

### Search

Type in **Search captures…**. SnapForge searches titles, tags, folder names, dates (type them as `yyyy-mm-dd`,
for example `2026-11-25`) and image sizes (for example `1920x1080`). Type `video` or `recording` to find
recordings. Several words must all match.

### Sort

Click **Sort:** and choose **Newest first**, **Oldest first**, **Recently opened**, **Name (A–Z)** or
**Largest first** (largest picture size in pixels).

### Right-click menu

| Command | What it does |
|---|---|
| **Open in Editor** (**Play** for videos) | Opens the capture. |
| **Edit video (trim, frames, GIF)…** | Videos only. See [Video editing](#10-video-editing). |
| **Rename…** (**F2**) | Changes the title. |
| **Duplicate** | Images only. Makes an independent copy. |
| **Toggle favorite** | Adds to or removes from Favorites. |
| **Tags…** | Type tags separated by commas. With several captures selected, the tags you type replace their existing tags. |
| **Move to folder…** | Type a folder name. Leave it empty to remove the capture from folders. A capture is in one folder at most. |
| **Export as PNG/JPEG…** (**Export MP4 video…** for videos) | Saves a copy as a file. |
| **Export editable capture (.sfcap)…** | Images only. See [Editable captures](#editable-captures-sfcap). |
| **Sanitize…** | Images only. See [Sanitize](#sanitize). |
| **Move to Trash** (**Del**) | Moves to the Trash. |
| **Restore** | In the Trash: puts the capture back. |
| **Delete permanently…** | Deletes it for good (after asking). |

The toolbar above the tiles has **Import…**, **Open**, **Export…**, **Move to Trash** (in the Trash:
**Delete permanently…**), **Restore**, **Empty Trash…** and **More**. **More** contains **Rename…**,
**Duplicate**, **Combine images side by side**, **Combine images stacked**, **Toggle favorite**, **Tags…**, **Move
to folder…**, **Export editable capture (.sfcap)…**, **Sanitize…** and **Delete permanently…**.

### Import images and MP4 videos

1. Click **Import…** in the Library (or **File → Import images…**).
2. Choose PNG, JPEG, BMP or MP4 files. You can choose several.

You can also drag **image** files (PNG, JPEG, BMP) from File Explorer onto the SnapForge window. MP4 files can
only be imported with **Import…**. An MP4 must contain H.264 video that Windows can play.

### Export

* **One image:** choose **Export…**. You can pick PNG, JPEG, BMP or TIFF.
* **Several images:** choose **Export…**, then a folder. Each is saved as a PNG named after its title.
* **Videos:** one video is saved where you choose; several are copied into a folder you choose. The MP4 is copied
  as it is.

Exports are always the finished image, with annotations and redactions applied.

### Combine images

1. Select two or more images.
2. Choose **More → Combine images side by side** or **More → Combine images stacked**.

SnapForge creates a new capture named "Combined (N images)", with the images in the order shown, centred, with a
small white gap between them. The originals are not changed. Videos are skipped.

### Trash and restore

* **Move to Trash** (**Del**) does not delete anything yet. Open **Trash** to see deleted captures.
* **Restore** puts selected captures back.
* **Empty Trash…** deletes everything in the Trash permanently.
* **Delete permanently…** deletes the selected captures permanently. For images, SnapForge overwrites the files
  before removing them. For videos, the MP4 file is deleted without being overwritten first. Backups,
  file-recovery tools or SSD wear-levelling may still hold old copies.

### Retention

By default, captures that have been in the Trash for more than **30 days** are deleted permanently. You can also
have old captures moved to the Trash automatically. See [Settings → Capture Library](#capture-library). Retention
runs each time SnapForge starts, or when you click **Apply retention now**.

### Storage location and usage

* The Library is stored in `%LOCALAPPDATA%\SnapForge\Library` by default. Images are in its `captures` folder and
  recordings in its `videos` folder.
* The sidebar's **Storage** section and **Settings → Capture Library** show how many captures there are and how
  much space they and the Trash use.
* To move the Library, use **Settings → Capture Library → Change location…**. See
  [Capture Library](#capture-library).

---

## 12. Drag and drop

### Dragging captures out of SnapForge

You can drag captures into File Explorer, the desktop, Word and other Office apps, Outlook and other mail apps,
Teams and other chat apps, and web browsers (for example into an upload box).

Where you can drag from:

* the **Drag** button in the Editor toolbar (press and hold it, then drag),
* a thumbnail in the **Recent Captures** tray (or several selected thumbnails),
* a tile in the **Library** (or several selected tiles).

### What the other app receives

* **Images:** a **flattened** image file: annotations drawn in, redactions burned in, no metadata. If the capture
  is open in the Editor, the drag includes your latest changes, even unsaved ones. The format is **PNG** by
  default; you can choose **JPEG** in **Settings → Drag and drop**.
* **Videos:** a copy of the MP4 file.
* Editable captures and original pixels under redactions are **never** included. The Library itself is never
  changed by a drop.

### File names

Files are named after the capture's title and the time it was taken, for example
`Capture 2026-11-25 13.49.05.png`. If the title does not contain the date, the date and time are added, for
example `Login error 2026-11-25 134905.png`. Characters that Windows does not allow in file names are replaced,
and duplicates get " (2)", " (3)" and so on.

### Temporary copies and how long they are kept

To drop a file, SnapForge must create a real file. It creates these copies in
`%LOCALAPPDATA%\SnapForge\Temp\Drag`, only when the other app asks for them.

* If you cancel the drag (drop nowhere), the copies are deleted at once.
* After a drop, the copies are kept for **24 hours**, because some apps read the file later (a browser that
  uploads when you click Send, a mail app that attaches when you send). This also applies across restarts.
* If all copies together grow larger than **2 GB** (for example after dragging large videos), the oldest are
  removed early, but never one younger than 15 minutes.
* Leftover copies are cleaned up when SnapForge starts.

**Delete them now:** **Settings → Privacy → Delete drag-and-drop copies now**. Files still in use by another app
are skipped and removed later. Files are deleted normally (not overwritten). The apps you dropped them into keep
their own copies.

### Dragging files into SnapForge

Drop PNG, JPEG or BMP files from File Explorer onto the SnapForge window to import them into the Library. See
[Import](#import-images-and-mp4-videos).

### If a drag does not work

See [Troubleshooting: I can't drag into an app](#i-cant-drag-a-capture-into-an-app).

---

## 13. Keyboard shortcuts

### Global hotkeys (work from any app while SnapForge runs)

| Action | Default | Where to change it |
|---|---|---|
| Capture with the current settings | **Print Screen** | Settings → Hotkeys |
| Show / hide the capture panel | none | Settings → Hotkeys |
| Start recording; while recording, pause / resume | **Shift+F9** | Settings → Capture Library → Recording |
| Stop and save recording | **Shift+F10** | Settings → Capture Library → Recording |
| Cancel and discard recording | none | Settings → Capture Library → Recording |
| Capture with a preset | none | Capture panel → Presets → (preset) → Set hotkey… |

### Main window – anywhere

| Action | Keys |
|---|---|
| Keyboard shortcuts help | **F1** |
| New capture (current settings) | **Ctrl+N** |
| Capture region | **Ctrl+1** |
| Capture window | **Ctrl+2** |
| Capture display | **Ctrl+3** |
| Capture full screen (all displays) | **Ctrl+4** |
| Capture active window | **Ctrl+5** |
| Scrolling capture | **Ctrl+6** |
| Repeat last capture | **Ctrl+R** |
| New from clipboard | **Ctrl+Shift+V** |
| Editor | **Ctrl+E** |
| Library | **Ctrl+L** |
| Settings | **Ctrl+,** |

### Main window – with a capture open

| Action | Keys |
|---|---|
| Export | **Ctrl+S** |
| Print | **Ctrl+P** |
| Extract text (OCR) | **Ctrl+Shift+T** |
| Pin / unpin the capture in the tray | **Ctrl+Shift+P** |
| Close capture (Editor showing) | **Ctrl+W** |
| Rotate right / left 90° | **Ctrl+]** / **Ctrl+[** |
| Resize image | **Ctrl+Alt+I** |
| Canvas size | **Ctrl+Alt+C** |
| Actual pixels (1:1) | **Ctrl+Alt+0** |

### Editor (a capture showing, not typing text)

| Action | Keys |
|---|---|
| Undo | **Ctrl+Z** |
| Redo | **Ctrl+Y** or **Ctrl+Shift+Z** |
| Copy image | **Ctrl+C** |
| Copy annotation | **Ctrl+Shift+C** |
| Paste annotation (or clipboard image as a new capture) | **Ctrl+V** |
| Duplicate annotation | **Ctrl+D** |
| Zoom in / out | **Ctrl+=** / **Ctrl+−** (number pad **+** / **−** also work), or **Ctrl + mouse wheel** |
| Fit to window | **Ctrl+0** |
| Delete selected annotation | **Delete** or **Backspace** |
| Cancel (text, drag, then selection) | **Esc** |
| Select / Arrow / Line / Shape (rectangle) / Ellipse | **V** / **A** / **L** / **R** / **E** |
| Highlight / Text / Step / Pen / Callout / Stamp | **H** / **T** / **N** / **D** / **K** / **S** |
| Redact / Blur / Pixelate / Crop | **X** / **B** / **P** / **C** |
| While drawing: straight 45° lines, squares, circles | hold **Shift** |
| While typing text: finish / new line / cancel | **Enter** / **Shift+Enter** / **Esc** |

### Selecting an area on screen

| Action | Keys |
|---|---|
| Move the pointer 1 pixel / 10 pixels | **Arrow keys** / **Shift + arrow keys** |
| Cancel | **Esc** (or right-click) |
| Cancel a delay or countdown | **Esc** |

### Library

| Action | Keys |
|---|---|
| Open selected capture | **Enter** (or double-click) |
| Rename | **F2** |
| Move to Trash | **Del** |
| Select all | **Ctrl+A** |
| Clear selection | **Esc** |
| Move between tiles | **Arrow keys**, **Home**, **End** |

### Capture panel

| Action | Keys |
|---|---|
| Capture / Record | **Enter** |
| Close the panel | **Esc** |

### Scrolling capture window

| Action | Keys |
|---|---|
| Add frame | **Space** |
| Finish | **Enter** |
| Cancel | **Esc** |

### Video editor

| Action | Keys |
|---|---|
| Play / pause | **Space** |
| One frame back / forward | **,** / **.** |
| Start here / End here (trim) | **[** / **]** |
| Mark section (press twice) | **M** |
| Close | **Esc** |

### Video player

| Action | Keys |
|---|---|
| Play / pause | **Space** |
| 5 seconds back / forward | **Left** / **Right** arrow |
| Close | **Esc** |

---

## 14. Settings

Open Settings with **File → Settings…** (**Ctrl+,**), the capture panel's **File → Settings…**, the
notification-area menu or the widget's **Presets → Widget settings…**. Changes take effect at once. Click
**Close** when you are done.

### Appearance

Choose **Windows setting** (follows light or dark mode in Windows), **Light** or **Dark**. The same choice is in
the main window's **View → Theme** menu.

### Behaviour

* **Keep running in the notification area when the window is closed (hotkeys keep working)** – on by default.
* **Show the capture panel when SnapForge starts** – on by default.
* **Keep the capture panel on top of other windows** – off by default.
* **Launch SnapForge when I sign in to Windows (starts in the notification area)** – off by default. This is a
  per-user setting and needs no administrator rights. You can also turn it off in Windows **Settings → Apps →
  Startup**. If the setting points to another copy of SnapForge (for example an older portable folder), SnapForge
  tells you; turn it off and on again to use the copy you are running.

### Capture widget

* **Show the capture widget at the screen edge** – on by default.
* **Keep the widget on top of other windows** – on by default.
* **Auto-hide: shrink to a thin strip until the pointer touches it** – off by default.
* **Open the widget:** **On hover** (default) or **On click**.
* **Move widget to top centre** – puts it back at the default place.

### Drag and drop

* **Dragged captures become** **PNG (lossless)** (default) or **JPEG (smaller)**.
* **JPEG quality (1–100)** – default 92. Only used for JPEG.

### Hotkeys

* **Capture (current settings)** – default **Print Screen**.
* **Show / hide capture panel** – none by default.

To change a hotkey, click in its box and press the key combination. **Backspace** or **Delete** (or the
**Clear** button) removes it. Only Print Screen, Pause and the function keys can be used without Ctrl, Alt, Shift
or Win; Shift alone is not allowed with ordinary keys.

Below the boxes, SnapForge lists every global hotkey with ✓ (working) or ✗ (not available, with the reason),
including hotkeys used twice. Recording hotkeys are under **Capture Library → Recording**; preset hotkeys are set
in the capture panel (**Presets → (preset) → Set hotkey…**).

### Capture Library

**Location**

* Shows the folder where the Library is stored.
* **Open folder** opens it in File Explorer.
* **Change location…** moves the whole Library (images and recordings) to an **empty** folder you choose.
  SnapForge copies and checks every capture, then removes the old copies. Avoid folders synced by OneDrive,
  Dropbox or similar services unless you want your captures uploaded by that service.
* The usage line shows the number of captures, the space used and the Trash.

**Recording**

* **Microphone (used when Microphone is on):** **Windows default microphone** (follows Windows when the
  default changes) or a specific device.
* **Record / pause / resume:** default **Shift+F9**.
* **Stop recording:** default **Shift+F10**.
* **Cancel recording:** none by default.
* **Save recordings to the Library** – on by default. Off: when you stop, SnapForge asks where to save the MP4 and
  keeps no copy in the Library.
* **Open the recording when it is saved** – on by default.
* **Stop and save when free disk space falls below (MB):** default 500 (100 to 100,000).

**Retention**

* **Permanently delete captures that have been in the Trash for more than (days)** – on, 30 days by default.
* **Move captures older than (days) to the Trash (favorites are kept)** – off by default (365 days when you turn
  it on).
* **Apply retention now** – runs the rules immediately.
* **Empty Trash…** – deletes everything in the Trash permanently.

**Backup**

* **Back up library…** and **Restore from backup…**. See [Backup and restore](#15-backup-and-restore).

### Privacy

* A line says how many Library captures still keep original pixels under their redactions.
* **Sanitize all captures with redactions…** – sanitizes all of them after asking.
* **Show privacy & storage notice** – the same notice as **Help → Where are my captures?**.
* **Delete drag-and-drop copies now** – see [Drag and drop](#temporary-copies-and-how-long-they-are-kept).

### Reset

**Reset all preferences…** restores every preference to its default: hotkeys, capture options, presets, widget,
recording and appearance. **Your captures, recordings and the Library location are kept.**

---

## 15. Backup and restore

### What a backup contains

**Settings → Capture Library → Back up library…** saves a `.zip` file (default name like
`SnapForge Library 2026-11-25.zip`) with:

* **all image captures**, including those in the Trash, as editable captures,
* including **original pixels under redactions that are not sanitized**.

A backup does **not** contain:

* **screen recordings** (videos),
* your settings.

Store backups somewhere safe. Anyone with the file can open your captures and undo unsanitized redactions.
Sanitize first if that matters.

### Back up your recordings separately

1. Exit SnapForge (notification-area icon → **Exit SnapForge**).
2. Open the Library folder (**Settings → Capture Library → Open folder**, or paste
   `%LOCALAPPDATA%\SnapForge\Library` into the File Explorer address bar).
3. Copy the **videos** folder to your backup drive.

To restore recordings, exit SnapForge and copy the folders inside your backed-up **videos** folder back into the
Library's **videos** folder. SnapForge finds them the next time it starts.

### Restore images

1. Open **Settings → Capture Library → Restore from backup…**.
2. Choose the backup `.zip` file.

Restoring adds captures that are not already in the Library. It **never overwrites** existing captures; if every
capture is already there, SnapForge says "Nothing to restore".

---

## 16. Upgrading

Your captures, recordings and settings are stored in `%LOCALAPPDATA%\SnapForge`, not in the program folder, so
upgrading keeps them.

**Installed version**

1. Exit SnapForge (notification-area icon → **Exit SnapForge**). If a recording is running, stop it first. Setup
   asks you to exit SnapForge if it is still running.
2. Run the new installer. It installs over the old version.

**Portable version**

1. Exit SnapForge.
2. Replace the files in your SnapForge folder with the files from the new ZIP (or extract the new ZIP into a new
   folder).
3. If you use **Launch SnapForge when I sign in to Windows** and you put the new version in a different folder,
   turn that setting off and on again in the new version.

If an older SnapForge is still running when you start the new one, SnapForge tells you which build is running and
offers to close it. See [Troubleshooting](#another-copy-of-snapforge-is-already-running).

Making a backup before an upgrade is a good idea, especially for a release candidate. See
[Backup and restore](#15-backup-and-restore).

---

## 17. Uninstalling

### Installed version

1. Exit SnapForge.
2. Open Windows **Settings → Apps → Installed apps**, find **SnapForge**, click **…** and choose **Uninstall**.
3. When the program has been removed, the uninstaller asks whether you also want to **delete your captures,
   recordings and settings**. The default answer is **No** (keep them).
4. If you answer **Yes**, it asks a second time ("Are you sure?") before deleting `%LOCALAPPDATA%\SnapForge`.

The uninstaller always removes the temporary drag-and-drop copies and the "Launch SnapForge when I sign in"
entry of this installation. A Library you moved to another folder is **never** deleted by the uninstaller.

### Portable version

1. If you turned on **Launch SnapForge when I sign in to Windows**, turn it off first in **Settings →
   Behaviour** (or in Windows **Settings → Apps → Startup**).
2. Exit SnapForge.
3. Delete the SnapForge folder.

Your captures, recordings and settings stay in `%LOCALAPPDATA%\SnapForge`.

### Removing your data yourself

To delete your captures, recordings and settings too, delete the folder `%LOCALAPPDATA%\SnapForge` (paste the path
into the File Explorer address bar). If you moved the Library, delete that folder as well. **This permanently
deletes your captures.** Back up first if you might need them.

---

## 18. Troubleshooting

### Print Screen opens the Snipping Tool instead of SnapForge

Windows 11 uses Print Screen for the Snipping Tool by default.

1. Open Windows **Settings → Accessibility → Keyboard**.
2. Turn off **Use the Print screen key to open screen capture**.
3. Exit SnapForge and start it again.

Or set another capture hotkey in **Settings → Hotkeys**.

### A hotkey says "unavailable" or does nothing

Another program (or Windows) is using that key combination. Check **Settings → Hotkeys**: the list shows each
hotkey with ✓ or ✗ and the reason. Choose a different combination. Hotkeys only work while SnapForge is running
(check for its icon in the notification area).

### The widget is missing

Choose **View → Show capture widget** in the main window, or right-click the notification-area icon and choose
**Show capture widget**. If **Auto-hide** is on, the widget is only a thin strip at the edge; point at it. See
[Recovering a missing widget](#recovering-a-missing-widget).

### I can't find SnapForge after closing its window

It is still running in the notification area. Click the **^** arrow next to the clock and click the SnapForge
icon, or start SnapForge again from the Start menu (this brings the running copy to the front).

### Windows SmartScreen says "Windows protected your PC"

This release candidate is **not code-signed**, so SmartScreen does not recognise it.

1. Make sure you got the file from the official SnapForge project and that its SHA-256 checksum matches (see
   [INSTALLATION.md](INSTALLATION.md#verifying-the-download-sha-256)).
2. Only then click **More info** and **Run anyway**.

Keep Microsoft Defender and SmartScreen turned on. If Defender reports the file as harmful, do not run it; report
it to the project instead.

### "No text-recognition language is installed" (OCR)

Install the **Optical character recognition** feature for a language in Windows. See
[Installing a text-recognition language](#installing-a-text-recognition-language-in-windows).

### The recording has no sound

* Check that **System audio** (and/or **Microphone**) was on in the capture panel **before** you started. If both
  were off at the start, the recording has no sound track and the toolbar switches cannot add one.
* Check the level meters on the toolbar while recording. If they do not move, check the sound in Windows (volume,
  output device, mute).
* For the microphone, check **Settings → Capture Library → Recording → Microphone** and Windows **Settings →
  Privacy & security → Microphone** (microphone access for desktop apps must be allowed).
* If a device is unplugged during the recording, the toolbar shows a notice; the video continues.

### The recording paused or stopped when I locked the PC or it went to sleep

That is intended. Locking the PC **pauses** the recording; unlock and press **Resume** on the toolbar (or
**Shift+F9**). Sleep **stops and saves** the recording, because Windows' screen capture does not survive sleep
reliably. Start a new recording after waking the PC.

### Screen recording says the Windows media components are missing (Windows N editions)

Windows "N" editions do not include the media components SnapForge needs. Install Microsoft's **Media Feature
Pack**: open Windows **Settings → System → Optional features** (on some versions **Settings → Apps → Optional
features**), click **View features**, find **Media Feature Pack**, install it and restart the PC.

### I can't drag a capture into an app

* Start the drag from the **Drag** button (press and hold, then move), a tray thumbnail or a Library tile.
* Some apps accept only certain file types. Try **Settings → Drag and drop → JPEG**, or use **Copy** (**Ctrl+C**)
  and paste instead.
* Apps running **as administrator** cannot receive drags from normal apps (a Windows rule). Use copy and paste,
  or export a file.
* If the status bar says "Drag failed: …", the copy could not be created, for example because the disk is full.
* Damaged captures and captures in the Trash cannot be dragged.

### The Library's disk is full

* SnapForge never damages existing captures when the disk is full; a capture that cannot be saved shows "⚠ Not
  saved" and a warning banner. Your edits are still open: export them or free disk space.
* Free space: empty the Trash (**Settings → Capture Library → Empty Trash…**), delete large recordings, or delete
  drag copies (**Settings → Privacy → Delete drag-and-drop copies now**).
* Or move the Library to a larger drive: **Settings → Capture Library → Change location…** (choose an empty
  folder).
* Recordings stop and are saved automatically when free space falls below the limit (500 MB by default). A
  recording is converted into a standard MP4 when it stops; this needs free space about as large as the
  recording. Without it, the recording is kept in its recording form. It plays, but Windows players may show
  sound slightly out of step; the tile's tooltip says so.

### "Another copy of SnapForge is already running"

Only one SnapForge can run at a time, even if you have several copies (for example an installed version and a
portable folder).

* If it is the **same build**, starting SnapForge again just brings the running copy to the front.
* If it is a **different build**, SnapForge shows which build is running and where, and asks whether to close the
  running copy and start this one. Its captures are saved first. Both copies use the same Library.
* If the running copy does not close (it may be waiting for an answer in one of its windows), close it from its
  notification-area icon and try again.

### Setup says SnapForge is running

Exit SnapForge first: right-click the notification-area icon and choose **Exit SnapForge**. Stop any recording
first. Then click **OK** in Setup.

### Colours look washed out on an HDR display

Screenshots are taken with Windows' standard desktop copy, which converts HDR content to normal (SDR) colours.
On an HDR display, captures can therefore look paler or different from the screen. If exact colours matter, turn
off **Use HDR** in Windows **Settings → System → Display** while capturing. SnapForge 1.0 has not been tested on
HDR displays.

### Scrolling capture says the frame "could not be matched"

The new frame does not overlap the previous one, or the content changed. Scroll back a little and press **Add
frame** again, scroll less each time, close pop-ups and pause animations, or press **Finish** to keep what you
have. Some apps cannot be captured this way. See [Scrolling capture](#5-scrolling-capture).

### The window I captured has other windows on top of it

Window capture uses the frozen screen, so anything overlapping the window is also in the picture. Bring the window
to the front (or move other windows away) before capturing.

### A capture is marked "Damaged"

Its files are incomplete or changed, so SnapForge does not open it. It is left as it is in the Library folder.
Restore it from a backup if you have one.

### "This recording cannot be played"

The video file is damaged or incomplete. Recordings interrupted by a crash are recovered at the next start where
possible; a recording that never wrote any complete part cannot be recovered.

### "Something went wrong"

SnapForge keeps running after an unexpected error and saves details (error text and Windows version only, never
images) to `%LOCALAPPDATA%\SnapForge\logs`. If you report the problem, you can attach that text file. Read it
first; do not attach screenshots that contain private information.

---

## 19. Privacy summary

* **Local only.** SnapForge has no network features: no upload, sync, account, telemetry or update checks.
* **Captures are kept by default.** Every screenshot and recording is saved to your Library on this PC
  (`%LOCALAPPDATA%\SnapForge\Library` unless you moved it). Turn off **Save to Library** (or **Save recordings to
  the Library**) to capture without keeping a copy, or use the **Full screen to Clipboard (no history)** preset.
* **Not encrypted by SnapForge.** The Library is protected by your Windows account. Use BitLocker or device
  encryption to protect the disk.
* **Cloud folders.** If you move the Library into a OneDrive, Dropbox or similar folder, that service uploads it.
* **Exports are clean.** Files, clipboard images, prints and drags contain only the finished image, with
  redactions burned in and no metadata.
* **Editable copies keep originals** under redactions until you sanitize: the Library copy, `.sfcap` files and
  backups.
* **Recordings** contain everything visible and audible while recording; they cannot be redacted.
* **Temporary drag copies** are kept for up to 24 hours (2 GB cap) and can be deleted at any time.
* **Deleting** moves captures to the Trash first. Permanent deletion overwrites image files before removing them
  (not videos), but SSDs, backups, shadow copies and cloud sync can keep old data.
* **Error logs** contain text only, never images.
* **Other places your data may be:** Windows clipboard history (**Win+V**), files you exported, apps you dropped
  files into, and backups you made.

More detail: [SECURITY.md](SECURITY.md) and [REDACTION.md](REDACTION.md).

---

## 20. Known limitations

**General**

* This is a release candidate. It has not been released publicly, and testing on a wide range of physical PCs
  is still in progress.
* Supported platform: Windows 11, 64-bit (x64), version 24H2 or later. Windows 10, Windows on Arm and other
  systems are not supported.
* The program is **not code-signed**, so SmartScreen warns when you run it.
* Only one SnapForge can run at a time. All copies on a PC share the same settings and Library.
* The user interface is in English only.
* **All-in-One** mode, **Effects** and **Share** destinations in the capture panel are not available yet.

**Screenshots and editing**

* Window capture includes windows that overlap the chosen window.
* HDR content is converted to normal colours and may look washed out.
* Annotations have no resize handles; their size follows the thickness setting.
* The Redact colour is always black. The size of a black box reveals the length of what is hidden.
* **Repeat last capture** remembers the last area only until SnapForge exits.
* Text recognition depends on the Windows OCR languages installed and is not always exact.
* Scrolling capture works only where content scrolls inside a fixed area, downwards; it does not work in every app.

**Library, backup and privacy**

* The Library is not encrypted by SnapForge.
* Secure deletion cannot be guaranteed on SSDs, with shadow copies, File History, cloud sync or backups. Deleting a
  video removes the file without overwriting it.
* Library backups contain images only; back up the `videos` folder separately.
* Settings are not included in backups.
* Dropping MP4 files onto the window does not import them; use **Import…**.

**Screen recording**

* Videos cannot be redacted, blurred or sanitized.
* A region that spans several displays is cut to the display holding most of it.
* A minimised window produces no new frames (the last frame repeats).
* After a crash, the last one or two seconds may be lost.
* Converting a recording to a standard MP4 needs free space about as large as the recording; without it the
  recording form is kept and Windows players may show sound slightly out of step. There is no later
  re-conversion.
* Very high resolutions (such as 4K at 60 FPS) need a hardware video encoder; without one, recording may not keep
  up.
* Locking the PC pauses a recording; sleep stops it.
* Recording on real audio hardware, multiple monitors, hardware encoders, Bluetooth devices, sleep, lock and power
  loss has not yet been fully verified on physical PCs.
* GIF export has at most 256 colours and no sound; long GIFs become very large.
