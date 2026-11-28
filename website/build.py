#!/usr/bin/env python3
"""Build and validate the SnapForge website (static HTML, Python standard library only).

  python3 website/build.py            regenerate website/public from website/src, website/static and config.json
  python3 website/build.py --check    build into a temporary folder and fail if website/public differs from it in any
                                      way (stale, edited by hand, missing or extra files), then validate the site
  python3 website/build.py --config other.json --out DIR   build with another configuration (tests)

Source of truth:  src/layout.html, src/pages/*.html (page content), static/** (CSS, JS, images, icons, favicon),
                  config.json (release, donations, security contact), release-verification.json (written only by
                  tools/verify-release.py after it has checked the real GitHub Release).
Generated:        everything in public/ — never edit it by hand; it is deleted and rebuilt on every build.

The site is plain HTML/CSS with one small script; every link is relative, so it works under
https://bazeocrisy.github.io/SnapForge/ and when opened from a local web server.
"""
from __future__ import annotations

import argparse
import datetime as _dt
import filecmp
import html
import json
import os
import re
import shutil
import sys
import tempfile
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import urlparse

ROOT = Path(__file__).resolve().parent
SRC = ROOT / "src"
STATIC = ROOT / "static"
PUBLIC = ROOT / "public"
VERIFICATION = ROOT / "release-verification.json"
REPO = "https://github.com/bazeocrisy/SnapForge"
SITE_BASE = "https://bazeocrisy.github.io/SnapForge/"  # updated from config in main()

NAV = [
    ("index.html", "Home"),
    ("features.html", "Features"),
    ("download.html", "Download"),
    ("guide.html", "User Guide"),
    ("whats-new.html", "What's New"),
    ("roadmap.html", "Roadmap"),
    ("support.html", "Support"),
    ("donate.html", "Donate"),
]
FOOTER = [
    ("privacy.html", "Privacy"),
    ("about.html", "About & License"),
    ("support.html", "Report a problem"),
]

# ----------------------------------------------------------------------------------------------- configuration

NOTES_URL = re.compile(r"^https://github\.com/bazeocrisy/SnapForge/releases/tag/[A-Za-z0-9._-]+$")
PRIVATE_REPORTING_URL = "https://github.com/bazeocrisy/SnapForge/security/advisories/new"
DOWNLOAD_PREFIX = "https://github.com/bazeocrisy/SnapForge/releases/download/"
RUN_URL = re.compile(r"^https://github\.com/bazeocrisy/SnapForge/actions/runs/\d+$")
# The CI package (artifact) of exactly that run: https://github.com/bazeocrisy/SnapForge/actions/runs/<run>/artifacts/<id>
ARTIFACT_URL = re.compile(r"^https://github\.com/bazeocrisy/SnapForge/actions/runs/(\d+)/artifacts/\d+$")
SOURCE_URL = re.compile(r"^https://github\.com/bazeocrisy/SnapForge(/tree/[A-Za-z0-9._/-]+)?$")
VERSION_RX = re.compile(r"^\d+\.\d+\.\d+([-+][0-9A-Za-z.-]+)?$")
# Fields that must be identical in config.json and the verification record — kept equal to
# tools/verify-release.py RELEASE_FIELDS / ASSET_FIELDS (a unit test checks this).
RELEASE_FIELDS = ("version", "tag", "date", "releaseNotesUrl", "publisher")
ASSET_FIELDS = ("kind", "fileName", "downloadUrl", "assetId", "sizeBytes", "sha256", "signed")
# FA-011: the two package kinds of one release, same version, each with its own asset record.
ASSET_NAMES = {"installer": "SnapForge-{v}-win-x64-setup.exe", "portable": "SnapForge-{v}-win-x64-portable.zip"}
ASSET_LABELS = {"installer": "Installer", "portable": "Portable ZIP"}
DONATION_RULES = {
    "githubSponsors": ("GitHub Sponsors", re.compile(r"^https://github\.com/sponsors/[A-Za-z0-9-]{1,39}$")),
    "buyMeACoffee": ("Buy Me a Coffee", re.compile(r"^https://(www\.)?buymeacoffee\.com/[A-Za-z0-9_]{1,64}/?$")),
    "paypal": ("PayPal", re.compile(r"^https://(www\.paypal\.com/donate/\?hosted_button_id=[A-Z0-9]{6,20}|(www\.)?paypal\.me/[A-Za-z0-9]{1,40}/?)$")),
}


class ConfigError(Exception):
    pass


