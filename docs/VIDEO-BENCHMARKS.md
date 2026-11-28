# Screen recording benchmarks (Milestone V1)

**Status: finished** (1080p30 incl. 120 min, 1080p60, 1440p measured; 4K partly — see below) — workflow *Video benchmarks*, run
[45910273309](https://github.com/bazeocrisy/SnapForge/actions/runs/45910273309) on commit `fccffbe` (same recorder
code as build `ed4815d`; the final build `7899691` adds the frame-slot fix in footnote ¹). Each
configuration's JSON is uploaded as artifact `video-bench-<label>`. Rows below come from the job logs.

## Method

* Production recorder and encoder (portable build), fragmented MP4 while recording, then conversion to the final
  progressive MP4 — conversion time is reported (**Finalize**).
* **screen** rows record the runner's real primary display through Windows.Graphics.Capture while a test window moves
  (the display is switched to the requested size for the run when the display offers it; restored afterwards).
* **synthetic** rows feed a generated pattern with a flash/beep each second, which measures the encoder path at sizes
  the runner's display cannot show and gives **A/V drift over long runs**.
* Reported per row: duration, frames written / captured / repeated (static screen) / dropped, **CPU %** of all cores
  (SnapForge process, includes the software encoder), **peak working set and private memory**, **encode latency**
  (frames handed to the encoder vs. encoded), **MB per minute**, **Finalize** time, **A/V drift** (synthetic), MP4
  validity. **GPU load is not measurable on the runner** (no GPU; *Microsoft Basic Render Driver*) — reported as
  "not measured" for every row.
* Rows the runner cannot do are reported as **unsupported** with the reason — never skipped.

## Configurations

| Size | FPS | Durations | Source |
|---|---|---|---|
| 1920×1080 | 30 | 5 min (screen, synthetic), 30 min (screen), 60 min (synthetic), **120 min (screen)** | display switched to 1080p |
| 1920×1080 | 60 | 5 min (screen, synthetic), 30 min (screen), 60 min (screen) | display switched to 1080p |
| 2560×1440 | 30, 60 | 5 min (screen, synthetic), 30 min (synthetic, 30 FPS) | screen: **unsupported on the runner** (largest mode 1920×1080) |
| 3840×2160 | 30, 60 | 5 min (screen, synthetic), 30 min (synthetic, 30 FPS) | screen: **unsupported on the runner** |

## Results so far (CI, software H.264 encoder, no GPU)

A/V error = measured flash→beep offset minus the test pattern's built-in one-frame flash delay (33.33 ms at 30 FPS,
16.67 ms at 60 FPS). Drift = slope of the offset over the whole recording. Every finished file was a valid progressive
MP4; 0 dropped frames in every row.

| Configuration | Source | Status | Length | Frames written (captured / repeated) | Dropped | CPU % (all cores) | Peak working set / private MB | Encode latency mean / max ms | MB/min | Finalize | A/V error / drift |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1920×1080 · 60 FPS | screen | measured | 5 min | 16 773 (6 454 / 10 319) | 0 | 43.3 | 354 / 572 | 17.9 / 49 | 34.5 | 0.4 s | — (no audio device) |
| 1920×1080 · 60 FPS | screen | measured | 30 min | 100 330 (38 761 / 61 569) | 0 | 42.2 | 353 / 573 | 14.6 / 48 | 34.4 | 3.1 s | — |
| 1920×1080 · 60 FPS | screen | measured | **60 min** | 201 356 (77 361 / 123 995) | 0 | 42.6 | **354 / 573** | 18.7 / 58 | 34.4 | 6.5 s | — |
| 1920×1080 · 60 FPS | synthetic | measured | 5 min | 17 998 | 0 | 44.2 | 294 / 557 | 17.0 / 149 | 71.9 | 0.9 s | ≈ 0.08 ms / 0.00 ms/min (299 pairs) |
| 1920×1080 · 30 FPS | screen | measured | 5 min | 7 472 (6 365 / 1 107) ¹ | 0 | 23.2 | 353 / 564 | 39.4 / 66 | 26.8 | 0.2 s | — (no audio device) |
| 1920×1080 · 30 FPS | screen | measured | 30 min | 44 875 (38 427 / 6 448) ¹ | 0 | 23.4 | 351 / 570 | 38.5 / 66 | 27.9 | 2.5 s | — |
| 1920×1080 · 30 FPS | screen | measured | **120 min** | 179 277 (153 505 / 25 772) ¹ | 0 | 23.7 | **348 / 569** | 36.2 / 66 | 27.9 | 10.4 s | — |
| 1920×1080 · 30 FPS | synthetic | measured | 5 min | 9 000 | 0 | 22.0 | 291 / 554 | 33.0 / 33 | 37.0 | 0.4 s | ≈ 0.07 ms / 0.00 ms/min |
| 1920×1080 · 30 FPS | synthetic | measured | **60 min** | 108 000 | 0 | 21.8 | 289 / 554 | 31.0 / 65 | 37.0 | 7.3 s | ≈ 0.09 ms / 0.00 ms/min (3 599 pairs) |
| 3840×2160 · 30/60 FPS | screen | **unsupported** | — | — | — | — | — | — | — | — | runner display has no 4K mode |
| 3840×2160 · 30 FPS | synthetic | completed ² | 5 min | — | — | — | — | — | — | — | numbers in artifact `video-bench-4K` (not readable from this session) |
| 3840×2160 · 60 FPS | synthetic | completed ² | 5 min | — | — | — | — | — | — | — | ditto; this row took 4 h 04 min including the frame-by-frame sync decode |
| 3840×2160 · 30 FPS | synthetic | **not completed** | 30 min | — | — | — | — | — | — | — | job time limit (300 min) reached |
| 2560×1440 · 30 FPS | screen | **unsupported** | — | — | — | — | — | — | — | — | runner display offers 1024×768 / 1920×1080 only |
| 2560×1440 · 30 FPS | synthetic | measured | 5 min | 9 000 | 0 | 38.0 | 366 / 651 | 33.3 / 99 | 63.9 | 0.8 s | ≈ 0.09 ms / 0.00 ms/min |
| 2560×1440 · 30 FPS | synthetic | measured | 30 min | 54 000 | 0 | 40.0 | 361 / 657 | 32.3 / 98 | 63.9 | 5.9 s | ≈ 0.09 ms / 0.00 ms/min |
| 2560×1440 · 60 FPS | screen | **unsupported** | — | — | — | — | — | — | — | — | as above |
| 2560×1440 · 60 FPS | synthetic | measured | 5 min | 17 997 | 0 | 77.8 | 424 / 680 | 26.1 / 82 | 125.2 | 1.7 s | ≈ 0.06 ms / 0.00 ms/min |

