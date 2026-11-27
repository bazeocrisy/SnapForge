# Installing SnapForge

For SnapForge **1.0.0-rc.1** (release candidate).

> **Release candidate, not yet released.** SnapForge 1.0 has not been published. Download links, with their
> checksums, will be on the SnapForge download page and the project's GitHub Releases page once the release is
> approved. Until then, release-candidate packages are produced by the project's automated builds for testing.

## System requirements

| | |
|---|---|
| **Operating system** | Windows 11, 64-bit (x64), version **24H2 (build 26100) or later**. This is the only supported platform. |
| **Not supported** | Windows 10, Windows on Arm, older Windows 11 versions, other operating systems. They are not tested. |
| **.NET** | Not needed. SnapForge includes everything it needs. |
| **Administrator rights** | Not needed. SnapForge installs and runs for your user account only. |
| **Disk space** | Space for the program, plus space for your captures and recordings. Screen recordings can be large. |
| **Screen recording** | Uses the media components built into Windows (Media Foundation). Windows "N" editions need Microsoft's **Media Feature Pack**. |
| **Text recognition (OCR)** | Uses Windows' built-in text recognition. At least one "Optical character recognition" language feature must be installed in Windows. |

## Installer or portable ZIP?

Each release comes in two forms, built from the same files:

| | **Installer** | **Portable ZIP** |
|---|---|---|
| File name | `SnapForge-<version>-win-x64-setup.exe` | `SnapForge-<version>-win-x64-portable.zip` |
| Installs to | `%LOCALAPPDATA%\Programs\SnapForge` | Any folder you choose |
| Start menu entry | Yes (and an optional desktop shortcut) | No |
| Listed in Settings → Apps | Yes, with an uninstaller | No |
| Upgrades | Run the new installer | Replace the files yourself |
| Your captures and settings | `%LOCALAPPDATA%\SnapForge` | `%LOCALAPPDATA%\SnapForge` (the same place) |

Choose the **installer** if you are not sure. Choose the **portable ZIP** if you prefer not to install anything,
for example to try SnapForge out.

Each release also lists a `SHA256SUMS.txt` file with the checksum of every package.

## Verifying the download (SHA-256)

Before you run a download, check that it is exactly the file the project published.

1. Open the folder that contains the download in File Explorer.
2. Click the address bar, type `powershell` and press **Enter**. A PowerShell window opens in that folder.
3. Type this command (use the real file name) and press **Enter**:

   ```
   Get-FileHash .\SnapForge-1.0.0-rc.1-win-x64-setup.exe -Algorithm SHA256
   ```

4. Compare the long value under **Hash** with the one published on the download page, the GitHub release and in
   `SHA256SUMS.txt`. It must match exactly (upper or lower case does not matter).

If the values do not match, **do not run the file**. Delete it and download it again from the official page. If it
still does not match, report it to the project.

## Windows security prompts (SmartScreen)

SnapForge 1.0.0-rc.1 is **not code-signed**. When you run the installer or `SnapForge.exe` for the first time,
Microsoft Defender SmartScreen may show **"Windows protected your PC"** because it does not recognise the
program yet.

1. Make sure you downloaded the file from the official SnapForge download page or the project's GitHub Releases
   page.
2. Make sure its SHA-256 checksum matches (see above).
3. Only then click **More info**, check the file name, and click **Run anyway**.

Please:

* **Keep Microsoft Defender and SmartScreen turned on.** Do not turn them off to run SnapForge.
* If **Defender reports the file as harmful**, do not run it. Delete it and report it to the project.
* If **Smart App Control** blocks SnapForge, the PC does not allow unsigned programs. SnapForge cannot be used
  on that PC until a signed version is available; we do not recommend turning Smart App Control off.

## Installing with the installer

1. Download the installer and verify its checksum.
2. Double-click `SnapForge-<version>-win-x64-setup.exe`. No administrator prompt appears; SnapForge is installed
   for your user account only.
3. Accept the licence (MIT).
4. If Setup asks for a folder, keep the suggested one.
5. Optionally tick **Create a desktop shortcut**.
6. Click **Install**.
7. Leave **Start SnapForge now** ticked and click **Finish**.

SnapForge is installed in `%LOCALAPPDATA%\Programs\SnapForge` and appears in the Start menu as **SnapForge**.

The installer only runs on Windows 11 version 24H2 (build 26100) or later, 64-bit. If SnapForge is already
running, Setup asks you to exit it first (right-click the SnapForge icon in the notification area and choose
**Exit SnapForge**; stop any recording first).

## Installing the portable ZIP

1. Download the ZIP and verify its checksum.
2. Right-click the ZIP and choose **Extract All…**. Pick a folder that belongs to you, for example
   `Documents\SnapForge`. Avoid `Program Files` (it needs administrator rights) and avoid folders that are synced
   to the cloud.
3. Open the extracted folder and double-click `SnapForge.exe`.

Keep all the files in the folder together: `SnapForge.exe` needs the DLL files next to it.

## First launch

On the first start SnapForge:

