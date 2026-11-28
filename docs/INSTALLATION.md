# Installing SnapForge

For SnapForge **1.0.0-rc.2** (release candidate 2, in testing).

> **Release candidate, not yet released.** SnapForge 1.0 has not been published. Download links, with their
> checksums, will be on the SnapForge download page and the project's GitHub Releases page once the release is
> approved. Until then, release-candidate packages are produced by the project's automated builds for testing.

## System requirements

| | |
|---|---|
| **Operating system** | Windows 11, 64-bit (x64), version **24H2 (build 26100) or later**. This is the only supported platform. |
| **Not supported** | Windows 10, Windows on Arm, older Windows 11 versions, other operating systems. They are not tested. |
| **.NET** | Not needed. SnapForge includes everything it needs. |
| **Administrator rights** | The installer asks once for administrator approval (UAC) to install for all users in Program Files; uninstalling asks again. SnapForge itself always runs **without** administrator rights. The portable ZIP needs no administrator rights at all. |
| **Disk space** | Space for the program, plus space for your captures and recordings. Screen recordings can be large. |
| **Screen recording** | Uses the media components built into Windows (Media Foundation). Windows "N" editions need Microsoft's **Media Feature Pack**. |
| **Text recognition (OCR)** | Uses Windows' built-in text recognition. At least one "Optical character recognition" language feature must be installed in Windows. |

## Installer or portable ZIP?

Each release comes in two forms, built from the same files:

| | **Installer** | **Portable ZIP** |
|---|---|---|
| File name | `SnapForge-<version>-win-x64-setup.exe` | `SnapForge-<version>-win-x64-portable.zip` |
| Installs to | `C:\Program Files\SnapForge`, for **all users** of the PC | Any folder you choose |
| Administrator approval | Once, for install / repair / uninstall (UAC) | Never |
| Start menu entry | Yes, for all users (and an optional desktop shortcut) | No |
| Listed in Settings → Apps → Installed apps | Yes, with an uninstaller | No |
| Upgrades | Run the new installer | Replace the files yourself |
| Your captures and settings | Each user's own `%LOCALAPPDATA%\SnapForge` | `%LOCALAPPDATA%\SnapForge` (the same place) |

Choose the **installer** if you are not sure and you can approve administrator prompts. Choose the **portable ZIP**
if you cannot or prefer not to install anything.

Each release also lists a `SHA256SUMS.txt` file with the checksum of every package.

## Verifying the download (SHA-256)

Before you run a download, check that it is exactly the file the project published.

1. Open the folder that contains the download in File Explorer.
2. Click the address bar, type `powershell` and press **Enter**. A PowerShell window opens in that folder.
3. Type this command (use the real file name) and press **Enter**:

   ```
   Get-FileHash .\SnapForge-1.0.0-rc.2-win-x64-setup.exe -Algorithm SHA256
   ```

4. Compare the long value under **Hash** with the one published on the download page, the GitHub release and in
   `SHA256SUMS.txt`. It must match exactly (upper or lower case does not matter).

If the values do not match, **do not run the file**. Delete it and download it again from the official page. If it
still does not match, report it to the project.

## Windows security prompts (SmartScreen)

SnapForge 1.0.0-rc.2 is **not code-signed** yet (signing is being prepared — see [SIGNING.md](SIGNING.md)). Windows
may therefore show **"Windows protected your PC"** (Microsoft Defender SmartScreen) or block the program (Smart App
Control) when you run the installer or `SnapForge.exe`.

* **Keep Microsoft Defender, SmartScreen and Smart App Control on.** SnapForge will never ask you to turn them off
  or to work around them.
* Release-candidate builds are for **invited testers** only. Only use a file you got from the project's own CI run
  page and whose SHA-256 checksum matches. If Windows warns you and you are not comfortable, do not run it — wait
  for the signed public release. Running an unsigned test build is your own decision (and, on a managed PC, your IT
  administrator's).
* If **Defender reports the file as harmful**, do not run it. Delete it and report it to the project.
* Signing proves who published the file; it does not by itself guarantee that SmartScreen will never warn (that also
  depends on reputation).

## Installing with the installer

1. Download the installer and verify its checksum.
2. Double-click `SnapForge-<version>-win-x64-setup.exe`. Windows asks for administrator approval (UAC) — approve it
   with your own administrator account, or ask an administrator to enter their credentials. This approval is only
   for installing; SnapForge itself will run as you, without administrator rights.
3. Accept the licence (MIT).
4. Optionally tick **Create a desktop shortcut for all users**.
5. Click **Install**, then **Finish**. (Tick **Start SnapForge now** if you want it to start straight away — it
   starts as the signed-in user, not as the administrator who approved Setup.)

SnapForge is installed in `C:\Program Files\SnapForge` and appears in every user's Start menu as **SnapForge**.
Each Windows user who starts it gets their own captures and settings.

The installer only runs on Windows 11 version 24H2 (build 26100) or later, 64-bit. If SnapForge is running — in
your session or in another user's session — Setup asks for it to be exited first (notification-area icon →
**Exit SnapForge**; stop any recording first). Setup never closes SnapForge by force, because it could be recording.

