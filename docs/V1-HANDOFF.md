# Milestone V1 — screen recording: handoff for independent review

Branch **`claude/video-v1`** (from the verified Milestone 3 baseline; Milestone 3 fixes merged in) · **not merged**
into main · **no release published** · Milestone 3 continues on `claude/pensive-euler-ulw6m4` / PR #1.
V2 editing and V3 extras were not started.

Evidence labels: **CI** = automated on GitHub's Windows Server 2025 runner (1024×768, software rendering, software
H.264 encoder, no audio devices) · **SIM** = unit tests · **PC** = on a physical Windows PC — **none performed yet**.

## 1. Owner requirements → what was delivered

| Owner item | Delivered | Evidence |
|---|---|---|
| VID-01…12 approved | Implemented (see PRODUCT-REQUIREMENTS.md *V1 Screen recording*) | per row |
| Decisions: Win 11 first; system audio on; mic off; cursor on; 30 FPS (60 opt.); countdown on, configurable; MP4 H.264/AAC; Standard/High; no subscriptions, paid deps, accounts, telemetry | All defaults as decided; no packages added; no network code | `Defaults_follow_the_owner_decisions` (SIM), panel test (CI), screenshot `capture-panel-video-*` |
| Separate feature branch from the verified M3 baseline; M3 defects fixed independently | `claude/video-v1` from `c96f631`; M3 fixes (panel test fix, About) made on the M3 branch and merged forward | git history |
| No video regressions in the screenshot workflow | All Milestone 3 tests pass on the V1 branch; Image mode and image Library operations unchanged | CI |
| Help → About: version and Git commit | Version, build, **full commit**, **branch** (also on the M3 branch) | About dialog |
| Video mode in panel and widget | Panel Video mode (Record: Region/Window/Display, FPS, quality, audio, cursor, countdown); widget **Record/Stop** | CI test, screenshots |
| Floating toolbar: indicator + elapsed, Pause/Resume, Stop and Save, Cancel and Discard, mic/system toggles, level meters, resolution/FPS, configurable hotkeys | All present; hotkeys Shift+F9 record/pause/resume, Shift+F10 stop, optional cancel; non-activating | screenshots `recording-*`; PC rows V1-UI-03/04 |
| Toolbar and outline not in the recording | `WDA_EXCLUDEFROMCAPTURE`; **verified by recording**: toolbar area 100 % background, control 22 % | CI `toolbar-not-recorded` |
| Preserve Windows capture consent/privacy indicators; no OS bypass | Only Windows.Graphics.Capture; capture border never disabled; mic opened only when on | VIDEO-THREAT-MODEL.md; PC V1-SEC-01/02 |
| Reliability VID-R01…R14 (unique IDs) | All implemented; see §2 | |
| Fragmented MP4 / recoverable format; no recovery promise without proof | Fragmented while recording, progressive when finished; recovery **demonstrated** by killing the process | CI kill test (8.7 s recovered) |
| Performance 1080p/1440p/4K × 30/60 × 5/30/60/120 min; CPU, GPU, memory, drops, sync, latency, size; unsupported reported | Benchmark workflow; results in VIDEO-BENCHMARKS.md (1440p/4K screen = unsupported on the runner, reported) | VIDEO-BENCHMARKS.md |
| Library: videos with thumbnails, play indicators, duration, resolution, size; playback, seeking, rename, tags, favourites, folders, search, export, trash, restore, native drag-out; no full-file loads; images preserved; older versions cannot corrupt | All; videos in `videos/` (older builds never read it); player streams from disk; drag/export copy by streaming | VideoLibraryTests (SIM), drag test (CI), screenshots |
| Security: supported APIs; licensing; local only; disclosure | VIDEO-ARCHITECTURE.md §4 (H.264/AAC — legal review recommended before public release), Settings disclosure, threat model | docs |
| External MP4 drag via separate-process drop target; Explorer/browser/Office/Teams pending | `Real_mouse_drag_of_a_video_into_a_separate_process_delivers_the_mp4` — SHA-256 identical | CI; real apps = PC |
| Distinguish simulated / CI / PC | Every result labelled | VIDEO-TEST-RESULTS.md, VIDEO-ACCEPTANCE.md |

