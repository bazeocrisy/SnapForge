# Milestone V1 acceptance checklist and known limitations

Branch `claude/video-v1`. Every check names how it is verified: **SIM** (simulated/unit test, any OS), **CI**
(automated on the GitHub Windows Server 2025 runner: real desktop, 1024×768, software rendering, software H.264,
**no audio devices**), **PC** (manual, on a physical Windows 11 PC — *pending until the owner or a tester runs it*).
Results go into ACCEPTANCE-RESULTS-TEMPLATE.md (one row per ID: pass/fail, PC model, Windows build, notes).

## A. Recording interface

| ID | Check | How |
|---|---|---|
| V1-UI-01 | Capture panel → Video: options card shows Record (Region/Window/Display), Frame rate 30/60, Quality Standard/High, System audio ✔, Microphone ☐, Cursor ✔, Countdown ✔ (3 s); button reads **Record**; hotkey **Shift+F9** shown; Image mode unchanged | CI (`Capture_panel_is_compact_horizontal…`) + PC |
| V1-UI-02 | Widget: **Record** button starts a recording; becomes **Stop** while recording | PC |
| V1-UI-03 | Toolbar shows blinking red indicator, elapsed time, `W×H · FPS`, Pause/Resume, Stop, Cancel, System audio / Microphone toggles with moving level meters | PC |
| V1-UI-04 | Clicking the toolbar does not take focus from the app being recorded (type in Notepad, click Pause, keep typing) | PC |
| V1-UI-05 | **Toolbar and dashed outline are not in the recording** (record a region with the toolbar dragged inside it; inspect frames) | PC (CI: Windows accepted the exclusion) |
| V1-UI-06 | Countdown 3-2-1 appears (Esc cancels); 0 = no countdown; countdown is not in the video | PC |
| V1-UI-07 | Cursor on → pointer in video; off → not | PC (CI: cursor capture flag accepted) |
| V1-HK-01 | Shift+F9 starts; Shift+F9 again pauses/resumes; Shift+F10 stops and saves — while another app has focus; changing them in Settings → Recording works; conflicts reported | PC |
| V1-PLAY-01 | Saved MP4 plays in the SnapForge player, Windows Media Player / Films & TV, Edge and Chrome | PC (CI: decode read-back) |

## B. Audio

| ID | Check | How |
|---|---|---|
| V1-AUD-01 | System audio: play a video in a browser while recording → sound in the file, in sync | PC |
| V1-AUD-02 | Microphone on: Windows mic indicator appears; speech in the file; off: no indicator, no mic in the file | PC |
| V1-AUD-03 | Toggle system/mic during recording → muted parts silent, timeline unchanged | PC |
| V1-AUD-04 | Choose a specific microphone in Settings → Recording; unplug it mid-recording → notice, recording continues silently; plug back → audio resumes | PC |
| V1-AUD-05 | Switch output to Bluetooth headphones mid-recording → toolbar notice “now recording from …”; system audio continues | PC |
| V1-AUD-06 | No audio devices at all → recording works, silent track | CI (`screen-recording`) |

## C. Reliability

| ID | Check | How |
|---|---|---|
| V1-REL-01 | A/V sync: mean and max offset ≤ 45 ms on the synthetic flash/beep recording; drift reported | CI (`av-sync`) |
| V1-REL-02 | Pause/resume: paused time removed exactly; sync kept | CI (`pause-resume-*`) + SIM |
| V1-REL-03 | Kill SnapForge while recording → next start recovers the video (“Recovered”), playable | CI (kill test) + PC (Task Manager → End task) |
| V1-REL-04 | Power loss while recording (pull the plug / hard power-off on a test PC) → recovered at next start | PC |
| V1-REL-05 | Normal stop → complete MP4 (fragments + index), listed with duration | CI |
| V1-REL-06 | Nearly full disk (e.g. a small USB drive as Library location) → warning, then stop and save | PC |
| V1-REL-07 | Hardware encoder used where present (result/notice shows the encoder); forced failure → software fallback | PC |
| V1-REL-08 | Record while the PC is locked (Win+L) → recording pauses with notice | PC |
| V1-REL-09 | Disconnect the recorded display → stop and save with notice; change resolution while recording → continues | PC |
| V1-REL-10 | Sleep while recording → saved before sleep; sign out / shut down → saved | PC |
| V1-REL-11 | Long recording 60 min (and 120 min where feasible) → memory flat, file valid | PC (CI where feasible — VIDEO-BENCHMARKS.md) |
| V1-REL-12 | Start a second recording while one runs → refused | CI (`single-session`) |
| V1-REL-13 | Cancel → confirmation; Yes → nothing kept | CI (`cancel-discards`) + PC |

