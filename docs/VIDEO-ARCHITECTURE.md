# Screen recording architecture and API research (Milestone V1)

Branch `claude/video-v1`. Evidence labels: **[CI]** verified on the GitHub Windows runner (Windows Server 2025,
build 26100, 1024×768, *Microsoft Basic Render Driver*, no audio devices); **[SDK]** read from Windows SDK interface
definitions (metadata of Microsoft.Windows.SDK.NET 10.0.22621 and the MinGW/Wine IDL headers in the scratch
workspace); **[Assumed]** documented Windows behaviour I could not re-check this session (learn.microsoft.com is
blocked from this environment); **[PC]** needs a physical PC.

## 1. API choices

| Need | Chosen API | Why | Alternatives rejected |
|---|---|---|---|
| Screen frames | **Windows.Graphics.Capture** (WGC), free-threaded frame pool, `CreateForMonitor` / `CreateForWindow` | Supported Windows capture API; respects DRM and secure desktop; shows the OS capture border; window capture follows the window; cursor on/off (`IsCursorCaptureEnabled`) [SDK][CI] | Desktop Duplication (no per-window capture, no OS indicator), GDI BitBlt (slow, no DWM content guarantees), hooks/drivers (OS-restriction bypass — excluded by the owner) |
| GPU surface handling | **Direct3D 11** (BGRA, video support, multithread protected; WARP fallback) | Required by WGC; crop on the GPU (`CopySubresourceRegion`) [CI] | — |
| Encoding + container | **Media Foundation Sink Writer**: H.264 (Main), AAC-LC 160 kbit/s 48 kHz stereo, **fragmented MP4** | Uses the encoders that ship with Windows; hardware encoder when present (`MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS`), else the Microsoft software encoder; fragmented MP4 limits crash loss [CI] | Bundled FFmpeg/x264 (licensing, size, supply chain), paid SDKs (owner: no paid dependencies) |
| System sound | **WASAPI loopback** on the default render endpoint, shared mode, `AUTOCONVERTPCM` to 48 kHz stereo float | Captures exactly what the user hears; follows default-device changes (Bluetooth) [SDK]; behaviour with real devices [PC] | Stereo-mix drivers (not present on most PCs) |
| Microphone | **WASAPI capture** on the default or a chosen endpoint | Opened only when Microphone is on → Windows mic privacy indicator [Assumed][PC] | — |
| Keep own UI out of video | `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` on toolbar and outline | Windows 10 2004+; accepted on the runner [CI]; pixel-level absence [PC] | Hiding the toolbar (would hide the controls) |
| Read back / validate | Media Foundation **Source Reader** (decode to RGB32 / PCM) | Sync measurement, thumbnails of recovered videos, recovery verification [CI] | — |

All native calls go through explicit COM/WinRT vtable interop (`src/SnapForge.App/Video/Interop`), with each vtable slot
checked against the SDK IDL (slot table in the file headers). No projection assembly and no new NuGet packages: the
Windows SDK projection package could not be restored here under locked, signature-verified restore, and weakening that
check was not acceptable.

## 2. Pipeline

```
 WGC frame pool ──(newest frame, GPU crop)──► staging texture ──Map──► pooled BGRA buffer ─┐
   (capture thread, fixed frame-rate schedule; screen unchanged → previous frame repeated)  │ bounded queue
                                                                                            ▼ (drop + count when full)
 WASAPI loopback ─┐                                                                  encoder thread
 WASAPI mic ──────┼─► audio thread: QPC timestamps → RecordingClock → AudioMixer ──► PCM blocks ─► Mp4Writer
 (10 ms polling)  ┘                    (pause removal)   (gaps→silence, drift,      (single owner of the
                                                          bounded buffers)            Sink Writer)
```

* **One clock** — `RecordingClock` maps QPC 100-ns timestamps (WGC `SystemRelativeTime`, WASAPI `qpcPosition`) to
  media time and removes paused intervals for both streams identically (VID-R12).
