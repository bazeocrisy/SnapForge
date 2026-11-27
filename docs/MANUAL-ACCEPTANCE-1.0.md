# SnapForge 1.0 — Windows manual acceptance checklist

For the owner/testers on a **physical Windows 11 24H2+ x64 PC**. CI already covers what it can (see
[RELEASE-CANDIDATE-HANDOFF.md](RELEASE-CANDIDATE-HANDOFF.md)); this list is what only a real desktop can prove.
Record each result (Pass / Fail / N/A + notes, PC model, Windows build, display setup) in
`ACCEPTANCE-RESULTS-TEMPLATE.md`. Earlier detailed lists remain valid: `ACCEPTANCE-TESTING.md` (screenshots, widget,
drag-and-drop) and `VIDEO-ACCEPTANCE.md` (recording).

**Before you start:** back up `%LOCALAPPDATA%\SnapForge` if you have used an earlier build. Verify the package
checksums (`Get-FileHash <file>` against `SHA256SUMS.txt`). Check Help → About shows the expected version and commit.

## 1. Install, upgrade, uninstall
| ID | Check |
|---|---|
| MA-PKG-01 | Run the installer as a standard user: no administrator prompt; Start menu shortcut; optional desktop shortcut works |
| MA-PKG-02 | SmartScreen: note the exact message (expected for unsigned builds). Do not disable SmartScreen |
| MA-PKG-03 | Install again over itself (upgrade): settings, widget position and captures are kept |
| MA-PKG-04 | Cancel the installer half-way: nothing broken; re-run completes |
| MA-PKG-05 | Uninstall: program gone; the dialog asks about deleting captures (default No); with No, captures remain; second confirmation needed for Yes |
| MA-PKG-06 | Portable ZIP: extract to a new folder, run; same Library as the installed build (expected) |
| MA-PKG-07 | Launch with Windows on → sign out/in → SnapForge starts in the notification area; uninstall removes the entry |
| MA-PKG-08 | Start a second, different build while one runs → clear question; same build → brings the running one forward; a running recording is never stopped without asking |

## 2. First launch and help
| ID | Check |
|---|---|
| MA-SET-01 | Clean profile: welcome window explains capture, widget, Library location, recording, redaction, help; Help → Getting started reopens it offline (network unplugged) |
| MA-SET-02 | Settings: change each section, restart, values kept; Reset all preferences restores defaults and keeps captures |

