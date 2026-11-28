# Shared by the installer job in .github/workflows/ci.yml (dot-sourced by each step). Test-only.
$new = (Get-ChildItem packages\*-setup.exe | Select-Object -First 1).FullName
$machine = Join-Path $env:ProgramFiles 'SnapForge'
$data = Join-Path $env:LOCALAPPDATA 'SnapForge'
$lib = 'C:\SF-CustomLibrary'                      # the test user's Library lives at a custom location
$programs = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$expectedVersion = ([xml](Get-Content (Join-Path $env:GITHUB_WORKSPACE 'Directory.Build.props') -ErrorAction SilentlyContinue)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $expectedVersion) { $expectedVersion = $env:SNAPFORGE_EXPECTED_VERSION }

function Setup([string]$exe, [string]$a) { (Start-Process -FilePath $exe -ArgumentList $a -Wait -PassThru).ExitCode }

# Starts a program and keeps its handle, so its exit code can be read after it exits.
function StartApp([string]$exe, [string[]]$arguments = @()) {
  $p = if ($arguments.Count -gt 0) { Start-Process -FilePath $exe -ArgumentList $arguments -PassThru } else { Start-Process -FilePath $exe -PassThru }
  $null = $p.Handle
  return $p
}

function LibHash { Get-ChildItem $lib -Recurse -File | Sort-Object FullName | ForEach-Object { "$($_.FullName.Substring($lib.Length)) $((Get-FileHash $_.FullName).Hash)" } }

function SettingsKeys {
  $j = Get-Content (Join-Path $data 'settings.json') -Raw | ConvertFrom-Json
  "LibraryPath=$($j.LibraryPath) | CaptureHotkey=$($j.CaptureHotkey) | RecordHotkey=$($j.Video.RecordHotkey) | StopHotkey=$($j.Video.StopHotkey) | Presets=$(@($j.Presets).Count)"
}

function LibraryCheck([string]$exe, [string]$tag) {
  $r = Join-Path $env:RUNNER_TEMP "$tag-library-check.txt"
  $p = Start-Process -FilePath "$machine\$exe" -ArgumentList '--library-check', "`"$r`"", '--expect-images', '3', '--expect-videos', '1' -Wait -PassThru
  Get-Content $r
  if ($p.ExitCode -ne 0) { throw "$exe could not open the Library ($tag)" }
  if (-not (Select-String -Path $r -SimpleMatch $lib -Quiet)) { throw "$exe did not use the custom Library location $lib ($tag)" }
}

function AssertClosedGracefully($proc, [string]$what, [string]$log) {
  if (-not $proc.HasExited) { throw "$what is still running after setup" }
  if ($proc.ExitCode -ne 0) { throw "$what did not exit normally (exit $($proc.ExitCode)): it must be asked to close, never killed" }
  if (-not (Select-String -Path $log -Pattern 'every SnapForge in this session has exited' -Quiet)) { Get-Content $log -Tail 40; throw "setup log does not show the graceful close" }
  Write-Output "PASS $what was closed gracefully by setup (exit code 0)"
}

# Both programs at the expected version and build, as Windows and both About boxes show them.
function AssertInstalledRc4 {
  $build = $env:GITHUB_RUN_NUMBER
  foreach ($exe in 'SnapForge.exe', 'SnapForgeEditor.exe') {
    $v = (Get-Item "$machine\$exe").VersionInfo
    if ($v.ProductVersion -notlike "$expectedVersion*") { throw "$exe product version $($v.ProductVersion), expected $expectedVersion" }
    if ($v.FileVersion -ne "1.0.0.$build") { throw "$exe file version $($v.FileVersion), expected 1.0.0.$build" }
    $f = Join-Path $env:RUNNER_TEMP "version-$exe-$([guid]::NewGuid().ToString('N')).txt"
    $p = Start-Process -FilePath "$machine\$exe" -ArgumentList '--version-info', "`"$f`"" -Wait -PassThru
    $info = Get-Content $f -Raw
    $program = if ($exe -eq 'SnapForge.exe') { 'SnapForge Capture' } else { 'SnapForge Editor' }
    if ($p.ExitCode -ne 0 -or $info -notmatch [regex]::Escape("About: $program $expectedVersion (build $build)")) { $info; throw "$exe About does not show $program $expectedVersion (build $build)" }
  }
  foreach ($lnk in 'SnapForge Capture.lnk', 'SnapForge Editor.lnk') {
    $path = Join-Path $programs $lnk
    if (-not (Test-Path $path)) { throw "Start menu shortcut $lnk missing" }
    $target = (New-Object -ComObject WScript.Shell).CreateShortcut($path).TargetPath
    $want = if ($lnk -like '*Capture*') { "$machine\SnapForge.exe" } else { "$machine\SnapForgeEditor.exe" }
    if ($target -ne $want) { throw "$lnk points at $target, expected $want" }
  }
  if (Test-Path (Join-Path $programs 'SnapForge.lnk')) { throw "the old single SnapForge shortcut is still present" }
  $entries = @(@("HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{6E0C3B1A-58A2-4E39-9F3B-0D6F4C1C7A51}_is1") | Where-Object { Test-Path $_ })
  if ($entries.Count -ne 1) { throw "Installed apps entries: $($entries.Count)" }
  $display = (Get-ItemProperty $entries[0]).DisplayVersion
  if ($display -ne $expectedVersion) { throw "Installed apps shows $display, expected $expectedVersion" }
  Write-Output "PASS installed: SnapForge Capture and SnapForge Editor $expectedVersion (build $build) in About, file version 1.0.0.$build, Installed apps $display, two working Start menu shortcuts"
}
