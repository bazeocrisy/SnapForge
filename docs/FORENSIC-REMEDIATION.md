# Forensic audit remediation — SnapForge 1.0.0-rc.2

Response to **FORENSIC-AUDIT-REPORT.md** (independent static audit of rc.1 at `2faecf8`) and the owner's physical
Windows testing (SF-001 … SF-015), per the *final forensic remediation and owner acceptance directive*.
**This is a release candidate for review: not approved for public release, not signed, download disabled.**

## How to read this document

| Status | Meaning |
|---|---|
| **Fixed — CI verified** | Code changed; regression tests written for the finding ID; those tests passed on the Windows/Linux CI run named in [RELEASE-CANDIDATE-HANDOFF.md](RELEASE-CANDIDATE-HANDOFF.md). |
| **Fixed — physical PC pending** | As above, but the finding can only be fully confirmed on a real PC (hardware, real apps, UAC with real accounts). Listed in [MANUAL-ACCEPTANCE-1.0.md](MANUAL-ACCEPTANCE-1.0.md). |
| **Partial** | Improved; a stated residual gap remains (see "Residual"). |
| **External** | Needs the owner, a provider, hardware or a qualified reviewer. Cannot be completed by the project's automation. |

**Reproduction class** (directive §1): *Confirmed* = the code demonstrably had the behaviour and a regression test
now fails on the old code path or asserts the corrected behaviour; *Reproduced* = a fault-injection/real-run test
reproduced the failure mode before the fix; *Hypothesis* = plausible risk, closed by design + test, not reproduced
as a failure. Tests carry the finding ID in their names (`FA015_…`, `test_fa012_…`).

## Data integrity and security (fixed first)

