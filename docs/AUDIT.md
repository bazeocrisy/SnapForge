# Self-audit — MVP development build

Branch: `claude/pensive-euler-ulw6m4` · Pull request #1 (draft, not merged)

## Round 3 — interface redesign and persistent Capture Library (2026-11-25)

### What was built
Three workspaces (Capture window, Editor, Library), Recent Captures Tray, notification-area icon, configurable global
hotkeys, capture presets, light/dark themes, a persistent local Capture Library with a documented editable format
(v1), automatic crash-resistant saving, capture without history, backup/restore, retention, storage usage, movable
location, and destructive sanitization. Crop and highlight were added; Quick Styles with favorites. Details:
REQUIREMENTS.md (status per requirement), ARCHITECTURE.md (D12–D20), CAPTURE-FORMAT.md, SECURITY.md (threat model),
COMPETITIVE-ANALYSIS.md.

### Verification performed
* **Automated (CI, Windows Server 2025 runner + Linux):** 135 core tests (Linux and Windows) and 33 Windows app
  tests. New areas covered: format round-trips and rejection of bad input; interrupted/disk-full writes (core and UI);
  crash between content and manifest; damaged captures; restart recovery with editable annotations; Editor ↔ Library
  switching; search and views; tray close/pin/delete; capture without history; storage location move; sanitize
  (pixels, hidden annotation text, thumbnail, history); hotkey conflicts between two registrations and inside
  SnapForge; 600-capture library view with lazy thumbnails; 1 500-capture index/search. All MVP tests still pass
  (including the six real-mouse UI tests).
* **Portable build:** self-test (now including a library save/reload in a temporary folder) and the screenshot tour
  both run with every .NET installation hidden.
* **Visual review:** the screenshot tour's previews of all workspaces in dark and light themes were inspected; this
  found and fixed a clipped thumbnail-size slider and a low-contrast warning badge in the Library.
* **Not performed:** any manual use on a physical Windows 10/11 PC, mixed-DPI or multi-monitor use, screen-reader
  testing, hands-on comparison with a running copy of Snagit.

### Problems found and fixed during this round
| Problem | Fix |
|---|---|
| Recent view sorted by file-write time (wall clock) instead of capture/open time | `RecentActivityUtc` uses capture/last-opened time |
| ISO timestamp parsing used an invalid DateTimeStyles combination (all library tests failed initially) | Corrected parsing |
| Redundant write-through + flush made saves ~2× slower on Windows | Single explicit flush before the atomic rename |
| 600-capture UI test timed out (seeding with per-file disk flush) | Test-only flush bypass for seeding; durability still tested elsewhere |
| CI concurrency cancelled a run when docs were pushed mid-run | Process note: wait for runs before pushing |
| Library toolbar clipped the thumbnail-size slider at ~1000 px width | Wrapping toolbar |
| "Unsanitized redactions" badge hard to read on thumbnails | Opaque badge with border |

### Known limitations and remaining defects (milestone 2)
* Autosave is debounced (1.2 s) and runs in the background: a crash or power loss within that window loses the last
  edits (the previous saved version stays intact).
* Undo history is not persisted: after reopening a capture, earlier edits cannot be undone step by step (redactions
  remain removable because the base image keeps the originals until sanitized).
* Library files are not encrypted; secure deletion is best effort (SSD/VSS/backups/sync).
* Quick Styles are shared across tools (no per-tool styles, themes, opacity or shadow); no toolbar customization;
  no tray drag-reordering or "clear unpinned on exit"; no "start with Windows"; no preset import/export.
* Planned editing features not yet implemented (and not shown as working): resize, blur/pixelate, callouts.
* The mouse pointer is captured into the pixels (not a movable object).
* Library start-up reads every capture's manifest; tested to 1 500 captures, not beyond.
* Single-instance and hotkeys are per Windows user session.
* Screenshots from CI are limited to the runner's 1024×768 desktop.
* Not code-signed; product name and copyright attribution await owner approval.

## Round 2 — quality-gate corrections (2026-11-25)

