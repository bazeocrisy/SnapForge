# SnapForge 1.0.0-rc.2 — release notes (release candidate 2, not a public release)

**Channel:** release candidate for independent review — not approved for public release, not code-signed, public
download disabled. **Platform:** Windows 11 24H2 or later, x64. **Licence:** MIT; free; no ads, accounts,
subscriptions or telemetry. This candidate answers the independent forensic audit of rc.1 and the owner's physical
Windows testing; every finding is tracked in [FORENSIC-REMEDIATION.md](FORENSIC-REMEDIATION.md).

## Your data is safer
- **Crash-safe saving.** A capture's files are now written as a new "generation" and switched over in a single step;
  a crash, power cut or full disk at any moment leaves either the previous or the new version — never a mix.
- **Sanitize can't be half-done.** Sanitize reports success only after the original pixels and hidden annotation
  content are gone from every file of the capture (including the thumbnail); an interrupted sanitize leaves the
  capture exactly as it was before.
- **Complete backups.** Library backups now include recordings, the Trash, tags/folders/favourites and damaged or
  incomplete items (for recovery), with a checksum for every file and a list of anything that could not be
  included. Older image-only backups still restore (and are labelled as such).
- **Safer restore.** Backups are fully checked before anything is added; damaged, oversized, unknown or tampered
  archives are refused; existing captures are never overwritten; an interrupted restore is completed or cleaned up.
- **Safer Library move.** Every file is copied and verified first; SnapForge switches to the new folder only after
  the new location is saved; the old copy is deleted only if you confirm — and only files that were verified.
- **Recording checks.** Recordings get a checksum; exports, backups and moves verify it.
- **Settings.** A settings file from a newer SnapForge is never overwritten; save problems are shown instead of
  being ignored.
- **Drag and drop.** A drag in progress is never cleaned up early.
- **Test safely.** `SnapForge.exe --profile <name>` runs a copy with completely separate data.

## Installation
- **Installed for all users** in `C:\Program Files\SnapForge` (administrator approval for install/uninstall only;
  SnapForge itself runs without administrator rights). Each user keeps their own captures.
- Upgrading from rc.1: SnapForge offers to remove the old per-user copy; your data is kept.
- **Uninstalling never deletes anyone's captures.** To delete your own data: Settings → Delete my SnapForge data.
- The portable ZIP is still available (no administrator rights).

## Starting and windows
- Starting with Windows shows **no window** — only the notification-area icon and the QuickDock.
- Opening SnapForge shows **one** compact window (the capture panel, or the Editor if you prefer).
- Windows always keep their title bar and close button on screen (any scaling, taskbar position or monitor change).
- **File → Close window** (keeps running) and **File → Exit SnapForge** are now separate; exiting during a recording
  asks first. **Help → About** shows the version, installation type and data folder.

## New look
- **QuickDock** (provisional name): a small dark pill with the orange SnapForge mark replaces the orange widget tab;
  it now has Text (OCR), Settings and a full **keyboard mode**.
- **Capture panel** redesigned: tabs, grouped options, full-width Capture button.

## SnapForge Studio (provisional name) — recording with your camera
- **Record** opens SnapForge Studio: **Screen only**, **Screen + camera** (round or rounded-rectangle overlay, size,
  corner, mirror) or **Camera only**.
- Camera and microphone are **off by default** and only start when you choose them; Windows shows its own camera and
  microphone indicators. Blocked, busy or unplugged cameras are explained; the camera is released when you stop,
  pause, lock or sleep.
- More reliable stop/cancel/exit (no more hangs on a stuck device); saving a recording outside the Library is verified
  before the Library copy is removed.

## Known limitations and pending verification
- Unsigned (SmartScreen may warn). No bypass advice — see [SIGNING.md](SIGNING.md).
- Physical-PC acceptance pending: real cameras, audio devices, GPUs, multi-monitor/scaling, real drag targets,
  interactive UAC with separate admin accounts, Narrator/NVDA ([MANUAL-ACCEPTANCE-1.0.md](MANUAL-ACCEPTANCE-1.0.md)).
- Camera support is tested on CI with a synthetic camera only.
- Captures saved by rc.2 use a newer format that rc.1 shows as unreadable (it leaves them unchanged); keep a backup
  if you might go back to rc.1.
- Legal review (H.264/AAC, names "SnapForge", "QuickDock", "SnapForge Studio") pending —
  [LICENSING-REVIEW.md](LICENSING-REVIEW.md), [IP-AND-DESIGN-REVIEW.md](IP-AND-DESIGN-REVIEW.md).
- Full list: [KNOWN-LIMITATIONS.md](KNOWN-LIMITATIONS.md).