¹ **Defect found by these runs, fixed afterwards:** on a real screen, frames written were 83 % of duration × FPS
(e.g. 120 min at 30 FPS: 179 277 of 216 000) with 0 dropped. When the screen changed only between two polls, the slot
in between was left empty instead of repeating the previous picture, so files had variable frame timing (timestamps
were still correct; players show the previous frame). Fixed in the capture loop — skipped slots now repeat the
previous frame — and the per-commit self-test now checks `screen-constant-frame-rate` on a real screen. The rows above
were measured before the fix; CPU, memory and size are not expected to change materially, frame counts will.

² The 4K job wrote its JSON results (artifact `video-bench-4K`, run 45910273309) but was stopped by the job time limit
before printing them to the log; artifact downloads are blocked from this session, so the 4K numbers are not
reproduced here. The 4K 60 FPS synthetic row taking 4 hours (for 5 minutes of video) indicates the software encoder
and/or the sync decode at 4K are far slower than real time on the runner's CPU — **4K60 on the software encoder is
not expected to be usable**; 4K needs a hardware encoder (PC test V1-REL-07).

Observations: memory does not grow with length (60 min peak = 5 min peak, VID-R13); no frame was dropped even at
1440p60 on the software encoder (78 % CPU — on a real PC a hardware encoder would take most of this load);
conversion to the final MP4 takes about 0.1 s per minute recorded. Screen rows are mostly repeated frames because the
runner's screen changes only where the test window moves; file size per minute therefore reflects a mostly static
screen. GPU load: not measurable (no GPU).

## Per-commit self-test (for comparison)

| Measurement (CI self-test, commit `ed4815d`; `7899691` adds real-screen constant frame rate: 151 frames for 5033 ms) | Result |
|---|---|
| 640×360 · 30 FPS · 12 s synthetic | A/V error max 0.07 ms, drift 0.05 ms/min; CPU 3.8 %; peak private memory 175 MB; encode latency 33 ms |
| 1280×720 · 60 FPS · 5 s synthetic | 0 dropped frames; A/V error 0.02 ms; CPU 19.6 %; peak private memory 507 MB |
| 1024×768 · 30 FPS · 5 s real screen | 0 dropped; 26–50 frames repeated (static screen); CPU ≈ 20 %; peak private 712 MB; ≈ 17 MB/min |

Peak private memory includes the bounded frame queue (≤ 256 MB of frames) and the software encoder's own buffers.

## Not measurable on CI (physical PC, pending)

Hardware encoders (NVIDIA/AMD/Intel) and GPU load; real 1440p/4K displays; laptops on battery; audio devices.
Use the same workflow on a self-hosted runner or the acceptance checklist (VIDEO-ACCEPTANCE.md V1-REL-07/11).