| # | Required correction | What was done | Evidence |
|---|---------------------|---------------|----------|
| 1 | MIT license | `LICENSE` (MIT, "Copyright (c) 2026 bazeocrisy"), shipped as `LICENSE.txt` in the portable build; assembly metadata updated | `LICENSE`, `Directory.Build.props` |
| 2 | Alternative names | Five candidates with preliminary searches and sources; no rename | `docs/NAMING.md` |
| 3 | Regression tests for the region-selection failure | Selection logic moved to `Core/Capture/SelectionTracker` (order-independent); 13 unit tests including the exact faulty event order; plus end-to-end real-mouse drag tests | `SelectionTrackerTests`, `InteractiveUiTests.Region_*` |
| 4 | Automated UI testing investigation | Options compared; real-input (`SendInput`) tests implemented for region, window and display selection and annotation draw/select/move/undo/redo/delete/redact | `docs/UI-TESTING.md`, `InteractiveUiTests` |
| 5 | Redaction pipeline review | Redaction now also covers earlier annotations (opaque mask); in-memory originals, document image and full-desktop frame zeroed when no longer needed; status warning after exports; JPEG metadata test; editing vs. exported behaviour documented | `docs/REDACTION.md`, new tests |
| 6 | Windows Graphics Capture | Documented from Microsoft's doc sources: 1903 minimum for `CreateForWindow`, border consent requires a package capability; comparison and recommendation (keep GDI; optional WGC later with fallback) | `docs/WINDOWS-GRAPHICS-CAPTURE.md` |
| 7 | Windows 10/11 compatibility | Verified against .NET 10 supported-OS list, .NET policy, microsoft.com Windows 10 EOS page and Microsoft API docs sources | `docs/COMPATIBILITY.md` |
| 8 | Portable build without .NET | `--self-test` mode; native DLLs ship next to exe (no `%TEMP%` extraction); CI job downloads the artifact on a fresh runner, renames every .NET installation, runs the self-test | CI job "Portable build runs without an installed .NET": `RESULT: PASS`, exit 0, nothing extracted |
| 9 | CI audit | Least privilege confirmed (`contents: read`); actions pinned to commit SHAs; `persist-credentials: false`; lock files + locked restore; nuget.org-only source mapping; required package signatures; checksums for every artifact file verified after download | `.github/workflows/ci.yml`, `nuget.config`, `docs/SECURITY.md` |
| 10 | Nondeveloper acceptance instructions | Rewritten as a plain-language step-by-step guide with download, checksum and self-test steps and a fill-in results form | `docs/ACCEPTANCE-TESTING.md`, `docs/ACCEPTANCE-RESULTS-TEMPLATE.md` |

### Automated results (CI run 45677731785, commit `df6fb08`)

* Windows (`windows-2025`): build 0 warnings / 0 errors; **92/92 core tests, 24/24 Windows tests** (6 real-input
  UI tests, 2 UI smoke/clipboard, 12 export/redaction/encoding, 4 GDI capture); no vulnerable packages.
* Portable verification job: 11/11 files match `SHA256SUMS.txt`; .NET hidden; self-test PASS.
* Linux: 92/92 core tests; Windows projects compile.

### Problems found while making the corrections

* Locked-mode restore failed at **publish** because publishing adds SDK-implied packages that build-time lock
  files cannot contain. Resolved by disabling locked mode for the publish restore only (signature validation still
  required); documented in the workflow and SECURITY.md.
* learn.microsoft.com is blocked in the build environment; Microsoft's documentation was read from its official
  GitHub source repositories instead, and anything not found there is marked unverified.

## Round 1 — initial MVP review findings (all fixed)

| # | Severity | Finding | Status |
|---|----------|---------|--------|
| 1 | High | Region selection could never complete (capture released before mouse-up was evaluated) | Fixed; now regression-tested (unit + real input) |
| 2 | Medium | Del/Ctrl+Z during a drag could commit a move for a removed annotation | Fixed |
| 3 | Medium | Dirty flag wrong after history overflow | Fixed; tested |
| 4 | Low-Med | Color/size changes lost when editing existing text | Fixed |
| 5 | Low | Zoom label stale after DPI change | Fixed |
| 6 | Low | Overlays could remain if one failed to open | Fixed |
| 7 | Low | Closing mid-save could leave a temp file | Fixed |
| 8 | Low | `GetWindowLongPtrW` missing on 32-bit Windows | Fixed |
| 9 | Note | Redaction did not cover earlier annotations | **Fixed in round 2** (mask) |

## Still NOT verified

* **No manual testing on a physical Windows 10 or 11 PC.** CI uses Windows Server 2025 with one 1024×768 display
  at 100 %; mixed-DPI and multi-monitor behaviour, keyboard shortcuts, text typing, the Save dialog and pasting
  into other applications are manual-test items.
* A truly clean machine (no Visual C++ runtime, no .NET ever installed) — the CI runner had .NET renamed away but
  is otherwise a developer image.
* Windows 10 (any edition), ARM64, HDR displays, > 8K desktops, Remote Desktop sessions.

## Known limitations (by design for MVP)

* Window capture shows overlapping windows on top of the picked window (freeze-frame model; see WGC document).
* HDR content is captured tone-mapped by GDI.
* Black box redaction reveals the size of what was hidden.
* Dark title bar on Windows 11 only.
* No global hotkeys/tray, timer, crop, settings, installer, code signing or localization.
