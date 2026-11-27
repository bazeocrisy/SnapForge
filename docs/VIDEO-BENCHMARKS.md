# Screen recording benchmarks (Milestone V1)

**Status: 1440p and 1080p60 jobs finished; 4K and 1080p30 (incl. 120 min) still running** — workflow *Video benchmarks*, run
[45910273309](https://github.com/bazeocrisy/SnapForge/actions/runs/45910273309) on commit `fccffbe` (same recorder
code as the final build `ed4815d`; later commits changed UI styling, the encoder label and documentation). Each
configuration's JSON is uploaded as artifact `video-bench-<label>`. This page is updated with the measured rows when
the jobs finish; until then the numbers below are from the per-commit CI self-test.

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
| 2560×1440 · 30 FPS | screen | **unsupported** | — | — | — | — | — | — | — | — | runner display offers 1024×768 / 1920×1080 only |
| 2560×1440 · 30 FPS | synthetic | measured | 5 min | 9 000 | 0 | 38.0 | 366 / 651 | 33.3 / 99 | 63.9 | 0.8 s | ≈ 0.09 ms / 0.00 ms/min |
| 2560×1440 · 30 FPS | synthetic | measured | 30 min | 54 000 | 0 | 40.0 | 361 / 657 | 32.3 / 98 | 63.9 | 5.9 s | ≈ 0.09 ms / 0.00 ms/min |
| 2560×1440 · 60 FPS | screen | **unsupported** | — | — | — | — | — | — | — | — | as above |
| 2560×1440 · 60 FPS | synthetic | measured | 5 min | 17 997 | 0 | 77.8 | 424 / 680 | 26.1 / 82 | 125.2 | 1.7 s | ≈ 0.06 ms / 0.00 ms/min |

Observations: memory does not grow with length (60 min peak = 5 min peak, VID-R13); no frame was dropped even at
1440p60 on the software encoder (78 % CPU — on a real PC a hardware encoder would take most of this load);
conversion to the final MP4 takes about 0.1 s per minute recorded. Screen rows are mostly repeated frames because the
runner's screen changes only where the test window moves; file size per minute therefore reflects a mostly static
screen. GPU load: not measurable (no GPU).

## Known before the long runs finish

| Measurement (CI self-test, commit `ed4815d`) | Result |
|---|---|
| 640×360 · 30 FPS · 12 s synthetic | A/V error max 0.07 ms, drift 0.05 ms/min; CPU 3.8 %; peak private memory 175 MB; encode latency 33 ms |
| 1280×720 · 60 FPS · 5 s synthetic | 0 dropped frames; A/V error 0.02 ms; CPU 19.6 %; peak private memory 507 MB |
| 1024×768 · 30 FPS · 5 s real screen | 0 dropped; 26–50 frames repeated (static screen); CPU ≈ 20 %; peak private 712 MB; ≈ 17 MB/min |

Peak private memory includes the bounded frame queue (≤ 256 MB of frames) and the software encoder's own buffers.

## Not measurable on CI (physical PC, pending)

Hardware encoders (NVIDIA/AMD/Intel) and GPU load; real 1440p/4K displays; laptops on battery; audio devices.
Use the same workflow on a self-hosted runner or the acceptance checklist (VIDEO-ACCEPTANCE.md V1-REL-07/11).