## D. Library

| ID | Check | How |
|---|---|---|
| V1-LIB-01 | Video tile: thumbnail, play badge, duration, resolution, size; Videos view; tray tile | PC |
| V1-LIB-02 | Double-click/Enter plays; seek bar, Space, ←/→, volume, mute | PC |
| V1-LIB-03 | Rename, tags, favourite, folder, search (title/tag/“video”), Trash, Restore, Empty Trash | SIM (`VideoLibraryTests`) + PC |
| V1-LIB-04 | Export MP4 (single and multiple) copies the file; Save to Library off → asks where to save, nothing kept | PC |
| V1-LIB-05 | Drag a video to a **separate-process drop target** (test tool) → MP4 file received; Explorer, desktop, Edge/Chrome upload, Outlook, Word, Teams | separate-process: CI (pending tool); real apps: PC |
| V1-LIB-06 | Image captures unchanged: open, edit, export, drag exactly as before | CI (all Milestone 3 tests) + PC |
| V1-LIB-07 | Open the same Library with the Milestone 3 build → images listed and usable, videos not shown, nothing changed in `videos/` | PC |

## E. Security / privacy / compatibility

| ID | Check | How |
|---|---|---|
| V1-SEC-01 | Windows capture border visible while recording | PC |
| V1-SEC-02 | Microphone privacy indicator only while Microphone is on | PC |
| V1-SEC-03 | Settings → Recording states that videos stay local and cannot be redacted or sanitized | PC |
| V1-COMPAT-01 | Windows 11 23H2/24H2: all of the above | PC |
| V1-COMPAT-02 | Windows 10 22H2: record, play, toolbar exclusion — report separately | PC |
| V1-COMPAT-03 | Standard (non-admin) user; high contrast theme; multi-monitor and mixed DPI (record on the secondary display) | PC |

## Known limitations (V1)

| ID | Limitation |
|---|---|
| KL-V01 | CI has no audio hardware, no hardware encoder, no GPU and a 1024×768 display: audio devices, hardware encoding, 1440p/4K capture, multi-monitor, Bluetooth, sleep/lock and power loss are **unverified until tested on a PC**. |
| KL-V02 | Videos cannot be redacted, blurred or sanitized; anything on screen or audible while recording is in the file. |
| KL-V03 | A region that spans several displays is clipped to the display holding most of it. |
| KL-V04 | Recovery keeps everything up to the last complete fragment; the final ~1–2 s before a crash can be lost. Recovery needs the MP4 to contain at least one complete fragment. |
| KL-V05 | Library backup (.zip) contains images only (format unchanged so older versions can restore it); back up `videos/` separately. |
| KL-V06 | Permanently deleting a video removes the file without overwriting it first (thumbnails and metadata are overwritten). |
| KL-V07 | Moving the Library location **with a Milestone 3 (pre-video) build** moves images only; videos stay in the old folder (not deleted). Move with the V1 build. |
| KL-V08 | Window recording follows the window; minimised windows produce no new frames (the last frame repeats). |
| KL-V09 | The player opens in its own window rather than inside the Editor area (VID-08 said "in the Editor area") — flagged for the review. |
| KL-V10 | Media Foundation is required (missing on Windows N editions without the Media Feature Pack): Record explains how to add it. |
| KL-V11 | Library integrity check for videos is the file size recorded in the manifest (no hash of multi-GB files at start-up). |
