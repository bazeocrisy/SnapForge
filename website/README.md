# SnapForge website

The SnapForge project website, for free GitHub Pages hosting at **https://bazeocrisy.github.io/SnapForge/**.
**Status: built and ready on the `gh-pages` branch; GitHub Pages not yet switched on; no software release.** Launch steps are in
[LAUNCH-CHECKLIST.md](LAUNCH-CHECKLIST.md); branding and name decisions in [BRANDING.md](BRANDING.md); the review
handoff in [HANDOFF.md](HANDOFF.md).

Plain HTML and CSS, one small local script (mobile menu, "copy checksum"), no frameworks, no third-party resources,
no analytics, no cookies, no storage. Every link is relative, so the site works under `/SnapForge/`, on a custom
domain, or from any local web server.

## Source files and generated files

| Path | Kind | What it is |
|---|---|---|
| `src/layout.html` | **source** | Shared page shell: header, navigation, footer, meta tags, Content-Security-Policy |
| `src/pages/*.html` | **source** | Page content with `{{tokens}}` filled by the build (Home, Features, Download, User Guide, What's New, Roadmap, Support/FAQ, Donate, Privacy, About/License, 404) |
| `static/**` | **source** | Copied verbatim into the site: `.nojekyll`, `assets/css/site.css`, `assets/js/site.js`, `assets/icons/*.svg`, `assets/img/` (logo, Open Graph image), `assets/screenshots/*.jpg`, `favicon.ico` |
| `static/assets/img/og-image.png` | **source** (rendered once) | Made by `node tools/make-og-image.js` from `tools/og-image.html`; re-render after changing the logo or slogan |
| `config.json` | **source** | Release, donation and security-contact settings — see below |
| `release-verification.json` | **source** (tool output, reviewed) | Written only by `tools/verify-release.py` after it has checked the real GitHub Release. Absent until a release exists |
| `public/**` | **generated — do not edit** | The site that is copied to the `gh-pages` branch. `build.py` deletes it and rebuilds it entirely: `static/**` copied, every `*.html`, `sitemap.xml` and `robots.txt` generated |
| `build.py` | tool | Build + validation (Python 3 standard library only) |
| `tools/verify-release.py` | tool | Release-publication verifier (read-only) |
| `tools/publish-gh-pages.sh` | tool | Builds, checks and commits `public/` to the `gh-pages` branch |
| `tests/test_website.py` | tests | Unit tests: build gates, stale-output detection, verifier against a fake GitHub |
| `tests/browser-check.js`, `tests/package*.json` | tests | Browser tests (Playwright + axe-core, pinned by `package-lock.json`); not needed to build or deploy |
| `test-evidence/` | evidence | Screenshots and JSON from the last local browser run |
| `design-references/` | reference | Supplied concept images (not published, not real UI) and the current app icon |

`public/` is committed so reviewers can read the exact HTML that goes to `gh-pages` and reviewers can read the exact HTML. **CI detects
stale or hand-edited output:** `python3 website/build.py --check` builds into a temporary folder and fails if
`public/` differs in any byte, is missing a file or has an extra one (tested in `tests/test_website.py`).
After changing anything under `src/`, `static/` or `config.json`, run `python3 website/build.py` and commit
`public/` together with the change.

## Preview locally

```bash
python3 website/build.py                                   # after any change to src/, static/ or config.json
python3 -m http.server 8000 --directory website/public     # then open http://localhost:8000/
```

## Release download

The Download button is **disabled** and the Download page says "No public release yet" unless **all** of these
hold — `release.ownerApproved: true` on its own is refused by the build:

1. The owner approved the software release, and it is published as a **GitHub Release** of `bazeocrisy/SnapForge`
   (not a draft or pre-release). Actions artifacts are rejected: they expire and need a GitHub sign-in.
2. `config.json → release` is filled in from that release.
3. `tools/verify-release.py` (run on your PC; on Windows if the release is signed) has
   checked the actual release: exact asset name, the downloadable bytes, file size, SHA-256, the package contents
   and the signing claim — and written `release-verification.json`, which must match `config.json` field for field.
4. Private vulnerability reporting is enabled and verified (`config.json → security.privateReporting`).

The website lists package contents (license, notices, checksum list) **only from the verification record**; until
then it says they will be listed once verified. Nothing in this repository creates or publishes a release.
Procedure: [LAUNCH-CHECKLIST.md §B](LAUNCH-CHECKLIST.md#b-before-the-site-may-offer-a-download).

## Security reporting

`config.json → security.privateReporting` stays `{ "url": null, "verified": false }` until the owner has enabled
GitHub private vulnerability reporting and checked it works ([LAUNCH-CHECKLIST.md §C](LAUNCH-CHECKLIST.md#c-private-vulnerability-reporting-required-before-launch)).
Until then the Support page says private reporting is not set up, asks people not to disclose details publicly
and to request a private channel without technical details. A release cannot be shown before it is verified.

## Donations (`config.json → donations`)

Each provider is **hidden** until it has a URL **and** `verified: true`. Accepted URL forms:

| Provider | Set up (by the owner) | URL form |
|---|---|---|
| GitHub Sponsors | github.com/sponsors → join; profile must be approved by GitHub | `https://github.com/sponsors/<username>` |
| Buy Me a Coffee | buymeacoffee.com → create page | `https://www.buymeacoffee.com/<name>` |
| PayPal | paypal.com → Donate button (hosted) or paypal.me link | `https://www.paypal.com/donate/?hosted_button_id=<ID>` or `https://paypal.me/<name>` |

Plain external links only — no embedded buttons, scripts or trackers; no tax-deductibility claims.

## Publishing (GitHub Pages from the `gh-pages` branch)

No GitHub Actions, variables or manual workflow runs are involved. GitHub Pages serves the `gh-pages` branch as-is.

**One-time setup (owner):** repository **Settings → Pages → Build and deployment → Source: Deploy from a branch →
Branch: `gh-pages`, folder `/ (root)` → Save.** The site appears at https://bazeocrisy.github.io/SnapForge/ within a
few minutes. To take it offline: Settings → Pages → **Unpublish site** (or set the source back to *None*).

**`gh-pages` contains only the built site:** the contents of `website/public/` at its root, including
`.nojekyll` (so GitHub serves the files directly instead of running Jekyll). All website sources stay on the
development branch (`claude/website`); `gh-pages` is never edited by hand.

**Updating the site later:** change the sources on the development branch, commit, then

```bash
bash website/tools/publish-gh-pages.sh     # rebuild, run build check + unit tests, commit public/ to gh-pages
git push origin gh-pages                   # the live site updates a minute or two later
```

The script refuses to publish if `website/` has uncommitted changes or if any check fails. Each `gh-pages`
commit says which source commit it was built from.

**Automatic checks:** `.github/workflows/website.yml` runs by itself on every push or pull request that touches
`website/` (build check, unit tests, browser tests in Chromium, Firefox and WebKit). It never deploys and needs no
action; a red result just means "don't publish this yet".

## Testing

```bash
python3 -B website/build.py --check                         # generated output up to date + site validation
python3 -B -m unittest discover -s website/tests -v         # 20 unit tests
cd website/tests && npm ci && npx playwright install chromium firefox webkit
node browser-check.js ../public ../test-output --label local --browsers chromium,firefox,webkit
```

`build.py --check` fails on: `public/` not matching a fresh build, invalid configuration (including a release
without a matching verification record or without verified private reporting), broken internal links or anchors,
root-absolute links, external images/CSS/scripts, iframes, trackers or cookie/storage code, missing
`lang`/title/description, not exactly one `<h1>`, skipped heading levels, images without `alt`, buttons without
`type`, links without text, duplicate ids, and declared colour pairs below WCAG AA contrast.

`browser-check.js` serves the site at `/SnapForge/` and, at 375 px (phone, reduced motion), 768 px and 1280 px,
checks each page in each engine: HTTP 200, no console errors or CSP violations, no request leaving the site, every
image decoded, no horizontal scrolling, **axe-core WCAG 2.0/2.1/2.2 A/AA + best-practice rules**, no cookies; plus
skip link, visible focus on 25 tabbed elements, mobile menu by keyboard and Escape, a nested-URL 404 page and
navigation without JavaScript. An engine that is not installed is reported as **NOT TESTED** (never as passed);
`--require-all` (used in CI) turns that into a failure.

Manual keyboard and screen-reader procedures: [MANUAL-ACCEPTANCE.md](MANUAL-ACCEPTANCE.md). Results of the
automated runs: [HANDOFF.md](HANDOFF.md#test-evidence).

## Known limitations

* The Content-Security-Policy is a `<meta>` tag (GitHub Pages cannot set headers), so `frame-ancestors` and other
  header-only protections are unavailable.
* GitHub Pages logs visitor IP addresses as the host; this is disclosed on the Privacy page.
* The 404 page uses absolute `/SnapForge/` paths (it is served at any depth); with a custom domain at the root,
  change `siteUrl` in `config.json` and rebuild.
* The User Guide describes the current development builds and must be re-checked against the released build.
* Signed-release claims can only be validated on Windows (run `tools/verify-release.py` there).
