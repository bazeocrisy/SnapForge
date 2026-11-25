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
| Redaction | Pixel overwrite of the working image with opaque black (covering pixel rectangle) **plus** an opaque block above all earlier annotations. Exports are produced from that state, so neither original pixels nor covered annotations can be recovered from saved or copied images. Full analysis: [REDACTION.md](REDACTION.md). |
| In-session undo of redaction | Original pixels are retained in memory inside the undo command so the user can undo while editing. They are zeroed when a new capture replaces the document or the app closes. Never serialized. |
| Capture buffer | The full-desktop frame is zeroed after the selection is cropped from it. |
| Metadata | PNG/JPEG are encoded from raw pixels with no EXIF, XMP, text chunks, user name or location. |
| Clipboard | Written only on explicit Copy. Clipboard contents are visible to other apps and to Windows clipboard history/sync by design of Windows; users should not copy unredacted secrets. |
| Protected content | Windows that opt out of capture (`SetWindowDisplayAffinity`, DRM video) appear black; SnapForge does not bypass this. |
| Diagnostics | On an unexpected error a plain-text log (exception and OS version, no image data) is written to `%LOCALAPPDATA%\SnapForge\logs`. |
| Native interop | Source-generated `LibraryImport`; all GDI handles released in `finally`; buffer sizes computed with `checked` arithmetic. |
| Supply chain | No runtime NuGet packages. `nuget.config`: nuget.org is the only source (package source mapping) and every package must carry a valid nuget.org repository signature (`signatureValidationMode=require`, trusted certificate fingerprints from the nuget.org service index). `packages.lock.json` pins versions and content hashes; CI restores in locked mode. NuGet vulnerability audit on restore plus `dotnet list package --vulnerable --include-transitive`. |
| CI | `permissions: contents: read` for all jobs; third-party actions pinned to full commit SHAs; `persist-credentials: false` on checkout; deterministic `ContinuousIntegrationBuild`; artifacts carry `SHA256SUMS.txt` for every file, re-verified by a separate job after download. |
| Portable build | No extraction of native libraries into `%TEMP%`; WPF native DLLs ship next to the exe. |

## Known gaps

* The development build is **not code-signed**; users cannot verify the publisher. SHA-256 checksums only prove
  the files match what CI produced, not who produced them.
* No build provenance attestation (GitHub artifact attestations need `id-token: write`; availability for this
  repository's plan was not verified).
* `actions/download-artifact@v6` targets Node.js 20, which GitHub now runs on Node.js 24 with a deprecation
  warning; move to a newer major (re-pinned by SHA) when available.
* Packages restored only at publish time (runtime packs, `Microsoft.NET.ILLink.Tasks`, chosen by the SDK) are not
  in the lock files; they are still subject to required nuget.org signature validation.
* Freed memory is not explicitly zeroed (managed runtime); a memory dump taken while SnapForge is running
  or shortly after could contain screenshot data.
* Window titles of other applications are read into memory during window selection (needed to enumerate
  windows; not displayed or stored).
