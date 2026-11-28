# SnapForge Capture and SnapForge Editor (1.0.0-rc.3)

One installer puts two programs in `C:\Program Files\SnapForge`, with a Start menu shortcut for each.

| | SnapForge Capture (`SnapForge.exe`) | SnapForge Editor (`SnapForgeEditor.exe`) |
|---|---|---|
| What it does | QuickDock, notification-area icon, global hotkeys, screenshots, the Record panel, recording and its controls, handing saved captures to the Editor | Editing, annotation, redaction, OCR, playback, trimming, GIF export, Library browsing, drag-out and export |
| Starts with Windows | Yes, if "Launch when I sign in" is on. It starts with only the tray icon and QuickDock, and opens no window | Never |
| Single instance | One per Windows user and data profile. A second launch shows its controls and registers no duplicate hotkeys | One per Windows user and data profile. A second launch opens the requested capture in the running Editor |
| Closing it | Exit from the tray icon. If a recording is running it is stopped and saved first | Closing the window ends the Editor process. Capture, its hotkeys and any recording keep running |

`SnapForge.exe` keeps the rc.2 name, folder and single-instance name. That means:

* a start-with-Windows entry created by rc.2 keeps working, and no second entry is created;
* the rc.2 upgrade and self-test paths are unchanged.

## Hand-off between the programs (`Services/Handoff.cs`)

A request is a small JSON file in the target program's inbox, `%LOCALAPPDATA%\SnapForge\Handoff\{editor|capture}`. With a test profile it goes in the profile's data folder instead, so test profiles and different Windows users never see each other's requests.

* **Writing:** the file is written atomically, to a temporary name and then renamed.
* **Delivering:** the sender then sets the target's activate event. If the target isn't running, the sender starts its executable, with the same `--profile`.
* **Reading:** the target reads pending requests oldest first and deletes each one after acting on it.
* **What a request carries:** only the stable id of a Library item whose save has **completed**. A capture kept out of the Library is sent as a private temporary image file that has already been written instead. A lost, failed or unread request therefore can never lose a capture: the item is already in the Library.
* **If the Editor is closed:** the request starts it.
* **If the Editor crashed:** the request waits in the inbox and is acted on at the next start. Requests older than a day, or damaged files, are deleted rather than acted on unexpectedly.
* **If the item is missing** (for example, deleted meanwhile), the Editor says so and opens the Library.
* **If Capture can't save to the Library**, it opens the capture in the Editor as an unsaved image rather than dropping it.

## Shared Library and settings

Both programs share one Library and one settings file. The details are in [LIBRARY-COORDINATION.md](LIBRARY-COORDINATION.md).

* **Writes:** every Library write takes a per-Library named mutex (`LibraryWriteGate`), so writes from the two processes never interleave.
* **Reads:** items are written atomically, and the other process reloads only the item that changed (`RefreshFromDisk`). A folder watcher (`LibraryWatcher`) keeps each program's view in step. It debounces changes and re-indexes if Windows reports an overflow.
* **Settings:** changes are read-modify-write under a named mutex, and a file watcher reloads them in the other program. If the Library location changed, the other program reopens the Library at the new location.
* **Backup, restore and moving the Library:** these first ask you to close the other SnapForge program. This avoids holding the write lock for minutes, and stops the other program from carrying on at the old location.
* **Temporary files:** cleanup removes only files that no process is using. Only Capture recovers interrupted recordings.

## Upgrade behaviour (installer)

* **Recognises older installs:** the rc.2 system-wide install has the same AppId and is upgraded in place. Its single "SnapForge" shortcut is replaced by the two new ones.
* **rc.1 per-user copies:** SnapForge itself offers to remove one the first time it starts as that user, and keeps their data.
* **User data is untouched:** setup never reads, moves or deletes user data, and never touches the registry hive of the user who approved the elevation (`HKCU`). Captures, recordings, editable projects, the Library location and compatible settings stay where they are. Uninstall keeps them too.
* **A running SnapForge blocks setup** (this applies to either program, for any user on the PC). Setup asks you to stop any recording, close the Editor and exit Capture. It never closes SnapForge by force.
* **If setup fails or is cancelled,** Inno Setup rolls back the files it added. If it is killed partway through, running the same setup again repairs the installation.