1. shows the **Welcome to SnapForge** window (also later under **Help → Getting started**),
2. opens the main window and the **capture panel**,
3. shows the **capture widget** at the top centre of the screen for a few seconds,
4. puts its icon in the notification area (next to the clock),
5. registers **Print Screen** as its capture hotkey.

On Windows 11, Print Screen usually opens the Snipping Tool. If SnapForge reports that Print Screen is
unavailable, open Windows **Settings → Accessibility → Keyboard**, turn off **Use the Print screen key to open
screen capture**, then exit and restart SnapForge. Or choose another hotkey in SnapForge's **Settings → Hotkeys**.

The full introduction is in the [User Guide](USER-GUIDE.md#1-getting-started-and-first-launch).

## Where SnapForge stores things

| What | Where |
|---|---|
| Program (installer) | `%LOCALAPPDATA%\Programs\SnapForge` |
| Program (portable) | The folder you extracted |
| Captures and recordings (Library) | `%LOCALAPPDATA%\SnapForge\Library` (unless you chose another folder in **Settings → Capture Library**) |
| Settings | `%LOCALAPPDATA%\SnapForge\settings.json` |
| Error logs (text only) | `%LOCALAPPDATA%\SnapForge\logs` |
| Temporary drag-and-drop copies | `%LOCALAPPDATA%\SnapForge\Temp\Drag` |
| "Launch when I sign in" entry (if turned on) | Your user's Windows startup list (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) |

To open one of these folders, paste the path into the File Explorer address bar and press **Enter**.

## Upgrading

Upgrading keeps your captures, recordings and settings, because they are stored in `%LOCALAPPDATA%\SnapForge`
and not in the program folder. Backing up first is still a good idea with a release candidate (see
[Backup and restore](USER-GUIDE.md#15-backup-and-restore)).

**Installer**

1. Exit SnapForge (notification-area icon → **Exit SnapForge**). Stop any recording first.
2. Run the new installer. It installs over the old version in the same folder.

**Portable**

1. Exit SnapForge.
2. Replace the files in your SnapForge folder with those from the new ZIP. (You can also extract the new ZIP to a
   new folder and delete the old one.)
3. If **Launch SnapForge when I sign in to Windows** is on and the new version is in a different folder, turn the
   setting off and on again in the new version. SnapForge shows a note in Settings when the startup entry points to
   another copy.

**Moving from the portable version to the installer (or back)** works the same way: exit the old copy, install or
extract the new one. Both use the same Library and settings.

## Running a development build next to your normal SnapForge

You may want to try a development or test build without risking your real captures. Please know how SnapForge
behaves before you do this:

* **All copies share the same data.** Every SnapForge build, installed or portable, uses the same settings file
  (`%LOCALAPPDATA%\SnapForge\settings.json`) and the same Library. **There is no option to start a build with a
  separate data folder.** Putting a build in its own folder separates only the program files.
* **Only one copy can run at a time.** If you start a different build while another is running, SnapForge shows
  which build is running and asks whether to close it and start the new one.
* **Settings → Capture Library → Change location…** does not isolate builds: it moves the Library for *every* copy,
  because the location is stored in the shared settings file.

To try a development build safely:

1. In your normal SnapForge, make a backup: **Settings → Capture Library → Back up library…**. This saves your image
   captures.
2. Exit SnapForge and copy the Library's **videos** folder somewhere safe (a backup does not include recordings).
3. Optionally copy `%LOCALAPPDATA%\SnapForge\settings.json` somewhere safe too.
4. Extract the development build into its **own folder** (do not overwrite your normal installation) and run it.
5. When you are done, exit the development build and start your normal SnapForge again.

If something goes wrong, restore your images with **Settings → Capture Library → Restore from backup…**, copy the
videos back into the Library's **videos** folder while SnapForge is not running, and put your copy of
`settings.json` back.

## Uninstalling

### Installer version

1. Exit SnapForge.
2. Open Windows **Settings → Apps → Installed apps**, find **SnapForge**, click **…** and choose **Uninstall**.
3. After the program is removed, the uninstaller asks: **Do you also want to DELETE your captures, recordings and
   settings?** The default is **No**, which keeps them.
4. If you answer **Yes**, it asks a **second time** ("Are you sure?"). Only if you confirm again does it delete
   `%LOCALAPPDATA%\SnapForge`.

The uninstaller always removes the temporary drag-and-drop copies, and removes the "Launch SnapForge when I sign
in" entry if it belongs to this installation. **A Library you moved to another folder is never deleted** by the
uninstaller. A silent (unattended) uninstall never deletes your data.

### Portable version

1. If **Launch SnapForge when I sign in to Windows** is on, turn it off in **Settings → Behaviour** (or in
   Windows **Settings → Apps → Startup**).
2. Exit SnapForge.
3. Delete the folder you extracted it to.

Your captures, recordings and settings stay in `%LOCALAPPDATA%\SnapForge`.

### Deleting your data

To remove your captures, recordings and settings as well, delete `%LOCALAPPDATA%\SnapForge`. If you moved the
Library, delete that folder too. **This permanently deletes your captures.** Back up first if you might need
them.

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
