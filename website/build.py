#!/usr/bin/env python3
"""Build and validate the SnapForge website (static HTML, Python standard library only).

  python3 website/build.py            build website/src -> website/public
  python3 website/build.py --check    build to a temporary folder, fail if website/public is out of date,
                                      then validate (links, accessibility basics, no trackers, config)
  python3 website/build.py --config other.json --out DIR   build with another configuration (tests)

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
PUBLIC = ROOT / "public"
GENERATED_SUFFIXES = (".html", ".xml", ".txt")  # files the build writes; everything else in public/ is a static asset
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

RELEASE_URL = re.compile(r"^https://github\.com/bazeocrisy/SnapForge/releases/download/[A-Za-z0-9._-]+/[A-Za-z0-9._-]+\.(zip|exe|msi|msix)$")
NOTES_URL = re.compile(r"^https://github\.com/bazeocrisy/SnapForge/releases/tag/[A-Za-z0-9._-]+$")
DONATION_RULES = {
    "githubSponsors": ("GitHub Sponsors", re.compile(r"^https://github\.com/sponsors/[A-Za-z0-9-]{1,39}$")),
    "buyMeACoffee": ("Buy Me a Coffee", re.compile(r"^https://(www\.)?buymeacoffee\.com/[A-Za-z0-9_]{1,64}/?$")),
    "paypal": ("PayPal", re.compile(r"^https://(www\.paypal\.com/donate/\?hosted_button_id=[A-Z0-9]{6,20}|(www\.)?paypal\.me/[A-Za-z0-9]{1,40}/?)$")),
}


class ConfigError(Exception):
    pass


def load_config(path: Path) -> dict:
    cfg = json.loads(path.read_text(encoding="utf-8"))
    errors: list[str] = []
    rel = cfg.get("release") or {}
    if rel.get("ownerApproved"):
        need = ["version", "date", "downloadUrl", "fileName", "sizeBytes", "sha256", "releaseNotesUrl"]
        for k in need:
            if rel.get(k) in (None, ""):
                errors.append(f"release.{k} is required when release.ownerApproved is true")
        if rel.get("version") and not re.match(r"^\d+\.\d+\.\d+([-+][0-9A-Za-z.-]+)?$", rel["version"]):
            errors.append("release.version must look like 1.2.3")
        if rel.get("date"):
            try:
                _dt.date.fromisoformat(rel["date"])
            except ValueError:
                errors.append("release.date must be YYYY-MM-DD")
        if rel.get("downloadUrl") and not RELEASE_URL.match(rel["downloadUrl"]):
            errors.append("release.downloadUrl must be a GitHub Release asset of bazeocrisy/SnapForge "
                          "(https://github.com/bazeocrisy/SnapForge/releases/download/<tag>/<file>); "
                          "Actions artifacts expire and are not allowed")
        if rel.get("downloadUrl") and rel.get("fileName") and not rel["downloadUrl"].endswith("/" + rel["fileName"]):
            errors.append("release.fileName must match the last part of release.downloadUrl")
        if rel.get("releaseNotesUrl") and not NOTES_URL.match(rel["releaseNotesUrl"]):
            errors.append("release.releaseNotesUrl must be https://github.com/bazeocrisy/SnapForge/releases/tag/<tag>")
        if rel.get("sha256") and not re.fullmatch(r"[0-9a-f]{64}", rel["sha256"]):
            errors.append("release.sha256 must be 64 lower-case hex characters")
        if rel.get("sizeBytes") is not None and (not isinstance(rel["sizeBytes"], int) or rel["sizeBytes"] <= 0):
            errors.append("release.sizeBytes must be a positive whole number")
        if rel.get("signed") and not rel.get("signer"):
            errors.append("release.signer is required when release.signed is true")
        if rel.get("packageType") not in ("portable", "installer"):
            errors.append("release.packageType must be 'portable' or 'installer'")
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


def download_cta(cfg: dict, large: bool = True) -> str:
    size = " btn-lg" if large else ""
    if release_live(cfg):
        r = cfg["release"]
        return (f'<a class="btn btn-primary{size}" href="{esc(r["downloadUrl"])}" rel="noopener noreferrer">'
                f'<img src="assets/icons/download.svg" alt="" width="20" height="20"> Download for Windows</a>'
                f'<p class="cta-note">Version {esc(r["version"])} · Windows 11 x64 · {human_size(r["sizeBytes"])} · '
                f'free</p>')
    return (f'<button class="btn btn-primary{size}" type="button" disabled aria-describedby="dl-status-note">'
            f'<img src="assets/icons/download.svg" alt="" width="20" height="20"> Download for Windows</button>'
            f'<p class="cta-note" id="dl-status-note">Not released yet — SnapForge is in development. '
            f'<a href="download.html">See download status</a>.</p>')


def release_panel(cfg: dict) -> str:
    if not release_live(cfg):
        return """
<section class="status-card" aria-labelledby="release-status">
  <span class="badge badge-dev">In development</span>
  <h2 id="release-status">No public release yet</h2>
  <p>SnapForge has not had an approved public release. Development builds are reviewed and tested before a
  release is published, and the download button on this site stays disabled until the owner approves a
  GitHub Release.</p>
  <p>The download will appear here, with its version, date, size, SHA-256 checksum and signing status, as soon
  as a release is published. Builds produced by automated tests are not offered here: they are temporary,
  unreviewed and expire.</p>
  <p><a class="btn btn-secondary" href="https://github.com/bazeocrisy/SnapForge" rel="noopener noreferrer">Follow the project on GitHub<span class="visually-hidden"> (external site)</span></a></p>
