<#
.SYNOPSIS
  SnapForge "blue screen" diagnostics — READ-ONLY. Writes one text file to your Desktop; uploads nothing.

.DESCRIPTION
  Collects what is needed to tell apart the three kinds of "blue screen" (docs/BLUE-SCREEN-TRIAGE.md):
    A. Windows SmartScreen's blue "Windows protected your PC" window when starting Setup (an unsigned program),
    B. a blue or blank picture inside SnapForge or in a saved recording,
    C. a Windows stop error ("Your device ran into a problem and needs to restart") that restarts the PC.
  It records: Windows version; the installed SnapForge versions and their signing status; optionally the signing
  status of a Setup file; stop-error (bug check) and unexpected-restart events of the last 30 days with their stop
  codes; the names and sizes of crash-dump files (the dumps themselves are NOT copied); SnapForge / .NET application
  errors; SnapForge's own error and recording logs; the graphics adapters and drivers.
  Run it as yourself (no administrator rights needed):
    powershell -ExecutionPolicy Bypass -File collect-snapforge-diagnostics.ps1 [-Setup "C:\path\SnapForge-...-setup.exe"]
  Then look through the file (it contains your computer and user name and folder paths) before sharing it.
#>
param([string]$Setup)

$ErrorActionPreference = 'Continue'
$out = Join-Path ([Environment]::GetFolderPath('Desktop')) ("SnapForge-diagnostics-{0:yyyyMMdd-HHmmss}.txt" -f (Get-Date))
$lines = New-Object System.Collections.Generic.List[string]
function Add([string]$text) { $lines.Add($text) }
function Section([string]$title) { Add ''; Add ('=' * 78); Add $title; Add ('=' * 78) }
$since = (Get-Date).AddDays(-30)

Section 'Windows'
$os = Get-CimInstance Win32_OperatingSystem
Add "$($os.Caption) $($os.Version) build $($os.BuildNumber), $($os.OSArchitecture); last boot $($os.LastBootUpTime)"

Section 'Installed SnapForge (A: signing status)'
foreach ($dir in "$env:ProgramFiles\SnapForge", "$env:LOCALAPPDATA\Programs\SnapForge") {
  foreach ($exe in 'SnapForge.exe', 'SnapForgeEditor.exe') {
    $f = Join-Path $dir $exe
    if (Test-Path $f) {
      $v = (Get-Item $f).VersionInfo
      $sig = Get-AuthenticodeSignature $f
      Add "$f : product $($v.ProductVersion), file $($v.FileVersion), signature: $($sig.Status) $($sig.SignerCertificate.Subject)"
    }
  }
}
$entry = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{6E0C3B1A-58A2-4E39-9F3B-0D6F4C1C7A51}_is1' -ErrorAction SilentlyContinue
if ($entry) { Add "Installed apps entry: $($entry.DisplayName) $($entry.DisplayVersion)" } else { Add 'Installed apps entry: none (system-wide)' }
if ($Setup -and (Test-Path $Setup)) {
  $sig = Get-AuthenticodeSignature $Setup
  Add "Setup file: $Setup"
  Add "  SHA-256: $((Get-FileHash $Setup -Algorithm SHA256).Hash.ToLowerInvariant())"
  Add "  Signature: $($sig.Status) ($($sig.StatusMessage))"
  $zone = Get-Content -Path $Setup -Stream Zone.Identifier -ErrorAction SilentlyContinue
  Add "  Downloaded-from-internet mark (Zone.Identifier): $(if ($zone) { ($zone -join ' ') } else { 'none' })"
}

Section 'C: Windows stop errors and unexpected restarts (last 30 days)'
$events = Get-WinEvent -FilterHashtable @{ LogName = 'System'; StartTime = $since; Id = 1001, 41, 6008 } -ErrorAction SilentlyContinue |
  Where-Object { $_.Id -ne 1001 -or $_.ProviderName -match 'WER-SystemErrorReporting|BugCheck' }
if (-not $events) { Add 'none found (no stop error or unexpected restart was recorded)' }
foreach ($e in $events) {
  $msg = ($e.Message -replace '\s+', ' ')
  if ($msg.Length -gt 400) { $msg = $msg.Substring(0, 400) + '…' }
  Add "$($e.TimeCreated)  id $($e.Id)  $($e.ProviderName): $msg"
}
foreach ($dump in "$env:SystemRoot\Minidump", "$env:SystemRoot\MEMORY.DMP", "$env:SystemRoot\LiveKernelReports") {
  if (Test-Path $dump) {
    Get-ChildItem $dump -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $since } |
      ForEach-Object { Add "dump file (not copied): $($_.FullName)  $([math]::Round($_.Length / 1MB, 1)) MB  $($_.LastWriteTime)" }
  }
}

Section 'B: SnapForge and .NET application errors (last 30 days)'
$app = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $since; Id = 1000, 1001, 1002, 1026 } -ErrorAction SilentlyContinue |
  Where-Object { $_.Message -match 'SnapForge' }
if (-not $app) { Add 'none found' }
foreach ($e in $app) { $m = ($e.Message -replace '\s+', ' '); if ($m.Length -gt 600) { $m = $m.Substring(0, 600) + '…' }; Add "$($e.TimeCreated)  id $($e.Id): $m" }

Section 'B: SnapForge logs'
foreach ($root in (Get-ChildItem $env:LOCALAPPDATA -Directory -Filter 'SnapForge*' -ErrorAction SilentlyContinue)) {
  foreach ($f in Get-ChildItem $root.FullName -Recurse -File -Include 'error-*.txt', 'recording.log' -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $since }) {
    Add "--- $($f.FullName) ($($f.LastWriteTime))"
    Get-Content $f.FullName -Tail 60 | ForEach-Object { Add "    $_" }
  }
}

Section 'B: graphics adapters (blank or blue recordings are often driver related)'
Get-CimInstance Win32_VideoController | ForEach-Object { Add "$($_.Name)  driver $($_.DriverVersion) ($($_.DriverDate))  $($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)" }

$lines | Set-Content -Path $out -Encoding UTF8
Write-Output "Written: $out"
Write-Output 'Nothing was uploaded. Please read the file before sharing it (it contains your PC name, user name and folder paths).'
