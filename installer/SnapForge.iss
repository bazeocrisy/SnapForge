; SnapForge installer (Inno Setup 6). Built by CI from the same files as the portable ZIP:
;   iscc /DAppVersion=1.0.0-rc.1 /DFileVersion=1.0.0.123 /DSourceDir=..\artifacts\portable\SnapForge /DOutputDir=..\artifacts\installer installer\SnapForge.iss
;
; Design decisions (see docs/INSTALLATION.md):
; * Per-user install into %LOCALAPPDATA%\Programs\SnapForge — no administrator rights, no UAC prompt.
; * Upgrades install over the previous version (same AppId); settings and the capture Library are untouched because
;   they live in %LOCALAPPDATA%\SnapForge, not in the program folder.
; * Uninstall NEVER deletes captures silently: the uninstaller asks, defaulting to "keep", and asks a second time
;   before removing %LOCALAPPDATA%\SnapForge. A Library moved elsewhere is never touched (the user chose that folder).
; * A running SnapForge is detected through its single-instance mutex and must be closed first (no forced kill:
;   it could be recording).

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
DefaultDirName={localappdata}\Programs\SnapForge
DefaultGroupName=SnapForge
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.26100
AppMutex=Local\SnapForge-SingleInstance-7c1d
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
SetupAppRunningError=SnapForge is running. Please exit it first: right-click the SnapForge icon in the notification area (next to the clock) and choose Exit SnapForge. If it is recording, stop the recording first.%n%nClick OK to continue, or Cancel to exit Setup.
UninstallAppRunningError=SnapForge is running. Please exit it first: right-click the SnapForge icon in the notification area and choose Exit SnapForge.%n%nClick OK to continue, or Cancel to exit.

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\SnapForge"; Filename: "{app}\SnapForge.exe"; Comment: "Capture. Create. Share."
Name: "{autodesktop}\SnapForge"; Filename: "{app}\SnapForge.exe"; Tasks: desktopicon; Comment: "Capture. Create. Share."

[Run]
Filename: "{app}\SnapForge.exe"; Description: "Start SnapForge now"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Temporary drag-and-drop copies only (never captures).
Type: filesandordirs; Name: "{localappdata}\SnapForge\Temp"

[Code]
const
  StartupRunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

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
var
  DataDir, RunValue: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { "Launch SnapForge when I sign in" is a per-user Run value. Remove it only if it starts THIS installation
      (a portable copy elsewhere keeps its own setting). }
    if RegQueryStringValue(HKEY_CURRENT_USER, StartupRunKey, 'SnapForge', RunValue) then
      if Pos(Lowercase(ExpandConstant('{app}')), Lowercase(RunValue)) > 0 then
        RegDeleteValue(HKEY_CURRENT_USER, StartupRunKey, 'SnapForge');

    DataDir := ExpandConstant('{localappdata}\SnapForge');
    if DirExists(DataDir) and not UninstallSilent() then
    begin
      if MsgBox('SnapForge has been removed. Your captures, recordings and settings are still in:' + #13#10 + DataDir + #13#10#13#10 +
                'Do you also want to DELETE your captures, recordings and settings?' + #13#10 +
                '(Choose No to keep them. A Library you moved to another folder is never deleted.)',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        if MsgBox('This permanently deletes every capture and recording in ' + DataDir + '.' + #13#10#13#10 +
                  'Are you sure?', mbError, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        begin
          DelTree(DataDir, True, True, True);
        end;
      end;
    end;
  end;
end;