**Repair:** run the same installer again; it reinstalls the program files and keeps everyone's data.

## Installing the portable ZIP

1. Download the ZIP and verify its checksum.
2. Right-click the ZIP and choose **Extract All…**. Pick a folder that belongs to you, for example
   `Documents\SnapForge`. Avoid `Program Files` (use the installer for that) and avoid folders that are synced to
   the cloud.
3. Open the extracted folder and double-click `SnapForge.exe`.

Keep all the files in the folder together: `SnapForge.exe` needs the DLL files next to it.

## First launch

When **you** open SnapForge for the first time it shows only the **Welcome to SnapForge** window (also later under
**Help → Getting started**). When you close it, the compact **capture panel** opens. SnapForge also puts its icon in
the notification area (next to the clock), shows the **QuickDock** (a small dark pill at the top centre of the
screen) for a few seconds, and registers **Print Screen** as its capture hotkey.

After that, opening SnapForge shows **one** window: the capture panel (or the Editor, if you chose that in
**Settings → Behaviour**). Opening it again while it runs brings that window back instead of starting a second copy.
When SnapForge **starts with Windows** (Settings → Launch when I sign in) it shows **no window** — only its
notification-area icon and the QuickDock.

Closing a window keeps SnapForge running in the notification area (hotkeys keep working). To quit, use
**File → Exit SnapForge** or right-click the notification-area icon → **Exit SnapForge**.

On Windows 11, Print Screen usually opens the Snipping Tool. If SnapForge reports that Print Screen is
unavailable, open Windows **Settings → Accessibility → Keyboard**, turn off **Use the Print screen key to open
screen capture**, then exit and restart SnapForge. Or choose another hotkey in SnapForge's **Settings → Hotkeys**.