## 3. Screenshots
| ID | Check |
|---|---|
| MA-CAP-01 | Print Screen (after turning off Windows' "Use the Print screen key to open screen capture") starts a capture; with it on, SnapForge reports the conflict |
| MA-CAP-02 | Region on each monitor, across two monitors, on a monitor left of/above the primary, on a portrait monitor, with 100 %/150 %/200 % scaling mixed — exact pixels, size label correct |
| MA-CAP-03 | Magnifier shows correct pixels and X/Y on every monitor; arrow keys move 1 px (Shift 10 px) |
| MA-CAP-04 | Window, Active window (Ctrl+5 from another app), Display, Full screen, Delay, Cursor on/off, Repeat last capture (Ctrl+R), New from clipboard |
| MA-CAP-05 | Remote Desktop session; HDR monitor (note colour differences); virtual desktop switch |
| MA-CAP-06 | Scrolling capture of: a long web page (Edge/Chrome), a Word document, File Explorer list, a PDF; Auto-scroll and manual. Check the result for gaps or repeated parts; note apps that cannot be aligned |

## 4. Widget
| ID | Check |
|---|---|
| MA-WID-01 | Clean start: widget at top centre; hover expands without stealing focus; all buttons work |
| MA-WID-02 | Dock left/right on the second monitor; restart → same place; unplug that monitor → widget returns to a visible place; View → Show capture widget recovers it |
| MA-WID-03 | Full-screen video/game: widget does not cover it; capture hides the widget |

## 5. Editor
| ID | Check |
|---|---|
| MA-EDT-01 | Every tool (arrow, line, rectangle, ellipse, highlight, text, step, pen, callout, stamp, redact, blur, pixelate, crop) draws, selects, moves, recolours, deletes; undo/redo |
| MA-EDT-02 | Rotate, flip, resize (px/percent), canvas size, border, shadow, watermark — annotations follow; undo restores |
| MA-EDT-03 | Copy/paste/duplicate annotations (also into another capture); Print to a real printer and to "Microsoft Print to PDF" |
| MA-EDT-04 | Export PNG/JPEG/BMP/TIFF: files open in Photos; file Properties → Details show no camera/location/author metadata |
| MA-EDT-05 | Extract text (OCR) on a real screenshot: text in reading order; copy works; with no OCR language installed the message explains how to add one |

## 6. Redaction and sanitization
| ID | Check |
|---|---|
| MA-RED-01 | Redact a password; export, copy, drag into WordPad/Paint: only black is visible; zooming/levels in an image editor reveals nothing |
| MA-RED-02 | Sanitize: confirmation shown; afterwards undo of the redaction is impossible; exported .sfcap contains no original pixels |

## 7. Library
| ID | Check |
|---|---|
| MA-LIB-01 | Search, tags, folders, favourites, sort, Trash/restore, permanent delete, retention |
| MA-LIB-02 | Import PNG/JPEG/BMP and an MP4 from a phone/camera; a non-H.264 or broken MP4 is rejected with a message |
| MA-LIB-03 | Combine 3 images side by side and stacked |
| MA-LIB-04 | Move the Library to a USB drive; unplug it while SnapForge runs; read-only folder; nearly full disk — clear messages, no lost captures |
| MA-LIB-05 | Back up, then restore into a clean Windows user account; captures open and are editable |
| MA-LIB-06 | 5,000+ captures: start-up time, search and scrolling stay responsive |

## 8. Drag and drop (record which apps accept files and which accept inline images)
| ID | Target |
|---|---|
| MA-DRG-01 | File Explorer, desktop |
| MA-DRG-02 | Outlook (new mail), Word, PowerPoint |
| MA-DRG-03 | Microsoft Teams chat |
| MA-DRG-04 | Chrome and Edge upload fields (e.g. a web mail attachment); wait several minutes before sending — file must still upload |
| MA-DRG-05 | Drag an MP4 from the Videos view into Explorer and a browser upload |

## 9. Recording and video editing
| ID | Check |
|---|---|
| MA-VID-01 | Region/window/display recording with system audio (play a video) and microphone; Windows capture border and microphone indicator visible; toolbar and outline not in the video |
| MA-VID-02 | Pause/resume/stop/cancel with hotkeys while another app has focus; countdown |
| MA-VID-03 | Unplug the microphone / switch to Bluetooth headphones mid-recording; lock (Win+L); sleep; low disk |
| MA-VID-04 | Hardware encoder in use (NVIDIA/AMD/Intel), 1080p60 and 1440p/4K if available: smooth, sound in sync |
| MA-VID-05 | Kill SnapForge in Task Manager while recording → next start recovers the video |
| MA-VID-06 | Edit video: trim, remove a section, save a frame, export MP4 (Library and file), export GIF; play results in Films & TV, Edge, Chrome; sound in sync after cuts |

## 10. Accessibility
| ID | Check |
|---|---|
| MA-ACC-01 | Keyboard only: reach every main command (menus Alt+letter, tools by letter keys, dialogs with Tab/Enter/Esc); focus is always visible |
| MA-ACC-02 | Narrator and NVDA: main window, capture panel, Settings, OCR window, video editor — controls have names; status messages are announced |
| MA-ACC-03 | Windows contrast themes (Aquatic/Desert/Night sky) and 150 %/200 % text size: everything readable, nothing cut off |
| MA-ACC-04 | Status never relies on colour only (badges, recording indicator have text) |

## 11. Performance (reference PC)
| ID | Check |
|---|---|
| MA-PER-01 | Start-up to usable window ≤ 3 s (cold start after reboot: note the time) |
| MA-PER-02 | 60-minute recording: CPU/GPU in Task Manager, file size, no dropped frames reported |