def check_release(rel: dict, errors: list[str]) -> None:
    """Shape of an owner-approved release (FA-011): distinct installer/portable assets of the same version."""
    for k in ("version", "tag", "date", "releaseNotesUrl"):
        if rel.get(k) in (None, ""):
            errors.append(f"release.{k} is required when release.ownerApproved is true")
    if rel.get("version") and not VERSION_RX.match(rel["version"]):
        errors.append("release.version must look like 1.2.3")
    if rel.get("date"):
        try:
            _dt.date.fromisoformat(rel["date"])
        except ValueError:
            errors.append("release.date must be YYYY-MM-DD")
    if rel.get("tag") and rel.get("releaseNotesUrl") and \
            rel["releaseNotesUrl"] != f"https://github.com/bazeocrisy/SnapForge/releases/tag/{rel['tag']}":
        errors.append("release.releaseNotesUrl must be https://github.com/bazeocrisy/SnapForge/releases/tag/<tag>")
    pub = rel.get("publisher") or {}
    assets = rel.get("assets") or []
    kinds = [a.get("kind") for a in assets]
    if not assets:
        errors.append("release.assets must list at least one asset")
    if len(set(kinds)) != len(kinds) or not set(kinds) <= set(ASSET_NAMES):
        errors.append(f"release.assets must have distinct kinds from {sorted(ASSET_NAMES)} (got {kinds})")
        return
    for a in assets:
        kind = a["kind"]
        for k in ("fileName", "downloadUrl", "assetId", "sizeBytes", "sha256"):
            if a.get(k) in (None, ""):
                errors.append(f"release asset {kind}: {k} is required")
        if rel.get("version") and a.get("fileName") and a["fileName"] != ASSET_NAMES[kind].format(v=rel["version"]):
            errors.append(f"release asset {kind}: fileName must be {ASSET_NAMES[kind].format(v=rel['version'])} "
                          "(every asset has the release's version)")
        if a.get("downloadUrl") and a.get("fileName") and rel.get("tag") and \
                a["downloadUrl"] != f"{DOWNLOAD_PREFIX}{rel['tag']}/{a['fileName']}":
            errors.append(f"release asset {kind}: downloadUrl must be a GitHub Release asset "
                          f"{DOWNLOAD_PREFIX}<tag>/<fileName>; Actions artifacts expire and are not allowed")
        if a.get("assetId") is not None and (not isinstance(a["assetId"], int) or a["assetId"] <= 0):
            errors.append(f"release asset {kind}: assetId must be GitHub's numeric asset id")
        if a.get("sha256") and not re.fullmatch(r"[0-9a-f]{64}", a["sha256"]):
            errors.append(f"release asset {kind}: sha256 must be 64 lower-case hex characters")
        if a.get("sizeBytes") is not None and (not isinstance(a["sizeBytes"], int) or a["sizeBytes"] <= 0):
            errors.append(f"release asset {kind}: sizeBytes must be a positive whole number")
        if not isinstance(a.get("signed"), bool):
            errors.append(f"release asset {kind}: signed must be true or false")
        if a.get("signed") and not (pub.get("subject") and pub.get("thumbprints")):
            errors.append(f"release asset {kind} is claimed signed: release.publisher.subject (full certificate "
                          "subject) and release.publisher.thumbprints are required")


def check_record(rel: dict, rec: dict, name: str, errors: list[str]) -> None:
    """The verification record must be for exactly this release and exactly these asset uploads (FA-011/012):
    a record for an older asset id, another digest or size is stale and refused."""
    if rec.get("result") != "verified":
        errors.append(f"{name}: result is not 'verified'")
    for k in RELEASE_FIELDS:
        if (rec.get("release") or {}).get(k) != rel.get(k):
            errors.append(f"{name} does not match config.json for release.{k} "
                          f"({(rec.get('release') or {}).get(k)!r} vs {rel.get(k)!r}) — re-run verification")
    have = {a.get("kind"): a for a in rec.get("assets") or []}
    want = {a.get("kind"): a for a in rel.get("assets") or []}
    for kind in sorted(set(have) - set(want)):
        errors.append(f"{name} has a record for a {kind} asset that config.json does not list — stale record")
    for kind, a in want.items():
        r = have.get(kind)
        if r is None:
            errors.append(f"{name} has no verification record for the {kind} asset — each asset needs its own")
            continue
        for k in ASSET_FIELDS:
            if r.get(k) != a.get(k):
                errors.append(f"{name} does not match config.json for the {kind} asset's {k} "
                              f"({r.get(k)!r} vs {a.get(k)!r}) — stale record, re-run verification")
        if not r.get("signing"):
            errors.append(f"{name}: the {kind} asset has no signing check")
        if kind == "portable" and not r.get("innerChecksumsVerified"):
            errors.append(f"{name}: the portable ZIP's SHA256SUMS.txt was not verified")


def load_config(path: Path, verification_path: Path = VERIFICATION) -> dict:
    cfg = json.loads(path.read_text(encoding="utf-8"))
    errors: list[str] = []
    rel = cfg.get("release") or {}
    sec = ((cfg.get("security") or {}).get("privateReporting") or {})
    if sec.get("url") and sec["url"] != PRIVATE_REPORTING_URL:
        errors.append(f"security.privateReporting.url must be {PRIVATE_REPORTING_URL}")
    if sec.get("verified") and not sec.get("url"):
        errors.append("security.privateReporting.verified needs security.privateReporting.url")
    if not SOURCE_URL.match(cfg.get("sourceUrl") or ""):
        errors.append("sourceUrl must be https://github.com/bazeocrisy/SnapForge or a /tree/<branch> of it")
    tb = cfg.get("testBuild") or {}
    if tb.get("version") and not VERSION_RX.match(tb["version"]):
        errors.append("testBuild.version must look like 1.0.0-rc.2")
    if tb.get("runUrl") and not RUN_URL.match(tb["runUrl"]):
        errors.append("testBuild.runUrl must be a run page https://github.com/bazeocrisy/SnapForge/actions/runs/<id> "
                      "(a link to the CI run, never a file hosted on this site)")
    if tb.get("commit") and not re.fullmatch(r"[0-9a-f]{40}", tb["commit"]):
        errors.append("testBuild.commit must be a full 40-character lower-case commit SHA")
    if tb.get("runUrl") and not (tb.get("commit") and tb.get("version")):
        errors.append("testBuild.runUrl needs testBuild.version and testBuild.commit")
    if tb.get("artifactUrl"):
        # RC4: a direct "Download Windows test build" link — still only a link to the GitHub Actions package of the
        # exact CI run that passed, never a file hosted here, and only with every identifying detail.
        m = ARTIFACT_URL.match(tb["artifactUrl"])
        if not m:
            errors.append("testBuild.artifactUrl must be https://github.com/bazeocrisy/SnapForge/actions/runs/<run>/artifacts/<id>")
        elif not tb.get("runUrl") or not tb["runUrl"].endswith("/runs/" + m.group(1)):
            errors.append("testBuild.artifactUrl must belong to the run in testBuild.runUrl")
        if not isinstance(tb.get("build"), int) or tb["build"] <= 0:
            errors.append("testBuild.build must be the CI run number (a positive integer)")
        if tb.get("fileName") != f"SnapForge-{tb.get('version')}-win-x64-setup.exe":
            errors.append("testBuild.fileName must be SnapForge-<version>-win-x64-setup.exe for testBuild.version")
        if not re.fullmatch(r"[0-9a-f]{64}", tb.get("sha256") or ""):
            errors.append("testBuild.sha256 must be the installer's 64-character lower-case SHA-256")
        if tb.get("signed") is not False:
            errors.append("testBuild.signed must be false: CI test builds are never code-signed")
        if not re.fullmatch(r"\d{4}-\d{2}-\d{2}", tb.get("expires") or ""):
            errors.append("testBuild.expires must be the artifact's expiry date (YYYY-MM-DD)")
    cfg["_verification"] = None
    if rel.get("ownerApproved"):
        # ownerApproved alone is not proof that a safe release exists: require the record written by
        # tools/verify-release.py for exactly these release details and asset uploads, and an operational private
        # security contact.
        if not (sec.get("url") and sec.get("verified")):
            errors.append("a release cannot be shown until private vulnerability reporting is enabled and verified "
                          "(security.privateReporting.url + verified: true) — see LAUNCH-CHECKLIST.md")
        check_release(rel, errors)
        if not verification_path.exists():
            errors.append(f"release.ownerApproved is true but {verification_path.name} is missing: run "
                          "tools/verify-release.py against the published GitHub Release first — see LAUNCH-CHECKLIST.md")
        else:
            rec = json.loads(verification_path.read_text(encoding="utf-8"))
            check_record(rel, rec, verification_path.name, errors)
            cfg["_verification"] = rec
    for key, (label, rx) in DONATION_RULES.items():
        d = (cfg.get("donations") or {}).get(key) or {}
        url = d.get("url")
        if url and not rx.match(url):
            errors.append(f"donations.{key}.url is not a valid {label} link: {url}")
        if url and not d.get("verified"):
            errors.append(f"donations.{key}: set verified to true only after the owner has confirmed the account "
                          "(or remove the url)")
    if errors:
        raise ConfigError("\n".join(errors))
    return cfg


