# Website launch checklist

Nothing here is authorised yet: **no deployment and no software release** until the owner says so. This is the
order of work for when they do. Each step names who does it and how it is checked.

## A. Before the site may be deployed (even with "no release yet")

| # | Step | Who | Check |
|---|---|---|---|
| A1 | Milestone 3 reviewed and merged into `main`, so the repository's front page shows the code the site describes | Owner / reviewer | `main` contains `src/`, `README.md`, `LICENSE` |
| A2 | **Private vulnerability reporting enabled**: Settings → Code security → *Private vulnerability reporting* → Enable | Owner | See §C |
| A3 | Issues enabled (Settings → General → Features → Issues) | Owner | `https://github.com/bazeocrisy/SnapForge/issues` opens |
| A4 | Software README and docs updated to the owner's decisions (official name, Windows 11 x64 target) | Software branches | No "working name" / "Windows 10 and 11" statements remain |
| A5 | Manual accessibility acceptance (`MANUAL-ACCEPTANCE.md`) passed | Tester | Results table filled in |
| A6 | Website PR #2 approved at the independent review gate and merged | Owner | — |
| A7 | Settings → Pages → Source: **GitHub Actions**; Settings → Environments → `github-pages` → required reviewer: owner | Owner | — |
| A8 | Settings → Secrets and variables → Actions → Variables: `WEBSITE_DEPLOY_APPROVED` = `true` | Owner | — |
| A9 | Actions → **Website** → Run workflow on `main`, tick **deploy** | Owner | Site loads at https://bazeocrisy.github.io/SnapForge/ ; repeat the browser checks against it |

## B. Before the site may offer a download

| # | Step | Check |
|---|---|---|
| B1 | Owner approves the software release (separate review gate) | Written approval |
| B2 | Release built by CI from the approved commit; owner publishes it as a **GitHub Release** (not draft, not pre-release) with the ZIP as an asset. *This website never publishes releases.* | Release page exists |
| B3 | Fill in `config.json → release` (all fields; `ownerApproved: true` only with the owner's approval) | — |
| B4 | **Verify the published release**: Actions → **Website release verification** → Run workflow (Windows runner; checks a signing claim with `Get-AuthenticodeSignature`). Or locally: `python3 website/tools/verify-release.py` (unsigned releases only) | Workflow green |
| B5 | Download the `release-verification-<sha>` artifact, read it, save it as `website/release-verification.json` | `result: verified`, checks list, package contents, signing |
| B6 | `python3 website/build.py` — the build refuses unless the record matches `config.json` exactly **and** private reporting is verified (A2/§C) | Builds without errors |
| B7 | Review the Download, Support and About pages (package contents, checksum, signing text come only from the record); commit; PR; deploy (A9) | — |

What `verify-release.py` checks (any failure → nothing written, download stays disabled):

1. The release for the tag in `downloadUrl` exists, is published, is not a pre-release; its page equals
   `releaseNotesUrl`; its publication date (UTC) equals `date`.
2. Exactly one asset with the **exact** name `fileName`, state `uploaded`, download URL `downloadUrl`, size
   `sizeBytes`; GitHub's own asset digest (when reported) equals `sha256`.
3. The **downloaded bytes**: byte count equals `sizeBytes`, SHA-256 equals `sha256`.
4. ZIP: valid archive, one `SnapForge.exe`, every line of an included `SHA256SUMS.txt` matches; the file list is
   recorded. The site lists only these files — it never assumes a license or notices file is included.
5. Signing: `signed: false` → the executable must have **no** Authenticode signature. `signed: true` → it must
   have one and Windows must report it **Valid** with a subject containing `signer`.

If anything about the release changes (re-uploaded asset, new tag), re-run B4–B6: a stale record no longer matches
and the build fails.

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
