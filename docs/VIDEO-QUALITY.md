# Video quality: why release candidate 2 recordings look grainy, and the RC3 fix

Owner report (RC2): the saved video looks grainy. Sample: H.264, 1920×1080, 30 FPS, average video bit rate ≈ 5 Mbit/s.

This document records the investigation (screen and camera are separate findings), the new defaults, and the
repeatable comparison that CI runs (`SnapForge.exe --video-quality <dir>`). File/line references marked
**rc2** point at `release/v1.0-candidate` commit `3faacf4` (the code the owner recorded with); unmarked
references point at this branch.

## 1. Pipeline as built in RC2

```
WGC frame (physical pixels, BGRA) → crop 1:1 (no scaling) → [camera overlay: CPU compositor] →
ScreenRecorder (constant-frame-rate slots) → Mp4Writer (sink writer, RGB32 input) →
Media Foundation colour converter RGB32 → NV12 (4:2:0) → H.264 encoder MFT (hardware if available, else
Microsoft software encoder) → fragmented MP4
```

## 2. Screen clarity — findings

| # | Cause | Where (rc2) | Effect | RC3 |
|---|---|---|---|---|
| S1 | **Bit rate far too low for desktop content.** 0.08 bits per pixel per frame: 1920×1080×30×0.08 ≈ 5.0 Mbit/s (High 0.16 ≈ 10 Mbit/s). Desktop text is sharp edges on flat colour; at ~0.08 bpp H.264 cannot keep 1–2 px glyph strokes, so they smear and ring ("mosquito" noise around letters), which reads as grain. Camera-style bpp figures do not transfer to screen content. | `src/SnapForge.Core/Settings/VideoSettings.cs:148-155` (rc2) | Primary cause of the owner's sample (its 5 Mbit/s is exactly this formula). | Fixed: 0.18 bpp (Standard, 1080p30 ≈ 11.2 Mbit/s), see §4. |
| S2 | **Rate control never set.** No `CODECAPI_AVEncCommonRateControlMode`, `AVEncCommonMeanBitRate/MaxBitRate`, `AVEncCommonQuality` — only `MF_MT_AVG_BITRATE` on the output type. The encoder's default applies; for the Microsoft software H.264 encoder that is **CBR**. CBR spends the same bits on a static screen as on a scroll, so when something moves (scrolling, the camera) the static text around it gets a coarser quantizer; there is no headroom for the frame after a burst. | `src/SnapForge.App/Video/Mp4Writer.cs:107-131` (rc2): output type only, `SetInputMediaType(..., 0)` at :130 (no encoding parameters) | Text degrades whenever anything moves; quality "pumps". | Fixed: peak-constrained VBR (mean = target, peak = 2× mean). |
| S3 | **Main profile.** `MF_MT_MPEG2_PROFILE = eAVEncH264VProfile_Main`. The 8×8 transform and 8×8 intra prediction exist only in High profile; they are what keeps flat areas and straight edges clean at a given bit rate. | `Mp4Writer.cs:114` (rc2) | Lower efficiency on exactly the content we record. | Fixed: High profile (falls back to Main if refused). |
| S4 | **CABAC not requested.** `CODECAPI_AVEncH264CABACEnable` never set; whether the encoder uses CABAC (≈10 % fewer bits than CAVLC) is up to its default. | `Mp4Writer.cs:107-131` (rc2) | Possibly ~10 % of the already small budget wasted. | Fixed: CABAC on (confirmed by read-back). |
| S5 | **Low-latency mode on.** `MF_LOW_LATENCY = 1` was set to get no B-frames (B-frames made fragmented-MP4 timestamps up to 300 ms wrong, CI 2026-11-26). On many encoders low-latency mode also shrinks the rate-control buffer / disables look-ahead — combined with CBR the per-frame bit budget becomes almost constant. | `Mp4Writer.cs:55` (rc2) | Less quality per bit; no flexibility for bursts. | Changed: B-frames are now requested explicitly as 0 (`AVEncMPVDefaultBPictureCount = 0`) and low-latency mode is turned off **only when the encoder confirms 0 B-frames**; otherwise the writer goes back to low-latency mode (timestamps stay exact either way). |
| S6 | GOP: a key frame every 2 s via `MF_MT_MAX_KEYFRAME_SPACING` only (no `AVEncMPVGOPSize`). The interval is sensible (bounds crash loss per fragment); at 5 Mbit/s CBR each key frame visibly "resets" sharpness. | `Mp4Writer.cs:119` (rc2) | Minor: periodic blur/sharpen cycle. | Kept 2 s; also set through `AVEncMPVGOPSize` and confirmed. |
| S7 | **No settings read-back.** Nothing reported which encoder settings were actually applied (hardware MFTs may ignore media-type hints); the log only had the encoder name. | `Mp4Writer.cs:77` (rc2, `GetEncoder`: name only) | Could not tell from a user's log what the encoder did. | Fixed: `ICodecAPI::GetValue` read-back logged per recording (`RecordingLog`), in `RecordingResult.EncoderSettings` and in the self-test reports. |
| S8 | 4:2:0 chroma: the sink writer's colour converter turns RGB32 into NV12, halving colour resolution. Coloured small text (red/blue on white, coloured syntax highlighting) loses colour sharpness regardless of bit rate. The YUV matrix (`MF_MT_YUV_MATRIX`) is not set, so players may assume BT.709 while the converter used its default — a possible slight colour shift, not grain. | `Mp4Writer.cs:123` (rc2) | Inherent to H.264 4:2:0 in Media Foundation. | **Not changed** (needs 4:4:4 or a different encoder; out of scope). Follow-up: set and verify the YUV matrix. |
| — | **Not a cause: scaling / DPI.** Windows.Graphics.Capture delivers physical pixels; the app is PerMonitorV2-aware (`src/SnapForge.App/app.manifest:24-25`), the selection rectangle is in physical pixels, and the crop is copied 1:1 (`Sources.cs:95-101, 184, 201` rc2). Encoded size = captured size rounded down to even (an odd width/height drops its last column/row). No rescaling anywhere on the screen path. | | | — |
| — | Hardware vs software encoder: `READWRITE_ENABLE_HARDWARE_TRANSFORMS` lets Media Foundation pick a hardware MFT; on failure the recorder restarts on the software encoder (`ScreenRecorder.cs:268-279, 934-946` rc2). Both paths now get the same tuning and fallback ladder. | | | Same ladder for both. |