def release_live(cfg: dict) -> bool:
    return bool((cfg.get("release") or {}).get("ownerApproved"))


def donation_links(cfg: dict) -> list[tuple[str, str]]:
    out = []
    for key, (label, _) in DONATION_RULES.items():
        d = (cfg.get("donations") or {}).get(key) or {}
        if d.get("url") and d.get("verified"):
            out.append((label, d["url"]))
    return out


def human_size(n: int) -> str:
    return f"{n / 1_000_000:.1f} MB"


# ----------------------------------------------------------------------------------------------- page fragments

def esc(s) -> str:
    return html.escape(str(s), quote=True)


def ext_link(url: str, text: str, cls: str = "") -> str:
    c = f' class="{cls}"' if cls else ""
    return f'<a{c} href="{esc(url)}" rel="noopener noreferrer">{text}<span class="visually-hidden"> (external site)</span></a>'


def live_assets(cfg: dict) -> list[tuple[dict, dict]]:
    """(config asset, verification record) pairs — only assets with their own matching record (FA-011)."""
    if not release_live(cfg):
        return []
    recs = {a.get("kind"): a for a in (cfg.get("_verification") or {}).get("assets") or []}
    order = {"installer": 0, "portable": 1}
    out = [(a, recs[a["kind"]]) for a in cfg["release"]["assets"] if a.get("kind") in recs]
    return sorted(out, key=lambda p: order[p[0]["kind"]])


def download_cta(cfg: dict, large: bool = True) -> str:
    size = " btn-lg" if large else ""
    if release_live(cfg):
        r = cfg["release"]
        kinds = " and ".join(ASSET_LABELS[a["kind"]].lower() for a, _ in live_assets(cfg))
        return (f'<a class="btn btn-primary{size}" href="download.html#public">'
                f'<img src="assets/icons/download.svg" alt="" width="20" height="20"> Download for Windows</a>'
                f'<p class="cta-note">Version {esc(r["version"])} · Windows 11 x64 · {esc(kinds)} · free</p>')
    return (f'<button class="btn btn-primary{size}" type="button" disabled aria-describedby="dl-status-note">'
            f'<img src="assets/icons/download.svg" alt="" width="20" height="20"> Download for Windows</button>'
            f'<p class="cta-note" id="dl-status-note">No public release yet — {esc(rc_label(cfg))} is in testing. '
            f'<a href="download.html">See download status</a>.</p>' + test_build_cta(cfg))


def rc_label(cfg: dict) -> str:
    """'release candidate 4' for testBuild.version 1.0.0-rc.4."""
    m = re.search(r"-rc\.(\d+)$", (cfg.get("testBuild") or {}).get("version") or "")
    return f"release candidate {m.group(1)}" if m else "a release candidate"


def test_build_cta(cfg: dict) -> str:
    """RC4: the clearly labelled test-build link on the home page (only when a verified CI package is configured)."""
    tb = cfg.get("testBuild") or {}
    if not tb.get("artifactUrl"):
        return ""
    return (f'<p class="cta-test">{ext_link(tb["artifactUrl"], "Download Windows test build", "btn btn-secondary")}</p>'
            f'<p class="cta-note">SnapForge {esc(tb["version"])} · build {esc(tb["build"])} · {esc(tb["fileName"])} · '
            f'<strong>not code-signed</strong> · GitHub sign-in needed · '
            f'<a href="download.html#test-builds">SHA-256 and details</a></p>')


