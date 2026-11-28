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
; * RC4: a running SnapForge is closed GRACEFULLY by Setup (install, repair, upgrade) and by the uninstaller — never
;   killed. Only SnapForge processes started from THIS installation folder count (a portable copy or an rc.1 per-user
;   copy elsewhere is left alone). Setup asks them to close (rc.4+: Local\SnapForge-SetupExit-7c1d for every program and
;   data profile; rc.1–rc.3: their "-exit" request), waits, and continues once they have exited and the program files
;   can be replaced. A recording or unsaved work makes SnapForge ask the user first; if the user keeps it open, Setup
;   offers Retry / Cancel. SnapForge running in ANOTHER Windows session cannot be asked from here: Setup names the
;   session and stops. See docs/INSTALLATION.md.
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
// ---------------------------------------------------------------- RC4: close SnapForge gracefully

const
  EVENT_MODIFY_STATE = $0002;

function OpenEventW(dwDesiredAccess: DWORD; bInheritHandle: BOOL; lpName: String): THandle; external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(hEvent: THandle): BOOL; external 'SetEvent@kernel32.dll stdcall';
function ResetEvent(hEvent: THandle): BOOL; external 'ResetEvent@kernel32.dll stdcall';
function CloseHandle(hObject: THandle): BOOL; external 'CloseHandle@kernel32.dll stdcall';
function GetCurrentProcessId(): DWORD; external 'GetCurrentProcessId@kernel32.dll stdcall';
function ProcessIdToSessionId(dwProcessId: DWORD; var pSessionId: DWORD): BOOL; external 'ProcessIdToSessionId@kernel32.dll stdcall';

var
  ClosingPage: TOutputProgressPage;
  ClosingTick: Integer;

function MySession(): Integer;
var
  Sid: DWORD;
begin
  Sid := 0;
  if ProcessIdToSessionId(GetCurrentProcessId(), Sid) then Result := Sid else Result := -1;
end;

// SnapForge processes started from Dir. Here = in Setup's own Windows session, Away = in other sessions.
procedure ListSnapForge(Dir: String; var Here, Away: Integer; var Lines: String);
var
  Locator, Service, Items, Item: Variant;
  I, Sid, Pid, Me: Integer;
  Path, Name: String;
begin
  Here := 0; Away := 0; Lines := '';
  Me := MySession();
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Items := Service.ExecQuery('SELECT ProcessId, Name, ExecutablePath, SessionId FROM Win32_Process WHERE Name=''SnapForge.exe'' OR Name=''SnapForgeEditor.exe''');
    for I := 0 to Items.Count - 1 do
    begin
      Item := Items.ItemIndex(I);
      Name := Item.Name;
      Pid := Item.ProcessId;
      Sid := Item.SessionId;
      try
        Path := Item.ExecutablePath;
      except
        Path := '';
      end;
      // Only this installation: a portable copy or an rc.1 per-user copy elsewhere does not hold these files.
      if (Path <> '') and (CompareText(RemoveBackslashUnlessRoot(ExtractFileDir(Path)), RemoveBackslashUnlessRoot(Dir)) <> 0) then
        Continue;
      if Name = 'SnapForgeEditor.exe' then Name := 'SnapForge Editor' else Name := 'SnapForge Capture (or an older SnapForge)';
      if Sid = Me then
        Here := Here + 1
      else
        Away := Away + 1;
      Lines := Lines + '  - ' + Name + ' (process ' + IntToStr(Pid) + ', Windows session ' + IntToStr(Sid) + ')' + #13#10;
    end;
  except
    Log('SnapForge close: could not list processes: ' + GetExceptionMessage);
  end;
end;

// Sets a named event in this session. Returns the handle (to reset later) or 0.
function SignalEvent(Name: String): THandle;
begin
  Result := OpenEventW(EVENT_MODIFY_STATE, False, Name);
  if Result <> 0 then
  begin
    SetEvent(Result);
    Log('SnapForge close: requested via ' + Name);
  end;
end;

// Older builds and other administrator accounts: ask as the signed-in user (their events are always reachable).
procedure SignalAsOriginalUser();
var
  Code: Integer;
begin
  if IsUninstaller() then Exit;
  ExecAsOriginalUser(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -WindowStyle Hidden -Command "foreach ($n in ''Local\SnapForge-SingleInstance-7c1d-exit'',''Local\SnapForge-Editor-SingleInstance-7c1d-exit'') { try { [void][Threading.EventWaitHandle]::OpenExisting($n).Set() } catch {} }"',
    '', SW_HIDE, ewWaitUntilTerminated, Code);
  Log('SnapForge close: requested as the signed-in user (exit ' + IntToStr(Code) + ')');
end;

// True if no other program has the file open (Windows refuses an exclusive open of a running .exe).
function CanReplace(FileName: String; var Reason: String): Boolean;
var
  F: TFileStream;
begin
  Result := True;
  if not FileExists(FileName) then Exit;
  try
    F := TFileStream.Create(FileName, fmOpenReadWrite or fmShareExclusive);
    F.Free;
  except
    Result := False;
    Reason := 'The file ' + FileName + ' is still in use by another program, so Setup cannot replace it.' + #13#10 +
      'Close SnapForge everywhere (also in other signed-in Windows accounts) and try again. If nothing is open, ' +
      'an antivirus scan may be holding the file: wait a minute and click Retry.';
  end;
