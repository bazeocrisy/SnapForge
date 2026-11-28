# Screen recording threat model (Milestone V1)

Scope: recording the screen, system sound and microphone to MP4 in the local Library; playback, export and drag-out.
Method: assets → entry points → threats (STRIDE-style) → mitigations → residual risk. Evidence classes as elsewhere:
**Verified-CI** (automated on a real Windows desktop), **Verified-code** (enforced and unit-tested), **Assumed**
(relies on Windows behaviour not tested here), **Pending-PC** (needs a physical PC).

## Assets

1. What is on screen and audible while recording (may include passwords, messages, notifications, personal data).
2. Recordings at rest (`videos/<id>/video.mp4`) and their copies (exports, drag folder).
3. The user's trust that recording happens only when they start it and stops when they stop it.
4. Existing image captures and the Library format.

## Threats and mitigations

| ID | Threat | Mitigation | Evidence |
|---|---|---|---|
| VT-01 | **Recording without the user knowing** (hidden capture) | Recording starts only from an explicit user action (panel Record, widget Record, hotkey) after selection + countdown. Windows' own capture border is left on (SnapForge never calls the API that hides it). The microphone is opened only when Microphone is on, so Windows' microphone indicator appears exactly then. A visible toolbar with a blinking indicator and elapsed time is shown for the whole recording. | Verified-code (no border-disabling call exists in the source); Pending-PC: border/mic indicator visible (V1-SEC-01/02) |
| VT-02 | **Toolbar/outline leaking into the recording** — or the inverse, SnapForge hiding *other* content | `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` is applied only to SnapForge's own toolbar and outline. If Windows does not confirm the affinity, the toolbar says so and is placed outside the recorded area. | Verified-CI: affinity accepted by Windows (probe `excludeFromCapture: true`); pixel check of the toolbar absent from frames: Pending (V1-UI-05) |
| VT-03 | **Bypassing OS restrictions** (DRM-protected video, secure desktop, UAC prompts, other users' sessions) | Only `Windows.Graphics.Capture` is used. Protected content appears black, the secure desktop is not captured, and locking the PC pauses the recording. No driver, injection, desktop duplication hooks or elevation. | Verified-code; behaviour on protected content: Assumed / Pending-PC |
| VT-04 | **Secrets captured unintentionally** (notifications, password typed during recording) | Disclosure in Settings → Recording and in this document: **videos cannot be redacted or sanitized**. Recording requires choosing a region/window/display; the countdown gives time to prepare; Pause and Cancel and discard are always one click away. | Verified-code (text present); user behaviour: residual risk |
| VT-05 | **Recordings exposed at rest** | Stored only under the user's local Library folder (per-user `%LOCALAPPDATA%` by default, inheriting its ACL). No upload, sync, account, telemetry or network code exists (V1 adds none). | Verified-code (no network APIs referenced; SECURITY.md audit) |
| VT-06 | **Drag-out exposing the Library file** to modification or deletion by a drop target | Drag offers a *copy* in a private temporary session folder (deleted after the grace period, at exit and at the next start), with Copy-only effect; the Library MP4 path is never put on the clipboard/drag data. | Verified-code; real Explorer/Teams/Office drops: Pending-PC (V1-LIB-05) |
| VT-07 | **Malicious or malformed MP4 in the Library folder** (planted by another process, or damaged) | Videos are parsed by Windows' Media Foundation (same as Films & TV). SnapForge's own MP4 parsing is limited to top-level box headers with bounds checks (`Mp4Inspector`: sizes validated against the file length, no allocation from file-controlled sizes). Damaged manifests/files are listed as damaged, not opened. | Verified-code (unit tests incl. garbage and truncated inputs) |
| VT-08 | **Recovery claiming a video that is not playable**, or silently discarding data | Recovery cuts only at verified fragment boundaries and then decodes the result; if nothing decodes, the file is left in place and reported. | Verified-CI (kill test) |
| VT-09 | **Disk exhaustion** affecting the system | Free space is checked every 2 s; a warning when < 10 min remain; the recording stops and is saved below the configured minimum (default 500 MB). | Verified-code; real disk-full run: Pending-PC (V1-REL-06) |
| VT-10 | **Two recordings at once** (double hotkey, two instances) | One recorder per process (`ScreenRecorder.Create` refuses a second); single-instance application. | Verified-CI (`single-session`) |
| VT-11 | **Older SnapForge corrupting videos** | Videos live in `videos/`, which older versions never read or write. | Verified-code (migration tests); old-binary run: Pending (V1-LIB-07) |
| VT-12 | **Supply chain** | No new packages: Windows APIs only (WGC, D3D11, Media Foundation, WASAPI) through explicit interop. CI still restores in locked mode from signed nuget.org packages only. | Verified-CI |
| VT-13 | **Codec licensing** | SnapForge ships no encoder/decoder; it uses the H.264/AAC codecs included with Windows. See VIDEO-ARCHITECTURE.md §Licensing — *not legal advice; review before public release*. | Documented |

## Residual risks (accepted for V1, to be revisited)

* Anything visible/audible during recording is in the file; there is no video redaction (owner-acknowledged).
* Permanent delete removes `video.mp4` without overwriting it (multi-GB overwrite impractical; SSDs make overwrite unreliable anyway).
* The microphone indicator and capture border are Windows features; on Windows versions/policies that hide them,
  SnapForge's own toolbar remains the indicator.