def release_panel(cfg: dict) -> str:
    if not release_live(cfg):
        return """
<section class="status-card" aria-labelledby="public">
  <span class="badge badge-dev">Not available yet</span>
  <h2 id="public">Public download</h2>
  <p>There is <strong>no public release of SnapForge yet</strong>. The download button stays disabled until the
  owner approves a release, it is published as a GitHub Release, and each of its files has been checked
  (size, SHA-256 checksum, contents and signing status).</p>
  <p><button class="btn btn-primary" type="button" disabled aria-describedby="public-note"><img src="assets/icons/download.svg" alt="" width="20" height="20"> Download for Windows</button></p>
  <p class="small" id="public-note">When a release is published, this section will list the installer and the
  portable ZIP separately, each with its own size, checksum and signing status.</p>
</section>"""
    r = cfg["release"]
    rec = cfg["_verification"]
    d = _dt.date.fromisoformat(r["date"])
    date = f"{d.day} {d:%B %Y}"
    cards = []
    for a, v in live_assets(cfg):
        kind = a["kind"]
        signed = ("Signed (verified: exact publisher, trusted chain, timestamp)" if a.get("signed")
                  else "Not code-signed — Windows SmartScreen may show a warning (see below)")
        what = ("Installs for all users into Program Files; asks for administrator approval once"
                if kind == "installer" else "No installation and no administrator rights; extract and run")
        hid = f"sha256-{kind}"
        cards.append(f"""
  <section class="asset-card" aria-labelledby="asset-{kind}">
    <h3 id="asset-{kind}">{ASSET_LABELS[kind]}</h3>
    <p><a class="btn btn-primary" href="{esc(a["downloadUrl"])}" rel="noopener noreferrer"><img src="assets/icons/download.svg" alt="" width="20" height="20"> Download {ASSET_LABELS[kind].lower()}<span class="visually-hidden"> (external site)</span></a></p>
    <dl class="facts">
      <div><dt>File</dt><dd><code>{esc(a["fileName"])}</code> · {human_size(a["sizeBytes"])}</dd></div>
      <div><dt>Package</dt><dd>{what}</dd></div>
      <div><dt>Signing</dt><dd>{signed}</dd></div>
      <div class="wide"><dt>SHA-256</dt><dd><code class="hash" id="{hid}">{esc(a["sha256"])}</code>
        <button class="btn btn-small copy" type="button" data-copy="{hid}">Copy</button></dd></div>
    </dl>
  </section>""")
    checked = esc(rec["checkedAt"][:10])
    return f"""
<section class="status-card status-live" aria-labelledby="public">
  <span class="badge badge-live">Released</span>
  <h2 id="public">Public download: SnapForge {esc(r["version"])}</h2>
  <dl class="facts">
    <div><dt>Version</dt><dd>{esc(r["version"])}</dd></div>
    <div><dt>Released</dt><dd><time datetime="{esc(r["date"])}">{esc(date)}</time></dd></div>
    <div><dt>Verified</dt><dd>Each file's download, size, checksum, contents and signing checked on
      <time datetime="{checked}">{checked}</time></dd></div>
  </dl>
  {"".join(cards)}
  <p>{ext_link(r["releaseNotesUrl"], "Release notes for " + esc(r["version"]))}</p>
</section>"""


def test_build_panel(cfg: dict) -> str:
    """FA-014/024, SF-012, RC4: honest description of the CI test packages; only links to the CI run / its package."""
    tb = cfg.get("testBuild") or {}
    version = esc(tb.get("version") or "the current release candidate")
    if tb.get("artifactUrl"):
        link = f"""
  <p>{ext_link(tb["artifactUrl"], "Download Windows test build", "btn btn-primary")}</p>
  <dl class="facts">
    <div><dt>Version</dt><dd>SnapForge {version}</dd></div>
    <div><dt>Build number</dt><dd>{esc(tb["build"])} (Windows file version 1.0.0.{esc(tb["build"])}; About in SnapForge Capture and SnapForge Editor shows “{version} (build {esc(tb["build"])})”)</dd></div>
    <div><dt>Installer</dt><dd><code>{esc(tb["fileName"])}</code>, inside the downloaded package (.zip) with an SBOM and <code>SHA256SUMS.txt</code></dd></div>
    <div><dt>SHA-256 of the installer</dt><dd><code class="hash">{esc(tb["sha256"])}</code></dd></div>
    <div><dt>Signing</dt><dd><strong>Not code-signed.</strong> Windows SmartScreen shows a blue “Windows protected your PC” window for unsigned programs. Check the SHA-256 first; do not install a file whose checksum differs.</dd></div>
    <div><dt>Built from</dt><dd>commit <code>{esc(tb["commit"])}</code> — {ext_link(tb["runUrl"], "the CI run that built and tested it")}</dd></div>
    <div><dt>Link valid until</dt><dd>{esc(tb["expires"])} (GitHub deletes the package then; this page will point to the next tested build)</dd></div>
  </dl>"""
    elif tb.get("runUrl"):
        link = (f'<p>{ext_link(tb["runUrl"], "Open the CI run for " + version, "btn btn-secondary")}</p>'
                f'<p class="small">Built from commit <code>{esc(tb["commit"])}</code>. The package names on that '
                f'page contain this commit.</p>')
    else:
        link = ('<div class="note" role="note"><p><strong>The link to the CI run for this test build will be added here</strong> '
                'once the maintainer has checked the run. Until then, invited testers receive the link directly.</p></div>')
    badge = esc(rc_label(cfg)[0].upper() + rc_label(cfg)[1:])
    return f"""
<section class="status-card" aria-labelledby="test-builds">
  <span class="badge badge-exp">{badge} (in testing)</span>
  <h2 id="test-builds">Windows test build for invited testers</h2>
  <p>Test packages of <strong>SnapForge {version}</strong> are produced automatically by the project's continuous
  integration (CI) on GitHub. They are <strong>not a release</strong>:</p>
  <ul>
    <li>They are GitHub Actions <em>artifacts</em> of the CI run. Downloading them needs a GitHub sign-in.</li>
    <li><strong>Sign-in is not owner-only access.</strong> The repository is public, so anyone with a free GitHub
    account can download these artifacts. Treat them as public test files.</li>
    <li>They <strong>expire</strong> (normally after 30 days) and are then gone.</li>
    <li>They are <strong>not code-signed</strong> and have not been approved for general use.</li>
    <li>Nothing is hosted on this website: it only links to the CI package.</li>
  </ul>
  {link}
</section>"""