## 3. Camera quality — findings (separate from the screen)

| # | Cause | Where (rc2) | Effect | RC3 |
|---|---|---|---|---|
| C1 | **Overlay downscaling is two-tap bilinear at every scale.** A 1280×720 camera is centre-cropped to 720×720 and drawn into a 324×324 circle (Medium on 1080p): a 2.2:1 downscale (3.3:1 from a 1080p camera). Bilinear sampling reads 2 of every 2.2–3.3 source pixels per axis and ignores the rest, so fine detail (hair, fabric, text on a shirt) and sensor noise alias into grain and shimmer that changes every frame — which also costs encoder bits. | `src/SnapForge.App/Video/CameraComposition.cs:104-111` (per-pixel 2×2 taps), `:169-187` (`Map`) (rc2) | Grainy, shimmering camera picture independent of the bit rate. | Fixed: **area average** (box filter as wide as an output pixel; every source pixel contributes by coverage) when downscaling, bilinear when upscaling — `src/SnapForge.Core/Imaging/ResampleKernel.cs`, `CameraCompositor.DrawArea`. |
| C2 | **Capture size fixed at 1280×720 @ 30.** The wanted size is the constructor default for every use; it does not depend on the overlay size or on camera-only mode (where the camera picture *is* the video, so a 1080p camera was recorded at 720p). On a 4K screen the Large overlay (864 px) is upscaled from 720 px. | `Camera.cs:300` (`Choose` defaults), `:400` (constructor defaults), `RecordingController.cs:338` (rc2) | Soft camera on large overlays / camera-only. | Fixed: `CameraFormatChooser.WantedSize` — camera-only 1920×1080; overlay 1280×720 while it gives ≥ 1.25× the overlay height, else 1920×1080; never above 1080p. `Choose` still prefers full frame rate, then the closest size, then uncompressed, so 160×120 / 320×240 defaults are used only by cameras that offer nothing larger. |
| C3 | **Refused native format is silent.** The result of setting the chosen native type is discarded; if the driver refuses it, its default type stays (often 640×480 or the first listed) and the reader's video processor scales it to the requested size — soft or blocky, with nothing in the log. | `Camera.cs:681` (rc2) | Possible soft camera on some drivers, undiagnosable. | Logged now: chosen format, whether it was set, and the size/format the reader delivers (`RecordingLog`). Behaviour otherwise unchanged. |
| C4 | Circle mask edge: one-pixel antialiased coverage from a signed distance. | `CameraComposition.cs:163` (rc2) | Fine. | Unchanged (both scaling paths use it). |
| C5 | Mirroring: column index reversal in the sampling map. | `CameraComposition.cs:177` (rc2) | Exact, no quality cost. | Unchanged (also in the area kernel). |
| C6 | YUV → RGB fallback (when the reader refuses RGB32) uses BT.601 limited range; HD webcams usually signal BT.709. | `Camera.cs:329-373` (rc2) | Slight colour shift, not grain. | Not changed. |
| C7 | The overlay shares the screen's bit budget; at 5 Mbit/s CBR the moving face competes with the screen. | S1/S2 | Blocky face during movement. | Fixed by S1/S2. |
| C8 | The Studio *preview* still opens the camera at the 1280×720 default. | `StudioWindow.cs:63` | Preview only, not recorded. | Not changed (file owned by the UI redesign). |

