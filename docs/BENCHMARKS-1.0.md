# Performance benchmark report — 1.0.0-rc.1

Proposed targets (not guarantees on every PC): start-up ≤ 3 s on the reference environment, UI responsive during
background indexing, no unbounded memory growth, no unexplained data loss, no corrupted completed recordings, < 1 %
dropped frames and A/V sync within ~80 ms on supported recording configurations. Misses are recorded, not hidden.

**Environment for all CI numbers below:** GitHub-hosted Windows Server 2025 runner (Windows 10.0.26100), 4 vCPUs, no
GPU (Microsoft Basic Render Driver), no audio devices, .NET 10.0.15 self-contained, software H.264 encoder. These are
**CI measurements, not physical-PC results**; physical-PC benchmarks are part of manual acceptance (MA-PER-01/02).

## Library scale (CI run 77, commit `0edd5f3`; job *Library scale benchmarks*)

UI start-up with a library of N captures (portable build, real window):

| Captures | Window shown since process start | Index complete | Longest UI freeze during indexing | UI tick p95 | Library view | Search | Working set |
|---|---|---|---|---|---|---|---|
| 100 | 1,083 ms | 787 ms | 151 ms | 151 ms | 181 ms | 33 ms | 226 MB |
| 1,000 | 860 ms | 710 ms | 127 ms | 57 ms | 150 ms | 71 ms | 232 MB |
| 5,000 | 811 ms | 1,493 ms | 156 ms | 29 ms | 182 ms | 85 ms | 243 MB |
| 10,000 | 842 ms | 2,487 ms | 172 ms | 24 ms | 202 ms | 87 ms | 243 MB |
| 25,000 | 840 ms | 4,948 ms | 210 ms | 31 ms | 299 ms | 96 ms | 261 MB |

Core library operations (production durability: temp file + flush + atomic rename):

| Captures | Index | Index memory | Search (common / rare) | Thumbnail per item | Autosave median / p95 | Disk |
|---|---|---|---|---|---|---|
| 5,000 | 777 ms | 1.6 MB | 3.2 / 4.1 ms | 0.15 ms | 2.7 / 3.2 ms | 212 MB |
| 10,000 | 1,562 ms | 3.2 MB | 6.2 / 8.1 ms | 0.24 ms | 2.7 / 3.4 ms | 423 MB |
| 25,000 | 4,000 ms | 7.8 MB | 15.5 / 19.8 ms | 0.23 ms | 2.7 / 3.2 ms | 1,058 MB |

(100 and 1,000 core rows are in the `benchmarks-<sha>` artifact.) **Result:** the window appears in well under 3 s
at every size (index continues in the background); the UI stays responsive (p95 tick ≤ 57 ms above 100 captures; the
longest single freeze 127–210 ms); memory grows slowly with library size (226 → 261 MB).

## Editing (packaged build, feature self-test, CI run 77)

4K (3840×2160) rotate + downscale to 1080p + upscale to 5000×3000 + full-image blur radius 8: **942 ms** (target
< 10 s).

## Recording (existing V1 benchmarks)

See [VIDEO-BENCHMARKS.md](VIDEO-BENCHMARKS.md): 1080p30 up to 120 min and 1080p60 up to 60 min with **0 dropped
frames**, flat memory (~570 MB peak private incl. the 256 MB frame budget), synthetic A/V error ≈ 0.1 ms with no drift,
finalisation ≈ 0.1 s per recorded minute; 1440p synthetic measured; **1440p/4K real-screen capture unsupported on the
runner**; 4K on the software encoder is far slower than real time (KL-V13) — 4K requires a hardware encoder and is
**unverified** until a physical-PC run. The constant-frame-rate fix (V1 defect 7) is checked on every commit by the
video self-test (`screen-constant-frame-rate`: 150 frames for 4,999 ms at 30 FPS in run 77).

## Not measured on CI

GPU load, hardware encoders, real audio sync with devices, drag-and-drop latency into real apps, capture latency on
physical displays, 1440p/4K real-screen recording. These are listed in MANUAL-ACCEPTANCE-1.0.md.