def package_contents(cfg: dict) -> str:
    """What is inside the download — only from the verification record, never assumed."""
    pairs = live_assets(cfg)
    if not pairs:
        return ("<p>The exact contents of each release package (for example the license, third-party notices and a "
                "checksum list) will be listed here once a release has been published and verified. Nothing is "
                "promised until then.</p>")
    checked = cfg["_verification"].get("checkedAt", "")[:10]
    out = []
    for a, v in pairs:
        if a["kind"] == "portable":
            items = "".join(f"<li><code>{esc(f)}</code></li>" for f in v.get("packageContents") or [])
            out.append(f"<p>Verified contents of <code>{esc(a['fileName'])}</code> (checked "
                       f"<time datetime=\"{esc(checked)}\">{esc(checked)}</time>):</p><ul>{items}</ul>")
        else:
            out.append(f"<p><code>{esc(a['fileName'])}</code> installs the same <code>SnapForge.exe</code> as the "
                       "portable ZIP (checked when the release was verified) together with its license and notices.</p>"
                       if len(pairs) > 1 else
                       f"<p><code>{esc(a['fileName'])}</code> is a single installer file (verified "
                       f"<time datetime=\"{esc(checked)}\">{esc(checked)}</time>).</p>")
    return "".join(out)


def sums_sentence(cfg: dict) -> str:
    for a, v in live_assets(cfg):
        if a["kind"] == "portable" and v.get("innerChecksumsVerified"):
            return ("<p>The portable ZIP also contains <code>SHA256SUMS.txt</code> with the checksum of every file "
                    "inside it; it was checked to cover every file exactly once when the release was verified.</p>")
    return ""


def notices_statement(cfg: dict) -> str:
    for a, v in live_assets(cfg):
        if "THIRD-PARTY-NOTICES.md" in (v.get("packageContents") or []):
            return ("The full list is in <code>THIRD-PARTY-NOTICES.md</code>, included in the release package "
                    "(verified) and in the source code.")
    return ("The full list is in <code>THIRD-PARTY-NOTICES.md</code> in the source code. Which notice files are "
            "inside a release package is confirmed when that release is verified.")


def source_link(cfg: dict, text: str, cls: str = "") -> str:
    return ext_link(cfg["sourceUrl"], text, cls)


def source_note(cfg: dict) -> str:
    if cfg.get("sourceIsCandidate"):
        return ("The code is on the <code>release/v1.0-candidate</code> branch, which becomes the main branch after "
                "the owner approves it; the repository's front page (<code>main</code>) shows only a short README "
                "until then. ")
    return ""


def security_block(cfg: dict) -> str:
    sec = ((cfg.get("security") or {}).get("privateReporting") or {})
    if sec.get("url") and sec.get("verified"):
        return f"""<p>Report vulnerabilities <strong>privately</strong> with GitHub's
  {ext_link(sec["url"], "Report a vulnerability form")} (a free GitHub account is needed). Only the maintainers can
  see the report. Please include the SnapForge version and commit (<em>Help → About</em>), your Windows version,
  the steps to reproduce and what an attacker could achieve.</p>
  <ul>
    <li>Do not post details in public issues, discussions or social media before a fix is available.</li>
    <li>Do not include other people's personal data, and do not test against systems you do not own.</li>
    <li>Reports are handled on a best-effort basis by volunteers; there is no bug bounty.</li>
  </ul>"""
    return """<div class="note" role="note"><p><strong>Private reporting is not set up yet.</strong> A private
  reporting channel will be in place before the first public release, and this page will explain how to use it.</p></div>
  <p>Until then, please <strong>do not describe vulnerabilities publicly</strong>. If you have found one, open a
  GitHub issue that only says you have a security report and asks the maintainer for a private channel — with no
  technical details.</p>"""


def verify_command(cfg: dict) -> str:
    pairs = live_assets(cfg)
    names = [a["fileName"] for a, _ in pairs] or ["SnapForge-<version>-win-x64-setup.exe",
                                                  "SnapForge-<version>-win-x64-portable.zip"]
    return "\n".join(esc(f'Get-FileHash .\\{n} -Algorithm SHA256') for n in names)


def donate_block(cfg: dict) -> str:
    links = donation_links(cfg)
    if not links:
        return """<div class="note" role="note"><p><strong>Donation options are not set up yet.</strong> They will appear here once
the owner has created and verified them. Nothing on this site asks for payment details.</p></div>"""
    items = "".join(f'<li>{ext_link(url, "Support on " + esc(label), "btn btn-secondary")}</li>' for label, url in links)
    return f'<ul class="donate-links" role="list">{items}</ul>' \
           '<p class="small">These links open the provider\'s own website; SnapForge never sees your payment details.</p>'


def donate_teaser(cfg: dict) -> str:
    link = '<a class="btn btn-secondary" href="donate.html">Ways to support</a>'
    return link


def jsonld(cfg: dict) -> str:
    data = {
        "@context": "https://schema.org",
        "@type": "SoftwareApplication",
        "name": "SnapForge",
        "description": "Free, open-source screenshot capture, annotation and redaction tool for Windows, with a local "
                       "capture library. In development; no public release yet." if not release_live(cfg) else
                       "Free, open-source screenshot capture, annotation and redaction tool for Windows, with a local "
                       "capture library.",
        "applicationCategory": "MultimediaApplication",
        "operatingSystem": "Windows 11",
        "isAccessibleForFree": True,
        "license": "https://opensource.org/licenses/MIT",
        "codeRepository": cfg["sourceUrl"],
        "url": cfg["siteUrl"],
        "offers": {"@type": "Offer", "price": "0", "priceCurrency": "USD"},
    }
    if release_live(cfg):
        r = cfg["release"]
        data.update({"softwareVersion": r["version"], "downloadUrl": cfg["siteUrl"] + "download.html#public",
                     "datePublished": r["date"]})
    return '<script type="application/ld+json">' + json.dumps(data, ensure_ascii=False) + "</script>"


