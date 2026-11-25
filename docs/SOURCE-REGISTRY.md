# Source registry (Gate A)

Every factual claim in the Gate A documents cites an ID from this registry. Access date for all external sources:
**2026-11-25**.

## Evidence classes (used in every Gate A document)

| Class | Meaning |
|---|---|
| **OBS** | Direct observation by the auditor in this session: code read, command run, CI log read, image viewed |
| **CI** | Automated evidence from GitHub Actions (Windows Server runner, not a person at a PC) |
| **DOC** | Stated by an official vendor page **as seen in a search-engine extract**. No external page was opened in full (see limits) |
| **INFER** | Auditor's inference from the above; may be wrong |
| **UNVERIFIED** | Claimed somewhere (non-official site, conflicting extracts, prior handoff) but not confirmed |

## Access limits (important)

* This cloud session's network policy **blocks** `www.techsmith.com`, `support.techsmith.com`,
  `assets.techsmith.com`, `download.techsmith.com` and `learn.microsoft.com` (DNS fails; re-checked 2026-11-25 with
  `curl`, HTTP 000). **No TechSmith or Microsoft Learn page was read in full.** All DOC items come from
  domain-restricted web-search extracts, which are model-written summaries with short quoted snippets; quotes may
  be imperfect. Every DOC item should be re-verified once those hosts are allowed or the owner supplies
  screenshots/PDFs (open question Q-01).
* No hands-on use of Snagit. No hands-on use of SnapForge on a physical Windows PC. Windows evidence is CI only.
* Snagit is used as a functional reference only. No TechSmith code, branding, icons, artwork or text is reused.

## Internal sources

| ID | Source | Class |
|---|---|---|
| I-01 | Repository `bazeocrisy/SnapForge`, branch `claude/pensive-euler-ulw6m4`, commit `301884176813935786292f1b28136d6281799868` (baseline) | OBS |
| I-02 | PR #1 https://github.com/bazeocrisy/SnapForge/pull/1 — open, not merged; 6 check runs green | OBS |
| I-03 | CI run https://github.com/bazeocrisy/SnapForge/actions/runs/45704874375 — Windows job 139524253513 log: build 0 warnings/0 errors, core 135/135, app 33/33, no vulnerable packages, `SnapForge.exe` SHA-256 `4f0bf414…f9d1`, artifact zip digest `5f0590c3…9efa` | CI |
| I-04 | Local Linux re-run in this session: `dotnet build` 0/0, core tests 135/135 | OBS |
| I-05 | Owner handoff package `SnapForge-Claude-Handoff.zip`: `CLAUDE-PROMPT.md`, `references/snagit-capture-panel.png` (742×237 px, viewed), `context/milestone-2-handoff.txt` (labelled "not independently verified") | OBS |
| I-06 | `docs/screenshots/*.jpg` — off-screen renders produced by `SnapForge.exe --screenshots` on the CI runner (1024×768, seeded demo data) | CI |

## External sources

The detailed source tables (URL, title, product version, access method) and the per-claim notes are kept in three
research notes. Each note's IDs are unique across the set:

| Range | Area | Notes file | Count |
|---|---|---|---|
| S-CAP-01 … S-CAP-34 | Capture modes, capture window, presets, hotkeys, widget, preferences, VPAT, release history | [research/snagit-capture.md](research/snagit-capture.md) | 34 |
| S-ED-01 … S-ED-49 | Editor tools, tool properties, Quick Styles, effects, shortcuts, context menus, accessibility | [research/snagit-editor.md](research/snagit-editor.md) | 49 |
| S-LIB-01 … S-LIB-60 | Tray, Library, SNAGX, templates/Step Capture, video, export/share, enterprise, drag/drop (incl. Microsoft shell docs) | [research/snagit-library.md](research/snagit-library.md) | 60 |

Most-cited sources:

