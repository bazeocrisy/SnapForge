# Milestone V1 — automated test results

Evidence labels: **SIM** = unit tests (Linux + Windows), **CI** = GitHub Actions Windows Server 2025 runner (real
desktop, 1024×768, *Microsoft Basic Render Driver*, software H.264 only, **no audio devices**), **PC** = physical PC
(pending — see VIDEO-ACCEPTANCE.md). Source of the numbers below: CI run
[45910436639](https://github.com/bazeocrisy/SnapForge/actions/runs/45910436639) on commit `7cee382` (portable build,
.NET hidden), unless noted. Later commits re-run the same checks on every push.

## 1. Video self-test (`SnapForge.exe --video-selftest`, CI)

| Check | Result | Measured |
|---|---|---|
| `av-sync` (VID-R01) | PASS | 11 flash/beep pairs over 12 s, 30 FPS: **A/V error mean −0.07 ms, max 0.07 ms, drift −0.05 ms/min**; video timestamps within 0.11 ms, audio within 0.03 ms of production time |
| `constant-frame-rate` | PASS | 361 frames for 12.033 s |
| `timestamps-exact` | PASS | first frame at 0 ms, 0 out-of-order timestamps |
| `fragmented-recording` (VID-R04/R05) | PASS | 41 fragments while recording |
| `progressive-final` (VID-R05) | PASS | final file `ftyp moov mdat` |
| `pause-resume-duration` (VID-R12) | PASS | 7.999 s recorded of 11 s with a 3 s pause |
| `pause-resume-sync` (VID-R12) | PASS | audio exact (≤ 0.03 ms); video after resume 7.7 ms — rounding to the 30 FPS frame grid (≤ ½ frame = 16.7 ms by design) |
| `60fps` / `60fps-sync` (VID-10) | PASS | 1280×720 at 60 FPS: 301 frames, 0 dropped; A/V error max 0.02 ms |
| `single-session` (VID-R14) | PASS | second recorder refused while one runs |
| `screen-recording` (real screen, VID-02/R07) | PASS | 1024×768, 149 frames (103 captured, 26 repeated for the static screen), 4.97 s, audio track present and silent — system audio and microphone requested, **no devices** on the runner |
| `toolbar-not-recorded` (VID-04/05) | PASS | real toolbar + outline over a green window, recorded through Windows.Graphics.Capture: **toolbar area 100 % background**; control run without the exclusion: 22.3 % (toolbar visible) |
| `cancel-discards` (VID-04) | PASS | file removed, recorder released |

**How sync is measured.** A synthetic source shows a white frame and plays a 1 kHz beep once per second; the
recorder records both through the normal pipeline; the finished MP4 is decoded by Windows (Media Foundation source
reader); each decoded flash and beep onset is compared with the time it was produced (mapped through the recording
clock). This isolates the recorder's own error from the test pattern.

**What the measurement found during development (fixed):**
1. B-frame reordering in the encoder shifted decoded video timestamps by two frames and out of order → low-latency
   encoding (no B-frames).
2. Media Foundation's fragmented-MP4 writer stores no fragment start times (`tfdt`); Windows' own reader then
   mis-times fragments (**233 ms** A/V error measured on the fragmented file — still reported each run as
   `fragmentedFileSync` for comparison). The recording is now converted to a standard progressive MP4 when it stops
   (`Mp4Defragmenter`); the stored timing was verified exact by parsing the file (`storedTiming`).
3. After Resume, a 10 ms audio gap and a one-frame video delay → exact audio re-anchor on resume; frames are only
   repeated into slots that have ended.

## 2. Crash recovery (VID-R04, CI)

`--video-record-until-killed` records the real screen into a Library; after ~9 s the process is **killed**
(`Stop-Process -Force`, no finalisation). `--video-recover` then runs the start-up recovery:
partial file 2 354 376 bytes, 29 complete fragments → **8.7 s recovered**, decoded successfully, converted to
progressive MP4, listed in the Library as "Recovered" (1024×768, no damage).

## 3. Windows UI and integration tests (CI, `dotnet test`)

All Milestone 3 tests still pass (no regression in the screenshot workflow), plus:

* `Capture_panel_is_compact_horizontal_and_marks_unavailable_modes` — Video mode enabled; options card, Record button,
  Shift+F9 and the owner defaults; Image mode restored unchanged; All-in-One/Effects/Share still unavailable.
* `Real_mouse_drag_of_a_video_into_a_separate_process_delivers_the_mp4` — real mouse drag of a Library video into a
  drop target running **in a separate process** (`SnapForge.exe --drop-target`): one `.mp4` named after the recording,
  same size and SHA-256 as the Library file; Library copy untouched. *Explorer, browsers, Office and Teams: pending PC.*

## 4. Core unit tests (SIM, Linux + Windows): 191 total

New for V1: `VideoCoreTests` (recording clock, pause intervals, audio mixer: summing, clamping, gaps, jitter,
overlap, bounded memory, mute/disconnect, resume re-anchor; MP4 inspection and recovery incl. truncated, garbage,
64-bit boxes), `Mp4TimingTests`, `Mp4DefragmenterTests` (two-track fragments → progressive with byte-exact samples,
interleaving, sync table), `VideoSettingsTests` (owner defaults, migration, bit rates, hotkey conflicts),
`VideoLibraryTests` (format, migration/compatibility, crash states, conversion on completion, operations).

## 5. Sample MP4s from automated testing

Download: [video-samples artifact of run 45910436639](https://github.com/bazeocrisy/SnapForge/actions/runs/45910436639/artifacts/15160225793)
(zip; artifacts are kept 30 days — later runs upload their own).

| File | What | Bytes | SHA-256 |
|---|---|---|---|
| `sample-sync-640x360-30fps.mp4` | 12 s synthetic flash/beep pattern (the sync test file), 640×360, 30 FPS, AAC | 1 291 055 | `6616a3eee88740ff0705e79ab9fe9c4f58f7e42737237f43c804a3485232529d` |
| `sample-screen-30fps.mp4` | 5 s of the runner's real screen (1024×768) with a moving test window, silent AAC track | 1 308 654 | `e5d826636fa60d7d465e4094a200571bee5114fce2449f746e64047710b2de94` |

## 6. Not verifiable on CI (pending a physical PC)

Real audio devices (system sound, microphone, unplug, Bluetooth switching), hardware encoders and the fallback from
them, GPUs, displays above 1920×1080, multi-monitor and mixed DPI, display disconnection, sleep/lock, power loss,
Windows' capture border and microphone indicator, standard-user and Windows 10 behaviour, Explorer/browser/Office/Teams
drops, playback in Windows Media Player / Films & TV / browsers. Each has a row in VIDEO-ACCEPTANCE.md.