# ----------------------------------------------------------------------------------------------- build

META_RX = re.compile(r"^<!--meta\s*(\{.*?\})\s*-->\s*", re.S)


def nav_html(current: str) -> str:
    items = []
    for href, label in NAV:
        cur = ' aria-current="page"' if href == current else ""
        items.append(f'<li><a href="{href}"{cur}>{label}</a></li>')
    return "\n".join(items)


def footer_html(current: str, cfg: dict) -> str:
    items = []
    for href, label in FOOTER:
        cur = ' aria-current="page"' if href == current else ""
        items.append(f'<li><a href="{href}"{cur}>{label}</a></li>')
    items.append(f'<li>{source_link(cfg, "Source code on GitHub")}</li>')
    return "\n".join(items)


def build(cfg: dict, out: Path) -> list[str]:
    """Rebuild `out` from scratch: static/ copied verbatim, then pages, sitemap.xml and robots.txt generated."""
    layout = (SRC / "layout.html").read_text(encoding="utf-8")
    if out.exists():
        is_public = out.resolve() == PUBLIC.resolve()
        looks_built = (out / "index.html").exists() and (out / "sitemap.xml").exists()
        if not (is_public or looks_built or not any(out.iterdir())):
            raise SystemExit(f"refusing to delete {out}: not website/public, not empty and not a previous build")
        shutil.rmtree(out)
    shutil.copytree(STATIC, out)
    tokens = {
        "{{download_cta}}": download_cta(cfg),
        "{{download_cta_small}}": download_cta(cfg, large=False),
        "{{release_panel}}": release_panel(cfg),
        "{{verify_command}}": verify_command(cfg),
        "{{package_contents}}": package_contents(cfg),
        "{{sums_sentence}}": sums_sentence(cfg),
        "{{notices_statement}}": notices_statement(cfg),
        "{{security_block}}": security_block(cfg),
        "{{test_build_panel}}": test_build_panel(cfg),
        "{{source_note}}": source_note(cfg),
        "{{source_url}}": esc(cfg["sourceUrl"]),
        "{{donate_block}}": donate_block(cfg),
        "{{donate_teaser}}": donate_teaser(cfg),
        "{{repo}}": REPO,
        "{{status_line}}": ("Version " + esc(cfg["release"]["version"]) + " is available.") if release_live(cfg)
        else f"No public release yet — {rc_label(cfg)} is in testing.",
        "{{rc_label}}": esc(rc_label(cfg)),
    }
    pages = []
    for src in sorted((SRC / "pages").glob("*.html")):
        text = src.read_text(encoding="utf-8")
        m = META_RX.match(text)
        if not m:
            raise SystemExit(f"{src}: missing <!--meta {{...}} --> header")
        meta = json.loads(m.group(1))
        body = text[m.end():]
        for k, v in tokens.items():
            body = body.replace(k, v)
        name = src.name
        page = layout
        canonical = cfg["siteUrl"] + ("" if name == "index.html" else name)
        rep = {
            "{{title}}": esc(meta["title"] if name == "index.html" else f'{meta["title"]} — SnapForge'),
            "{{og_title}}": esc(meta["title"]),
            "{{description}}": esc(meta["description"]),
            "{{canonical}}": esc(canonical),
            "{{site_url}}": esc(cfg["siteUrl"]),
            "{{nav}}": nav_html(name),
            "{{footer_nav}}": footer_html(name, cfg),
            "{{content}}": body,
            "{{jsonld}}": jsonld(cfg) if name == "index.html" else "",
            "{{robots}}": '<meta name="robots" content="noindex">' if meta.get("noindex") else "",
            "{{status_line}}": tokens["{{status_line}}"],
            "{{body_class}}": esc(meta.get("bodyClass", "")),
        }
        for k, v in rep.items():
            page = page.replace(k, v)
        if meta.get("absolutePaths"):
            # 404.html is served for any missing path, at any depth: make its links absolute to the site root
            base = urlparse(cfg["siteUrl"]).path
            page = re.sub(r'(href|src)="(?!https?:|//|#|/)([^"]*)"', lambda m: f'{m.group(1)}="{base}{m.group(2)}"', page)
        if "{{" in page:
            raise SystemExit(f"{src}: unreplaced token {re.search(r'{{[^}]*}}', page).group(0)}")
        (out / name).write_text(page, encoding="utf-8", newline="\n")
        if not meta.get("noindex"):
            pages.append(name)
    sm = ['<?xml version="1.0" encoding="UTF-8"?>', '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">']
    for name in pages:
        sm.append(f"  <url><loc>{esc(cfg['siteUrl'] + ('' if name == 'index.html' else name))}</loc></url>")
    sm.append("</urlset>")
    (out / "sitemap.xml").write_text("\n".join(sm) + "\n", encoding="utf-8", newline="\n")
    (out / "robots.txt").write_text(f"User-agent: *\nAllow: /\n\nSitemap: {cfg['siteUrl']}sitemap.xml\n",
                                    encoding="utf-8", newline="\n")
    return pages


# ----------------------------------------------------------------------------------------------- validation

TRACKERS = re.compile(r"google-analytics|googletagmanager|gtag\(|doubleclick|facebook\.net|connect\.facebook|hotjar|"
                      r"plausible\.io|matomo|piwik|segment\.(io|com)|clarity\.ms|mixpanel|amplitude|fullstory|"
                      r"cloudflareinsights|newrelic|sentry|document\.cookie|localStorage|sessionStorage", re.I)