| ID | Title | URL | Version |
|---|---|---|---|
| S-CAP-01 | Capture Modes in Snagit Windows | https://support.techsmith.com/hc/en-us/articles/360002743452-Capture-Modes-in-Snagit-Windows | current |
| S-CAP-02 | Capture Your Screen | https://www.techsmith.com/learn/tutorials/snagit/how-to-capture-your-screen/ | current |
| S-CAP-03 | Common Captures with Snagit | https://www.techsmith.com/learn/tutorials/snagit/common-captures/ | current |
| S-CAP-06 | Snagit Preferences – Windows | https://www.techsmith.com/learn/tutorials/snagit/preferences/ | current |
| S-CAP-07 | Snagit Hotkeys Guide | https://www.techsmith.com/learn/tutorials/snagit/snagit-hotkeys/ | current |
| S-CAP-09 | Save Capture Settings as Presets | https://www.techsmith.com/learn/tutorials/snagit/presets/ | current |
| S-CAP-10 | Snagit Windows 2026 Version History | https://support.techsmith.com/hc/en-us/articles/42674936732685-Snagit-Windows-2026-Version-History | 2026.0.0–2026.4.0 |
| S-CAP-11 | Snagit Windows 2025 Version History | https://support.techsmith.com/hc/en-us/articles/39102293598349-Snagit-Windows-2025-Version-History | 2025.0.0–2025.4.3 |
| S-CAP-24 | Snagit 2025 VPAT 2.5 (International) | https://assets.techsmith.com/docs/Snagit_2025-VPAT-2.5-Int.pdf | 2025 |
| S-ED-05 | How to Set and Save Default Tool Properties in Snagit | https://support.techsmith.com/hc/en-us/articles/45790491793165-How-to-Set-and-Save-Default-Tool-Properties-in-Snagit (the owner-supplied ID …45790530141069 did not resolve in search) | n/s |
| S-ED-08 | Blur Tool | https://www.techsmith.com/learn/tutorials/snagit/blur/ | 2025.0.0+ |
| S-ED-21 | Add Effects | https://www.techsmith.com/learn/tutorials/snagit/effects/ | current |
| S-ED-22 | Snagit Hotkeys Guide (editor section) | https://www.techsmith.com/learn/tutorials/snagit/snagit-hotkeys/ | current |
| S-ED-24 | Customize the Snagit Toolbar | https://www.techsmith.com/learn/tutorials/snagit/customize-toolbar/ | current |
| S-LIB-01 | Recent Captures Tray | https://www.techsmith.com/learn/tutorials/snagit/recent-captures-tray/ | current |
| S-LIB-03 | Snagit Windows Version History (2024.1.2 drag-out file type) | https://support.techsmith.com/hc/en-us/articles/115006435067 | 2024.1.2 |
| S-LIB-04 | Introduction to Snagit Library | https://www.techsmith.com/learn/tutorials/snagit/snagit-library/ | current |
| S-LIB-11 | Best Practices for Saving Your Snagit Files | https://www.techsmith.com/learn/tutorials/snagit/file-save-tips/ | current |
| S-LIB-28 | What File Formats Does Snagit Support | https://support.techsmith.com/hc/en-us/articles/41688493993613-What-File-Formats-Does-Snagit-Support | Dec 2025 |
| S-LIB-55 | Handling Shell Data Transfer Scenarios (Microsoft) | https://learn.microsoft.com/en-us/windows/win32/shell/datascenarios | n/a |
| S-ED-04 | What's new in Snagit? | https://www.techsmith.com/snagit/whats-new/ | mixed |

Non-official sources (S-LIB-13 filext.com, S-LIB-52 Capterra, S-LIB-53 appdeploynews, S-LIB-57 Neowin) are used only
as UNVERIFIED pointers, never as the sole basis for a requirement.

## Sources the owner named that could not be confirmed

* https://www.techsmith.com/snagit/features/ — searched; no extract surfaced its contents (S-LIB-58 title only).
* https://support.techsmith.com/hc/en-us/articles/45790530141069-… — ID not found; same title found under ID
  45790491793165 (S-ED-05).
