# SnapForge 1.0 threat model and data-flow review

Scope: the SnapForge 1.0 release candidate (Windows 11 x64 desktop app, portable ZIP and per-user installer) and the
static website. Builds on [SECURITY.md](SECURITY.md) (storage table, original threat model) and
[VIDEO-THREAT-MODEL.md](VIDEO-THREAT-MODEL.md). This document adds the 1.0 features (OCR, scrolling capture, video
editing/GIF, MP4 import, new annotation types, installer) and states what is and is not protected.

## 1. Assets (sensitive data)

| Asset | Where | Sensitivity |
|---|---|---|
| Screenshots (editable originals, incl. pixels under reversible redactions) | Library `captures/<id>/` | High: may contain anything on screen |
| Recordings (video + system/mic audio) | Library `videos/<id>/` | High; audio may capture conversations |
| Flattened exports, clipboard images, drag copies | User-chosen files, clipboard, `%LOCALAPPDATA%\SnapForge\Temp\Drag` | High; leave SnapForge's control once shared |
| Recognised text (OCR) | In memory, OCR window only | High while shown; never stored by SnapForge |
| Settings (hotkeys, paths, presets) | `%LOCALAPPDATA%\SnapForge\settings.json` | Low |
| Error logs | `%LOCALAPPDATA%\SnapForge\logs` | Low (exception text only, no image or OCR data) |
| Library backups (.zip) | User-chosen | High (images only) |

## 2. Trust boundaries and data flows

```
Screen (GDI/WGC) ─┐                               ┌─► Export file (PNG/JPEG/BMP/TIFF/MP4/GIF) ─► other apps / people
Audio (WASAPI) ───┼─► SnapForge process (user) ───┼─► Clipboard ─► any app
Clipboard image ──┤     │  in-memory edit          ├─► Drag copy (Temp\Drag, 24 h) ─► drop target app
Import (PNG/JPEG/ ┘     ▼                          └─► Print spooler
 BMP/MP4/.sfcap/zip) Library on disk (user ACLs)
                        │
             Windows OCR (in-process WinRT, local) — no network
```

Boundaries: (1) the user's Windows account (all data is under the user's profile and ACLs); (2) files entering from
outside (imports, backups, packages); (3) data leaving SnapForge (exports, clipboard, drag, print) — after this point
SnapForge cannot protect or delete it; (4) Windows components (Media Foundation, OCR, capture APIs) — trusted platform.

## 3. Threats and mitigations

| # | Threat | Mitigation | Residual risk / documented limitation |
|---|---|---|---|
| T1 | Sensitive pixels leak through a "redacted" export | Redaction writes opaque pixels; exports/clipboard/drag/thumbnails are rendered from the flattened image; adversarial tests (`Redacted_secret_colour_appears_in_no_thumbnail_or_flattened_output`, export/drag tests) | Library copy keeps originals until **Sanitize**; earlier exports, backups and copies held by others are out of reach; deleted files are not overwritten (SSD wear levelling) |
| T2 | User mistakes blur/pixelate for redaction | UI hints and docs say they are not redaction; Redact tool separate | Blurred text may be reconstructable |
| T3 | Video contains sensitive content | Disclosed: videos cannot be redacted; toolbar/outline excluded; Windows capture border and mic indicator stay on | Anything visible/audible is recorded |
| T4 | OCR text exposure | Recognition local (Windows.Media.Ocr); text only shown in the OCR window and copied on request; never written to disk or indexed | Clipboard is readable by other apps once copied |
| T5 | Malformed image import (decoder bugs, decompression bombs) | WPF/WIC decoders; size limits (`ImageImport.MaxPixels`, side limit) checked before allocation; metadata discarded | Decoder vulnerabilities are Windows' responsibility (patched by Windows Update) |
| T6 | Malformed MP4 import | Validated by Windows' decoder (Media Foundation) before copying; failure → rejected with message | Parsing done by Windows components |
| T7 | Malicious .sfcap / backup zip (path traversal, oversized entries, bad JSON) | Entry names validated, traversal rejected (`Restore_rejects_path_traversal_and_foreign_zips`), strict JSON schema; unknown annotation types rejected; new types validated (point counts, finite numbers) | — |
| T8 | Symbolic links/junctions in the Library folder redirecting writes | Library writes are atomic temp+rename inside the chosen folder; SnapForge runs as the user (no elevation), so it can only write where the user can | A user who points the Library at a link target accepts that target |
| T9 | Resource exhaustion (huge images, long recordings, huge GIFs, scrolling captures) | Pixel limits (32,768 px/side, 400 MP for image ops; stitcher 120 MP / 30,000 px), bounded recording queues (256 MB), streaming GIF encoder, disk-space monitoring while recording | Very long GIF exports can be large (warned in the dialog) |
| T10 | Drag copies left on disk | Private per-drag folders under the user profile; 24 h retention, 2 GB cap, deletion when no drop; Settings → Delete drag copies now | Files are deleted, not shredded |
| T11 | Other local processes read temp files or the Library | User-profile ACLs; no world-writable locations | Malware running as the same user can read everything (out of scope; use device encryption) |
| T12 | Network exfiltration | No network code in the app: no updates, telemetry, accounts, cloud OCR | Website is static; GitHub Pages logs IPs (disclosed on the website) |
| T13 | Privilege escalation via installer | Per-user installer (`PrivilegesRequired=lowest`), installs to `%LOCALAPPDATA%\Programs`; no services, drivers or scheduled tasks | Unsigned installer (see SIGNING.md) can be impersonated: verify SHA-256 |
| T14 | Tampered downloads | SHA-256 for every file and package; website shows a release only after `verify-release.py` checks the published asset | No trusted signature until signing is set up |
| T15 | Global hotkeys/keyboard snooping | `RegisterHotKey` only, no keyboard hooks; scrolling capture sends wheel input only while the user runs Auto-scroll, to the selected area | — |
| T16 | Capturing protected content | Standard capture APIs only; DRM/secure desktop content is not captured (Windows returns black) | — |
| T17 | Logs leak content | Logs contain exception text and OS version only | — |
| T18 | Dependency vulnerabilities | No third-party runtime packages; .NET runtime pinned by SDK; CI vulnerability audit; SBOM per package | Runtime updates require a new SnapForge build |

## 4. What Windows protects (and what it does not)

- **Windows account permissions** keep other *users* on the same PC out of your Library. They do not protect against
  programs you run yourself.
- **Device encryption / BitLocker** protects the disk if the PC is lost or stolen while off. SnapForge does **not**
  encrypt the Library itself.
- **Sanitize** removes original pixels from the editable Library copy. It cannot reach exported files, backups,
  cloud-synced copies, copies in other apps, or data remaining on SSD cells after deletion.

## 5. Security testing

See [SECURITY-TESTING.md](SECURITY-TESTING.md) for the executed tests and results.
