# Library coordination between SnapForge programs

From 1.0 RC3, two SnapForge programs run as separate Windows processes and share the same Capture Library folder:

| Program | Writes |
| --- | --- |
| **SnapForge Capture** (`SnapForge.exe`) | new captures (`Create`), recordings (`BeginVideo` / `CompleteVideo` / `AbandonVideo`), recovery of interrupted recordings |
| **SnapForge Editor** | edits of existing items (`SaveContent`, `Sanitize`), metadata (rename, tags, folder, favourite, Trash), permanent deletion, backup and restore, relocation |

The file format does not change (see [CAPTURE-FORMAT.md](CAPTURE-FORMAT.md) and
[LIBRARY-VIDEO-FORMAT.md](LIBRARY-VIDEO-FORMAT.md)). The rules below are implemented in `SnapForge.Core`
(`CaptureLibrary`, `LibraryWriteGate`, `LibraryWatcher`).

## 1. One write gate per Library folder

* Every write to the library folder and every clean-up or recovery runs inside the **write gate**: the instance's
  in-process monitor plus a named OS mutex `Local\SnapForge-Library-<first 32 hex digits of SHA-256 of the upper-cased
  full root path>`. Every process (and every `CaptureLibrary` instance) opened on the same folder uses the same mutex.
* Covered: `Create`, `SaveContent`, `Sanitize`, `UpdateMetadata` and everything built on it (rename, tags, folder,
  favourite, Trash, restore from Trash, `MarkOpened`), `DeletePermanently`, `ImportPackage`, `BeginVideo`,
  the commit phase of `CompleteVideo` (thumbnail, metadata, manifest, marker removal; the long MP4 conversion runs
  before it, outside the gate, on the recording's own files), `AbandonVideo`, recording legacy video hashes,
  `CreateBackup`, `RestoreFromBackup`, the resume of an interrupted restore, `CopyTo` (whole copy, verification and
  journal), `DeleteSourceAfterVerifiedMove` (holds the *source* library's gate), and on open/index: removal of
  `.creating-` folders, recovery of capture folders, removal of stale video markers and of temporary files.
  Re-reading a record (`Refresh`, `RefreshFromDisk`, each folder during `Reindex`) also happens inside the gate, so a
  commit is never seen half-done.
* The gate is re-entrant on its thread. Mutex ownership is per thread: enter and leave on the same thread, never across
  an `await` (all Core library operations are synchronous).
* A mutex abandoned by a process that crashed counts as acquired; the next open's recovery deals with what it left.
* Waiting for the other program is bounded: after 2 minutes the operation throws `IOException`
  ("The Capture Library is busy in another SnapForge program. Try again in a moment."). Nothing is half-written.
  Within one process, waiting for another thread is unbounded, as before.
* Long maintenance operations (backup, restore, relocation copy) hold the gate for their whole duration, so the other
  program's writes wait (and may time out) meanwhile. The programs should not start them while the other is
  recording or saving, or should retry a write that timed out.

## 2. Clean-up never touches another program's work

* A `.creating-<id>` folder is deleted only inside the gate and only if it still exists then (it may have been
  published meanwhile). Inside the gate no `Create` can be in progress anywhere.
* Uncommitted generation files of a capture folder are removed only inside the gate (no commit of that folder can be
  in progress). A temporary file that any handle still has open is never removed.
* Temporary files (`*.sftmp`) are removed on open inside the gate, never when open by any handle. Under `captures/`
  and in completed video folders (every write there happens inside the gate) a closed temporary file is a leftover and
  is removed; anywhere else (the library root, an in-progress recording's folder, unknown folders) only temporary
  files older than 10 minutes (`CaptureLibrary.TemporaryFileSafeAge`) are removed.
* An interrupted restore is resumed, and `.restore-` / `.import-` staging folders are removed, only inside the gate
  (both are created only inside it).
* Indexing never deletes or changes a video folder without a manifest (a recording in progress or interrupted). A
  stale `recording.inprogress` marker beside a manifest is removed only inside the gate, where `CompleteVideo` cannot be
  between writing the manifest and removing the marker.

## 3. Recovery of interrupted recordings has one owner

`InterruptedRecordings()` is read-only and lists every video folder with a marker and no manifest, which includes a
recording that another process is making right now. Only **SnapForge Capture** (the program that records) may act on
it (`CompleteVideo` for recovery or `AbandonVideo`), at its start-up before it starts recording. SnapForge Editor must
never recover or abandon recordings.

## 4. Seeing the other program's changes

Each process keeps its own in-memory index.

* `CaptureRecord? RefreshFromDisk(Guid id)` re-reads one item: updates its record, adds a new one, or removes it if it
  is gone (returns null), and raises `Changed`. It never cleans up or recovers anything.
* `Reindex()` re-reads everything.
* `LibraryWatcher` (opt-in, `IDisposable`, created by the app per opened library) watches `captures/` and `videos/`
  with `FileSystemWatcher`: item folders created, renamed or deleted, and `manifest.json`, `meta.json`, `video.json`
  and `recording.inprogress` written. After 250 ms without events it calls `RefreshFromDisk` for each affected id.
  `.creating-` / `.import-` folders, temporary files and other content files are ignored. If the watcher reports an
  error (buffer overflow), it falls back to `Reindex()`. Failures are reported by its `RefreshFailed` event. `Changed`
  is raised on a thread-pool thread. The program's own writes are refreshed once more too (harmless).

## 5. Relocation

`CopyTo` holds the gate of the current library for its whole run; `DeleteSourceAfterVerifiedMove` holds the gate of the
source library, and deletes only files unchanged since they were copied (anything the other program wrote after the
copy is reported and left in place). After a move, the other program must switch to the new location (it does not
follow by itself): the app must tell it, or ask the user to restart it, before the old location is cleaned up.
