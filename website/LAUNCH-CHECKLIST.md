# Website launch checklist

Nothing here is authorised yet: **no deployment and no software release** until the owner says so. This is the
order of work for when they do. Each step names who does it and how it is checked.

## A. Switching the website on (no release needed)

The `gh-pages` branch already holds the built, checked site. The Download button is disabled and donation links are
hidden, so the site can go live before any software release.

| # | Step | Who | Check |
|---|---|---|---|
| A1 | Recommended first: merge `release/v1.0-candidate` into `main` after approval, so the repository's front page shows the code the site describes; then set `config.json → sourceUrl` to the repository URL and `sourceIsCandidate` to false, and `CANONICAL_BRANCH` in `tools/publish-gh-pages.sh` to `main` | Owner / reviewer | `main` contains `src/`, `README.md`, `LICENSE` |
| A2 | Settings → Pages → Source: **Deploy from a branch** → `gh-pages` / `(root)` → Save | Owner | https://bazeocrisy.github.io/SnapForge/ loads (allow a few minutes) |
| A3 | Click through every page on a PC and a phone; then run `MANUAL-ACCEPTANCE.md` | Owner / tester | Results table filled in |
| A4 | Before a release (§B): private vulnerability reporting (§C), Issues enabled (Settings → General → Features), software README updated to the official name and Windows 11 x64 | Owner / software branches | — |

To update the live site later: `bash website/tools/publish-gh-pages.sh` then `git push origin gh-pages`.
To take it offline: Settings → Pages → Unpublish site.

## B. Before the site may offer a download

| # | Step | Check |
|---|---|---|
| B1 | Owner approves the software release (separate review gate) | Written approval |
| B2 | Release built by CI from the approved commit; owner publishes it as a **GitHub Release** (not draft, not pre-release) with the installer `SnapForge-<version>-win-x64-setup.exe` and/or the portable `SnapForge-<version>-win-x64-portable.zip` (same version) as assets. *This website never publishes releases.* | Release page exists |
| B3 | Fill in `config.json → release`: version, tag, date, notes URL, publisher (`subject` + `thumbprints` if signed) and, **per asset**, file name, URL, GitHub asset id, size, SHA-256 and signing claim; `ownerApproved: true` only with the owner's approval | — |
| B4 | **Verify the published release** on a Windows PC with `innoextract` on PATH: `python website/tools/verify-release.py` (a signed claim can only be validated on Windows) | Ends with `VERIFIED` |
| B5 | Read the written `website/release-verification.json` | `result: verified`, one record per asset, checks list, package contents, signing |
| B6 | `python3 website/build.py` — the build refuses unless every asset's record matches `config.json` (asset id, size, SHA-256, signing claim) **and** private reporting is verified (§C) | Builds without errors |
| B7 | Review the Download, Support and About pages; commit and push on `release/v1.0-candidate` (later `main`); then `bash website/tools/publish-gh-pages.sh` and `git push origin gh-pages` | Live Download page lists each asset |

What `verify-release.py` checks is in [README.md → Public release](README.md#public-release-fa-011) and the tool's
header. Any failure → nothing written, download stays disabled. If anything about the release changes (re-uploaded
asset = new asset id, new tag), `verify-release.py --recheck` fails; update B3 and the build refuses the old record until B4–B7 are redone.

## C. Private vulnerability reporting (required before launch)

1. Owner: Settings → Code security → **Private vulnerability reporting** → Enable.
2. Check it is operational: sign in to GitHub with a **different** account (or ask someone), open
   `https://github.com/bazeocrisy/SnapForge/security`, confirm a **Report a vulnerability** button is shown and that
   `https://github.com/bazeocrisy/SnapForge/security/advisories/new` opens the private form. Optionally submit a
   clearly-marked test report and confirm the owner is notified, then close it.
3. Owner: confirm notification settings deliver security advisories to an inbox that is monitored.
4. Only then set in `config.json`:
   `"security": { "privateReporting": { "url": "https://github.com/bazeocrisy/SnapForge/security/advisories/new", "verified": true } }`
   and rebuild. The Support page then explains how to report privately; until then it tells people not to disclose
   details publicly and that private reporting will exist before the first release. The build refuses to show a
   release while this is not verified.

Status on 27 November 2026: **not verified** (could not be checked from the build environment).

## D. Donations (optional, any time)

Per provider: owner creates the account → opens the link and confirms it is theirs → sets `url` and
`verified: true` → rebuild. Unverified providers stay hidden. No tax-deductibility claims; no embedded payment code.
