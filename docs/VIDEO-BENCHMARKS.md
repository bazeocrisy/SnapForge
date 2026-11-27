# Screen recording benchmarks (Milestone V1)

**Status: running** — workflow *Video benchmarks*, run
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
