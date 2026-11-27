# SnapForge 1.0 — requirements traceability

Release candidate `1.0.0-rc.1` on branch `release/v1.0-candidate`. Every requirement has an ID, priority, status, the
source files that implement it, measurable acceptance criteria, automated test references, the manual test still
required, the environment the evidence comes from and remaining limitations. **Implementation alone is not
verification.**

**Statuses:** Not started · In progress · Implemented (no automated evidence yet) · Automated test passed (Windows CI
or platform tests; physical-PC test may still be listed) · Manual verification pending (automated evidence exists
but a physical-PC test is required for acceptance) · Verified (by design/inspection or owner test) · Blocked (external
action) · Deferred with justification.

**Environments:** SIM = platform-independent unit tests (Linux CI and locally) · WCI = GitHub-hosted Windows Server
2025 runner (real desktop, no GPU, no audio devices) · PC = physical Windows 11 PC (owner acceptance; pending unless
stated). Evidence links: [RELEASE-CANDIDATE-HANDOFF.md](RELEASE-CANDIDATE-HANDOFF.md).

## Summary

| Priority | Count |
|---|---|
| P0 | 63 |
| P1 | 48 |
| P2 | 2 |

| Status | Count |
|---|---|
| Manual verification pending | 52 |
| Automated test passed | 42 |
| Implemented | 10 |
| Verified | 4 |
| Blocked | 3 |
| Deferred with justification | 2 |

P0 = release blocker · P1 = required professional functionality (implemented or presented for owner disposition) ·
P2 = enhancement.

## Matrix


### CAP

| ID | P | Requirement | Status | Source | Acceptance criteria | Automated tests | Manual test still required | Env | Evidence | Limitations |
|---|---|---|---|---|---|---|---|---|---|---|
| CAP-01 | P0 | Full-screen capture (all displays) | Manual verification pending | Capture/ScreenCaptureService.cs, Services/CaptureController.cs | Image equals virtual desktop bounds; no SnapForge window in it | ScreenCaptureTests; InteractiveUiTests | PC: multi-monitor | WCI | App tests (CI) | — |
| CAP-02 | P0 | Individual monitor capture | Manual verification pending | Capture/OverlaySession.cs | Click a display → exactly that monitor's pixels | InteractiveUiTests (display) | PC: 2+ monitors, mixed DPI | WCI | App tests (CI) | CI has one monitor |
| CAP-03 | P0 | Region capture, pixel-accurate, size label in pixels | Automated test passed | Core/Capture/SelectionTracker.cs, Capture/SelectionOverlayWindow.cs | Drag → exact physical-pixel rectangle; label shows W × H | SelectionTrackerTests; InteractiveUiTests (real mouse) | PC: mixed DPI | SIM+WCI | Core+App tests | — |
| CAP-04 | P0 | Window capture (click a window) | Automated test passed | Core/Capture/WindowPicker.cs | Click → window bounds (DWM frame) | InteractiveUiTests | PC: maximised/snapped windows | WCI | App tests | — |
| CAP-05 | P1 | Active-window capture (no click) | Automated test passed | Services/CaptureController.cs | Captures the foreground window of another program | ReleaseCandidateUiTests.Active_window_* | PC | WCI | App tests (CI run 77) | Falls back to topmost other window when SnapForge was in front |
| CAP-06 | P1 | Multi-monitor and negative coordinates | Manual verification pending | Core/Capture/ScreenLayout.cs | Selections across monitors with negative origins are exact | ScreenLayoutTests (SIM) | PC: monitor left of primary, portrait, mixed DPI | SIM | Core tests | CI single monitor |
| CAP-07 | P1 | Delayed capture with countdown (Esc cancels) | Automated test passed | Dialogs/InputDialog.cs (CountdownWindow) | Countdown then capture; Esc cancels | GateBUiTests (delay) | — | WCI | App tests | — |
| CAP-08 | P1 | Repeat last capture | Automated test passed | Services/CaptureController.cs, App.xaml.cs | Same screen rectangle without selection | ReleaseCandidateUiTests.Repeat_last_* | — | WCI | App tests (CI run 77) | In memory only (not after restart) |
| CAP-09 | P1 | Cursor inclusion/exclusion | Manual verification pending | ScreenCaptureService.cs | Pointer drawn only when enabled | ScreenCaptureTests (cursor) | PC visual check | WCI | App tests | — |
| CAP-10 | P1 | Capture from clipboard image | Automated test passed | MainWindow.xaml.cs (OnNewFromClipboard) | Clipboard image opens as a capture | ReleaseCandidateUiTests.New_from_clipboard_* | — | WCI | App tests (CI run 77) | — |
| CAP-11 | P0 | Capture cancellation (Esc / right-click) | Automated test passed | Capture/OverlaySession.cs | Nothing captured, windows restored | InteractiveUiTests | — | WCI | App tests | — |
| CAP-12 | P1 | Presets with own hotkeys | Automated test passed | Core/Settings/AppSettings.cs, Views/CaptureWindow.xaml.cs | Preset hotkey runs its options | GateBUiTests (presets) | PC: global hotkeys while another app has focus | WCI | App tests | — |
| CAP-13 | P0 | Configurable shortcuts, Print Screen, conflict reporting | Manual verification pending | Interop/ShellIntegration.cs | Conflicts reported with fix hint | TrayAndSettingsTests | PC: Print Screen with Snipping Tool setting on/off | SIM+WCI | Tests | Windows may reserve Print Screen |
| CAP-14 | P0 | Automatic Library saving and filenames | Automated test passed | Core/Library/CaptureLibrary.cs, Core/Export/ExportFileNames.cs | Every capture saved; names title + time, Windows-safe | LibraryWorkflowTests; ExportFileNames tests | — | SIM+WCI | Tests | — |
| CAP-15 | P1 | Magnifier and 1-px keyboard nudging during region selection | Implemented | Capture/SelectionOverlayWindow.cs | 8x loupe with X/Y and colour; arrows move 1 px | — (visual) | PC: check loupe at each monitor DPI | PC | Pending manual | — |
| CAP-16 | P0 | Overlays/controls not in captures | Automated test passed | CaptureController.cs (hide), RecordingWindows.cs (exclusion) | No SnapForge UI pixels in captures | InteractiveUiTests; video self-test toolbar-not-recorded | — | WCI | Tests | — |
| CAP-17 | P1 | HDR, Remote Desktop, virtual desktops | Manual verification pending | — | Captures work; HDR colours documented | — | PC: HDR display, RDP session | PC | Pending | HDR captured via GDI is tone-mapped by Windows (documented limitation) |
| CAP-18 | P1 | Protected content not bypassed | Verified | — | DRM/secure desktop content not captured (Windows returns black) | — | — | Design | Uses only standard GDI/WGC APIs | — |
| CAP-19 | P1 | Scrolling capture: auto-scroll, manual frames, stitching, cancel, size limit, preview | Automated test passed | Capture/ScrollingCapture.cs, Core/Capture/ScrollStitcher.cs | Stitched image equals the page; unalignable frames refused | ScrollStitcherTests (26); ScrollingCaptureUiTests (real wheel) | PC: browsers, Word, Explorer, long PDFs | SIM+WCI | Core + App tests | Only content scrolling inside a fixed area; apps that animate/redraw may not align; horizontal: Core only |
| CAP-20 | P0 | Capture panel: Image/Video/All-in-One modes, selection, options, presets, Capture button | Manual verification pending | Views/CaptureWindow.xaml(.cs) | Every control works or is disabled with explanation | GateBUiTests (panel) | PC walkthrough | WCI | App tests | All-in-One = Image capture with all options in one panel (documented) |

