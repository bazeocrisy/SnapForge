; SnapForge installer (Inno Setup 6). Built by CI from the published folder holding both programs:
;   iscc /DAppVersion=1.0.0-rc.3 /DFileVersion=1.0.0.123 /DSourceDir=..\artifacts\portable\SnapForge /DOutputDir=..\artifacts\installer installer\SnapForge.iss
;
; Design decisions (FA-002 / SF-002; see docs/INSTALLATION.md):
; * System-wide install for all users into the native 64-bit Program Files folder ({autopf} in 64-bit admin mode =
;   C:\Program Files\SnapForge), registered under HKLM (Settings → Apps → Installed apps), all-users Start menu
;   shortcut, optional all-users desktop shortcut. Administrator approval (UAC) is needed for install, repair and
;   uninstall only; SnapForge itself always runs as a standard user and never asks for elevation.
; * Program Files keeps its default protected permissions: no [Dirs]/[Files] Permissions entries, so the program files
;   are never writable by standard users.
; * All mutable data (settings, Library, recordings, logs, temporary drag copies) stays in each user's own profile
;   (%LOCALAPPDATA%\SnapForge). The elevated setup/uninstaller never reads, moves or deletes any user's data, and never
;   touches HKEY_CURRENT_USER (under elevation with different administrator credentials that would be the wrong
;   account). Deleting your own data is a standard-user action inside SnapForge (Settings → Delete my SnapForge data).
; * Older per-user installations (1.0.0-rc.1 and earlier, %LOCALAPPDATA%\Programs\SnapForge) are not searched for or
;   removed here (setup runs as an administrator and must not touch other profiles). SnapForge itself detects the
;   older copy of the signed-in user on first start and offers to remove it (data kept) — see LegacyInstallMigration.
; * A running SnapForge in this session (Local\ mutex) or any other session (Global\ mutex) must be exited first. Setup
;   never kills it: it could be recording.
; * Upgrades install over the previous version (same AppId); repair = run the same setup again.
; * RC3: two programs — SnapForge Capture (SnapForge.exe: QuickDock, notification-area icon, hotkeys, capture,
;   recording; the only one that can start with Windows) and SnapForge Editor (SnapForgeEditor.exe: editing, playback,
;   Library). Two Start menu shortcuts. The single "SnapForge" shortcut of 1.0.0-rc.2 is removed on upgrade. The
;   executable of SnapForge Capture keeps its name and folder, so a "start with Windows" entry made by rc.2 keeps
;   working and no second entry is created.
; * Interrupted setup: Inno Setup rolls back the files it added when setup fails or is cancelled; if setup is killed
;   or the PC loses power while files are being replaced, run the same setup again — it repairs the installation.
;   User data is never touched by setup, so there is nothing of the user's to roll back.

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef FileVersion
  #define FileVersion "0.0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\portable\SnapForge"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

[Setup]
AppId={{6E0C3B1A-58A2-4E39-9F3B-0D6F4C1C7A51}
AppName=SnapForge
AppVersion={#AppVersion}
AppVerName=SnapForge {#AppVersion}
AppPublisher=bazeocrisy
AppPublisherURL=https://bazeocrisy.github.io/SnapForge/
AppSupportURL=https://bazeocrisy.github.io/SnapForge/support.html
AppUpdatesURL=https://bazeocrisy.github.io/SnapForge/download.html
AppCopyright=Copyright (c) 2026 bazeocrisy
VersionInfoVersion={#FileVersion}
VersionInfoProductVersion={#FileVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoDescription=SnapForge Setup
DefaultDirName={autopf}\SnapForge
DefaultGroupName=SnapForge
DisableProgramGroupPage=yes
DisableDirPage=auto
UsePreviousAppDir=yes
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.26100
AppMutex=Local\SnapForge-SingleInstance-7c1d,Local\SnapForge-Editor-SingleInstance-7c1d,Global\SnapForge-Running-7c1d
CloseApplications=no
RestartApplications=no
LicenseFile={#SourceDir}\LICENSE.txt
OutputDir={#OutputDir}
OutputBaseFilename=SnapForge-{#AppVersion}-win-x64-setup
SetupIconFile=..\src\SnapForge.App\Assets\SnapForge.ico
UninstallDisplayIcon={app}\SnapForge.exe
UninstallDisplayName=SnapForge
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
SetupAppRunningError=SnapForge is running — in your session or in another user's session on this PC.%n%nIf it is recording, stop the recording first so it is saved. Then close SnapForge Editor and exit SnapForge Capture (right-click the SnapForge icon in the notification area next to the clock → Exit). Older versions are called just "SnapForge" and exit the same way. Every user on this PC must do this. Setup never closes SnapForge by force.%n%nClick OK to check again, or Cancel to exit Setup.
UninstallAppRunningError=SnapForge is running — in your session or in another user's session on this PC. Stop any recording, close SnapForge Editor and exit SnapForge Capture (notification-area icon → Exit) first.%n%nClick OK to check again, or Cancel to exit.

[Tasks]
Name: "desktopicon"; Description: "Create &desktop shortcuts for all users"; GroupDescription: "Shortcuts:"; Flags: unchecked

[InstallDelete]
; The single all-users shortcut of 1.0.0-rc.2 (replaced by "SnapForge Capture" and "SnapForge Editor").
Type: files; Name: "{autoprograms}\SnapForge.lnk"
Type: files; Name: "{autodesktop}\SnapForge.lnk"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\SnapForge Capture"; Filename: "{app}\SnapForge.exe"; Comment: "Capture the screen and record — QuickDock, hotkeys and recording"
Name: "{autoprograms}\SnapForge Editor"; Filename: "{app}\SnapForgeEditor.exe"; Comment: "Edit, play and organise your captures"
Name: "{autodesktop}\SnapForge Capture"; Filename: "{app}\SnapForge.exe"; Tasks: desktopicon; Comment: "Capture the screen and record"
Name: "{autodesktop}\SnapForge Editor"; Filename: "{app}\SnapForgeEditor.exe"; Tasks: desktopicon; Comment: "Edit, play and organise your captures"

[Run]
; Started as the user who ran Setup (not as the administrator who approved it), and only when Setup is interactive.
Filename: "{app}\SnapForge.exe"; Description: "Start SnapForge Capture now"; Flags: nowait postinstall skipifsilent runasoriginaluser unchecked

[Code]
function InitializeSetup(): Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  Result := True;
  if Version.Build < 26100 then
  begin
    Result := MsgBox('SnapForge 1.0 supports Windows 11 version 24H2 or later (build 26100+). This PC runs build ' +
      IntToStr(Version.Build) + '. Install anyway? It may not work correctly.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent() then
  begin
    MsgBox('SnapForge has been removed from this PC.' + #13#10#13#10 +
           'Captures, recordings and settings were NOT deleted. They belong to each Windows user and stay in that user''s ' +
           'own folder (%LOCALAPPDATA%\SnapForge, or the Library location chosen in Settings).' + #13#10#13#10 +
           'To delete your own data, reinstall or run the portable SnapForge and use Settings → Delete my SnapForge data, ' +
           'or delete that folder yourself after making sure you have copies of anything you want to keep.' + #13#10 +
           'If "Launch when I sign in" was on, Windows may still list SnapForge under Settings → Apps → Startup; you can turn it off there.',
           mbInformation, MB_OK);
  end;
end;