</section>"""
    r = cfg["release"]
    signed = (f'Signed by {esc(r["signer"])}' if r.get("signed")
              else "Not code-signed — Windows SmartScreen may show a warning (see below)")
    kind = "Portable ZIP (no installer)" if r.get("packageType") == "portable" else "Installer"
    d = _dt.date.fromisoformat(r["date"])
    date = f"{d.day} {d:%B %Y}"
    return f"""
<section class="status-card status-live" aria-labelledby="release-status">
  <span class="badge badge-live">Released</span>
  <h2 id="release-status">SnapForge {esc(r["version"])}</h2>
  {download_cta(cfg)}
  <dl class="facts">
    <div><dt>Version</dt><dd>{esc(r["version"])}</dd></div>
    <div><dt>Released</dt><dd><time datetime="{esc(r["date"])}">{esc(date)}</time></dd></div>
    <div><dt>File</dt><dd><code>{esc(r["fileName"])}</code> · {human_size(r["sizeBytes"])}</dd></div>
    <div><dt>Package</dt><dd>{kind}</dd></div>
    <div><dt>Signing</dt><dd>{signed}</dd></div>
    <div class="wide"><dt>SHA-256</dt><dd><code class="hash" id="sha256">{esc(r["sha256"])}</code>
      <button class="btn btn-small copy" type="button" data-copy="sha256">Copy</button></dd></div>
  </dl>
  <p>{ext_link(r["releaseNotesUrl"], "Release notes for " + esc(r["version"]))}</p>
</section>"""


def verify_command(cfg: dict) -> str:
    name = cfg["release"]["fileName"] if release_live(cfg) else "SnapForge-<version>-win-x64.zip"
    return esc(f'Get-FileHash .\\{name} -Algorithm SHA256')


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
        "codeRepository": REPO,
        "url": cfg["siteUrl"],
        "offers": {"@type": "Offer", "price": "0", "priceCurrency": "USD"},
    }
    if release_live(cfg):
        r = cfg["release"]
        data.update({"softwareVersion": r["version"], "downloadUrl": r["downloadUrl"], "datePublished": r["date"],
                     "fileSize": human_size(r["sizeBytes"])})
    return '<script type="application/ld+json">' + json.dumps(data, ensure_ascii=False) + "</script>"


# ----------------------------------------------------------------------------------------------- build

META_RX = re.compile(r"^<!--meta\s*(\{.*?\})\s*-->\s*", re.S)


def nav_html(current: str) -> str:
    items = []
    for href, label in NAV:
        cur = ' aria-current="page"' if href == current else ""
        items.append(f'<li><a href="{href}"{cur}>{label}</a></li>')
    return "\n".join(items)


def footer_html(current: str) -> str:
    items = []
    for href, label in FOOTER:
        cur = ' aria-current="page"' if href == current else ""
        items.append(f'<li><a href="{href}"{cur}>{label}</a></li>')
    items.append(f'<li>{ext_link(REPO, "Source code on GitHub")}</li>')
    return "\n".join(items)


def build(cfg: dict, out: Path) -> list[str]:
    layout = (SRC / "layout.html").read_text(encoding="utf-8")
    if out.exists():
        for p in out.rglob("*"):
            if p.is_file() and p.suffix in GENERATED_SUFFIXES and "assets" not in p.relative_to(out).parts:
                p.unlink()
    else:
        out.mkdir(parents=True)
    if out.resolve() != PUBLIC.resolve():
        # copy static assets so the output is a complete site
        for p in PUBLIC.rglob("*"):
            if p.is_file() and not (p.suffix in GENERATED_SUFFIXES and "assets" not in p.relative_to(PUBLIC).parts):
                dest = out / p.relative_to(PUBLIC)
                dest.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(p, dest)
    tokens = {
        "{{download_cta}}": download_cta(cfg),
        "{{download_cta_small}}": download_cta(cfg, large=False),
        "{{release_panel}}": release_panel(cfg),
        "{{verify_command}}": verify_command(cfg),
        "{{donate_block}}": donate_block(cfg),
        "{{donate_teaser}}": donate_teaser(cfg),
        "{{repo}}": REPO,
        "{{status_line}}": ("Version " + esc(cfg["release"]["version"]) + " is available.") if release_live(cfg)
        else "In development — no public release yet.",
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
            "{{footer_nav}}": footer_html(name),
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

    def handle_data(self, data):
        if self._in_a is not None:
            self._a_text += data

    def handle_startendtag(self, tag, attrs):
        self.handle_starttag(tag, attrs)
        if tag == "img" and self._in_a is not None:
            alt = dict(attrs).get("alt", "")
            self._a_text += alt

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


def validate(out: Path) -> list[str]:
    problems: list[str] = []
    external: set[str] = set()
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
    ap.add_argument("--out")
    args = ap.parse_args()
    try:
        cfg = load_config(Path(args.config))
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
                print("website/public is out of date — run python3 website/build.py\n  " + "\n  ".join(diffs[:20]),
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