The full introduction is in the [User Guide](USER-GUIDE.md#1-getting-started-and-first-launch).

## Where SnapForge stores things

| What | Where |
|---|---|
| Program (installer) | `C:\Program Files\SnapForge` (shared by all users, read-only for them) |
| Program (portable) | The folder you extracted |
| Captures and recordings (Library) | `%LOCALAPPDATA%\SnapForge\Library` (unless you chose another folder in **Settings → Capture Library**) |
| Settings | `%LOCALAPPDATA%\SnapForge\settings.json` |
| Error logs (text only) | `%LOCALAPPDATA%\SnapForge\logs` |
| Temporary drag-and-drop copies | `%LOCALAPPDATA%\SnapForge\Temp\Drag` |
| "Launch when I sign in" entry (if turned on) | Your user's Windows startup list (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) |

To open one of these folders, paste the path into the File Explorer address bar and press **Enter**.

## Upgrading

Upgrading keeps your captures, recordings and settings, because they are stored in your own
`%LOCALAPPDATA%\SnapForge` and not in the program folder. With a release candidate, **back up first**: exit
SnapForge and copy the whole `%LOCALAPPDATA%\SnapForge` folder (and a Library you moved elsewhere) to a safe place,
and/or use **Settings → Capture Library → Back up library…** (from rc.2 backups include recordings, the Trash and
all metadata).

**From 1.0.0-rc.1 (installed only for you in `%LOCALAPPDATA%\Programs\SnapForge`) to rc.2**

1. Exit SnapForge (notification-area icon → **Exit SnapForge**). Stop any recording first.
2. Run the rc.2 installer and approve the administrator prompt. rc.2 is installed for all users in Program Files.
3. Start SnapForge from the Start menu. It finds your older per-user copy and asks: **Remove the older copy now?**
   Answer **Yes**: only the old program files, its Start menu entry and its Installed-apps entry are removed; your
   captures, recordings and settings are kept, and "Launch when I sign in" is switched to the new copy. (Answer No to
   keep it; you can remove it later in Settings → Apps → Installed apps — choose the SnapForge entry installed for you
   only, and keep **No** when its uninstaller asks whether to delete your captures.)

Other users of the same PC who had their own rc.1 copy see the same question when they first start the new copy.

**Installer (rc.4 and later): SnapForge is closed for you.** Run the new installer while SnapForge Capture and
SnapForge Editor are open; you don't need to exit them first. Setup asks every SnapForge started from this
installation in your Windows session to close (SnapForge rc.1–rc.3 understand this request too), waits until they have
exited and the program files can be replaced, then continues. It works the same for reinstall, repair and upgrade, and
for uninstall. Setup never closes SnapForge by force:

* **Recording in progress:** SnapForge asks whether to stop and save the recording. Answer **No** to keep recording;
  Setup then explains that SnapForge is still open and offers **Retry** (after you stop the recording) or **Cancel**.
* **Work not yet saved:** edits are saved to the Library before the Editor closes. A capture taken without history
  that was never exported makes the Editor ask before discarding it; keep it open and Setup offers Retry / Cancel.
* **SnapForge running for another signed-in Windows user:** Setup cannot ask it, so it names that session and stops
  without changing anything. Ask that user to exit SnapForge (or sign them out), then run Setup again.
* A portable copy or an old per-user rc.1 copy in another folder is not closed: it does not use the installed files.

Captures, recordings, editable projects, settings and your Library location are never touched by Setup.

**Installer rc.2 and rc.3**: exit SnapForge in every user session, then run the new installer.

**Portable**: exit SnapForge, replace the files in your SnapForge folder with those from the new ZIP. If **Launch
SnapForge when I sign in** is on and the new copy is in a different folder, turn the setting off and on again.

**Going back to an older version (downgrade)** is not supported. Captures saved by rc.2 use a newer storage format:
an older build shows them as unreadable and leaves them unchanged. rc.2 also refuses to overwrite a settings file
written by a newer version. Builds released before rc.2 cannot be changed retroactively, so keep your backup if you
might need to return to rc.1.

## Running a test build next to your normal SnapForge (isolated profiles)

From rc.2, a test copy can use a completely separate **data profile**, so it never touches your real captures:

```
SnapForge.exe --profile test1
```

(or set the environment variable `SNAPFORGE_PROFILE=test1`). A profile has its own settings, Library, logs,
temporary files and single-instance key, stored in `%LOCALAPPDATA%\SnapForge-Profiles\test1`. A profile copy does
not register global hotkeys and cannot be set to start with Windows, so it does not interfere with your normal
SnapForge; it can even run at the same time. **Help → About** shows which profile and data folder a copy uses.

Without `--profile`, every copy (installed or portable, any version) uses the same `%LOCALAPPDATA%\SnapForge` data
and only one copy can run at a time — SnapForge shows which build is running and asks before handing over; a running
recording is never stopped without asking. Builds before rc.2 do not know about profiles: back up your data before
trying one of those.

## Uninstalling

### Installer version

1. Make sure SnapForge is exited in every user session (stop recordings first).
2. Open Windows **Settings → Apps → Installed apps**, find **SnapForge**, click **…** and choose **Uninstall**, and
   approve the administrator prompt.
3. The program, its Start menu entry and its Installed-apps entry are removed. **Nobody's captures, recordings or
   settings are deleted** — each user's data stays in their own `%LOCALAPPDATA%\SnapForge`. (The uninstaller runs as
   an administrator and deliberately never touches users' personal folders.)

If "Launch when I sign in" was on, Windows may still list SnapForge under **Settings → Apps → Startup**; turn it off
there.

### Portable version

1. If **Launch SnapForge when I sign in to Windows** is on, turn it off in **Settings → Behaviour** (or in
   Windows **Settings → Apps → Startup**).
2. Exit SnapForge.
3. Delete the folder you extracted it to.

Your captures, recordings and settings stay in `%LOCALAPPDATA%\SnapForge`.

### Deleting your data

Data removal is your own, deliberate action, done as yourself (no administrator rights):

* In SnapForge: **Settings → Reset → Delete my SnapForge data…** (asks twice, then deletes your settings, captures,
  recordings, logs and temporary files and exits). A Library you moved to a folder of your choice is not deleted —
  SnapForge shows its path so you can delete it yourself.
* Or exit SnapForge and delete `%LOCALAPPDATA%\SnapForge` (and a moved Library folder) yourself.

**This permanently deletes your captures.** Back up first if you might need them. Deleted files may remain
recoverable on some drives (see the user guide's privacy section).

## Troubleshooting installation

**"Windows protected your PC" (SmartScreen).** SnapForge is not code-signed. Verify the checksum, then use **More
info → Run anyway**. Do not turn SmartScreen off. See [above](#windows-security-prompts-smartscreen).

**Microsoft Defender reports the file.** Do not run it. Delete it and report it to the project.

**Setup says SnapForge is running.** Right-click the SnapForge icon in the notification area and choose **Exit
SnapForge**. If it is recording, stop the recording first. Then click **OK** in Setup.

**Setup refuses to run / says the Windows version is not supported.** SnapForge 1.0 needs Windows 11 version 24H2
(build 26100) or later, 64-bit. Check your version in Windows **Settings → System → About**, and install Windows
updates.

**`SnapForge.exe` does not start (portable).** Make sure you extracted the whole ZIP (do not run it from inside
the ZIP) and that all files are in the same folder. Check that the checksum of the ZIP matched.

**"Another copy of SnapForge is already running".** Another build is running (perhaps an older copy in the
notification area). Let SnapForge close it, or exit it from its notification-area icon and start the new copy
again. Both copies use the same Library.

**Screen recording says the Windows media components are missing.** On Windows "N" editions, install the **Media
Feature Pack**: Windows **Settings → System → Optional features** (on some versions **Settings → Apps → Optional
features**) → **View features** → **Media Feature Pack**, then restart the PC.

**"The Capture Library could not be opened" at start.** The Library folder chosen in Settings is not available
(for example an unplugged drive). SnapForge then uses the default location instead. The captures in the
unavailable folder are not touched. Reconnect the drive, exit SnapForge and start it again. If SnapForge still
opens the default location, that folder is no longer selected; keep it (do not delete it) and ask for help on the
project's issue tracker.

**Print Screen does nothing or opens the Snipping Tool.** See [First launch](#first-launch).

More problems and fixes: [User Guide → Troubleshooting](USER-GUIDE.md#18-troubleshooting).
