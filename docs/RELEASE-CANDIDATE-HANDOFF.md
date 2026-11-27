# SnapForge 1.0.0-rc.2 — release-candidate handoff (independent review gate)

**This is a release candidate for review. It is not approved for public release, not code-signed, and the public
download stays disabled.** Merging into `main`, publishing a GitHub Release, signing, switching on GitHub Pages and
enabling the download each need the owner's explicit approval ([RELEASE-PROCESS.md](RELEASE-PROCESS.md)). rc.2 answers
the independent forensic audit of rc.1 and the owner's physical Windows testing: every finding (FA-001…FA-024,
SF-001…SF-015, QA-001…QA-010) is tracked with evidence in [FORENSIC-REMEDIATION.md](FORENSIC-REMEDIATION.md).

## 1. Where everything is

| Item | Link |
|---|---|
| Integration pull request (`release/v1.0-candidate` → `main`, **draft — do not merge without approval**) | [PR #3](https://github.com/bazeocrisy/SnapForge/pull/3) |
| Exact source commit of the verified packages | [`a70ae88`](https://github.com/bazeocrisy/SnapForge/commit/a70ae88f1ce98fd6f962178a6a203540fad33b74) (`a70ae88f1ce98fd6f962178a6a203540fad33b74`). Later commits on the branch change only documentation and the website — check with `git diff --stat a70ae88 release/v1.0-candidate -- src tests installer .github` (empty) |
| CI run for that commit (push; all 6 jobs) | [CI run 135](https://github.com/bazeocrisy/SnapForge/actions/runs/46083178881); independent repeat of the same commit (pull-request run), also all green: [CI run 136](https://github.com/bazeocrisy/SnapForge/actions/runs/46083184232) |
| Windows packages (portable ZIP, **all-users installer**, SBOM, `SHA256SUMS.txt`) | Artifact `SnapForge-1.0.0-rc.2-packages-a70ae88…` on run 135 ([direct link](https://github.com/bazeocrisy/SnapForge/actions/runs/46083178881/artifacts/15238530646)). GitHub sign-in required; Actions artifacts expire after 14–30 days. This is **not a public download** and not "owner-only": anyone who can see the repository's Actions can fetch it |
| Portable build only | Artifact `SnapForge-portable-win-x64-dev-a70ae88…` on run 135 |
| Test results (TRX + coverage) | Artifact `test-results` on run 135 |
| Screenshots (1920×1080 tour, rc.2 designs) | Artifact `screenshots-a70ae88…`; the same images are on the website gallery |
| Forensic remediation (finding → change → test → status) | [FORENSIC-REMEDIATION.md](FORENSIC-REMEDIATION.md) |
| Requirements traceability | [PRODUCT-REQUIREMENTS-TRACEABILITY.md](PRODUCT-REQUIREMENTS-TRACEABILITY.md) |
| Defect register (incl. defects found while getting rc.2 green) | [DEFECT-REGISTER.md](DEFECT-REGISTER.md) |
| Known limitations | [KNOWN-LIMITATIONS.md](KNOWN-LIMITATIONS.md) |
| Manual acceptance checklist (physical PC) | [MANUAL-ACCEPTANCE-1.0.md](MANUAL-ACCEPTANCE-1.0.md) |
| Release notes | [RELEASE-NOTES-1.0.0-rc.2.md](RELEASE-NOTES-1.0.0-rc.2.md) |
| Threat model, signing, legal/IP review | [THREAT-MODEL.md](THREAT-MODEL.md), [SIGNING.md](SIGNING.md), [LICENSING-REVIEW.md](LICENSING-REVIEW.md), [IP-AND-DESIGN-REVIEW.md](IP-AND-DESIGN-REVIEW.md) |
| Website (built output on `gh-pages`; Pages not switched on; download disabled) | see §7 |

**Package checksums** (from the CI log of run 135; verify with `Get-FileHash` against `SHA256SUMS.txt`):

| File | SHA-256 |
|---|---|
| `SnapForge-1.0.0-rc.2-win-x64-portable.zip` | `45d5f4443ff2e9cfdcffcd11273b14194433fc320daf93841d308869cc80b89a` |
| `SnapForge-1.0.0-rc.2-win-x64-setup.exe` | `bba1e52889e328ce1a11b60eb5ad8d059b8fb26c61b9613c0011565dca795125` |
| `SnapForge-1.0.0-rc.2-sbom.cdx.json` (CycloneDX 1.5, 14 components) | `ea17077b2791ebb36ed5b4fa4b3a37833284f7dc394abdba2580ce2869186479` |
| `SnapForge.exe` inside the portable build | `661b0a553c0a6198dc2e59475c8e098eb0513b02cfebcdbdc8cd485b0adca170` |

File version 1.0.0.135; product version 1.0.0-rc.2; channel "Release candidate (not approved for public release)".
**Unsigned** — Windows SmartScreen may warn; no bypass advice is given ([SIGNING.md](SIGNING.md)).

## 2. What CI proved for this commit

CI run 135 (`a70ae88`), every job passed; numbers copied from the job logs. Run 136 (same commit, pull-request build) passed every job as well.

| Job | Result |
|---|---|
| Core tests (Linux) | 449 / 449 passed |
| Windows build (warnings as errors), locked restore, vulnerability audit | Passed; no vulnerable packages |
| Windows tests | Core 449 / 449 and App 137 / 137 passed — real mouse/keyboard input, real screen capture, clipboard, OLE drag-and-drop into the same and a separate process, real-wheel scrolling capture, recorder fault injection |
| Portable build, every .NET installation hidden | `--self-test` PASS |
| Start-up windows (FA-001), isolated profiles | Windows start-up (`--background`): **0** windows; first launch: **1** (Welcome); normal launch: **1** (capture panel); second launch hands over, still **1**; profiles wrote only to `SnapForge-Profiles` |
| Video self-test | PASS — 21 checks passed; camera enumeration *unavailable* (no camera on the runner). A/V error 0.07 ms max, drift −0.05 ms/min; 361 frames / 12.0 s constant frame rate; exact and regular timestamps in the final progressive MP4; pause/resume 8.0 s recorded, A/V error 0.78 ms; 60 FPS 300 frames, 0 dropped; real screen 150 frames / 5.0 s; recording toolbar 100 % absent (control 0 %); cancel discards; **SYNTHETIC** camera overlay 100 % in its corner, 0 % elsewhere, SnapForge Studio window 100 % absent; camera-only 1280×720 and camera released; double stop, encoder stall (Stop returned in 2.04 s) and disk-full save outside the Library all handled |
| Feature self-test | PASS. OCR en-US: 3 lines in order; tall image 160 of 160 lines. Video trim + cut 6.0 s → 4.0 s, parts in order, audio kept; GIF export; frame extraction. 4K image operations 759 ms (target < 10 s); GIF encoder 278 ms (target < 3 s); transforms with undo restore the exact original |
| Crash recovery (process killed while recording) | Recovered 8.4 s of the interrupted recording at the next start |
| Screenshot tour (1920×1080) | 18 full-resolution PNGs, SHA-256 verified, on the website gallery |
| Installer (every .NET installation hidden) | Real **rc.1** per-user installer (built from `748eedb`) installed with an rc.1-format Library (3 images incl. Trash, 1 video, 1 damaged item) → rc.2 **system-wide** install to `C:\Program Files\SnapForge`, HKLM Installed-apps entry, all-users Start-menu shortcut; Program Files not writable by Users/Everyone/Authenticated Users; **user data byte-identical** after install and after migration of the old per-user copy; installed app's self-test and `--library-check --expect-images 3 --expect-videos 1` pass; a running SnapForge blocks setup and is **not** killed; repair; a **second Windows user** gets their own data folder and leaves the first user's data unchanged; uninstall removes program, entry and shortcut and **keeps both users' data** |
| Library scale benchmarks | Completed (artifact `benchmarks-a70ae88…`) |

All figures come from a GitHub-hosted Windows Server 2025 runner (4 vCPU, **no GPU, no audio or camera devices, one
1024×768 virtual display; 1920×1080 for the screenshot tour**). They are not physical-PC results.

### Defects found and fixed while getting rc.2 green (details in DEFECT-REGISTER.md)

| Defect | Kind | Summary |
|---|---|---|
| Decoded frames sheared for sizes not a multiple of 16 | App | The decoder pads width and height to whole macroblocks; the read-back assumed the visible size. Pitch now follows the padded layout (unit test for each padding case; 1000×562 recording test) |
| Second launch of a `--profile` copy waited on a message box | App | Instance information was read under a different name than it was published under |
| Auto-scroll could fail on its first step | App | The first frame was taken when the session opened; Auto-scroll now retakes it (pointer in place) before scrolling |
| Pause/resume A/V test failed when a sync marker coincided with the resume instant | Test design | Pause moved between markers; measured A/V error after resume 1.8–2.4 ms (limit 20 ms). A frame-snapping change was tried, made things worse and was reverted |
| "Active window" label cut off (DEF-RC2-05) | App (UI) | Column widened; test measures every capture-type label |
| Multi-item drag test intermittently got no drop (DEF-RC2-06) | **Test harness** | The drop target was placed partly off the 1024×768 CI screen; Windows clamps the pointer to the edge, where the runner's console window took the drop. Evidence-based root cause (window under the pointer logged; tooltip hypothesis tested and rejected; no application change shipped). Targets now kept on screen and checked; single and multi-item drags repeated 3× each with a pause and adjustment |
| SnapForge Studio self-test intermittently recorded no test background (**DEF-RC2-07, open**) | Unknown (under investigation) | `studio-screen-plus-camera-synthetic` / `studio-window-not-recorded` failed in runs 132 and 133 and passed in 131, 134, 135 and 136. Ruled out: another window over the test area (hit-test precondition found only the test's windows). The self-test now reports a direct screen capture taken just before recording and a colour grid of the recorded frame, so the next occurrence separates screen state from the recording pipeline. Not a claim of a fix |
| Installer test issues | Test | Registry lookup by AppId in both views; ACL check limited to write/delete/ownership rights; second user started with their own profile environment |

## 3. Status by owner requirement

| Requirement | Status | Evidence | Still needs a physical PC / owner |
|---|---|---|---|
| Data integrity and security (FA-005…009, 015…020, 023) | Fixed | Fault-injection tests at every durable boundary; backup/restore limits; verified relocation; video hashes | Real disks (USB, near-full, read-only) |
| System-wide Program Files installation (FA-002, FA-010) | Fixed | Installer job (above) | Interactive UAC with separate admin credentials; SmartScreen |
| Simplified start-up (FA-001) | Fixed | Window counts of the real process | Real Windows sign-in |
| Window positioning and closing (FA-003, SF-007, SF-008) | Fixed | `WindowFit` + `WorkAreaGuard` tests; Close window vs Exit | 125–200 % scaling, multi-monitor, taskbar positions |
| Original QuickDock and capture panel (SF-004, SF-005, FA-004) | Fixed | UI tests, keyboard-mode test, screenshots | Owner visual review; Narrator/NVDA |
| Webcam recording and SnapForge Studio | Implemented | Self-test with a **synthetic** camera: overlay position/shape, camera-only, Studio not recorded, camera released. **Open: DEF-RC2-07** (intermittent CI result, 2 of the last 6 runs) | **Real cameras** (none on CI) — MANUAL-ACCEPTANCE; confirm Studio is never in a recording |
| Native drag-and-drop | Fixed | Real OLE drags (tray, Editor, Library single/multi ×3, video into a separate process); FA-009 retention | Outlook, Word, PowerPoint, Teams, browsers, Explorer |
| Complete Library backup and restore | Fixed | `FA005`/`FA017`/`FA023` tests | Large real libraries |
| Trusted code-signing preparation | Prepared (nothing signed) | [SIGNING.md](SIGNING.md) plan; `website/tools/verify-release.py` checks Authenticode signatures and publisher before any download is enabled; no signing step in CI yet | Owner: provider/certificate; then add the signing step |
| Website and download testing | Updated | Website build + 65 tests + browser/axe check; download verifier tests | Owner: switch on Pages; approve download |

## 4. Blocked — owner action required

| Item | Why blocked | Owner action |
|---|---|---|
| Trusted code signing | Needs an approved provider/certificate and identity verification | [SIGNING.md](SIGNING.md) (e.g. SignPath Foundation for OSS) |
| Legal review | H.264/AAC distribution; names "SnapForge", "QuickDock", "SnapForge Studio"; design similarity | [LICENSING-REVIEW.md](LICENSING-REVIEW.md), [IP-AND-DESIGN-REVIEW.md](IP-AND-DESIGN-REVIEW.md) — no clearance is claimed |
| Private vulnerability reporting | Repository setting (owner authorization) | Settings → Code security → enable; then `config.json → security.privateReporting` |
| Physical-PC acceptance | No physical hardware in CI | [MANUAL-ACCEPTANCE-1.0.md](MANUAL-ACCEPTANCE-1.0.md) |
| GitHub Pages / public download / Release | Repository settings and approval | website/LAUNCH-CHECKLIST.md, [RELEASE-PROCESS.md](RELEASE-PROCESS.md) |
| Donation links | No real accounts exist; none invented | Owner creates accounts, then `config.json → donate` |

## 5. Review instructions

1. Read this page and FORENSIC-REMEDIATION.md. 2. Download the packages artifact from run 135 and verify the checksums
above. 3. On a Windows 11 24H2+ x64 PC: install the setup (UAC prompt), or unzip the portable build; to test without
touching your real data, start a copy with `SnapForge.exe --profile review`. 4. Follow MANUAL-ACCEPTANCE-1.0.md and file
failures in DEFECT-REGISTER.md. 5. Decide: approve, request changes, or defer items with justification. Nothing is
merged, signed, released or made downloadable until you do.

## 6. Branches and history

Earlier branches are preserved unchanged (`main`, `claude/pensive-euler-ulw6m4`, `claude/video-v1`, `claude/website`,
`gh-pages`). `release/v1.0-candidate` contains them; no history was rewritten and nothing was force-pushed.

## 7. Website

Built from `release/v1.0-candidate` and published to the `gh-pages` branch with `website/tools/publish-gh-pages.sh`
(`build-info.json` on that branch names the source commit). GitHub Pages is **not** switched on (owner setting);
the Download button stays **disabled** (`release.ownerApproved: false`). The site shows the rc.2 screenshots from run 135
and says the test-build link will be added: per the site's rules the maintainer sets `testBuild.runUrl` (run 135) and
`testBuild.commit` (`a70ae88f1ce98fd6f962178a6a203540fad33b74`) in `website/config.json` after checking that run — a
link to GitHub, never a file on the site. Website checks: `build.py` validation, 65 unit tests and the Playwright + axe browser check all passed locally.
