# SnapForge website

The SnapForge project website, for free GitHub Pages hosting at **https://bazeocrisy.github.io/SnapForge/**.
**Status: draft for review — not deployed.** Nothing is published until the owner completes the steps under
[Deployment](#deployment).

Plain HTML and CSS, one small local script (mobile menu, "copy checksum"), no frameworks, no third-party resources,
no analytics, no cookies, no storage. Every link is relative, so the site works under `/SnapForge/`, on a custom
domain, or from any local web server.

## Layout

| Path | What it is |
|---|---|
| `config.json` | **The only file to edit for releases and donations** (see below) |
| `src/layout.html` | Shared page shell: header, navigation, footer, meta tags, Content-Security-Policy |
| `src/pages/*.html` | Page content (Home, Features, Download, User Guide, What's New, Roadmap, Support/FAQ, Donate, Privacy, About/License, 404) |
| `build.py` | Builds `src/` + `config.json` into `public/` and validates it (Python 3 standard library only) |
| `public/` | **The generated site that is deployed** (HTML is generated; CSS, JS, images and icons live here directly) |
| `tests/browser-check.js` | Browser tests (Playwright + axe-core) — see [Testing](#testing) |
| `tools/` | Open Graph image source (`og-image.html`) and renderer (`make-og-image.js`) |
| `test-evidence/` | Browser screenshots and JSON results from the last test run |
| `design-references/` | The supplied concept images (generated references — **not published**, not real UI) and the current app icon for comparison |

## Preview locally

```bash
python3 website/build.py                                   # after any change to src/ or config.json
python3 -m http.server 8000 --directory website/public     # then open http://localhost:8000/
```

To preview under the real base path, serve the folder as `/SnapForge/` — `tests/browser-check.js` does exactly that.

## Release download (config.json → `release`)

The Download button is **disabled** and the Download page says "No public release yet" until all of this is true:

1. The owner has approved a release and published it as a **GitHub Release** of `bazeocrisy/SnapForge`
   (Actions artifacts are rejected by the build: they expire and need a GitHub sign-in).
2. `config.json` is filled in from that release:

```json
"release": {
  "ownerApproved": true,
  "version": "1.0.0",
  "date": "2027-01-15",
  "downloadUrl": "https://github.com/bazeocrisy/SnapForge/releases/download/v1.0.0/SnapForge-1.0.0-win-x64.zip",
  "fileName": "SnapForge-1.0.0-win-x64.zip",
  "sizeBytes": 56487727,
  "sha256": "<64 lower-case hex characters of the ZIP>",
  "signed": false,
  "signer": null,
  "releaseNotesUrl": "https://github.com/bazeocrisy/SnapForge/releases/tag/v1.0.0",
  "packageType": "portable"
}
```

`sizeBytes` and `sha256` must be measured from the file **as uploaded to the release** (download it and run
`Get-FileHash` / `sha256sum`; size from Explorer → Properties → Size in bytes). Set `signed: true` and `signer`
only for an Authenticode-signed build. `build.py` validates every field and refuses to build otherwise. After a
change run `python3 website/build.py`, check the Download page, commit.

## Donations (config.json → `donations`)

Each provider is **hidden** until it has a URL **and** `verified: true`. The build accepts only these URL forms:

| Provider | Set up (by the owner) | URL form |
|---|---|---|
| GitHub Sponsors | github.com/sponsors → join; profile must be approved by GitHub | `https://github.com/sponsors/<username>` |
| Buy Me a Coffee | buymeacoffee.com → create page | `https://www.buymeacoffee.com/<name>` |
| PayPal | paypal.com → Donate button (hosted) or paypal.me link | `https://www.paypal.com/donate/?hosted_button_id=<ID>` or `https://paypal.me/<name>` |

Open each link yourself, confirm it shows your account, then set `verified: true`. The site uses plain external
links only — no embedded buttons, scripts or trackers. Do not describe donations as tax-deductible.

## Deployment

The workflow `.github/workflows/website.yml` **validates** the site on every push/PR that touches `website/`.
It **deploys only** when all of these are true — nothing deploys automatically:

1. This branch has been reviewed and merged into `main` (workflows can only be run by hand from the default branch).
2. Repository **Settings → Pages → Build and deployment → Source: GitHub Actions**.
3. Repository **Settings → Secrets and variables → Actions → Variables**: `WEBSITE_DEPLOY_APPROVED` = `true`.
4. **Actions → Website → Run workflow** on `main` with **deploy** ticked.

To take the site down: Settings → Pages → Unpublish (and set the variable to `false`).

Recommended before deploying: Settings → Environments → `github-pages` → required reviewer = owner.

## Testing

```bash
python3 website/build.py --check          # what CI runs: up to date, config, links, a11y basics, no trackers
# browser tests (Playwright and axe-core installed in the environment):
node website/tests/browser-check.js website/public /tmp/site-test --label unconfigured --browsers chromium,firefox,webkit
```

`build.py --check` fails on: stale `public/`, invalid configuration, broken internal links or anchors,
root-absolute links (break under `/SnapForge/`), external images/CSS/scripts, iframes, trackers or
cookie/storage code, missing `lang`/title/description, not exactly one `<h1>`, skipped heading levels, images
without `alt`, buttons without `type`, links without text, duplicate ids, and declared colour pairs below WCAG AA
contrast. It prints every external link.

`browser-check.js` serves the site at `/SnapForge/` and, at 375 px (phone, reduced motion), 768 px (tablet) and
1280 px (desktop), checks each page: HTTP 200, no console errors or CSP violations, no request leaving the site,
every image decoded, no horizontal scrolling, **axe-core WCAG 2.0/2.1/2.2 A and AA rules + best practices**,
cookies unset; plus skip link and focus order, visible focus on 25 tabbed elements, mobile menu by keyboard and
Escape, the nested-URL 404 page, and navigation without JavaScript.

### Results (27 November 2026, this branch)

| Check | Environment | Result |
|---|---|---|
| `build.py --check` | Linux, Python 3 | **Pass** — 11 pages, 4 external links (GitHub repo, Issues, Security, GitHub privacy statement) |
| Configuration validation | Invalid test configs: Actions-artifact URL, unverified donation link, bad SHA-256/date | **Rejected** with clear messages (exit 2) |
| Browser checks, unconfigured (live state) | Chromium (Playwright), 3 viewports × 11 pages + keyboard/menu/404/no-JS | **Pass, 0 failures, 0 axe violations** — `test-evidence/unconfigured-results.json` |
| Browser checks, configured | Same, built with a **test-only** config (fake v0.0.0-test release, fake sponsor/PayPal links; never committed) | **Pass, 0 failures** — `test-evidence/configured-results.json` |
| Firefox, WebKit (Safari engine) | — | **Not run**: browsers not installed in the build environment. Run the command above with `--browsers firefox,webkit` where they are installed |
| Real devices, screen readers (NVDA, Narrator, VoiceOver), Windows High Contrast | — | **Not tested** |
| Lighthouse | — | **Not run**. Page weight (HTML + CSS + JS + images): Home 72 KB, Features 261 KB (lazy-loaded screenshots), other pages 24–32 KB; no web fonts, no blocking third-party requests |
| Deployed site, GitHub Pages headers, link previews (Open Graph) | — | **Not tested** (not deployed) |
| External links | — | Not fetched by the tests; `…/security` private reporting and Issues being enabled are **unverified** |

Screenshots in `test-evidence/` are real Chromium renders of this site (phone/tablet/desktop, menu open, no-JS,
configured and unconfigured Download and Donate pages).

## Content sources and claims

Every feature claim was checked against the repository (README, Milestone 3 and V1 handoffs, SECURITY.md,
COMPATIBILITY.md, VIDEO-ACCEPTANCE.md) on 27 November 2026. Status labels: *Development build* (Milestone 3 —
built, CI-tested, awaiting review), *Experimental* (screen recording — separate branch, not approved), *Planned*
(V2/V3 — not authorised). Nothing is labelled *Available* because nothing is released. Product screenshots are the
real CI screenshots from `docs/screenshots/v1` (development build, fictional demo data), captioned as such; the
recording toolbar image is a documentation capture shown without its capture exclusion, and is captioned so.

Claims deliberately **not** made: encryption, guaranteed privacy, video redaction, compatibility with specific
drop targets (Explorer/Office/Teams/browsers — pending manual tests), Windows 10 support, hardware-encoder or 4K
recording, a release date.

## For owner review (flags)

1. **Logo differs from the app icon.** The site uses the supplied proposed logo (orange corner brackets around an
   aperture, `public/assets/img/logo-mark.svg`); the app currently uses an orange rounded square with white corner
   brackets and a dot (`design-references/current-app-icon.png`). Approve one, or ask for the app icon to be updated.
2. **Name.** The site uses "SnapForge" as the official name (per the brief); the repository README still calls it a
   working name and documents naming conflicts (docs/RESEARCH.md §4, docs/NAMING.md). Resolve before launch.
3. **`main` is empty.** The repository's default branch currently holds only an initial commit; the app code is on
   unmerged review branches. "View source on GitHub" therefore shows an empty repository until Milestone 3 is
   merged. Deploy the site after that merge. (The license text is embedded on the About page for this reason.)
4. **H.264/AAC distribution licensing** is stated as "to be confirmed before screen recording is released"
   (from VIDEO-ARCHITECTURE.md §4).
5. **Security reporting:** enable GitHub private vulnerability reporting (Settings → Code security) or provide a
   contact; the Support page links to the Security tab. **Issues** must be enabled for bug reports.
6. **Supported Windows versions** follow docs/COMPATIBILITY.md (Windows 11 24H2+ x64 target; Windows 10 not
   supported/tested), which is stricter than the repository README's "Windows 10 and 11".
7. Donation accounts, release, signing status and the "Last updated" date on the Privacy page need owner input
   at launch.

## Known limitations

* Only Chromium was tested; Firefox/Safari, real phones and screen readers are untested.
* The Content-Security-Policy is set with a `<meta>` tag (GitHub Pages cannot set headers), so `frame-ancestors`
  and other header-only protections are not available.
* GitHub Pages logs visitor IP addresses as the host; this is disclosed on the Privacy page.
* The 404 page uses absolute `/SnapForge/` paths (it is served at any depth); with a custom domain at the root,
  change `siteUrl` in `config.json` and rebuild.
* The User Guide describes the current development builds and must be re-checked against the released build.