### WID

| ID | P | Requirement | Status | Source | Acceptance criteria | Automated tests | Manual test still required | Env | Evidence | Limitations |
|---|---|---|---|---|---|---|---|---|---|---|
| WID-01 | P0 | Widget enabled by default at top centre, topmost, non-activating | Automated test passed | Views/CaptureWidget.xaml.cs, Core/Settings/WidgetSettings.cs | Fresh settings → widget at top centre | GateBUiTests (widget) | PC | WCI | App tests | — |
| WID-02 | P0 | Expand on hover/click, collapse, Capture/Record/Editor/Library/Presets | Automated test passed | CaptureWidget.xaml.cs | Hover expands without focus | GateBUiTests | PC | WCI | App tests | — |
| WID-03 | P0 | Docking top/left/right on any monitor, remembered, DPI aware | Automated test passed | Core/Settings/WidgetSettings.cs | Placement persists; falls back when monitor removed | WidgetDock tests (SIM); GateBUiTests | PC: unplug monitor, change scaling | SIM+WCI | Tests | — |
| WID-04 | P0 | Hidden during capture; not in captures | Automated test passed | CaptureController.cs | Widget pixels absent | GateBUiTests (hidden during capture) | — | WCI | App tests | — |
| WID-05 | P1 | Recovery: View → Show capture widget; respects user disabling | Automated test passed | MainWindow.xaml.cs, App.xaml.cs | Command shows and peeks widget; disabled stays disabled on upgrade | GateBUiTests (peek) | PC | WCI | App tests | — |
| WID-06 | P1 | Optional launch with Windows | Manual verification pending | Services/StartupRegistration.cs | Per-user Run value; removed by uninstaller only if it points to the installation | TrayAndSettingsTests | PC: sign out/in | SIM+WCI | Tests | — |
| WID-07 | P1 | Avoid interfering with full-screen apps | Manual verification pending | CaptureWidget.xaml.cs | Widget not above exclusive full-screen apps | — | PC: full-screen video/game | PC | Pending | — |

