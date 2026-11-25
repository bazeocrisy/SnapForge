# Security and privacy threat model

Updated 2026-11-25 for the persistent Capture Library. Screenshots routinely contain passwords, tokens, personal
data and confidential documents. With the Library, SnapForge now **keeps captures on disk by default**, which changes
the privacy model from "nothing persists unless exported" to "captures persist locally until deleted".

## What is stored, where

| Data | Location | Created when | Removed when |
|---|---|---|---|
| Editable captures (base pixels, annotations, metadata, thumbnail) | `%LOCALAPPDATA%\SnapForge\Library\captures\<id>\` (or the folder chosen in Settings) | Every capture, unless **Save to Library** is off | Delete permanently / Empty Trash / retention |
| Recent tray layout | `<Library>\tray.json` | Tray changes | Never contains pixels |
| Settings (options, presets, hotkeys, quick styles, library path) | `%LOCALAPPDATA%\SnapForge\settings.json` | Settings changes | Uninstall/delete folder |
| Crash logs (exception text + OS version, **no image data**) | `%LOCALAPPDATA%\SnapForge\logs\` | Unexpected error | Manually |
| Exported PNG/JPEG, .sfcap packages, backups | Wherever the user chooses | Explicit Export/Backup | Outside SnapForge's control |
| Clipboard image | Windows clipboard (and clipboard history / cloud clipboard if the user enabled them) | Copy, or "Copy to clipboard" capture option | Next copy / user clears history |

The privacy notice (first start; Help → Where are my captures?) and Settings → Library state the exact folder.
`%LOCALAPPDATA%` is chosen over Documents/Pictures because those folders are commonly redirected to OneDrive.

## Assets and adversaries

| Adversary | Goal | Relevant controls |
|---|---|---|
| Recipient of an exported image | Recover redacted content | Exports are built only from the visible image (redactions burned in) + annotations (masks drawn above earlier annotations); no metadata; tests assert redacted pixels are exactly black in PNG, JPEG and clipboard output |
| Recipient of a `.sfcap` package or backup ZIP | Undo redactions | **These are editable copies** and contain original pixels unless sanitized. Export of an unsanitized `.sfcap` shows a warning; backups are documented as containing originals |
| Another user / malware with access to the user profile | Read the library | Out of scope for app-level controls: the library inherits the Windows profile's ACLs; BitLocker/device encryption is the mitigation. SnapForge does not encrypt the library (documented limitation) |
| Someone with physical/forensic access after deletion | Recover deleted captures | Delete permanently overwrites file contents with zeros before deletion, but SSD wear-levelling, copy-on-write, Volume Shadow Copies, File History, cloud sync and backups can retain old data (documented) |
| Malicious `.sfcap` / backup file | Write outside the library, crash the app | Strict entry-name allow-list (no paths → no zip slip), hash verification, size limits, decoding validation; imports get a new id; restore never overwrites |
| Network attacker | Intercept captures | SnapForge has **no network code**: no upload, sync, telemetry, update checks or accounts |
| Another local program | Steal hotkeys / inject input | Hotkeys registered with RegisterHotKey (no keyboard hooks); conflicts are reported, never forced |

## Reversible editing vs irreversible export (and the warning)

* **In the Editor and in the editable Library copy, redactions are reversible**: the base image keeps the original
  pixels so Undo works, even after restarting SnapForge. The Editor shows a banner while a library capture is in this
  state, tiles in the Library are marked "Unsanitized redactions", and Settings counts such captures.
* **Exports (PNG/JPEG/clipboard) are irreversible**: they never contain original pixels or annotations hidden under a
  redaction.
* **Sanitize** (File → Sanitize, Library → Sanitize, Settings → Sanitize all) is the explicit destructive step: it
  burns redactions into the stored base image, flattens annotations hidden under redactions into the image and removes
  them as objects, regenerates the thumbnail, clears undo history, and overwrites the previous base and thumbnail files
  with zeros before replacing them. Crash-safe (completed on next start).

Full pipeline review: [REDACTION.md](REDACTION.md).

## Controls summary

| Area | Control |
|---|---|
| Privileges | `asInvoker`; no services, drivers, keyboard hooks, startup entries or elevation |
| Storage integrity | Atomic temp-file + flush + rename writes; manifest with SHA-256 written last; damaged captures listed but never opened; stale temp files cleaned |
| Capture without history | "Save to Library" off keeps the capture only in memory; closing asks before losing it |
| Memory hygiene | Full-desktop frame zeroed after crop; document buffers zeroed when a capture is closed or evicted (best effort) |
| Metadata | No app/website/user/location metadata recorded or exported |
| Deletion | Trash first; permanent delete and retention overwrite then delete (best effort) |
| Supply chain | No runtime NuGet packages; nuget.org-only, signature-required restore; lock files; SHA-pinned actions; least-privilege CI; checksums verified after download |

## Known limitations

* Library files are not encrypted by SnapForge; protection relies on Windows account security and disk encryption.
* Secure deletion cannot be guaranteed on SSDs, copy-on-write volumes, shadow copies, File History, cloud-synced folders
  or backups; earlier exports, backups and `.sfcap` copies are outside SnapForge's control.
* If the user places the library in a cloud-synced folder, that service uploads captures (warned in Settings).
* The development build is not code-signed (release blocker).
* WPF keeps its own copies of on-screen bitmaps in unmanaged memory; a memory dump of the running process could
  contain screen content.
