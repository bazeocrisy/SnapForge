# Security and privacy model

Screenshots routinely contain secrets (passwords, tokens, personal data). SnapForge's security goal is
that **nothing leaves the machine unless the user explicitly copies or saves it, and redaction is real**.

## Assets

Captured pixels, window titles (read only to pick windows, never stored), files the user saves.

## Controls

| Area | Control |
|------|---------|
| Network | No networking code, no telemetry, no update checks, no crash reporting service. |
| Privileges | `asInvoker` manifest; never requests administrator rights. No services, drivers, hooks or startup entries. |
| Storage | Screenshots live in memory only. No auto-save, temp files or history on disk. Files are written only to the path chosen in the Save dialog, via a temp file in that same folder that is renamed or deleted. |
| Redaction | Implemented as a pixel overwrite of the canonical image buffer with an opaque color, using a covering pixel rectangle (partial edge pixels are included). Exports are produced from that buffer, so the original pixels cannot be recovered from saved or copied images. Black fill is used rather than blur/pixelate, which can be reversed. |
| In-session undo of redaction | The original pixels of a redacted area are retained in memory inside the undo command so the user can undo. They are discarded when a new capture is loaded or the app closes. They are never serialized. |
| Metadata | PNG/JPEG are encoded from raw pixels with no EXIF, XMP, text chunks, user name or location. |
| Clipboard | Written only on explicit Copy. Clipboard contents are visible to other apps and to Windows clipboard history/sync by design of Windows; users should not copy unredacted secrets. |
| Protected content | Windows that opt out of capture (`SetWindowDisplayAffinity`, DRM video) appear black; SnapForge does not bypass this. |
| Diagnostics | On an unexpected error a plain-text log (exception and OS version, no image data) is written to `%LOCALAPPDATA%\SnapForge\logs`. |
| Native interop | Source-generated `LibraryImport`; all GDI handles released in `finally`; buffer sizes computed with `checked` arithmetic. |
| Supply chain | No runtime NuGet packages. CI runs NuGet vulnerability audit on restore and `dotnet list package --vulnerable --include-transitive`. CI workflow uses `permissions: contents: read`. |

## Known gaps

* The development build is **not code-signed**; users cannot verify the publisher. The CI step publishes a
  SHA-256 checksum of the exe alongside the artifact.
* Freed memory is not explicitly zeroed (managed runtime); a memory dump taken while SnapForge is running
  or shortly after could contain screenshot data.
* Window titles of other applications are read into memory during window selection (needed to enumerate
  windows; not displayed or stored).
