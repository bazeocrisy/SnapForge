# SnapForge editable capture format — version 1

Status: implemented in `SnapForge.Core/Storage` and `SnapForge.Core/Library`; covered by `CaptureFormatTests`
and `CaptureLibraryTests`. All text files are UTF-8 JSON. All timestamps are ISO-8601 UTC.

## Library layout

```
<Library root>/                    default: %LOCALAPPDATA%\SnapForge\Library
  tray.json                        Recent Captures Tray (ordered ids, pinned flags)
  captures/
    <capture-id (GUID)>/
      manifest.json                commit record (written last)
      base.sfpx                    base pixels (see below)
      annotations.json             editable annotations, back to front
      meta.json                    user metadata (title, tags, folder, favorite, trash, last opened)
      thumb.sfpx                   thumbnail of the flattened image (cache; regenerable)
```
Settings are stored separately in `%LOCALAPPDATA%\SnapForge\settings.json`. Exported PNG/JPEG files are never stored
in the library.

## Crash resistance

Every file is written by `AtomicFile`: data goes to `.<name>.<random>.sftmp` in the same folder, is flushed to disk,
then atomically renamed over the target. Order of a save: `base.sfpx` (only if the pixels changed) →
`annotations.json` → `thumb.sfpx` → **`manifest.json` last**. The manifest contains SHA-256 hashes of `base.sfpx` and
`annotations.json`; a capture whose files do not match its manifest is reported as damaged and is never opened with
mixed content. Leftover `*.sftmp` files are deleted when the library opens.

Sanitize uses `base.sanitized.sfpx`: written and flushed, then the old `base.sfpx` is overwritten with zeros and
deleted, then the new file is renamed into place. If a crash interrupts this, the rename is completed on next start.

## manifest.json

| Field | Type | Meaning |
|---|---|---|
| `format` | string | `"snapforge.capture"` |
| `version` | int | `1`. Readers reject versions they do not know. |
| `id` | string | capture GUID (equals the folder name) |
| `createdUtc`, `modifiedUtc` | string | capture time, last save |
| `width`, `height` | int | pixel size of the base image |
| `annotationCount`, `redactionCount` | int | counts (redaction masks excluded from `annotationCount`) |
| `containsUnsanitizedRedactions` | bool | `true` if `base.sfpx` may still hold original pixels under a redaction |
| `sanitizedUtc` | string/null | time of the last sanitize, cleared if a new redaction is added later |
| `baseSha256`, `annotationsSha256` | string | lowercase hex SHA-256 of the files |
| `generator` | string | application and version that wrote it |

## base.sfpx / thumb.sfpx (SFPX pixel container)

| Offset | Size | Content |
|---|---|---|
| 0 | 4 | ASCII `SFPX` |
| 4 | 2 | version (uint16 LE) = 1 |
| 6 | 4 | width (uint32 LE) |
| 10 | 4 | height (uint32 LE) |
| 14 | … | zlib (RFC 1950) stream of `width × height × 4` bytes, BGRA order, top-down rows, alpha = 255 |

Lossless. Maximum 32 000 × 32 000 pixels. The base image is the capture **without** redactions burned in (unless the
capture was sanitized), so redactions remain reversible in the editable copy.

## annotations.json

```json
{ "format": "snapforge.capture.annotations", "version": 1, "annotations": [ { ... }, ... ] }
```
Every annotation has `type`, `id` (GUID), `color` (`#AARRGGBB`) and `strokeWidth` (number). Additional fields:

| `type` | Fields |
|---|---|
| `arrow`, `line` | `start` `[x,y]`, `end` `[x,y]` |
| `rectangle`, `ellipse` | `rect` `[x,y,width,height]` |
| `highlight` | `rect`, `opacity` (0–1) |
| `text` | `position` `[x,y]` (top-left), `text`, `fontSize`, `measuredSize` `[w,h]` |
| `step` | `center` `[x,y]`, `number`, `radius` |
| `redaction` | `region` `[x,y,width,height]` in whole pixels; color is always opaque |

Coordinates are image pixels. Order is z-order (first = bottom). A `redaction` covers every annotation before it.
Unknown types, missing fields and non-finite numbers are rejected (the capture is reported as damaged).

## meta.json

```json
{ "format": "snapforge.capture.meta", "version": 1, "title": "…", "tags": ["…"], "folder": "…",
  "favorite": false, "deletedUtc": null, "lastOpenedUtc": "…" }
```
A damaged `meta.json` falls back to defaults without affecting the content.

## .sfcap package

A ZIP containing exactly the files above (no directories). Import accepts only these names (rejecting paths, which
prevents "zip slip"), verifies the hashes, decodes the content and assigns a new id.

## Library backup

A ZIP with `snapforge-backup.json` (`{"format":"snapforge.backup","version":1,…}`) and entries
`captures/<id>/<file>`. Restore validates every entry name, skips ids that already exist and verifies each capture
before adopting it.

## Versioning rules

Additive, backward-compatible changes keep `version` and must be ignorable by older readers. Anything else increments
`version`; older SnapForge builds then refuse the capture instead of misreading it.