* **Constant frame rate** — a static screen produces no WGC frames (the runner delivered 3 frames in 1 s of an idle
  desktop [CI]); the capture thread repeats the previous frame in each empty slot so the MP4 is CFR.
* **Bounded memory** (VID-R13) — frame queue sized from a 256 MB budget (3–60 frames); audio buffers capped at 4 s
  per source; when exceeded, frames/samples are dropped and counted in the result.
* **Encoder fallback** (VID-R02/R03) — the writer is created with hardware transforms allowed; if creation or one of
  the first frames fails, the file is restarted on the software encoder and the user is told. The encoder actually
  used is read back from the sink writer (`IMFSinkWriterEx::GetTransformForStream`, friendly name + hardware flag).
  On the runner only `H264 Encoder MFT` (software) exists [CI]; the hardware path is [PC].
* **Key frame every 2 s** (`MF_MT_MAX_KEYFRAME_SPACING`) and fragmented MP4 → crash loss bounded to the last
  unfinished fragment (measured: see test results).

## 3. Failure handling

| Event | Behaviour | Req |
|---|---|---|
| Hardware encoder unavailable / fails at start | software encoder, notice | VID-R02/R03 |
| App crash, kill, power loss | fragments on disk; next start: cut to last complete fragment, decode-verify, add as "Recovered" | VID-R04/R05 |
| Normal stop | Sink Writer `Finalize`; manifest written; file listed | VID-R05 |
| Disk below threshold | warning at < 10 min left; stop + save below minimum (default 500 MB); write error → stop, recover | VID-R06 |
| Microphone/loopback device removed | source marked inactive → silence; retried every 2 s; notice on toolbar | VID-R07 |
| Default output changed (Bluetooth headset) | loopback re-opened on the new default within ~1 s; gap filled with silence | VID-R08 |
| Display disconnected / window closed | recording stops and is saved, notice | VID-R09 |
| Resolution change / window resize | frame pool recreated; output size fixed; content clipped/letterboxed | VID-R09 |
| PC locked | recording paused, notice; user resumes | VID-R10 |
| Sleep / sign-out / shutdown | stop and save before suspend / session end | VID-R10 |
| Long recording | 64-bit timestamps, fragmented file grows without rewriting; bounded buffers | VID-R11 |
| Second recording requested | refused (one recorder per process; single-instance app) | VID-R14 |

## 4. Licensing and distribution (H.264 / AAC)

* SnapForge **does not ship** any H.264 or AAC encoder/decoder. It calls the Media Foundation codecs that are part of
  Windows (and the GPU vendor's hardware encoder MFT when present). This keeps SnapForge free of codec binaries and of
  GPL/LGPL obligations that bundling FFmpeg/x264 would bring.
* H.264 and AAC are patent-licensed formats (pools administered by Via LA, formerly MPEG LA / Via Licensing). Microsoft
  licenses the codecs it includes in Windows for use on that Windows installation. **Whether distributing an application
  that invokes the OS codecs requires its own licence is a legal question — not answered here. Recommendation: legal
  review before a public release.** [Assumed — not legal advice]
* Some Windows editions lack Media Foundation (Windows "N"/"KN" editions without the Media Feature Pack; Windows Server
  without the *Server-Media-Foundation* feature). The runner reported the feature as *Available* and encoding worked
  [CI]. On a PC without it, Record reports that the Windows media components are missing. [PC]
* No new third-party components were added; THIRD-PARTY-NOTICES unchanged.

## 5. Windows 10

WGC and `WDA_EXCLUDEFROMCAPTURE` need Windows 10 version 2004 (build 19041) or later; cursor on/off needs 2004+;
the yellow border cannot be hidden (SnapForge would not hide it anyway). Windows 11 is the primary target (owner
decision); Windows 10 remains *unverified* until tested on a PC (ACCEPTANCE V1-COMPAT-02).
