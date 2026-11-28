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
| Keep own UI out of video | `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` on toolbar, outline and SnapForge Studio | Windows 10 2004+; accepted on the runner [CI]; pixel-level absence [PC] | Hiding the toolbar (would hide the controls) |
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
| PC locked | recording paused (camera released), notice; user resumes | VID-R10 |
| Sleep / sign-out / shutdown | stop and save before suspend / session end (bounded waits, then crash recovery) | VID-R10, FA-021 |
| Recorder thread stalls while stopping | reported after 10 s, partial file recovered, its native objects released only when it exits | FA-021 |
| Stop pressed twice / stop + cancel at once | one finalization (state machine + idempotent recorder) | FA-021 |
| Camera busy / denied / unplugged | see §3a | SF-006 |
| Long recording | 64-bit timestamps, fragmented file grows without rewriting; bounded buffers | VID-R11 |
| Second recording requested | refused (one recorder per process; single-instance app) | VID-R14 |

## 3a. SnapForge Studio and the camera pipeline (SF-006)

`RecordingController.OpenStudio()` is the Record entry point for the capture panel and the dock: it shows
**SnapForge Studio** (`StudioWindow`), or brings the toolbar to the front while recording. Studio's Start saves the
choices (all in `VideoSettings`: `StudioMode`, `CameraDeviceId`, `CameraShape`, `CameraSize`, `CameraCorner`,
`CameraMirror` plus the existing audio/video fields) and calls `StartAsync()`, which the record hotkey also uses
directly. Studio is excluded from capture (`WDA_EXCLUDEFROMCAPTURE`) like the toolbar and outline.

```
 MFEnumDeviceSources(VIDCAP) ─► IMFActivate::ActivateObject(IMFMediaSource) ─► MFCreateSourceReaderFromMediaSource
   (lists names + symbolic links;            (opens the device: camera light on)      (ADVANCED_VIDEO_PROCESSING)
    does not open the camera)
          native type closest to 1280×720@30 (CameraFormatChooser) ─► output RGB32 (else NV12 / YUY2 converted in code)
          camera thread: ReadSample ─► BGRA ─► CameraFrameRing (4 frames, QPC timestamps)
                                                    │ newest frame not newer than the screen frame
 WGC screen frame ─► CompositeFrameSource ──────────┴─► CameraCompositor (crop, bilinear, mirror, AA mask) ─► recorder
 Camera only:        CameraFrameSource (camera size, rounded down to even) ─────────────────────────────────► recorder
```

* **One owner thread per camera** — `MediaFoundationCameraSource` opens, reads and shuts down the device on a single
  MTA thread; no other thread releases its COM objects. `Stop()` waits 3 s, then calls `IMFMediaSource::Shutdown`
  (unblocks a stuck `ReadSample`; the pointer is guarded so the reader thread cannot release it meanwhile), waits again,
  and otherwise logs and leaves the objects to that thread. Shutdown order: reader released → `IMFMediaSource::Shutdown`
  → `IMFActivate::ShutdownObject` (camera light off). [SDK] order; light behaviour [PC].
* **Clock** — camera sample times are mapped onto QPC with the smallest arrival offset seen (no jitter, never in the
  future; re-anchored after a > 200 ms discontinuity), so frames are on the recorder's existing clock. [PC] latency.
* **Composition** — Screen + camera: each screen frame is copied and the newest camera frame not newer than it is
  drawn in the chosen corner (margin 3 % of the shorter side; sizes 22/30/40 %; circle = square box, rounded
  rectangle = 16:9 box with 16 % radius), centre-cropped to the box aspect (no distortion), bilinear, optional mirror,
  one-pixel antialiased edge. A camera change alone also yields a new frame, so the overlay moves over a static screen.
  CPU cost is per overlay pixel (maps and mask cached per layout). [CI synthetic]
* **Camera only** — the camera feed is the video at its native size; audio as configured. [CI synthetic]
* **Privacy** — camera and microphone are off by default; the camera opens only after an explicit camera choice in
  Studio (preview) or at Start in a camera mode, and is released on Screen only, close, minimize, lock, sleep, Start
  (the recorder reopens it after the area is chosen), pause and hiding the overlay. A Windows privacy block
  (`E_ACCESSDENIED`, or `Deny` in the CapabilityAccessManager consent store [Assumed]) is explained with a link to
  Settings → Privacy & security → Camera / Microphone; nothing retries silently.
* **Testing seam** — `ICameraSource` with `MediaFoundationCameraSource` and `SyntheticCameraSource` (CI has no camera;
  every synthetic result is labelled SYNTHETIC). `RecordingController.CameraFactory` substitutes the camera.

| Camera event | Behaviour |
|---|---|
| No camera | Studio says so and disables Start for camera modes; `StartAsync` explains | 
| Busy (`MF_E_HW_MFT_FAILED_START_STREAMING`, `ERROR_BUSY`, sharing violation, preempted) | "used by another app"; Screen + camera may continue without it |
| Access denied (`E_ACCESSDENIED`) | privacy message + open Settings; no retry |
| Unplugged / fails mid-recording (`ReadSample` error / `MF_SOURCE_READERF_ERROR` / end of stream) | Screen + camera: notice, overlay removed, screen keeps recording; Camera only: recording stops and is saved |
| Pause / lock / overlay hidden | camera released (light off); reopened on resume / show (failure → notice) |
| Stop / cancel / exit / sleep | camera released with the recorder's sources (after the capture thread has exited) |

