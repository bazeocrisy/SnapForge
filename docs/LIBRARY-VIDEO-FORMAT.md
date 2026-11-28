# Library video format (Milestone V1)

> **rc.2:** the video manifest (still version 1) gains an additive field `videoSha256` — the streamed SHA-256 of
> `video.mp4`, recorded when a recording is completed or a video is imported. Start-up indexing checks only the file
> length (fast); the hash is verified during backup, restore, Library moves, exports and Help/`--library-check` integrity
> scans, so a modified file of the same length is detected (FA-008). Older videos without the field get it recorded on
> their first verification ("hash recorded now"). Older builds ignore the field.

Status: implemented on branch `claude/video-v1`. Format version **1**. Owner requirement: *"Preserve existing image
captures and ensure older versions cannot corrupt the new Library format."*

## 1. Where videos live

```
<Library root>/
  captures/<id>/            ← images, unchanged (format snapforge.capture v1 — docs/CAPTURE-FORMAT.md)
  videos/<id>/              ← NEW: one folder per recording
    video.mp4               fragmented MP4: H.264 video + AAC audio (when audio was recorded)
    thumb.sfpx              thumbnail (same codec as image thumbnails), from the first recorded frame
    meta.json               user metadata — identical schema to images (title, tags, folder, favorite, trash)
    video.json              the manifest, written LAST (the commit point)
    recording.inprogress    present only while recording (crash marker)
```

Videos are **not** written into `captures/`. A SnapForge version without video support only enumerates
`captures/`, so it never lists, opens, edits, moves, backs up or deletes a video, and it cannot rewrite one. This is
the compatibility mechanism; it does not depend on older versions tolerating unknown fields.

## 2. Manifest `video.json`

```json
{
  "format": "snapforge.video",
  "version": 1,
  "id": "9fb55c60-3902-4912-b613-d9c2512e4642",
  "createdUtc": "2026-11-26T22:26:49.000Z",
  "modifiedUtc": "2026-11-26T22:27:01.000Z",
  "width": 1920, "height": 1080,
  "framesPerSecond": 30,
  "durationMs": 65000,
  "hasAudio": true,
  "recovered": false,
  "container": "mp4", "videoCodec": "h264", "audioCodec": "aac",
  "videoBytes": 41234567,
  "generator": "SnapForge 0.1.0"
}
```

* `version` greater than this build understands → the item is listed as **"saved by a newer version"** and is not
  opened (test `Video_saved_by_a_newer_format_is_listed_as_unreadable_not_misread`).
* `videoBytes` must equal the size of `video.mp4`; otherwise the item is listed as damaged (no SHA-256 of multi-GB
  files at start-up; size is the integrity check, documented as a limitation).
* `recovered: true` marks a recording salvaged after an interruption (shown as "Recovered" on the tile).

## 3. Life cycle and crash safety

| Step | Files on disk | Listed in Library? |
|---|---|---|
| `BeginVideo` | folder + `recording.inprogress` | No |
| Recording | + `video.mp4` growing in self-contained fragments | No |
| `CompleteVideo` | + `thumb.sfpx`, `meta.json`, then `video.json`, then the marker is deleted | Yes |
| Cancel | folder deleted | — |
| Crash / kill / power loss while recording | marker + partial `video.mp4`, no manifest | Reported by `InterruptedRecordings()` → start-up recovery |
| Stop between manifest write and marker delete | manifest + marker | Yes (marker removed at next start) |

All small files are written with `AtomicFile` (temp file, flush to disk, rename), as for images.

Start-up recovery (`RecordingRecovery`): the MP4 is cut back to the end of its last complete `moof`+`mdat` fragment
(`Mp4Inspector.Recover`), then **decoded with the Windows decoder**; only if frames decode is it added to the Library
(`recovered: true`). Otherwise the file is left in place and the user is told it could not be recovered. Recovery is
demonstrated on CI by killing the recording process (see VIDEO-TEST-RESULTS.md).

## 4. Operations

| Operation | Images | Videos |
|---|---|---|
| Rename, tags, favorite, folders, search, sort, Trash, Restore, Empty Trash | ✓ | ✓ (same `meta.json`) |
| Open | Editor | Player (streams from disk) |
| Edit, Sanitize, Duplicate, `.sfcap` package | ✓ | Refused with a clear message (`RequireImage`) |
| Export | PNG/JPEG | MP4 copy (streamed file copy) |
| Drag out | flattened PNG/JPEG | MP4 copy in the temporary drag folder (never the Library file) |
| Move library location | ✓ | ✓ (streamed copy, manifest copied last; in-progress recordings stay) |
| Backup / restore (.zip) | ✓ | **Not included** — backup format unchanged so older versions can restore it (known limitation KL-V05) |
| Delete permanently | files overwritten with zeros, then deleted | thumbnails/metadata overwritten; `video.mp4` deleted without overwrite (multi-GB overwrite is impractical; KL-V06) |

## 5. Migration tests (tests/SnapForge.Core.Tests/VideoLibraryTests.cs)

* `Existing_image_library_opens_unchanged_and_videos_never_enter_the_captures_folder` — an image library written
  before video, then reopened and given two videos: every file in `captures/` keeps its exact SHA-256 and no new folder
  appears there.
* `Older_versions_backup_format_is_unchanged_and_ignores_videos` — backups contain only `captures/…` entries.
* `Video_saved_by_a_newer_format_is_listed_as_unreadable_not_misread`, `Modified_or_missing_video_file_is_reported_as_damaged`,
  `Recording_in_progress_is_not_listed_and_is_reported_after_a_crash`, `Stop_between_manifest_and_marker_removal_counts_as_completed`,
  `Cancelled_recording_leaves_nothing_and_completed_videos_cannot_be_abandoned`,
  `Moving_the_library_moves_videos_and_leaves_in_progress_recordings`, `Image_only_operations_refuse_videos_with_a_clear_message`,
  `Videos_take_tags_favourites_folders_rename_trash_restore_and_search`.
* Settings: `Old_settings_file_without_video_gets_defaults_and_bad_values_are_repaired`.

What is **not** tested automatically: running an actual older SnapForge build against a library containing videos.
The guarantee rests on the older code only reading `captures/` (verified by reading the Milestone 3 source:
`CaptureLibrary.Reindex`, `Backup`, `RestoreBackup`, `MoveTo` touch only `captures/`). Manual check: ACCEPTANCE V1-LIB-07.
