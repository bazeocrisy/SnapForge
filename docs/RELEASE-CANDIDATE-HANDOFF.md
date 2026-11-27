# SnapForge 1.0.0-rc.1 — release-candidate handoff (independent product review gate)

**This is a release candidate for review. It is not approved for public release, not code-signed, and the public
download stays disabled.** Merging into `main`, publishing a GitHub Release, signing and enabling the download each
need the owner's explicit approval (see [RELEASE-PROCESS.md](RELEASE-PROCESS.md)).

## 1. Where everything is

| Item | Link |
|---|---|
| Integration pull request (`release/v1.0-candidate` → `main`, **do not merge without approval**) | {{PR}} |
| Exact source commit | {{COMMIT}} |
| Latest CI run for that commit | {{RUN}} |
| Windows packages (portable ZIP, per-user installer, SBOM, `SHA256SUMS.txt`) | Artifact `{{PKG_ARTIFACT}}` on that run (GitHub sign-in required; Actions artifacts expire after 14–30 days — not a public download) |
| Portable build only | Artifact `SnapForge-portable-win-x64-dev-<commit>` on that run |
| Test results (TRX + coverage) | Artifact `test-results` on that run |
| Library benchmarks | Artifact `benchmarks-<commit>`; summary in [BENCHMARKS-1.0.md](BENCHMARKS-1.0.md) |
| Requirements traceability (113 requirements) | [PRODUCT-REQUIREMENTS-TRACEABILITY.md](PRODUCT-REQUIREMENTS-TRACEABILITY.md) |
| Defect register | [DEFECT-REGISTER.md](DEFECT-REGISTER.md) |
| Known limitations | [KNOWN-LIMITATIONS.md](KNOWN-LIMITATIONS.md) |
| Manual acceptance checklist (physical PC) | [MANUAL-ACCEPTANCE-1.0.md](MANUAL-ACCEPTANCE-1.0.md) |
| Release notes | [RELEASE-NOTES-1.0.0-rc.1.md](RELEASE-NOTES-1.0.0-rc.1.md) |
| Website (built output on `gh-pages`; GitHub Pages not yet switched on by the owner) | branch `gh-pages`, commit {{GHPAGES}}; would be served at https://bazeocrisy.github.io/SnapForge/ |
| Product inventory at start of this work | [FINAL-PRODUCT-INVENTORY.md](FINAL-PRODUCT-INVENTORY.md) |

**Package checksums** (from the CI log of the run above; verify with `Get-FileHash` against `SHA256SUMS.txt`):

{{SHA}}

## 2. What CI proved for this commit

{{CI_EVIDENCE}}

All numbers are from a GitHub-hosted Windows Server 2025 runner (Windows 10.0.26100, 4 vCPU, **no GPU, no audio
devices, one 1024×768 or 1920×1080 virtual display**). They are not physical-PC results.

## 3. Status by area (details and evidence per requirement in the traceability matrix)

| Area | Implemented | Automated evidence | Still needs a physical PC / owner |
|---|---|---|---|
| Capture (region, window, active window, display, full screen, delay, cursor, presets, hotkeys, repeat, clipboard, magnifier) | Yes | Real-mouse UI tests, capture tests | Multi-monitor, mixed DPI, HDR, RDP, Print Screen setting, magnifier visuals |
| Scrolling capture | Yes | Stitcher unit tests (exact reassembly, sticky regions, refusal); real-wheel UI test stitching a scrolling window exactly | Browsers, Word, Explorer, PDFs (MA-CAP-06) |
| Capture panel, widget, tray | Yes | UI tests | Docking on second monitor, full-screen apps |
| Editor (all tools, transforms, effects, watermark, copy/paste, print, formats) | Yes | Unit + UI tests (print: none — needs a printer) | Every tool by hand; printing |
| Redaction and sanitize | Yes | Pixel tests, adversarial secret-colour test, export metadata tests | MA-RED-01/02 |
| OCR (offline, Windows.Media.Ocr) | Yes | Feature self-test: en-US text in reading order, tall image in bands | Real screenshots, other languages |
| Library (incl. MP4 import, combine) | Yes | Unit + UI tests, scale benchmarks to 25,000 | USB/read-only/near-full disk |
| Drag-and-drop | Yes | Real OLE drags into a separate process (images, MP4, multi-select) | Outlook, Word, PowerPoint, Teams, Chrome, Edge |
| Recording | Yes | Video self-test (14 checks: CFR, A/V sync, pause, recovery, exclusion) | Audio devices, hardware encoders, 1440p/4K, sleep/lock |
| Video editing (trim, cut, frames, MP4, GIF) | Yes | Feature self-test: durations, colour order, audio kept, GIF, frame extraction | Playback in Films & TV/browsers |
| First launch, settings, reset, version/About, single instance | Yes | UI tests, self-test | Clean-profile walkthrough |
| Accessibility | Keyboard access, names, high contrast palette | High-contrast palette test, automation names | Narrator/NVDA, contrast themes, 200 % text |
| Packaging | Portable ZIP, per-user Inno Setup installer, SBOM, checksums | Installer job: install, run self-test, upgrade, uninstall keeps data | SmartScreen behaviour, cancel mid-install |
| Website | Updated content and full-resolution gallery from this build | Website build/validation + unit tests | Owner switches on GitHub Pages |

## 4. Blocked — owner action required (cannot be done by the assistant)

| Item | Why blocked | Owner action |
|---|---|---|
| Trusted code signing (PKG-04) | Needs an approved signing provider/certificate and an account | Apply to SignPath Foundation (free for OSS) or choose another option — [SIGNING.md](SIGNING.md) |
| Legal review (REL-02) | H.264/AAC distribution and trademark/name clearance need a qualified reviewer | [LICENSING-REVIEW.md](LICENSING-REVIEW.md) |
| Private vulnerability reporting (SEC-05) | A repository setting | Settings → Code security → Private vulnerability reporting → Enable; then set `config.json → security.privateReporting` |
| Physical-PC acceptance | No physical hardware in CI | Run [MANUAL-ACCEPTANCE-1.0.md](MANUAL-ACCEPTANCE-1.0.md), record results in `ACCEPTANCE-RESULTS-TEMPLATE.md` |
| GitHub Pages | Repository setting | Settings → Pages → Deploy from branch `gh-pages` / root (website/LAUNCH-CHECKLIST.md) |
| Public release + download button | Requires all of the above and approval | [RELEASE-PROCESS.md](RELEASE-PROCESS.md), website/README.md "Release download" |
| Donation links | No real accounts exist; none were invented | Create accounts yourself, then fill `config.json → donate` |

## 5. Review instructions

1. Read the PR diff summary and this page. 2. Download the packages artifact from the CI run, verify checksums.
3. Install on a Windows 11 24H2+ x64 PC as a standard user, follow MANUAL-ACCEPTANCE-1.0.md. 4. File each failure in
DEFECT-REGISTER.md. 5. Decide: approve for release, request changes, or defer items (with justification) in the
traceability matrix. Nothing is merged, signed, released or made downloadable until you do.

## 6. Branches and history

All earlier branches are preserved unchanged: `main`, `claude/pensive-euler-ulw6m4` (PR #1), `claude/video-v1`,
`claude/website` (PR #2), `gh-pages`. `release/v1.0-candidate` contains all of them (merges, no history rewritten).