| ID | Finding | What changed | Evidence (tests) | Reproduction | Status |
|---|---|---|---|---|---|
| FA-015 | Base/annotations/thumbnail replaced independently before the manifest — mixed generations possible | Generation-based commit: new files `base.<gen>.sfpx`, `annotations.<gen>.json`, `thumb.<gen>.sfpx` written and flushed, then `manifest.json` (names + SHA-256 + size of each) atomically replaced as the single commit point; superseded/uncommitted files are overwritten and deleted on commit and on open; new captures built in `captures/.creating-<id>` and published by one rename. Capture format v2 (v1 read unchanged, upgraded on next save). Fault-injection hook at every durable boundary (`DurabilityFaults`). | `CaptureCommitCrashTests` (9 × `FA015_…`: crash at every boundary of Create, SaveContent with/without new base, reopen → previous or new generation, never mixed) | Reproduced by fault injection | Fixed — CI verified* |
| FA-016 | Interrupted sanitize swapped the base alone | Sanitize is a full generation commit (burned-in base, annotations without redaction masks, new thumbnail) and reports success only after commit **and** overwrite+delete of the old generation; recovery on open leaves either the complete unsanitized state (record still says "reversible redactions") or the complete sanitized state. | `FA016_…` (4): crash at every sanitize boundary, byte scan of every file in the capture folder for a fictional secret colour after sanitize and after recovery; legacy interrupted-sanitize fixture | Reproduced by fault injection | Fixed — CI verified* |
| FA-005 | Backup excluded videos and damaged items | Backup format v2: images (current generation + meta), videos (stored, streamed), Trash, tags/folders/favourites, thumbnails, damaged/incomplete items copied raw under a separate prefix; manifest lists every entry with size + SHA-256 and an explicit excluded-items list with reasons; temp file + atomic rename; cancellation; free-space preflight. UI reports counts, exclusions, warnings. | `BackupRestoreTests` `FA005_…` (7) | Confirmed | Fixed — CI verified* |
| FA-017 | No global limits on backup/package archives | Restore accepts v1 (labelled legacy image-only) and v2 only; rejects unknown/future versions, malformed manifests, duplicate (case-insensitive) names, unexpected names, traversal/absolute paths, > 200,000 entries, per-entry/total sizes beyond `BackupLimits` or beyond free space minus a 256 MiB reserve, and entries whose streamed size/hash differ (reading stops at declared size + 1). | `FA017_…` (11) | Confirmed | Fixed — CI verified* (residual: .NET's ZipArchive reads the central directory before the entry-count check) |
| FA-023 | Restore committed item by item; later failure left partial state | Whole archive prevalidated and extracted with hash verification into one staging folder; only then items are adopted one by one under a restore journal; an interruption is completed or cleaned up at next open and reported (`RecoveredRestore`); existing ids never overwritten. Rejection = nothing added. | `FA023_…` (2) + interruption tests in `BackupRestoreTests` | Reproduced by fault injection | Fixed — CI verified* |
| FA-006 | Move skipped incomplete videos then deleted remaining source folders | `CopyTo` copies ALL files (incl. interrupted recordings, damaged and unknown files), verifies each byte for byte, opens the target and compares item counts, writes a relocation journal — and never deletes. | `RelocationTests` `FA006_…` (6) | Confirmed | Fixed — CI verified* |
| FA-007 | Library moved/deleted before the settings pointer was saved | App flow `RelocateLibraryAsync`: verified copy → durable settings save of the new path (if it fails: old Library stays in use, nothing deleted) → switch → user confirmation → `DeleteSourceAfterVerifiedMove` deletes only files that were verified and are unchanged; everything else is left in place and reported. | Core `FA007_…` (4) + App `FA007_storage_location_change_copies_verifies_and_deletes_old_files_only_after_confirmation`, `FA007_declining_cleanup_keeps_the_complete_old_library` | Confirmed | Fixed — CI verified* |
| FA-008 | Video integrity checked by length only | Streamed SHA-256 recorded when a video is completed or imported; length-only at start-up (fast); full verification on backup, restore, relocation, export (`ExportVideo`, hash-checked, temp + rename) and `VerifyLibrary` / `--library-check`; legacy videos get "hash recorded now". | `VideoIntegrityTests` `FA008_…` (8, incl. same-length byte modification detected) | Reproduced | Fixed — CI verified* |
| FA-018 | 32,000² pixel limit could allocate several GB | `ImageBudget`: checked arithmetic, 268,435,456-pixel (1 GiB BGRA) budget for decode, buffers and every ImageOps output; malformed headers rejected before allocation; 4K, 5000×3000 and 120 M-pixel scrolling captures still work. | `ImageBudgetTests` `FA018_…` (8) | Confirmed | Fixed — CI verified* |
| FA-019 | Newer settings file treated as damaged and later overwritten | `LoadWithStatus`: newer/unreadable → defaults in read-only mode, file never written this session; damaged → unique copy `settings.damaged-<utc>-<random>.json` (never overwrites earlier copies); start-up message explains. Captures from rc.2 (format v2) are shown as unreadable by rc.1 and left unchanged (rc.1 only deletes `*.sftmp`; verified in the rc.1 source). | `SettingsIntegrityTests` `FA019_…` (6) | Confirmed | Fixed — CI verified* |
| FA-020 | Settings save failures swallowed | `UpdateSettings` uses `TrySave`, returns false, records `LastSettingsSaveError`, raises `SettingsSaveFailed`; the app tells the user once per session and keeps the change in memory; a critical pointer (Library location) is never switched unless saved. | FA-019 tests (disk full, unwritable path) + FA-007 App tests | Confirmed | Fixed — CI verified* |
| FA-009 | Size cap could evict a drag in progress | Active-drag marker: never removed by sweep, cap or "delete now" (stale after 24 h = crash leftover); after a drop the cap protection runs from the drop time; Settings explains finite retention and points to Export for permanent copies. | `FA009_…` (3) + updated drag test | Confirmed | Fixed — physical PC pending (real receivers: MA-DRG) |
| QA-001…004 | Storage QA items from the directive | Covered by the rows above; isolated test profiles (`--profile`) keep CI/test copies away from real data. | `ProfileNameTests`, CI startup step uses profiles | — | Fixed — CI verified* |

## Installation and lifecycle

| ID | Finding | What changed | Evidence | Status |
|---|---|---|---|---|
| FA-002 / SF-002 | Per-user AppData install | Inno Setup: `{autopf}\SnapForge` (native x64 Program Files), all users, HKLM entry, all-users shortcuts, UAC only for setup/uninstall, default Program Files ACLs, post-install start as the original user (unchecked). Elevated uninstaller never touches user data or HKCU; data deletion is a standard-user action in Settings. `Global\` running marker so setup sees copies in any session; never kills SnapForge. Per-user rc.1 copy migrated by the app itself (signed-in user, data kept). | CI installer job: install, HKLM entry, all-users shortcut, ACL check (no write for Users/Everyone/Authenticated Users), running copy blocks setup and survives, repair, second Windows user with own data folder, uninstall keeps both users' data; `FA002_…` unit tests | Fixed — physical PC pending (interactive UAC with separate admin credentials, fast user switching) |
| FA-010 | Upgrade test reinstalled the same build | CI builds the **real rc.1 installer from 748eedb**, installs it per-user, seeds the rc.1-format fixture Library, installs rc.2 system-wide, migrates, compares every user-data file hash (byte-identical), then `--library-check --expect-images 3 --expect-videos 1` with full integrity scan | `rc1-baseline` + `installer` jobs | Fixed — CI verified* (M2/M3 portable builds have no installer: their data format equals rc.1's and is covered by the fixture) |
| FA-001 / SF-003 | Editor + panel shown at every start, also at Windows start-up | `--background` → notification-area icon + enabled dock only; normal launch → exactly one window (capture panel or Editor by setting); first launch → welcome only, which opens the panel when closed; second launch hands over to the same window | CI portable job "Startup windows": counts captioned windows of the real process (0 / 1 / 1 / still 1 after second launch) | Fixed — CI verified* |
| FA-003 / SF-007 | Title bar off screen | `WorkAreaGuard`: every captioned window fitted into its monitor work area in physical pixels on show, restore and display/taskbar changes; MainWindow minimum size lowered; Core `WindowFit` | `WindowFitTests` (Linux), `WindowingTests` `FA003_…` (Windows) | Fixed — physical PC pending (owner's 1920×1080 at 125–200 %, multi-monitor) |
| SF-008 | Close vs exit unclear | File → Close window (keeps running) vs Exit SnapForge with tooltips; exit asks while recording | `SF008_file_menu_separates_close_window_from_exit` | Fixed — CI verified* |
| SF-014 | Older running build confusion | About: version, build, commit, channel, installation kind (Program Files / portable / old per-user), data profile and folder; different-build hand-over unchanged (asks; recording never stopped silently) | `SF014_about_reports_installation_kind_and_profile` | Fixed — CI verified* |
| Directive §3 | Isolated test profile | `--profile <name>` / `SNAPFORGE_PROFILE`: own settings, Library, logs, temp, instance key; no global hotkeys or start-up entry | `ProfileNameTests`; CI uses profiles for real-process tests | Fixed |

## Design and accessibility

| ID | Finding | What changed | Evidence | Status |
|---|---|---|---|---|
| SF-004 | Orange floating widget resembled a competitor | QuickDock (provisional name): charcoal/navy pill with the orange aperture mark and a chevron; expanded Capture / Record / Text / Library / Editor / Settings / Presets | GateB UI tests (`QuickDock_is_a_small_pill…`), before/after screenshots | Fixed — owner visual review pending |
| SF-005 | Capture panel resembled a competitor | Original panel: top segmented tabs, option cards, chips, full-width rectangular action with key cap; large round button removed | `Capture_panel_is_compact_and_marks_unavailable_modes`, `Capture_panel_video_tab_opens_the_studio…` | Fixed — owner visual review pending |
| FA-004 | Dock not keyboard accessible | Keyboard mode (tray / panel "Open QuickDock with keyboard"): focus ring, Tab/arrows/Home/End, Enter/Space, Esc restores the previous window; names and help text on every button; mouse use still non-activating | `QuickDock_keyboard_mode_moves_focus…` (real key presses) | Fixed — Narrator/NVDA pending (MA-ACC) |
| SF-010 | Widget absent | First deliberate start peeks the dock; View/tray "Show QuickDock" recovers it; disabled preference preserved | existing GateB widget tests | Fixed — physical PC pending |
| SF-015 | IP similarity concern | Original design direction + provenance documented; reviewer checklist | [IP-AND-DESIGN-REVIEW.md](IP-AND-DESIGN-REVIEW.md) | External (qualified review; no clearance claimed) |

## Recording and camera

| ID | Finding | What changed | Evidence | Status |
|---|---|---|---|---|
| SF-006 | Webcam option on Record | SnapForge Studio (provisional): Screen only / Screen + camera / Camera only; camera and mic off by default; device selectors; preview only on request; circle/rounded overlay, size, corner, mirror; privacy-blocked and busy/unplugged handling; camera released on stop/pause/lock/sleep | `StudioCompositionTests`, `StudioWindowTests`, self-test `studio-*-synthetic` (**synthetic camera — labelled**), `camera-enumeration` (UNAVAILABLE on CI: no camera) | Fixed — physical camera pending (QA-005); **open CI issue DEF-RC2-07** (Studio self-test intermittently found its own test window unpainted; recording verified faithful; Studio's UI thread measured unblocked) |
| FA-021 | Unbounded joins, synchronous stop on exit | Single idempotent lifecycle state machine; bounded joins (10 s; stalled threads reported, file kept recoverable, objects not disposed while in use); exit waits ≤ 8 s then leaves the file to crash recovery | `RecordingLifecycleTests`, `RecordingFaultTests` (concurrent stops, stop/cancel race, injected stall, injected disk full), self-tests `lifecycle-*` | Fixed — CI verified* |
| FA-022 | Overwrite copy then delete source | `VerifiedFileCopy`: temp in the destination folder, flush, size + SHA-256 verify, atomic replace; Library copy removed only after success; originals untouched on failure | `RecordingSaveOutsideTests`, self-test `save-outside-library-atomic` | Fixed — CI verified* |
| QA-005 | No physical device testing | — | MANUAL-ACCEPTANCE §9 + VIDEO-ACCEPTANCE F/G | External |

## Website and release

| ID | Finding | What changed | Evidence | Status |
|---|---|---|---|---|
| FA-011 | Single release asset | Per-asset records (installer, portable ZIP) with own size/SHA-256/asset id/signing claim; each needs its own verification record | `test_fa011_…` (7) | Fixed (no release activated) |
| FA-012 | Verifier gaps | Complete SHA256SUMS coverage, duplicate/case/path checks, executable allow-list, exact publisher subject + thumbprint, valid chain, timestamp, installer + embedded exe (innoextract), asset-id/digest pinning, `--recheck` | `test_fa012_…` (23) | Fixed (Windows signing checks run only with a real signed release) |
| FA-013 | Publish from any branch | Canonical branch constant, clean/up-to-date checks, `build-info.json`, refuses older sources unless `--force-older`; still plain gh-pages, no Actions/variables (SF-013) | `test_fa013_…` (7) | Fixed |
| FA-014/024, SF-012 | Download page | Public download (disabled) vs invited-tester CI artifacts (sign-in is not owner-only; expiring; unsigned; checksums), installer vs portable, Windows 11 24H2+, UAC, uninstall keeps data | `test_fa014_…`, `test_sf012_…` | Fixed |
| SF-011 | Blurry screenshots | Native-size PNGs only; build fails on size mismatch, JPEG, upscaling or missing full-size link | `test_sf011_…` (6) | Fixed |
| SF-013 | No deployment complexity | Unchanged: gh-pages branch, `.nojekyll`, no Actions deployment | publish script tests | Fixed |
| SF-001 | SmartScreen / signing | SignPath Foundation plan, draft code-signing policy, alternatives with costs flagged as owner decisions; no bypass advice anywhere | [SIGNING.md](SIGNING.md) | External (owner application/approval) |
| QA-010 | Private vulnerability reporting | Unchanged: owner must enable and verify | SECURITY.md | External |
| QA-007/008/009 | Docs, matrix, accessibility evidence | Traceability matrix reconciled (see PRODUCT-REQUIREMENTS-TRACEABILITY.md), docs updated | — | See handoff |

\* "CI verified" refers to the CI run recorded in the handoff for the final rc.2 commit. If a row's tests did not run
or failed there, the handoff says so explicitly and that row is not verified.

## Residual risks (not hidden)

* Directory entries are not flushed after renames (no portable .NET API); a power cut right after a commit can in
  rare cases lose the latest generation on some file systems (the previous generation remains valid).
* Restore is all-or-nothing on a corrupt entry (by design, safer); a partially damaged backup cannot be partly
  restored by the app.
* Camera behaviour on real drivers (formats, MJPG/NV12 variants, privacy error codes, LED) is only tested with a
  synthetic camera on CI.
* UAC with separate administrator credentials, fast user switching and Remote Desktop sessions are not interactive on
  CI; the second-user test uses a scripted logon.
* Existing rc.1 binaries cannot be changed: rc.1 will show rc.2 captures as unreadable (it leaves them unchanged).
