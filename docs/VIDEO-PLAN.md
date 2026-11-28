# Milestone V1 — screen recording

Status: **Approved by the owner on 2026-11-26** (VID-01…VID-12, subject to the reliability requirements VID-R01…R14
and the decisions below). Implemented on branch `claude/video-v1` — **under independent review**, not merged.
V2 editing and V3 extras remain outside the approved scope.

**Owner decisions (2026-11-26):** Windows 11 first · system audio on by default · microphone off · cursor on · 30 FPS
default, 60 optional · countdown on by default, configurable · MP4 (H.264 + AAC) · Standard and High presets · no
subscriptions, paid dependencies, accounts or telemetry · separate feature branch from the verified Milestone 3
baseline · Help → About shows version and Git commit.

Where things are: requirements in PRODUCT-REQUIREMENTS.md (sections *V1 Screen recording*, *V1 Recording
reliability*, *V2/V3 Video*); architecture and API research VIDEO-ARCHITECTURE.md; threat model VIDEO-THREAT-MODEL.md;
Library format LIBRARY-VIDEO-FORMAT.md; benchmarks VIDEO-BENCHMARKS.md; checklist and known limitations
VIDEO-ACCEPTANCE.md; test results VIDEO-TEST-RESULTS.md.

**ID reconciliation.** The earlier backlog in PRODUCT-REQUIREMENTS.md also used VID-01…VID-11, with other meanings.
The approved list keeps VID-01…VID-12; backlog items outside V1 were renumbered so no ID means two things and nothing
is dropped: webcam → VID-V3-01 · draw on screen → VID-V3-02 · cursor highlight/click effects → VID-V3-03 · combine
clips → VID-V3-04 · video from images → VID-V3-05 · VFR / presets beyond V1 → VID-V3-06 · trim/cut → VID-V2-01 · save
frame → VID-V2-02 · animated GIF → VID-V2-03 (the rest map onto VID-01…12).

**Deviations from this plan, for the review:** (1) videos are stored in a separate `videos/` folder instead of a
version bump of the capture format — older builds never see them at all, which is stronger than rejecting them;
(2) the player opens in its own window, not in the Editor area (KL-V09); (3) technical approach uses direct COM/WinRT
interop instead of CsWinRT projections (no new packages; see VIDEO-ARCHITECTURE.md §1).

Snagit reference (DOC, search extracts only — S-LIB-21…27, S-CAP-07): region/window/full-screen recording to MP4,
separate microphone and system-audio toggles, webcam, pause/resume (Shift+F9) and stop (Shift+F10), on-screen drawing,
trim/cut, save frame, combine clips, animated GIF. SnapForge implements its own versions — no TechSmith code or assets.

## V1 scope (approved)

| ID | Requirement | Acceptance (summary) |
|---|---|---|
| VID-01 | **Video mode** enabled in the capture panel's rail and the widget; Selection: Region, Window, Full screen (one display) | Video selected → Capture starts a recording of that area |
| VID-02 | **Record to MP4** (H.264 video, AAC audio) using Windows' own screen-capture API and Windows' built-in encoder — no bundled codecs, no paid components | File plays in Windows Media Player / Films & TV and in browsers |
| VID-03 | **Audio**: system sound and microphone as separate switches (microphone chooser in Settings) | Recording contains the selected sources; level meters move |
| VID-04 | **Recording toolbar ("floater")**: small floating bar outside the recorded area with Pause/Resume, Stop, Cancel (discard), elapsed time, mic/system-audio toggles with level meters; never takes focus; never appears in the recording | Toolbar works by mouse and hotkeys; pixel check that it is not in the video |
| VID-05 | **Countdown** 3-2-1 before recording (can be turned off) and an outline around the recorded area while recording (not recorded) | Visible outline; not in the video |
| VID-06 | **Hotkeys**: start/pause/resume (default Shift+F9) and stop (default Shift+F10), configurable, conflicts reported | Hotkeys work while another app has focus |
| VID-07 | **Cursor** in the recording on/off | Option honoured |
| VID-08 | **Library and tray**: videos saved to the Library automatically (or "without history"), thumbnail = first frame, duration and size shown, open in a simple player (play/pause/seek) in the Editor area | Video tiles appear, play, survive restart |
| VID-09 | **Drag and export**: drag a video tile out as an .mp4 (same drag rules as images); Export… saves a copy | MP4 arrives in Explorer/Teams/Outlook |
| VID-10 | **Quality**: 30 fps default, 60 fps option; resolution = recorded area; sensible bitrate presets (Standard / High) | Settings honoured; file size reported |
| VID-11 | **Safety and privacy**: visible recording indicator, recording stops cleanly on display changes/lock/disk-full (file kept up to the failure point or clearly reported), nothing uploaded; Settings explains that video cannot be redacted like screenshots | Fault tests: disk full, display change, cancel |
| VID-12 | **Performance**: recording does not freeze SnapForge or the recorded app; CPU and file size measured for 1080p 30/60 fps | Benchmark report |

## Not in V1 (later, separate approval)

* **V2 editing**: trim/cut, save a frame as an image, export as animated GIF.
* **V3 extras**: webcam overlay, drawing on screen while recording, cursor highlight/click effects, combining clips,
  All-in-One mode.

## Technical approach (summary)

* Capture: Windows.Graphics.Capture (monitor or window item, or a monitor item cropped to the region) → Direct3D 11
  textures, no GDI polling.
* Encoding: Media Foundation sink writer with the Windows H.264 and AAC encoders (hardware-accelerated where
  available). Audio: WASAPI loopback (system sound) and WASAPI capture (microphone), mixed and time-stamped.
* Interop via Windows' own APIs (CsWinRT/C# projections or direct COM); every new package is checked for licence
  compatibility (MIT-compatible, free) before it is added.
* Library format: a new "video" capture kind (MP4 + thumbnail + metadata) added to the documented format with a version
  bump that older SnapForge builds reject safely.

## Platform

* Windows 11 is the target (PD-03). Windows.Graphics.Capture needs Windows 10 version 1903 or later; the yellow capture
  border can only be removed on Windows 11 (and needs a package capability on some versions — see
  WINDOWS-GRAPHICS-CAPTURE.md). Windows 10 support for video would be evaluated separately.
* H.264/AAC encoders are part of Windows; licensing of the built-in encoders is to be confirmed before a public release.

## Testing

* CI: recording a known animated test window for a few seconds → decode the MP4 and check frame size, duration, frame
  content (colour patches), audio track present; toolbar not in frames; pause gap; cancel leaves no file.
* CI cannot test microphones or real audio devices — those are manual checks.
* Manual acceptance checklist additions for a real PC (mic, system audio, multi-monitor, 4K, laptops on battery).

## Estimate

Large (comparable to milestone 3). Highest risks: audio/video synchronisation, hardware-encoder differences between
PCs, and Windows.Graphics.Capture behaviour on multi-monitor/mixed-DPI setups.

## Owner decisions (answered 2026-11-26)

1. V1 scope VID-01…VID-12 — **approved**, plus reliability requirements VID-R01…R14.
2. Windows 11 first — **yes**; Windows 10 evaluated separately.
3. Defaults system audio on, microphone off, cursor on, 30 FPS, countdown on — **yes**.
4. Order — **separate feature branch**, Milestone 3 defects fixed independently on its branch.