**Parity matrix on this branch** (183 rows, regenerated requirements validated: 258 IDs, 0 duplicates, 0 dangling
references): 63 ✅, 28 ◐, 87 ✗, 3 ≠, 1 ⊕, 1 n/a. V1 moved FP-VID-01…05 from ✗ to ◐ — "◐" because each still has a
PC-only part (devices, hardware encoders, 4K displays) or a V2/V3 part (webcam, VFR).

## 2. Reliability requirements

| ID | Status | Evidence |
|---|---|---|
| VID-R01 A/V sync & drift | **CI: max error 0.07 ms (30 FPS), 0.02 ms (60 FPS); drift 0.05 ms/min**; long-run drift in benchmarks | self-test, benchmarks |
| VID-R02 HW encoding + SW fallback | Implemented; actual encoder reported. CI has only the software encoder | PC V1-REL-07 |
| VID-R03 unavailable encoders | Clear message when Media Foundation is missing; writer fallback | code; PC on an N edition |
| VID-R04 crash recovery | **CI: killed process → 8.7 s recovered, decoded, listed "Recovered"** | kill test |
| VID-R05 safe finalisation & validation | Finalize → convert → manifest last; read-back checks | self-test, unit tests |
| VID-R06 disk space | Warning < 10 min left; stop & save below 500 MB (configurable) | code; PC V1-REL-06 |
| VID-R07 device disconnection | Silence + retry + notice; CI covers "no device at all" | SIM, CI; PC V1-AUD-04 |
| VID-R08 Bluetooth/default output change | Loopback follows the default device | PC V1-AUD-05 |
| VID-R09 display disconnect / resolution change | Stop & save on disconnect; pool recreated on resize | PC V1-REL-09 |
| VID-R10 sleep, lock, shutdown | Lock pauses; sleep/sign-out/shutdown stop & save; crash → recovery | CI (kill); PC V1-REL-08/10 |
| VID-R11 long recordings | Benchmarks up to 120 min (see VIDEO-BENCHMARKS.md) | CI |
| VID-R12 pause/resume timestamps | **CI: audio exact; video ≤ ½ frame (grid rounding)** | self-test |
| VID-R13 bounded memory | 256 MB frame budget, 4 s audio per source; drops counted; peak memory in benchmarks | SIM, CI |
| VID-R14 one session | Second start refused | CI |

## 3. Defects found by the new tests and fixed on this branch

1. A/V offset up to 300 ms: B-frames (fixed: low-latency encoding) and Windows' reader mis-timing fragmented MP4 without
   `tfdt` (fixed: progressive MP4 on completion; `Mp4Defragmenter`, byte-exact, unit-tested).
2. 10 ms audio gap and one-frame video delay after Resume (fixed; measured).
3. Window recording fell back to a fixed region for maximised windows (fixed: frontmost window containing the selection).
4. Recovery could discard a finished video if stopped between conversion and manifest (fixed; unit test).
5. Backup wrote video metadata under `captures/` (fixed before commit; test).
6. Dark-theme styling of toolbar toggles, player and microphone picker (fixed; screenshots).

## 4. Deviations and decisions for the reviewer

* **Player in its own window**, not inside the Editor area as VID-08 worded it (KL-V09) — avoids touching the Editor's
  canvas/session code; can be moved later.
* **Separate `videos/` folder** instead of a capture-format version bump (VIDEO-PLAN.md planned a bump): older builds
  never see videos, which is stronger than rejecting them.
* **Direct COM/WinRT interop** instead of the Windows SDK projection package (could not be restored under the existing
  signature-verified locked restore; weakening that was not acceptable).
* **ID reconciliation**: the earlier backlog's VID-01…11 renumbered to VID-V2-*/VID-V3-* (mapping in VIDEO-PLAN.md).

## 5. Known limitations

KL-V01…KL-V12 in VIDEO-ACCEPTANCE.md. Most important: everything involving real audio devices, hardware encoders,
GPUs, displays above 1080p, multi-monitor, sleep/lock and power loss is **unverified until tested on a PC**; videos
cannot be redacted; Library backup excludes videos.

## 6. Build and how to test

See §7 (filled from the final CI run). Then follow VIDEO-ACCEPTANCE.md on a Windows 11 PC.

## 7. Final build

_(added after the final CI run)_

## 8. Stop point

Work stops here at the independent review gate. Nothing is merged; no release is published; V2/V3 are not started.