end;

procedure Animate(Ms: Integer);
var
  T: Integer;
begin
  T := 0;
  while T < Ms do
  begin
    if (ClosingPage <> nil) and not IsUninstaller() then
    begin
      ClosingTick := (ClosingTick + 1) mod 100;
      ClosingPage.SetProgress(ClosingTick, 100);
    end;
    Sleep(100);
    T := T + 100;
  end;
end;

// Closes every SnapForge of the installation in Dir gracefully. False (with Reason) if it cannot be done safely.
function CloseSnapForge(Dir: String; Interactive: Boolean; var Reason: String): Boolean;
var
  Here, Away, Waited, Limit: Integer;
  Lines: String;
  Broadcast: THandle;
  Retry: Boolean;
begin
  Result := False;
  Reason := '';
  Limit := StrToIntDef(ExpandConstant('{param:SNAPFORGEWAIT|60}'), 60) * 1000;
  if Interactive then Limit := 90000;
  ListSnapForge(Dir, Here, Away, Lines);
  if Here + Away > 0 then
    Log('SnapForge close: running from ' + Dir + ':' + #13#10 + Lines);
  repeat
    Retry := False;
    if Here > 0 then
    begin
      if (ClosingPage <> nil) and not IsUninstaller() then
      begin
        ClosingPage.SetText('Closing SnapForge…', 'Setup has asked SnapForge Capture and SnapForge Editor to close. If SnapForge asks about a recording or unsaved work, answer it there (it may be behind this window).');
        ClosingPage.Show();
      end;
      Broadcast := SignalEvent('Local\SnapForge-SetupExit-7c1d');      // rc.4 and later, every data profile
      Animate(2000);
      ListSnapForge(Dir, Here, Away, Lines);
      if Here > 0 then
      begin
        if (SignalEvent('Local\SnapForge-SingleInstance-7c1d-exit') = 0) and (SignalEvent('Local\SnapForge-Editor-SingleInstance-7c1d-exit') = 0) then
          SignalAsOriginalUser();                                       // rc.1–rc.3, or a different administrator account
      end;
      Waited := 0;
      while (Here > 0) and (Waited < Limit) do
      begin
        Animate(500);
        Waited := Waited + 500;
        ListSnapForge(Dir, Here, Away, Lines);
      end;
      if Broadcast <> 0 then
      begin
        ResetEvent(Broadcast);
        CloseHandle(Broadcast);
      end;
      if (ClosingPage <> nil) and not IsUninstaller() then ClosingPage.Hide();
      if Here > 0 then
      begin
        Reason := 'SnapForge is still open and did not close:' + #13#10 + Lines + #13#10 +
          'It may be recording, or asking whether to keep unsaved work (look for its message, possibly behind this window ' +
          'or on the taskbar). Finish or save your work in SnapForge, close it, then click Retry. ' +
          'Setup never closes SnapForge by force, and your captures, recordings and settings are not touched.';
        Log('SnapForge close: blocked (still running in this session)');
        if Interactive then
          Retry := MsgBox(Reason, mbError, MB_RETRYCANCEL) = IDRETRY;
        if not Retry then Exit;
      end
      else
        Log('SnapForge close: every SnapForge in this session has exited');
    end;
  until not Retry;
  if Away > 0 then
  begin
    Reason := 'SnapForge is running in another Windows session on this PC (another signed-in or disconnected account):' + #13#10 + Lines + #13#10 +
      'Setup can only ask SnapForge in your own session to close. Ask that user to stop any recording and exit SnapForge ' +
      '(SnapForge icon next to the clock → Exit), or sign that account out, then run Setup again. Nothing has been changed.';
    Log('SnapForge close: blocked (running in another session)');
    Exit;
  end;
  if not CanReplace(AddBackslash(Dir) + 'SnapForge.exe', Reason) then begin Log('SnapForge close: blocked (' + Reason + ')'); Exit; end;
  if not CanReplace(AddBackslash(Dir) + 'SnapForgeEditor.exe', Reason) then begin Log('SnapForge close: blocked (' + Reason + ')'); Exit; end;
  Result := True;
end;

procedure InitializeWizard();
begin
  ClosingPage := CreateOutputProgressPage('Closing SnapForge', 'Setup is closing SnapForge so it can update the program files.');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Reason: String;
begin
  Result := '';
  if not DirExists(ExpandConstant('{app}')) then Exit;
  if not CloseSnapForge(ExpandConstant('{app}'), not WizardSilent(), Reason) then
    Result := Reason;
end;

function InitializeUninstall(): Boolean;
var
  Reason: String;
begin
  Result := CloseSnapForge(ExpandConstant('{app}'), not UninstallSilent(), Reason);
  if not Result then
  begin
    Log(Reason);
    if not UninstallSilent() then MsgBox(Reason, mbError, MB_OK);
  end;
end;

// ---------------------------------------------------------------- system requirements

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