### EDT

| ID | P | Requirement | Status | Source | Acceptance criteria | Automated tests | Manual test still required | Env | Evidence | Limitations |
|---|---|---|---|---|---|---|---|---|---|---|
| EDT-01 | P0 | Select, move, resize, recolour, delete annotations | Manual verification pending | Editor/AnnotationCanvas.cs | Works from menu/toolbar/shortcut; undoable where it changes the image | InteractiveUiTests | PC: visual check of output | SIM+WCI | Tests | — |
| EDT-02 | P0 | Crop | Manual verification pending | Core/Editing/CropCommand.cs | Works from menu/toolbar/shortcut; undoable where it changes the image | EditorDocumentTests | PC: visual check of output | SIM+WCI | Tests | — |
| EDT-03 | P1 | Rotate 90/180/270, flip H/V (annotations follow) | Manual verification pending | Core/Editing/TransformImageCommand.cs | Works from menu/toolbar/shortcut; undoable where it changes the image | TransformCommandTests; ReleaseCandidateUiTests | PC: visual check of output | SIM+WCI | Tests | Text keeps orientation under rotate |
| EDT-04 | P1 | Resize image (px/percent, aspect lock) | Manual verification pending | Core/Editing/ResizeImageCommand.cs, Dialogs/ImageSizeDialogs.cs | Works from menu/toolbar/shortcut; undoable where it changes the image | TransformCommandTests; feature self-test | PC: visual check of output | SIM+WCI | Tests | — |
| EDT-05 | P1 | Canvas size with anchor | Manual verification pending | Core/Editing/CanvasCommands.cs | Works from menu/toolbar/shortcut; undoable where it changes the image | TransformCommandTests | PC: visual check of output | SIM+WCI | Tests | — |
| EDT-06 | P0 | Arrow, line, rectangle, ellipse | Manual verification pending | Core/Annotations/* | Works from menu/toolbar/shortcut; undoable where it changes the image | AnnotationTests; InteractiveUiTests | PC: visual check of output | SIM+WCI | Tests | — |
| EDT-07 | P0 | Text | Manual verification pending | Core/Annotations/TextAnnotation.cs | Works from menu/toolbar/shortcut; undoable where it changes the image | InteractiveUiTests | PC: visual check of output | SIM+WCI | Tests | — |
| EDT-08 | P1 | Callouts | Manual verification pending | Core/Annotations/CalloutAnnotation.cs | Works from menu/toolbar/shortcut; undoable where it changes the image | NewAnnotationTests | PC: visual check of output | SIM+WCI | Tests | — |
| EDT-09 | P0 | Highlight | Manual verification pending | HighlightAnnotation.cs | Works from menu/toolbar/shortcut; undoable where it changes the image | AnnotationTests | PC: visual check of output | SIM+WCI | Tests | — |
| EDT-10 | P1 | Freehand pen | Manual verification pending | Core/Annotations/FreehandAnnotation.cs | Works from menu/toolbar/shortcut; undoable where it changes the image | NewAnnotationTests | PC: visual check of output | SIM+WCI | Tests | — |
| EDT-11 | P0 | Numbered steps | Manual verification pending | StepMarkerAnnotation.cs | Works from menu/toolbar/shortcut; undoable where it changes the image | AnnotationTests | PC: visual check of output | SIM+WCI | Tests | — |
| EDT-12 | P1 | Stamps (original vector artwork) | Manual verification pending | Core/Annotations/StampAnnotation.cs, Rendering/AnnotationRenderer.cs | Works from menu/toolbar/shortcut; undoable where it changes the image | NewAnnotationTests | PC: visual check of output | SIM+WCI | Tests | — |
| EDT-13 | P1 | Blur region (not redaction) | Manual verification pending | Core/Editing/ApplyRegionEffectCommand.cs | Works from menu/toolbar/shortcut; undoable where it changes the image | ImageOpsTests; ReleaseCandidateUiTests | PC: visual check of output | SIM+WCI | Tests | — |
| EDT-14 | P1 | Pixelate region (not redaction) | Manual verification pending | ApplyRegionEffectCommand.cs | Works from menu/toolbar/shortcut; undoable where it changes the image | ImageOpsTests; ReleaseCandidateUiTests | PC: visual check of output | SIM+WCI | Tests | — |
| EDT-15 | P1 | Borders | Manual verification pending | CanvasCommands.cs (AddBorderCommand) | Works from menu/toolbar/shortcut; undoable where it changes the image | TransformCommandTests | PC: visual check of output | SIM+WCI | Tests | — |
| EDT-16 | P1 | Drop shadow | Manual verification pending | CanvasCommands.cs (AddDropShadowCommand) | Works from menu/toolbar/shortcut; undoable where it changes the image | TransformCommandTests | PC: visual check of output | SIM+WCI | Tests | — |
| EDT-17 | P1 | Text watermark | Manual verification pending | Core/Editing/OverlayImageCommand.cs, Rendering/WatermarkRenderer.cs | Works from menu/toolbar/shortcut; undoable where it changes the image | OverlayImageCommandTests | PC: visual check of output | SIM+WCI | Tests | — |
| EDT-18 | P1 | Combine images (side by side / stacked) | Manual verification pending | Views/LibraryView.xaml.cs, Core/Imaging/ImageOps.Combine | Works from menu/toolbar/shortcut; undoable where it changes the image | ImageOpsTests (Combine) | PC: visual check of output | SIM+WCI | Tests | Combined in Library from flattened images (no layers) |
| EDT-19 | P0 | Zoom and pan, fit, 1:1 | Manual verification pending | MainWindow.xaml.cs | Works from menu/toolbar/shortcut; undoable where it changes the image | InteractiveUiTests | PC: visual check of output | SIM+WCI | Tests | — |
| EDT-20 | P0 | Undo/redo for every edit | Manual verification pending | Core/Editing/UndoRedoHistory.cs | Works from menu/toolbar/shortcut; undoable where it changes the image | EditorDocumentTests; TransformCommandTests | PC: visual check of output | SIM+WCI | Tests | — |
| EDT-21 | P1 | Quick Styles, saved styles, favourites | Manual verification pending | Core/Settings (QuickStyle) | Works from menu/toolbar/shortcut; undoable where it changes the image | TrayAndSettingsTests | PC: visual check of output | SIM+WCI | Tests | — |
| EDT-22 | P1 | Printing | Implemented | MainWindow.xaml.cs (OnPrint) | Works from menu/toolbar/shortcut; undoable where it changes the image | — | PC: visual check of output | PC | Pending manual | Prints the flattened image fitted to the page; no print preview |
| EDT-23 | P1 | Copy/paste/duplicate annotations | Implemented | MainWindow.xaml.cs | Works from menu/toolbar/shortcut; undoable where it changes the image | — | PC: visual check of output | PC | Pending manual | Annotation clipboard is in-app only |
| EDT-24 | P0 | Lossless PNG, JPEG quality, BMP, TIFF export; no metadata | Manual verification pending | Rendering/ImageExporter.cs, Core/Export/ImageFileFormat.cs | Works from menu/toolbar/shortcut; undoable where it changes the image | ExportTests; ExportRulesTests | PC: visual check of output | SIM+WCI | Tests | — |
| EDT-25 | P0 | Editable project (.sfcap) and flattened exports | Manual verification pending | Core/Storage/CaptureFormat.cs | Works from menu/toolbar/shortcut; undoable where it changes the image | CaptureFormat tests | PC: visual check of output | SIM+WCI | Tests | — |
| EDT-26 | P2 | Alignment, grouping, snapping, templates | Deferred with justification | — | — | — | — | — | — | Evaluated; deferred to 1.x (owner disposition): annotations are free-positioned; Shift constrains angles/squares |

### SEC

| ID | P | Requirement | Status | Source | Acceptance criteria | Automated tests | Manual test still required | Env | Evidence | Limitations |
|---|---|---|---|---|---|---|---|---|---|---|
| SEC-RED-01 | P0 | Permanent redaction in exports, clipboard, drag files, thumbnails, metadata | Automated test passed | Core/Editing/RedactCommand.cs, Rendering/ImageExporter.cs, Services/CaptureDragDrop.cs | Known sensitive pixel pattern absent from every output | Redacted_pixels_are_exactly_black_in_the_export; Clipboard_and_file_exports_share_the_redacted_flattened_image; Drag_data_offers_only_flattened_redacted_images_named_by_title_and_time; Redacted_secret_colour_appears_in_no_thumbnail_or_flattened_output; Region_effect_keeps_redactions_intact_and_does_not_leak_hidden_pixels; Resize_redaction_cover_hides_every_pixel_derived_from_hidden_content | — | SIM+WCI | Tests; docs/REDACTION.md | — |
| SEC-RED-02 | P0 | Sanitize removes originals from the editable Library copy (with confirmation) | Automated test passed | Core/Library/CaptureLibrary.cs (Sanitize) | After sanitize the base image contains no original pixels | Sanitize_removes_original_pixels_from_storage_and_hidden_annotations; Interrupted_sanitize_is_completed_on_next_open | — | SIM | Tests | Does not erase earlier backups, exports or SSD remnants (documented) |
| SEC-RED-03 | P0 | Blur/pixelate clearly not redaction; videos cannot be redacted | Verified | UI hints, docs | Labels and docs say so | — | — | Design | UI text + docs | — |

### OCR

| ID | P | Requirement | Status | Source | Acceptance criteria | Automated tests | Manual test still required | Env | Evidence | Limitations |
|---|---|---|---|---|---|---|---|---|---|---|
| OCR-01 | P1 | Offline OCR of a capture (Windows.Media.Ocr), review, copy | Manual verification pending | Ocr/OcrService.cs, Ocr/TextRecognitionWindow.cs | Known text recognised in order; nothing stored or sent | feature self-test ocr-recognize (UNAVAILABLE on runners without an OCR language) | PC: English OCR on screenshots | WCI/PC | Feature self-test report | Accuracy not guaranteed; language must be installed in Windows |
| OCR-02 | P1 | Language list/choice, clear missing-language and failure messages | Implemented | OcrService.cs | Messages name the Windows setting to change | feature self-test ocr-languages | PC: remove OCR language and retry | PC | — | — |
| OCR-03 | P1 | OCR from a Library image | Implemented | Open capture → Image → Extract text | Same as OCR-01 | — | PC | PC | — | From the Editor (open the Library item first) |
| OCR-04 | P1 | Tall images (scrolling captures) recognised in bands | Automated test passed | OcrService.cs | Last line found exactly once | feature self-test ocr-tall-image | — | WCI | Feature self-test | Requires OCR language on the machine |

### LIB

| ID | P | Requirement | Status | Source | Acceptance criteria | Automated tests | Manual test still required | Env | Evidence | Limitations |
|---|---|---|---|---|---|---|---|---|---|---|
| LIB-01 | P0 | Auto-save, thumbnails (images + videos), recent tray | Manual verification pending | Core/Library/*, Views/LibraryView.xaml.cs | As described; never silently deletes or overwrites captures | LibraryWorkflowTests; VideoLibraryTests | PC: real library, restore on a second PC | SIM+WCI | Tests | — |
| LIB-02 | P0 | Search, tags, folders, favourites, sort, filter, views | Manual verification pending | Core/Library/*, Views/LibraryView.xaml.cs | As described; never silently deletes or overwrites captures | LibraryQuery tests; LibraryWorkflowTests | PC: real library, restore on a second PC | SIM+WCI | Tests | — |
| LIB-03 | P0 | Rename, duplicate, multi-select, batch operations | Manual verification pending | Core/Library/*, Views/LibraryView.xaml.cs | As described; never silently deletes or overwrites captures | LibraryWorkflowTests | PC: real library, restore on a second PC | SIM+WCI | Tests | — |
| LIB-04 | P1 | Image import (PNG/JPEG/BMP, metadata stripped) | Manual verification pending | Core/Library/*, Views/LibraryView.xaml.cs | As described; never silently deletes or overwrites captures | ImageImport tests; GateBUiTests (import) | PC: real library, restore on a second PC | SIM+WCI | Tests | — |
| LIB-05 | P1 | Video import (MP4, validated by Windows decoder) | Implemented | Core/Library/*, Views/LibraryView.xaml.cs | As described; never silently deletes or overwrites captures | — | PC: real library, restore on a second PC | PC | Pending | — |
| LIB-06 | P0 | Export (images and MP4) | Manual verification pending | Core/Library/*, Views/LibraryView.xaml.cs | As described; never silently deletes or overwrites captures | ExportTests; LibraryWorkflowTests | PC: real library, restore on a second PC | SIM+WCI | Tests | — |
| LIB-07 | P0 | Trash, restore, permanent delete, retention | Manual verification pending | Core/Library/*, Views/LibraryView.xaml.cs | As described; never silently deletes or overwrites captures | CaptureLibraryTests | PC: real library, restore on a second PC | SIM+WCI | Tests | — |
| LIB-08 | P0 | Backup and restore (clean environment) | Manual verification pending | Core/Library/*, Views/LibraryView.xaml.cs | As described; never silently deletes or overwrites captures | CaptureLibraryTests (backup/restore) | PC: real library, restore on a second PC | SIM+WCI | Tests | Backup .zip contains images only; back up videos/ separately |
| LIB-09 | P1 | Configurable location, move library, storage usage | Manual verification pending | Core/Library/*, Views/LibraryView.xaml.cs | As described; never silently deletes or overwrites captures | CaptureLibraryTests (MoveTo) | PC: real library, restore on a second PC | SIM+WCI | Tests | — |
| LIB-10 | P1 | Background indexing; large collections | Manual verification pending | Core/Library/*, Views/LibraryView.xaml.cs | As described; never silently deletes or overwrites captures | Library scale benchmarks (100–25,000) | PC: real library, restore on a second PC | SIM+WCI | Tests | — |
| LIB-11 | P0 | Atomic writes, damaged items reported, duplicate IDs, missing files | Manual verification pending | Core/Library/*, Views/LibraryView.xaml.cs | As described; never silently deletes or overwrites captures | CaptureLibraryTests (damage), AtomicFile tests | PC: real library, restore on a second PC | SIM+WCI | Tests | — |
| LIB-12 | P0 | Upgrade/migration safety (older builds cannot corrupt videos; new annotation types refused, not dropped) | Manual verification pending | Core/Library/*, Views/LibraryView.xaml.cs | As described; never silently deletes or overwrites captures | VideoLibraryTests; AnnotationSerializer tests | PC: real library, restore on a second PC | SIM+WCI | Tests | Older builds show captures with new annotation types as damaged (not changed) |
| LIB-13 | P1 | Unavailable/read-only directories, disk full | Manual verification pending | Core/Library/*, Views/LibraryView.xaml.cs | As described; never silently deletes or overwrites captures | LibraryWorkflowTests.Disk_full_* | PC: real library, restore on a second PC | SIM+WCI | Tests | — |

### DRG

| ID | P | Requirement | Status | Source | Acceptance criteria | Automated tests | Manual test still required | Env | Evidence | Limitations |
|---|---|---|---|---|---|---|---|---|---|---|
| DRG-01 | P0 | Native drag-out (CF_HDROP) from tray, Editor, Library, videos; PNG/JPEG/MP4; multi-file | Automated test passed | Services/CaptureDragDrop.cs, MainWindow.DragDrop.cs | Separate-process drop target receives the files | Real_mouse_drag_* tests incl. separate process (video) | PC: Explorer, Outlook, Word, PowerPoint, Teams, Chrome, Edge | WCI | App tests | Real-app compatibility pending manual tests |
| DRG-02 | P0 | Only flattened redacted images leave via drag | Automated test passed | CaptureDragDrop.cs | No original pixels/metadata in dragged files | Drag_data_offers_only_flattened_redacted_images | — | WCI | App tests | — |
| DRG-03 | P0 | Safe temporary-file lifecycle (24 h retention, 2 GB cap, no-drop deletes immediately) | Automated test passed | Core/Export/TempExportStore.cs | Lazy readers can still read; cleanup bounded | Temp_export_* tests | PC: browser upload after minutes | SIM | Core tests | Deleted files are not overwritten (SSD); Settings → Delete drag copies now |

### VID

| ID | P | Requirement | Status | Source | Acceptance criteria | Automated tests | Manual test still required | Env | Evidence | Limitations |
|---|---|---|---|---|---|---|---|---|---|---|
| VID-01 | P0 | Region/window/display recording to MP4 (H.264/AAC) | Automated test passed | Video/ScreenRecorder.cs, Mp4Writer.cs | Valid MP4, constant frame rate | video self-test (screen-recording, screen-constant-frame-rate) | PC | WCI | Video self-test | — |
| VID-02 | P0 | System audio + microphone (off by default), device selection, levels, toggles | Manual verification pending | Video/Sources.cs, Core/Video/AudioMixer.cs | Mic off by default; levels move | AudioMixer tests (SIM) | PC: real devices | SIM | Tests | CI has no audio devices |
| VID-03 | P0 | Toolbar: timer, pause/resume/stop/cancel, not in recording; countdown; hotkeys | Automated test passed | Video/RecordingWindows.cs | Toolbar pixels absent from video | video self-test toolbar-not-recorded, pause-resume | PC | WCI | Video self-test | — |
| VID-04 | P0 | 30/60 FPS, Standard/High presets | Automated test passed | Core/Settings/VideoSettings.cs | 0 dropped at 1080p60 on CI | video self-test 60fps; benchmarks | PC: hardware encoder | WCI | Benchmarks | — |
| VID-05 | P0 | A/V sync and drift | Automated test passed | Core/Video/RecordingClock.cs, AudioMixer.cs | < 45 ms on synthetic flash/beep | video self-test av-sync | PC: real audio | WCI | ~0.1 ms measured | — |
| VID-06 | P0 | Crash-safe recording and recovery; no corrupt file presented as success | Automated test passed | Video/RecordingRecovery.cs, Core/Video/Mp4Defragmenter.cs | Killed process → recovered | CI crash-recovery step | PC: power loss | WCI | CI | — |
| VID-07 | P0 | Single session; bounded queues; low disk; device/display changes; lock/sleep | Manual verification pending | Video/RecordingController.cs | Second session refused; handled events | video self-test single-session | PC: unplug devices, Win+L, sleep, low disk | WCI/PC | Partial (CI) | Hardware events only testable on a PC |
| VID-08 | P0 | Hardware encoder with software fallback | Manual verification pending | Mp4Writer.cs | Encoder named; fallback works | video probe | PC: NVIDIA/AMD/Intel | PC | Pending | CI has no GPU |
| VID-09 | P0 | Playback, Library integration, export, MP4 drag | Automated test passed | Video/VideoPlayerWindow.cs | Plays; exports; drags | VideoLibraryTests; drag test | PC | WCI | Tests | — |

### VED

| ID | P | Requirement | Status | Source | Acceptance criteria | Automated tests | Manual test still required | Env | Evidence | Limitations |
|---|---|---|---|---|---|---|---|---|---|---|
| VED-01 | P1 | Precise seeking, frame stepping, trim start/end, remove sections | Automated test passed | Video/VideoEditorWindow.cs, Core/Video/VideoEditPlan.cs | Exported duration and segment content as planned; audio kept | VideoEditPlanTests; feature self-test video-trim-and-cut | PC: real recordings | SIM+WCI | Feature self-test | — |
| VED-02 | P1 | Extract still frame to Library | Automated test passed | VideoEditorWindow.cs (SaveFrame) | Correct frame saved | feature self-test video-frame-extract | — | WCI | Feature self-test | — |
| VED-03 | P1 | GIF export (size, fps, dithering, loop) | Automated test passed | Core/Imaging/GifEncoder.cs, Video/GifExport.cs | Valid GIF89a, decodes to frames | GifEncoderTests (34); feature self-test video-gif-export | PC: open in browsers | SIM+WCI | Tests | — |
| VED-04 | P1 | Export edited video with quality choice; original preserved | Automated test passed | VideoExporter.cs | New file/Library item; original unchanged | feature self-test | — | WCI | Feature self-test | — |
| VED-05 | P2 | Combine clips, webcam overlay, recording annotations, cursor highlight | Deferred with justification | — | — | — | — | — | — | Deferred to 1.x; not represented as available |

### SET

| ID | P | Requirement | Status | Source | Acceptance criteria | Automated tests | Manual test still required | Env | Evidence | Limitations |
|---|---|---|---|---|---|---|---|---|---|---|
| SET-01 | P0 | Organised settings incl. capture, recording, audio, hotkeys, widget, library, export, privacy, appearance, startup, storage | Manual verification pending | Views/SettingsWindow.xaml(.cs) | All persist | TrayAndSettingsTests | PC walkthrough | SIM+WCI | Tests | — |
| SET-02 | P1 | Reset preferences; corrupted settings recovery | Automated test passed | Views/SettingsWindow.xaml.cs; Core/Settings/AppSettings.cs (.damaged) | Corrupt file moved aside, defaults loaded | Damaged_settings_fall_back_to_defaults_and_keep_the_bad_file | PC | SIM | Tests | — |
| SET-03 | P0 | First-launch welcome; offline Help → Getting started | Implemented | Dialogs/WelcomeWindow.cs | Shown once on clean start; reachable from Help | — | PC: clean profile | PC | Pending | — |
| SET-04 | P0 | Build identification (version, build, commit, channel, architecture) in Help → About; exe metadata | Automated test passed | Services/InstanceInfo.cs, Directory.Build.props | Matches file version | Installer job prints file/product version | PC: Explorer → Properties | WCI | CI installer job | — |
| SET-05 | P0 | Existing-instance detection and safe handoff (no recording killed) | Automated test passed | App.xaml.cs | Different build asks; same build hands over | InstanceInfo tests | PC: run two builds | SIM | Tests | — |

### ACC

| ID | P | Requirement | Status | Source | Acceptance criteria | Automated tests | Manual test still required | Env | Evidence | Limitations |
|---|---|---|---|---|---|---|---|---|---|---|
| ACC-01 | P0 | Keyboard operation, focus visibility, accessible names, screen readers, high contrast | Manual verification pending | All windows | Manual procedure passes | Some automation names asserted in UI tests | PC: Narrator + NVDA (docs/MANUAL-ACCEPTANCE-1.0.md) | PC | Pending | Not claimed compliant from automated checks |

### SEC

| ID | P | Requirement | Status | Source | Acceptance criteria | Automated tests | Manual test still required | Env | Evidence | Limitations |
|---|---|---|---|---|---|---|---|---|---|---|
| SEC-01 | P0 | No network access, telemetry, accounts; least privilege | Verified | Whole app | No network code; per-user install | Code review; SECURITY.md | — | Design | Threat model | — |
| SEC-02 | P0 | Malformed image/video/package inputs rejected safely | Automated test passed | ImageImport, AnnotationSerializer, CaptureFormat, ImageFileImporter (MP4 via Windows decoder) | Rejected with message; no crash | Malformed_new_annotations_are_rejected; Corrupt_manifest_is_listed_as_damaged_and_other_captures_still_load; Rejects_non_fragmented_and_damaged_input_without_writing_garbage; ImageImport size checks | — | SIM | Tests | MP4 parsing relies on Windows Media Foundation |
| SEC-03 | P0 | Archive extraction safe (path traversal, size) | Automated test passed | CaptureLibrary.RestoreBackup / ImportPackage | Traversal entries rejected | Restore_rejects_path_traversal_and_foreign_zips | — | SIM | Tests | — |
| SEC-04 | P1 | Logs contain no capture content | Verified | logs | Text only | — | — | Design | SECURITY.md | — |
| SEC-05 | P0 | Confidential vulnerability reporting | Blocked | SECURITY.md, website | Private reporting enabled and tested | — | Owner enables GitHub private vulnerability reporting | — | — | Blocked on owner action |

### PKG

| ID | P | Requirement | Status | Source | Acceptance criteria | Automated tests | Manual test still required | Env | Evidence | Limitations |
|---|---|---|---|---|---|---|---|---|---|---|
| PKG-01 | P0 | Self-contained portable ZIP with checksums, license, notices | Automated test passed | .github/workflows/ci.yml | Runs with .NET removed; checksums verify | portable-without-dotnet job | PC: clean Windows 11 | WCI | CI | — |
| PKG-02 | P0 | Per-user installer: shortcuts, upgrade, uninstall keeps data, explicit data removal | Automated test passed | installer/SnapForge.iss | Install/upgrade/uninstall; data kept | installer job | PC: interactive install/uninstall, interrupted install | WCI | CI installer job | Interrupted-install test manual |
| PKG-03 | P0 | SBOM + dependency inventory | Automated test passed | tools/sbom/generate_sbom.py | CycloneDX JSON in packages | CI packages step | — | WCI | Package artifact | — |
| PKG-04 | P0 | Trusted code signing | Blocked | docs/SIGNING.md | Signed with publicly trusted certificate | — | Owner: SignPath Foundation application or certificate | — | — | Unsigned until owner action |

### PER

| ID | P | Requirement | Status | Source | Acceptance criteria | Automated tests | Manual test still required | Env | Evidence | Limitations |
|---|---|---|---|---|---|---|---|---|---|---|
| PER-01 | P1 | Startup ≤ 3 s on reference environment; responsive UI during indexing | Manual verification pending | StartupBenchmark.cs | Measured | Library scale benchmarks (startup) | PC reference | WCI | BENCHMARKS.md | — |
| PER-02 | P1 | Library at 100–25,000 captures | Automated test passed | tools/SnapForge.Benchmarks | Measured each size | Library scale benchmarks | — | WCI | CI benchmarks | — |
| PER-03 | P1 | Recording 1080p/1440p/4K × 30/60 × 5–120 min; <1 % drops; sync ≤ 80 ms | Manual verification pending | video-benchmarks.yml | Measured where feasible | VIDEO-BENCHMARKS.md | PC: hardware encoders, 1440p/4K displays | WCI | VIDEO-BENCHMARKS.md | 4K software encoding not real-time; 1440p/4K screen unsupported on runner |

### WEB

| ID | P | Requirement | Status | Source | Acceptance criteria | Automated tests | Manual test still required | Env | Evidence | Limitations |
|---|---|---|---|---|---|---|---|---|---|---|
| WEB-01 | P0 | Website on gh-pages, accurate claims, download disabled until approval, donations hidden | Automated test passed | website/ | build.py --check, browser tests | Website workflow | Owner: Pages enabled | WCI | Website CI | — |
| WEB-02 | P1 | High-resolution real screenshots gallery | Implemented | website/static/assets/screenshots | 1920×1080 PNG from the packaged build | Screenshot tour (CI) | Visual review | WCI | CI log PNGs | — |

### DOC

| ID | P | Requirement | Status | Source | Acceptance criteria | Automated tests | Manual test still required | Env | Evidence | Limitations |
|---|---|---|---|---|---|---|---|---|---|---|
| DOC-01 | P0 | User guide, installation, troubleshooting, privacy, limitations, developer docs | Implemented | docs/USER-GUIDE.md etc. | Matches behaviour | — | Review | — | Docs | — |

### REL

| ID | P | Requirement | Status | Source | Acceptance criteria | Automated tests | Manual test still required | Env | Evidence | Limitations |
|---|---|---|---|---|---|---|---|---|---|---|
| REL-01 | P0 | Release process, verification, rollback, maintenance | Implemented | docs/RELEASE-PROCESS.md, ROLLBACK.md, MAINTENANCE.md | Documented and dry-run where possible | website verifier tests | Owner approval | — | Docs | — |
| REL-02 | P0 | Legal/licensing review (H.264/AAC, name) | Blocked | docs/LICENSING-REVIEW.md | Qualified review | — | Owner: legal review | — | — | Research only; no legal conclusions |
