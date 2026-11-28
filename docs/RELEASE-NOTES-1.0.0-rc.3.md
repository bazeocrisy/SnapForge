# SnapForge 1.0.0-rc.3: release notes (release candidate 3, owner test build)

**Status:** an owner test build for review. It is **not** approved for public release, is **not code-signed**, and public downloads stay disabled.

**Platform:** Windows 11 24H2 or later, x64.

This candidate answers the owner's rc.2 feedback:

* recording was hard to figure out;
* the video looked grainy;
* he could not see himself while talking.

Start with [OWNER-CHECKLIST-rc.3.md](OWNER-CHECKLIST-rc.3.md).

## Two programs: SnapForge Capture and SnapForge Editor

One installer adds two Start menu entries.

**SnapForge Capture** stays ready next to the clock. It runs QuickDock, the hotkeys, screenshots, the Record panel and recording. If you choose to start SnapForge with Windows, only Capture starts: just its icon and QuickDock, no windows.

**SnapForge Editor** opens when you need it, either from its own shortcut or when Capture hands it a new capture. It handles editing, annotation, redaction, OCR, playback, trimming, the Library and drag-out.

You can close the Editor at any time. Capture, its hotkeys and any recording keep running.

A new capture is handed to the Editor only after it has been saved. If the Editor is closed, has crashed, or can't be started, nothing is lost: the capture is in the Library, and the Editor opens it at its next start.

The details are in [PROCESS-SPLIT.md](PROCESS-SPLIT.md).

## Recording made simple

**The Record panel.** Choose **Screen**, **Window** or **Area**, then three clear switches (**Camera**, **Mic** and **Computer audio**) and **Start recording**.

* Camera and Mic are off each time SnapForge starts. You turn them on deliberately.
* Everything else is under **More options**: camera only, devices, frame rate, quality, countdown, camera position, shape and size, mirror, mouse pointer, keeping recordings in the Library, and opening the Editor after recording.

**See yourself.** With Camera on, a live camera preview floats on the screen before you start, and stays there while you record.

* You can drag it anywhere, and it is always kept fully on a screen.
* Its caption says whether you'll be in the video and where.
* You can hide it; the recording is not affected.
* It is never part of the video, even when it sits over the recorded area. The camera is added to the video exactly once, in the corner you chose.

**Hear yourself.** With Mic on, a level bar shows that the microphone hears you before you start.

**While recording.** A small control strip shows the time, the mic level, **Pause/Resume** and **Stop**, plus a **More** menu with these options:

* camera preview;
* camera in the recording;
* microphone;
* computer audio;
* discard the recording.

The notification-area icon always offers **Stop recording and save**, **Pause** and **Show recording controls**.

**After Stop.** A **Recording saved** message offers **Play**, **Open in Editor** and **Show in folder**. If a recording could not be finished, it says so in plain words, and whatever could be recovered is kept.

## Sharper video

The rc.2 sample was 1080p30 at about 5 Mbit/s. At that bitrate small text smears, and the encoder's default constant bitrate made it worse whenever anything moved.

What rc.3 changes:

* **Screen picture:** the default is now H.264 High profile with a variable bitrate (11.2 Mbit/s mean, 22.4 Mbit/s peak at 1080p30). SnapForge reads back and logs the settings the encoder actually applied. If an encoder refuses one of the settings, it steps down safely.
* **Camera picture:** it is scaled down by area averaging instead of two-point sampling. This removes the shimmer and grain on fine detail.
* **Advanced settings:** technical tuning stays under More options (Standard or High quality, 30 or 60 FPS).

**Measured on CI** (run 155, Windows software encoder). The same deterministic 1920×1080 test scene was recorded with the rc.2 settings and the new default:

| | rc.2 settings | rc.3 default |
|---|---|---|
| Small text, static (PSNR / SSIM) | 46.90 dB / 0.9998 | 47.62 dB / 0.9998 |
| Small text, scrolling (PSNR / SSIM) | 42.45 dB / 0.9995 | **47.50 dB** / 0.9998 |
| Camera overlay, static (PSNR) | 29.47 dB | **42.44 dB** |
| Camera overlay, scrolling frame (PSNR) | 29.43 dB | **43.28 dB** |
| Encoder reported | CBR 5.0 Mbit/s, Main profile | Peak-constrained VBR 11.2 / 22.4 Mbit/s, High profile, CABAC, no B-frames |
| Measured video bitrate (this scene) | 4.97 Mbit/s | 2.56 Mbit/s |
| Frames dropped | 0 % | 0 % |

The verdict for small text was **+2.89 dB**, a pass (the threshold is +1 dB). How to read these numbers:

* **File size depends on content:** a variable bitrate spends bits only where the picture changes. This test scene is mostly static, so it came out smaller than rc.2. Busy content can reach about 11 Mbit/s, roughly 83 MB per minute, against 37 MB per minute in rc.2.
* **The camera test is synthetic:** the camera picture was a synthetic test image; real webcams still need checking on a physical PC.
* **Native-size crops:** the CI log and artifact include native-size crops of both recordings.

The full analysis is in [VIDEO-QUALITY.md](VIDEO-QUALITY.md).

## Installer and upgrade

* **Where it installs:** for all users in `C:\Program Files\SnapForge`, with both programs and two shortcuts. Administrator approval is needed only to install, repair or uninstall.
* **Upgrades:** an rc.2 installation is upgraded in place, and its single "SnapForge" shortcut is replaced. An rc.1 per-user copy is offered for removal the first time SnapForge starts.
* **Your data is never touched:** captures, recordings, editable projects, the Library location and settings stay where they are, also when you uninstall.
* **A start-with-Windows entry from rc.2 keeps working**, and no duplicate is added.
* **If SnapForge is running,** setup asks you to stop any recording and exit it. Setup never closes SnapForge by force. If setup is interrupted, run it again to repair the installation.

## Known open items

* DEF-RC2-07: on some CI runs, a test window shown by a self-test is not painted in time, so a screen-based check fails on its precondition. Diagnostics were added; this is still open.
* Not testable in CI, so these need a physical PC:
  * real webcams and microphones;
  * interactive UAC prompts;
  * a second real Windows user;
  * dragging into File Explorer with a physical mouse;
  * hardware video encoders.
