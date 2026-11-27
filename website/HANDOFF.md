# Website PR #2 — handoff

## Publishing (simplified, 27 November 2026)

At the owner's request, publishing no longer uses GitHub Actions. The built site is on the **`gh-pages`** branch
(website files at the branch root, plus `.nojekyll`). GitHub Pages serves it once the owner selects
**Settings → Pages → Deploy from a branch → `gh-pages` / (root)**. The deploy job, the `WEBSITE_DEPLOY_APPROVED`
variable and the manual release-verification workflow have been removed. The remaining Website workflow only runs
automatic checks. Updates: `bash website/tools/publish-gh-pages.sh` then `git push origin gh-pages`.
No software branch is merged; the website sources stay on `claude/website`.


Branch `claude/website` → `main`, **draft PR #2**. Not merged, not deployed, no release published, no software
branch modified. Commit and CI evidence: see [Test evidence](#test-evidence).

## Owner decisions applied

| Decision | Where |
|---|---|
| SnapForge is the official product name | Site-wide; About → "Name and trademarks"; [BRANDING.md](BRANDING.md) (no trademark registration or clearance claimed; clearance checks unresolved) |
| Orange aperture logo is the official branding | Site logo, favicon, Open Graph image; [BRANDING.md](BRANDING.md) — app-icon migration kept separate, not in this PR |
| Windows 11 x64 is the initial supported platform | Download → System requirements; Support FAQ; About; structured data `operatingSystem: Windows 11` |
| GitHub Pages hosting | `gh-pages` branch, served by GitHub Pages' standard "Deploy from a branch" (no Actions) |
| No release authorised | Download button disabled; nothing in this repository publishes releases. The site goes live only when the owner selects `gh-pages` in Settings → Pages |
| Donations optional, hidden until verified | `config.json → donations` (all unset) |

## Review corrections (owner's list)

| # | Request | Done |
|---|---|---|
| 1 | Remove `__pycache__`/`.pyc`; ignore rules | Removed `website/__pycache__/build.cpython-313.pyc`; `website/.gitignore` ignores `__pycache__/`, `*.py[cod]`, `tests/node_modules/`, `test-output/`. CI runs Python with `-B`. (Kept inside `website/` so this PR adds no root `.gitignore` that would collide with the software branches' own.) |
| 2 | Generated vs source; CI detects stale output | Sources moved to `src/` + `static/`; `public/` is generated **entirely** (deleted and rebuilt). README table lists each path as source / generated / tool / tests / evidence. `build.py --check` fails on any differing, missing or extra file; unit test edits a generated page and adds a stray file and checks both are caught; build refuses to delete an unrelated folder |
| 3 | `ownerApproved` is not proof; verify the real release | `tools/verify-release.py` (read-only) checks the GitHub Release (published, not draft/pre-release, page and date), **exact asset name**, upload state, download URL, size, GitHub digest, the **downloaded bytes** (count + SHA-256), ZIP integrity, inner `SHA256SUMS.txt`, package file list, and the **signing claim** (unsigned → no Authenticode signature present; signed → signature present and `Get-AuthenticodeSignature` = Valid with matching signer, Windows only). Writes `release-verification.json`; the build refuses a release unless that record matches `config.json` field for field. Run it on a PC (Windows for signed releases); it writes the record and publishes nothing. (The earlier Windows workflow for it was removed when publishing was simplified.) 13 verifier tests against a fake GitHub |
| 4 | Security reporting; operational private channel before launch | `config.json → security.privateReporting`; Support page shows real private-reporting instructions only when verified, otherwise says it is not set up and asks for no public details; the build refuses to show a release while unverified; [LAUNCH-CHECKLIST.md §C](LAUNCH-CHECKLIST.md#c-private-vulnerability-reporting-required-before-launch) has the enable-and-test procedure |
| 5 | Name documentation vs trademark clearance | [BRANDING.md](BRANDING.md); About page: "official name … not a registered trademark, and no trademark registration or clearance is claimed" |
| 6 | Windows 10/11 discrepancy | Site: Windows 11 x64 only; Windows 10/Arm "not supported at launch". The development branches' README still says "Windows 10 and 11" — **not changed here** (software branches untouched); listed as launch step A4 |
| 7 | MIT, notices, requirements, source availability vs the real package | Package contents, notices-included and `SHA256SUMS.txt` statements come **only** from the verification record; otherwise "will be listed once verified". Requirements say development builds are self-contained and release requirements are confirmed at verification. Source: "developed in public on GitHub; until the first release the code is on development branches under review". MIT text on About is the repository `LICENSE` verbatim |
| 8 | Firefox and WebKit tests | CI job **Browser tests** installs Chromium, Firefox and WebKit for the pinned Playwright 1.63.0 and runs every check (including axe-core) in all three with `--require-all`. Locally only Chromium exists; Firefox/WebKit are reported **NOT TESTED** there |
| 9 | Manual keyboard and screen-reader acceptance | [MANUAL-ACCEPTANCE.md](MANUAL-ACCEPTANCE.md): keyboard (K-01…10), zoom/reflow/spacing/motion/contrast themes/no-JS (Z-01…07), NVDA+Firefox, Narrator+Edge, VoiceOver iOS, TalkBack (S-01…12), results table. **Not yet run** |
| 10 | App icon migration separate | [BRANDING.md](BRANDING.md) documents the target logo and what the separate icon task needs; no software branch touched |

## Test evidence

Code commit: **`6fa37fa`**. Documentation-only follow-ups: `8f6e2c2` (this handoff) and the commit adding these CI
results. CI evidence is from **`8f6e2c2`**, whose site, build and tests are identical to `6fa37fa`.

| Check | Where it ran | Result |
|---|---|---|
| `build.py --check` (generated output up to date + site validation) | Local (Linux) | **Pass** — 11 pages, 3 external links (GitHub repository, Issues, GitHub privacy statement) |
| Unit tests (20: build gates, stale-output detection, verifier) | Local (Linux, Python 3.13) | **Pass 20/20** (signed-claim Windows path skipped off Windows by design) |
| Browser checks, unconfigured site | Local, **Chromium 158** only | **Pass, 0 failures, 0 axe violations** (`test-evidence/unconfigured-results.json`); Firefox, WebKit: **NOT TESTED** (not installed locally) |
| Browser checks, configured with a **test-only** verified release and donation links (never committed) | Local, Chromium | **Pass, 0 failures** (`test-evidence/configured-results.json`) |
| CI: Website workflow (build check, unit tests, Chromium + Firefox + WebKit) | GitHub Actions, ubuntu-latest | *see the CI section below* |
| Release verifier against a **real** GitHub Release | — | **Not possible**: no release exists (none authorised). Exercised against a local fake of the GitHub API only |
| Signed-release validation with `Get-AuthenticodeSignature` | — | **Not run** (no signed build exists) |
| Private reporting operational | — | **Not verified** (owner setting; see LAUNCH-CHECKLIST §C) |
| Manual keyboard / screen-reader acceptance | — | **Not run** (procedure provided) |
| Real phones, Lighthouse, deployed site | — | **Not tested** |

### CI

Website workflow on `8f6e2c2` — push run [46012220610](https://github.com/bazeocrisy/SnapForge/actions/runs/46012220610)
and pull-request run [46012227470](https://github.com/bazeocrisy/SnapForge/actions/runs/46012227470): **success**.

| Job | Result |
|---|---|
| Build is up to date and valid | **Pass**: `build.py --check` reported "OK: website/public is up to date and valid (11 pages)", and unit tests ran 20 / OK |
| Browser tests (Chromium, Firefox, WebKit) | **Pass**: "ALL CHECKS PASSED" with `--require-all`, so all three engines ran. Engines from Playwright 1.63.0: Chrome for Testing 153.0.8010.12, Firefox 155.0, WebKit 26.6 (Ubuntu runner). 11 pages × 3 viewports per engine, axe-core in every engine, plus the keyboard, menu, 404 and no-JS checks. Results and screenshots are in artifact `website-browser-tests-8f6e2c2…` ([15203888776](https://github.com/bazeocrisy/SnapForge/actions/runs/46012220610/artifacts/15203888776), kept 30 days) |
| Deploy to GitHub Pages | **Skipped**, as designed: it runs only by hand from `main` with owner approval |

The CI runs on `6fa37fa` (46012082355, 46012092495) passed the build/unit-test job. Their browser job was
**cancelled**, not failed: the workflow cancels an in-progress run on the same branch when a newer commit
(`8f6e2c2`) is pushed.

WebKit here is Playwright's Linux build of the Safari engine. It is not Safari on macOS or iOS, which still need
the manual checks in MANUAL-ACCEPTANCE.md.

## Open items for the owner

1. Enable and test private vulnerability reporting (LAUNCH-CHECKLIST §C); enable Issues.
2. Merge Milestone 3 before deploying, so the repository shows the code (LAUNCH-CHECKLIST A1).
3. Update the software README/docs to the official name and Windows 11 x64 target (separate PR on the software branches).
4. Trademark search / naming clearance (BRANDING.md) — not done, not claimed.
5. H.264/AAC distribution licensing before screen recording is released (stated on About).
6. App-icon migration to the aperture logo — separate task.
7. Run MANUAL-ACCEPTANCE.md.