class PageParser(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.links: list[tuple[str, str]] = []
        self.ids: list[str] = []
        self.h1 = 0
        self.imgs_without_alt = 0
        self.scripts: list[str | None] = []
        self.iframes = 0
        self.html_lang = None
        self.title = 0
        self.description = False
        self.headings: list[int] = []
        self.blank_targets = 0
        self.buttons_without_type = 0
        self.ext_without_rel = []
        self._in_a = None
        self.empty_links = 0
        self._a_text = ""
        self.imgs: list[dict] = []   # every <img> with its width/height and the href of the link around it

    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        if "id" in a:
            self.ids.append(a["id"])
        if tag == "html":
            self.html_lang = a.get("lang")
        if tag == "title":
            self.title += 1
        if tag == "meta" and a.get("name") == "description" and a.get("content"):
            self.description = True
        if tag in ("h1", "h2", "h3", "h4", "h5", "h6"):
            lvl = int(tag[1])
            self.headings.append(lvl)
            if lvl == 1:
                self.h1 += 1
        if tag == "img" and "alt" not in a:
            self.imgs_without_alt += 1
        if tag == "img":
            self.imgs.append({"src": a.get("src", ""), "width": a.get("width"), "height": a.get("height"),
                              "srcset": a.get("srcset"), "link": (self._in_a or {}).get("href")})
        if tag == "script":
            self.scripts.append(a.get("src"))
        if tag == "iframe":
            self.iframes += 1
        if tag == "button" and "type" not in a:
            self.buttons_without_type += 1
        for attr in ("href", "src"):
            if attr in a:
                self.links.append((tag, a[attr]))
        if tag == "link" and "href" in a:
            pass
        if tag == "a":
            if a.get("target") == "_blank":
                self.blank_targets += 1
            href = a.get("href", "")
            if href.startswith("http") and "noopener" not in (a.get("rel") or ""):
                self.ext_without_rel.append(href)
            self._in_a = a
            self._a_text = a.get("aria-label", "")
        if tag == "img" and self._in_a is not None:
            # An image inside a link names the link by its alt text (both <img> and <img/>).
            self._a_text += dict(attrs).get("alt", "") or ""

    def handle_data(self, data):
        if self._in_a is not None:
            self._a_text += data

    def handle_startendtag(self, tag, attrs):
        self.handle_starttag(tag, attrs)  # <img .../>: alt text is counted in handle_starttag

    def handle_endtag(self, tag):
        if tag == "a" and self._in_a is not None:
            if not self._a_text.strip():
                self.empty_links += 1
            self._in_a = None


def contrast(c1: str, c2: str) -> float:
    def lum(h):
        h = h.lstrip("#")
        rgb = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
        lin = [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in rgb]
        return 0.2126 * lin[0] + 0.7152 * lin[1] + 0.0722 * lin[2]
    a, b = sorted((lum(c1), lum(c2)), reverse=True)
    return (a + 0.05) / (b + 0.05)


SCREENSHOTS = "assets/screenshots/"
# SF-011: screenshots are native-size captures from a 1920×1080 test display; anything larger was upscaled.
SCREENSHOT_MAX = (1920, 1080)
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"


def png_size(path: Path) -> tuple[int, int]:
    """(width, height) from the PNG IHDR chunk; raises ValueError for anything that is not a PNG."""
    with open(path, "rb") as f:
        head = f.read(24)
    if len(head) < 24 or head[:8] != PNG_SIGNATURE or head[12:16] != b"IHDR":
        raise ValueError("not a PNG file")
    return int.from_bytes(head[16:20], "big"), int.from_bytes(head[20:24], "big")


def check_screenshots(out: Path) -> list[str]:
    """SF-011: every file in assets/screenshots is a PNG no larger than the capture display (no JPEG, no upscaling)."""
    problems = []
    folder = out / SCREENSHOTS
    for f in sorted(folder.rglob("*")) if folder.exists() else []:
        if not f.is_file():
            continue
        rel = f.relative_to(out).as_posix()
        if f.suffix != ".png":
            problems.append(f"{rel}: screenshots must be lossless PNG files (no JPEG or other formats)")
            continue
        try:
            w, h = png_size(f)
        except ValueError:
            problems.append(f"{rel}: has a .png name but is not a PNG file")
            continue
        if w > SCREENSHOT_MAX[0] or h > SCREENSHOT_MAX[1]:
            problems.append(f"{rel}: {w}×{h} is larger than the {SCREENSHOT_MAX[0]}×{SCREENSHOT_MAX[1]} capture "
                            "display — upscaled screenshots are not allowed")
    return problems


def check_page_images(out: Path, name: str, imgs: list[dict]) -> list[str]:
    """SF-011: each screenshot <img> declares the PNG's real size and links to the full-resolution file itself."""
    problems = []
    for im in imgs:
        src = im["src"]
        if not src.startswith(SCREENSHOTS) and SCREENSHOTS not in src:
            continue
        local = src[src.index(SCREENSHOTS):]
        f = out / local
        if not f.exists():
            continue                                   # reported as a broken link
        try:
            w, h = png_size(f)
        except ValueError:
            problems.append(f"{name}: {src} is not a PNG")
            continue
        if (im["width"], im["height"]) != (str(w), str(h)):
            problems.append(f"{name}: <img src={src}> declares {im['width']}×{im['height']}, the PNG is {w}×{h} "
                            "(width/height must equal the real size)")
        if im.get("srcset"):
            problems.append(f"{name}: {src} must not use srcset (one native-size PNG per screenshot)")
        link = im.get("link") or ""
        target = link[link.index(SCREENSHOTS):] if SCREENSHOTS in link else None
        if target != local:
            problems.append(f"{name}: {src} is not wrapped in a link to its full-resolution file")
    return problems


def validate(out: Path) -> list[str]:
    problems: list[str] = []
    external: set[str] = set()
    if not (out / ".nojekyll").exists():
        problems.append(".nojekyll missing: GitHub Pages would run Jekyll over the site (keep static/.nojekyll)")
    problems += check_screenshots(out)
    for page in sorted(out.glob("*.html")):
        text = page.read_text(encoding="utf-8")
        p = PageParser()
        p.feed(text)
        n = page.name
        if p.html_lang != "en":
            problems.append(f"{n}: <html lang> missing")
        if p.title != 1 or not p.description:
            problems.append(f"{n}: needs exactly one <title> and a meta description")
        if p.h1 != 1:
            problems.append(f"{n}: has {p.h1} <h1> elements (expected 1)")
        for prev, cur in zip(p.headings, p.headings[1:]):
            if cur > prev + 1:
                problems.append(f"{n}: heading level jumps from h{prev} to h{cur}")
                break
        problems += check_page_images(out, n, p.imgs)
        if p.imgs_without_alt:
            problems.append(f"{n}: {p.imgs_without_alt} <img> without alt")
        if p.iframes:
            problems.append(f"{n}: contains an iframe")
        if p.blank_targets:
            problems.append(f"{n}: links opening new windows (target=_blank) are not used on this site")
        if p.buttons_without_type:
            problems.append(f"{n}: <button> without type")
        if p.empty_links:
            problems.append(f"{n}: {p.empty_links} link(s) without accessible text")
        for href in p.ext_without_rel:
            problems.append(f"{n}: external link without rel=noopener: {href}")
        dup = {i for i in p.ids if p.ids.count(i) > 1}
        if dup:
            problems.append(f"{n}: duplicate ids {sorted(dup)}")
        for src in p.scripts:
            if src and (src.startswith("http") or src.startswith("//")):
                problems.append(f"{n}: third-party script {src}")
        scan = re.sub(r'<script type="application/ld\+json">.*?</script>', "", text, flags=re.S)
        if TRACKERS.search(scan):
            problems.append(f"{n}: tracker/cookie/storage pattern found: {TRACKERS.search(scan).group(0)}")
        for tag, url in p.links:
            if url.startswith(("http://", "https://", "//")):
                if url.startswith("http://"):
                    problems.append(f"{n}: insecure link {url}")
                if tag in ("img", "link", "script", "source") and not (tag == "link" and url.startswith(SITE_BASE)):
                    problems.append(f"{n}: external resource {url} (all assets must be local)")
                if not url.startswith(SITE_BASE):
                    external.add(url)
                continue
            if url.startswith(("mailto:", "tel:", "data:")):
                problems.append(f"{n}: {url.split(':')[0]} link not expected")
                continue
            base = urlparse(SITE_BASE).path
            if url.startswith(base) and page.name == "404.html":
                url = url[len(base):]
            elif url.startswith("/"):
                problems.append(f"{n}: root-absolute link {url} breaks under /SnapForge/ — use a relative path")
                continue
            path, _, frag = url.partition("#")
            target = (out / path) if path else page
            if not target.exists():
                problems.append(f"{n}: broken link {url}")
                continue
            if frag and target.suffix == ".html":
                tp = PageParser()
                tp.feed(target.read_text(encoding="utf-8"))
                if frag not in tp.ids:
                    problems.append(f"{n}: link to missing anchor {url}")
    for js in out.rglob("*.js"):
        t = js.read_text(encoding="utf-8")
        if TRACKERS.search(t) or re.search(r"\bfetch\(|XMLHttpRequest|navigator\.sendBeacon|new WebSocket|import\(", t):
            problems.append(f"{js.relative_to(out)}: network, storage or tracker call in script")
    for css in out.rglob("*.css"):
        t = css.read_text(encoding="utf-8")
        if re.search(r"url\(\s*['\"]?(https?:)?//", t) or "@import" in t:
            problems.append(f"{css.relative_to(out)}: external resource in CSS")
        # contrast of the declared colour pairs (documented in the CSS header)
        for fg, bg, minimum, label in re.findall(r"contrast:\s*(#[0-9a-fA-F]{6})\s+on\s+(#[0-9a-fA-F]{6})\s*>=\s*([\d.]+)\s*\(([^)]*)\)", t):
            r = contrast(fg, bg)
            if r < float(minimum):
                problems.append(f"{css.name}: contrast {fg} on {bg} is {r:.2f} < {minimum} ({label})")
    print(f"external links ({len(external)}):")
    for u in sorted(external):
        print("  " + u)
    return problems


def same_tree(a: Path, b: Path) -> list[str]:
    diffs = []
    files_a = {p.relative_to(a) for p in a.rglob("*") if p.is_file()}
    files_b = {p.relative_to(b) for p in b.rglob("*") if p.is_file()}
    for f in sorted(files_a ^ files_b):
        diffs.append(f"only in one tree: {f}")
    for f in sorted(files_a & files_b):
        if not filecmp.cmp(a / f, b / f, shallow=False):
            diffs.append(f"differs: {f}")
    return diffs


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--config", default=str(ROOT / "config.json"))
    ap.add_argument("--verification", default=str(VERIFICATION))
    ap.add_argument("--out")
    args = ap.parse_args()
    try:
        cfg = load_config(Path(args.config), Path(args.verification))
    except ConfigError as e:
        print("configuration error:\n" + str(e), file=sys.stderr)
        return 2
    global SITE_BASE
    SITE_BASE = cfg["siteUrl"]
    if args.check:
        tmp = Path(tempfile.mkdtemp(prefix="snapforge-site-"))
        try:
            build(cfg, tmp / "site")
            diffs = same_tree(tmp / "site", PUBLIC)
            if diffs:
                print("website/public does not match a fresh build (stale, hand-edited, missing or extra files) — "
                      "run python3 website/build.py and commit the result\n  " + "\n  ".join(diffs[:30]),
                      file=sys.stderr)
                return 1
            problems = validate(PUBLIC)
        finally:
            shutil.rmtree(tmp, ignore_errors=True)
        if problems:
            print("validation failed:\n  " + "\n  ".join(problems), file=sys.stderr)
            return 1
        print(f"OK: website/public is up to date and valid ({len(list(PUBLIC.glob('*.html')))} pages)")
        return 0
    out = Path(args.out) if args.out else PUBLIC
    pages = build(cfg, out)
    problems = validate(out)
    if problems:
        print("validation failed:\n  " + "\n  ".join(problems), file=sys.stderr)
        return 1
    print(f"built {len(pages)} indexed pages into {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