## 4. The new default (one setting, no new UI)

The user-facing choice stays **Standard / High** (Settings → Recording). Everything below is internal
(`VideoEncoderTuning.Default`, `src/SnapForge.Core/Video/VideoEncoderTuning.cs`).

| | RC2 (`VideoEncoderTuning.Rc2`) | RC3 default (`VideoEncoderTuning.Default`) |
|---|---|---|
| Bit rate model | `W×H×fps×bpp`, bpp 0.08 / 0.16 (`VideoSettings.Rc2BitRate`) | `W×H×30×bpp×(fps/30)^0.75`, bpp **0.18 / 0.28** (`VideoSettings.BitRate`), rounded to 100 kbit/s, 1–80 Mbit/s |
| Standard 1080p30 / 1080p60 | 5.0 / 10.0 Mbit/s | **11.2 / 18.8 Mbit/s** |
| High 1080p30 / 1080p60 | 10.0 / 19.9 Mbit/s | 17.4 / 29.3 Mbit/s |
| 1440p60 Standard · 4K30 Standard | 17.7 · 19.9 Mbit/s | 33.5 · 44.8 Mbit/s |
| Profile | Main | **High** |
| Rate control | encoder default (CBR on the Windows software encoder) | **peak-constrained VBR**, mean = bit rate, peak = 2× mean |
| GOP | 2 s (media type only) | 2 s (media type + `AVEncMPVGOPSize`) |
| CABAC | encoder default | **on** |
| B-frames | none (via low-latency mode) | **0 requested and confirmed**; low-latency mode **off** when confirmed, on otherwise |
| Camera overlay scaling | bilinear | **area average** (downscale), bilinear (upscale) |
| Camera capture size | 1280×720 always | 1280×720 or 1920×1080 by overlay need; 1920×1080 camera-only |

Why (fps/30)^0.75: at 60 FPS consecutive frames differ half as much, so motion-compensated prediction needs
fewer bits per frame; 60 FPS gets 1.68× the 30 FPS rate rather than 2× (keeps 60 FPS 1080p under 20 Mbit/s for
the CPU/encoder budget behind the "60 FPS, 0 dropped" check).

**Applying it safely.** `Mp4Writer` passes the CODECAPI values as the sink writer's encoding parameters
(`SetInputMediaType(..., pEncodingParameters)`), then reads each one back through the encoder's `ICodecAPI`;
a value the encoder does not report is set once directly with `ICodecAPI::SetValue` and read again. When the
sink writer refuses a combination (or B-frames = 0 cannot be confirmed with low-latency off), it steps down
`VideoEncoderTuning.FallbackLadder()`:

1. the requested tuning;
2. the same with low-latency mode on;
3. same profile, no ICodecAPI properties, low-latency on;
4. RC2's encoder settings (Main, low-latency) **at the new bit rate** — what every encoder accepted in RC2.

Every rung keeps exact timestamps (B-frames confirmed 0, or low-latency mode). What was refused and what
the encoder reports is one log line per recording (`RecordingLog`: "video encoder … : rc3-default: High profile,
PeakConstrainedVbr … encoder reports rate control …, GOP 60, CABAC 1, B-frames 0"), and is in
`RecordingResult.EncoderSettings` (shown in `--video-selftest` report.json as `EncoderSettings`).
The software-encoder fallback in `ScreenRecorder` uses the same tuning and ladder.

**File size / disk estimates.** `VideoSettings.BytesPerSecond` is unchanged and now gets the new bit rate:
Standard 1080p30 with audio ≈ 1.45 MB/s ≈ **83 MB/min** (RC2 ≈ 37 MB/min). With VBR this is an upper estimate
of the mean (a static screen uses far less), so the low-disk warning and the automatic stop
(`ScreenRecorder.CheckDiskSpace`) are conservative. docs/VIDEO-BENCHMARKS.md MB/min figures were measured with
RC2 settings and will change.

**Self-test thresholds are unchanged** (A/V sync ≤ 20 ms, constant frame rate ±2 frames, 60 FPS ≥ 280 frames
and < 30 dropped, exact timestamps, regular frame spacing, Studio dropped < 1 %).

## 5. Repeatable comparison: `SnapForge.exe --video-quality <dir>`

Code: `src/SnapForge.App/Video/VideoSelfTest.Quality.cs`. CI step: "Video quality comparison" in the
`portable-without-dotnet` job (right after the video recording self-test), artifact `video-quality-<sha>`.

**Scene** (`QualityScene`, deterministic — frame *i* is a pure function of *i*, not the live screen):

| Region | Rect (x, y, w×h) | Content |
|---|---|---|
| small-text | 40, 40, 860×440 | Segoe UI 9 / 10 / 11 pt (12 / 13.3 / 14.7 px) at 96 DPI (100 %), WPF grayscale antialiasing with display formatting (as menus render), dark and grey on white, one white-on-blue selected line, confusable glyphs (`Il1| O0o rn m cl`) |
| static-detail | 960, 40, 920×440 | 1-px and 2-px checkerboards, 1-px line grids and antialiased diagonals, a photo-like gradient with texture and fixed noise |
| scrolling | 40, 520, 860×480 | text panel, static for 2 s, then scrolls up 6 px/frame (180 px/s) |
| moving object | 960, 520, 560×200 | 120 px gradient tile moving 10 px/frame after 2 s |
| camera-overlay | Medium circle, bottom right (324 px), mirrored | **SYNTHETIC** static 1280×720 picture (zone plate, face-like gradients, 1-px "hair" lines, "SYNTHETIC CAMERA" label) composited through the real `CompositeFrameSource`/`CameraCompositor`; measured on the square inscribed in the circle (no mask edge) |
| frame code | 40, 1024, 384×24 | 16-bit frame number (24 px black/white blocks) |

**Recordings.** Two 5 s, 30 FPS recordings through the real `ScreenRecorder` → `Mp4Writer` pipeline, Standard
quality, no audio: `quality-rc2.mp4` (RC2 tuning + bilinear camera scaling) and `quality-rc3.mp4` (current
default). Hardware encoder allowed, as in the app (on the CI runner this is normally the software encoder).

**Measurements per recording.** Dimensions, decoded frames, duration and fps, key frames, profile stored in the
file, measured average video bit rate (sum of compressed video sample sizes ÷ duration), requested mean bit
rate, file size, encoder name, hardware/software, software fallback, applied settings and encoder read-back,
frames written / repeated / dropped (share = dropped ÷ (written + dropped)), CPU.

**Fidelity.** Every decoded frame's frame code is read, and the two sample frames are the frame numbers present in
**both** files nearest to 45 (static part, mid-GOP) and 105 (scrolling and moving, mid-GOP). The reference is the
scene frame with that number, with the camera scaled by an exact double-precision area average (the ideal
box-filtered downscale) through the same circle mask. Per region and sample: luma PSNR (dB), SSIM (8×8 windows,
step 4) and mean absolute error (`src/SnapForge.Core/Imaging/ImageQuality.cs`). Additionally the camera scaler
alone (before encoding) is compared with the reference for RC2 bilinear and RC3 area average. Note: because the
reference *is* a box filter, the camera-overlay numbers favour the area-average path by construction; the scaler-
only line separates that from the encoding difference.

**Outputs.** `quality-report.json`; native-size PNG crops (no rescaling) of the same frame and region from the
source, RC2 and RC3: `quality-text-{source,rc2,rc3}` (480×270 of small text, static frame),
`quality-detail-*` (480×270 of static detail, static frame), `quality-scroll-*` (480×270 of the scrolling panel,
scrolling frame), `quality-camera-*` (the 324×324 overlay box, static frame). The CI step prints them in the same
base64 format as the website PNGs: `WEBPNG-BEGIN <name> <sha256>`, `WEBPNG <≤4000 chars>` lines,
`WEBPNG-END <name>`; all names start with `quality-`. stdout lines start with `QUALITY`.

**Exact CI pass criterion** (also printed as `QUALITY criterion:`): the step fails unless

1. mean small-text PSNR of the new default over the static and the scrolling sample frame ≥ RC2's mean + **1.0 dB**,
2. mean small-text SSIM of the new default (same two frames) ≥ RC2's, and
3. the new recording dropped ≤ **1 %** of its frames (dropped ÷ (written + dropped)).

It also fails (with `QUALITY VERDICT FAIL: the comparison could not run …`) if the comparison throws.

## 6. Measured results: pending CI

Nothing in this section has been measured yet. The development machine is Linux; the comparison only runs on
the Windows CI runner. Fill in from the CI log (`QUALITY …` lines) and `video-quality-<sha>` artifact:

| | RC2 settings | RC3 default |
|---|---|---|
| Encoder (HW/SW), rung applied | pending CI | pending CI |
| Encoder read-back (rate control, mean, max, GOP, CABAC, B) | pending CI | pending CI |
| Size, fps, key frames, profile in file | pending CI | pending CI |
| Measured video bit rate / file size | pending CI | pending CI |
| Dropped / repeated frames | pending CI | pending CI |
| small-text PSNR / SSIM / MAE (static · scrolling) | pending CI | pending CI |
| static-detail PSNR / SSIM | pending CI | pending CI |
| scrolling PSNR / SSIM | pending CI | pending CI |
| camera-overlay PSNR / SSIM | pending CI | pending CI |
| camera scaler only (no encoding) | pending CI | pending CI |
| Verdict | pending CI | |

## 7. Uncertain / not verified here

- The Microsoft software encoder's acceptance of each CODECAPI value through encoding parameters vs direct
  `SetValue`, and whether it reports `AVEncMPVDefaultBPictureCount` (if it does not, the writer runs with
  low-latency mode on — rung 2 — which is still High profile + VBR + CABAC). The CI log line shows which.
- Hardware encoders (Intel/AMD/NVIDIA MFTs) on the owner's PC were not available; they may report fewer values.
  The ladder guarantees a working configuration, and the log says what was applied.
- The A/V sync and timestamp checks with low-latency mode off are expected to hold (B-frames confirmed 0), but
  only the Windows CI run can show it.
- Real webcams: the capture-size choice and area-average path are unit-tested with synthetic pictures; real
  devices are covered by the physical acceptance tests (docs/VIDEO-ACCEPTANCE.md).
- CODECAPI GUIDs and the `ICodecAPI` / `IMFSinkWriter::GetServiceForStream` vtable slots were checked against the
  public mingw-w64 headers (`codecapi.h`, `strmif.idl`, `strmiids.c`) and an `mfreadwrite.idl` copy, not against the
  Windows SDK itself.