### Interop used (vtable slots from mfobjects.idl / mfidl.idl / mfreadwrite.idl order) [SDK]

| Interface | Slots used | File |
|---|---|---|
| IMFAttributes (3–32) | GetUINT32 7, GetUINT64 8, GetGUID 10, GetAllocatedString 13, SetUINT32 21, SetUINT64 22, SetGUID 24 | `MediaFoundation.cs` |
| IMFActivate : IMFAttributes | ActivateObject 33, ShutdownObject 34 | `MediaFoundation.Camera.cs` |
| IMFMediaSource : IMFMediaEventGenerator (3–6) | Shutdown 12 (GetCharacteristics 7, CreatePresentationDescriptor 8, Start 9, Stop 10, Pause 11) | `MediaFoundation.Camera.cs` |
| IMFSourceReader | SetStreamSelection 4, GetNativeMediaType 5, GetCurrentMediaType 6, SetCurrentMediaType 7, ReadSample 9 | both |
| IMFSample : IMFAttributes | GetSampleTime 35, ConvertToContiguousBuffer 41 | `MediaFoundation.cs` |
| IMFMediaBuffer | Lock 3, Unlock 4 | `MediaFoundation.cs` |
| IMF2DBuffer | Lock2D 3, Unlock2D 4 | `MediaFoundation.Camera.cs` |
| Functions | `MFEnumDeviceSources` (mf.dll), `MFCreateSourceReaderFromMediaSource` (mfreadwrite.dll) | `MediaFoundation.Camera.cs` |

GUIDs: `MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE` c60ac5fe-…, `…_VIDCAP_GUID` 8ac3587a-…, `…_FRIENDLY_NAME` 60d0e559-…,
`…_VIDCAP_SYMBOLIC_LINK` 58f0aad8-…, `MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING` 0f81da2c-…, IID_IMFMediaSource
279a808d-…, IID_IMF2DBuffer 7dc9d5f9-… — all from the scratch-workspace IDL copies [SDK]. The `MF_E_*` HRESULTs come
from mferror.h, which was not in that set [Assumed]; any other failure is reported with its code.

## 3b. Recording lifecycle (FA-021)

```
          TryBeginStart            TryCompleteStart          TryBeginStop / TryBeginCancel
  Idle ───────────────► Starting ───────────────► Recording ◄──► Paused ───────────────► Stopping
   ▲  ▲                    │  stop/cancel here = start abandoned        (exactly once)       │ recorder threads
   │  └── AbortStart ──────┘  (selection, camera, countdown)                                  ▼ joined (bounded)
   │                                                                                       Finalizing
   └──────────── Finish(ok) ──────────────────────────────────────────────────────────────── │ Library / save-as
                                         Faulted ◄── Finish(faulted) ─────────────────────────┘ (new start allowed)
```

* `RecordingLifecycle` holds the phase under one lock; every request is a single atomic transition, so double stops,
  stop + cancel races, pause during stop etc. cannot interleave. The controller acts only on a successful transition.
* `ScreenRecorder.Stop/Cancel` are idempotent: the first call owns the work (a `TaskCompletionSource`), concurrent and
  later calls wait for it and receive the same result; a cancel after a stop returns false (the file is saved).
* **Bounded joins** — capture, audio and encoder threads are each joined for at most 10 s (`RecorderHooks.JoinTimeout`).
  A thread that does not finish is reported (`RecordingResult.Stalled`, toolbar notice, `recording.log`); the fragmented
  file stays in the Library for recovery; COM objects that thread still uses are **not** released — sources are
  disposed only when `ThreadsExited` completes (deliberate, logged leak until then). The controller then recovers the
  file as soon as the threads exit (or at the next start). The encoder thread is the writer's only user, including
  `Finalize` and release.
* **Exit** — `StopForExit` waits at most 8 s on the UI thread, then leaves the fragmented MP4 for
  `RecordingRecovery` at the next start. Sign-out uses the same path. **Sleep** — the suspend notification stops the
  recording and waits (≤ 5 s) for the file before returning; the Library step runs after wake. **Lock** — pauses
  (and releases the camera) via the same `TryPause` transition as the button.
* **Fault seams** (`RecorderHooks`, tests/self-test only): delay or throw before each encoder write (disk full =
  `IOException` 0x80070070), block the capture loop, before `Finalize`, and the join timeout.

## 3c. Saving outside the Library (FA-022)

`VerifiedFileCopy.Copy`: copy to `.<name>.snapforge-tmp-<random>` in the destination folder (hashing the source while
reading), `Flush(true)`, verify size and SHA-256 by reading the copy back, then `File.Move(temp, target, overwrite:
true)` (MoveFileEx replace — atomic on the volume; the save dialog already confirmed any overwrite). Only after that
does the controller delete the Library copy. Any failure deletes the temp file and leaves the Library copy and any
existing destination untouched; the user is told.

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
