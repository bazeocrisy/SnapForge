#!/usr/bin/env python3
"""Verify a published SnapForge GitHub Release against website/config.json (read-only; standard library only).

    python3 website/tools/verify-release.py                 # verify every asset, then write website/release-verification.json
    python3 website/tools/verify-release.py --dry-run       # verify only, write nothing
    python3 website/tools/verify-release.py --recheck       # is the existing record still current? (no download)

It never creates, edits or publishes a release: it only reads the GitHub API and downloads the assets.
`release.ownerApproved: true` is not proof of anything; the website build refuses to show a release unless the record
written here matches config.json for the release AND for every asset (asset id, size, SHA-256, signing claim).

A release has one or two distinct assets of the SAME version (FA-011), each verified on its own:
    installer  SnapForge-<version>-win-x64-setup.exe      (Inno Setup, all users, Program Files)
    portable   SnapForge-<version>-win-x64-portable.zip   (no installation)

Checks (all must pass, otherwise nothing is written and the exit code is 1):
  1. The release for release.tag exists in bazeocrisy/SnapForge, is published (not a draft), is not a pre-release;
     its page is release.releaseNotesUrl and its publication date (UTC) is release.date. No two release assets have
     names that differ only in letter case.
  2. Per asset, as GitHub describes it: exactly one asset with the exact name; its GitHub asset id is the configured
     assetId (a re-uploaded asset gets a new id, so a record for an older upload is rejected); state "uploaded";
     download URL, size and GitHub's SHA-256 digest equal the configured values (the digest is required).
  3. Per asset, the final bytes actually downloaded: count == sizeBytes and SHA-256 == sha256. Every later check
     reads this downloaded file — never a local build.
  4. Portable ZIP (FA-012): valid archive with safe relative names; no duplicate entry names (also not differing only
     in case); exactly one SHA256SUMS.txt that lists EVERY other file exactly once — no missing, extra or duplicate
     (case-insensitive) names, strict "<64 hex>  <name>" lines — and every hash matches; exactly one SnapForge.exe;
     no other executable or script (by extension or by an MZ header); DLLs only from the expected WPF native set.
  5. Installer: the downloaded file is a PE executable; its embedded application is extracted with `innoextract`
     (must be on PATH) and {app}/SnapForge.exe is checked for the same signing claim and, when the release also has
     a portable ZIP, must be byte-identical to the ZIP's SnapForge.exe.
  6. Signing claim per asset (installer: the setup .exe AND its embedded SnapForge.exe; portable: SnapForge.exe):
     - signed: false -> the file must carry no Authenticode signature at all (PE certificate table empty);
     - signed: true  -> it must carry one AND, on Windows only, PowerShell Get-AuthenticodeSignature must report
       Status "Valid" (WinVerifyTrust: file hash matches and the chain ends at a root trusted by Windows, not
       revoked), SignatureType "Authenticode", a signer certificate whose Subject EQUALS release.publisher.subject
       (exact string, not a substring) and whose SHA-1 thumbprint is listed in release.publisher.thumbprints, the
       Code Signing EKU (1.3.6.1.5.5.7.3.3), and a timestamp countersignature (TimeStamperCertificate present, its
       chain builds). Off Windows a signed claim cannot be validated and FAILS — run the tool on a Windows PC.
       Cross-check by hand if wanted: `signtool verify /pa /all /tw /v <file>` (Windows SDK).

The record pins each asset by GitHub asset id + SHA-256 + size (+ GitHub's updated_at). `--recheck` compares an
existing record with the live release and fails if any asset was replaced, removed or changed.
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
import re
import shutil
import struct
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request
import zipfile
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parent.parent
OWNER_REPO = "bazeocrisy/SnapForge"
DOWNLOAD_PREFIX = f"https://github.com/{OWNER_REPO}/releases/download/"
# Fields that must be identical in config.json and the verification record (the website build compares them).
RELEASE_FIELDS = ("version", "tag", "date", "releaseNotesUrl", "publisher")
ASSET_FIELDS = ("kind", "fileName", "downloadUrl", "assetId", "sizeBytes", "sha256", "signed")
ASSET_NAMES = {"installer": "SnapForge-{v}-win-x64-setup.exe", "portable": "SnapForge-{v}-win-x64-portable.zip"}
APP_EXE = "SnapForge.exe"
SUMS = "SHA256SUMS.txt"
# Native DLLs a self-contained single-file WPF publish leaves next to the exe. Anything else is unexpected.
EXPECTED_DLLS = {"d3dcompiler_47_cor3.dll", "penimc_cor3.dll", "presentationnative_cor3.dll",
                 "vcruntime140_cor3.dll", "wpfgfx_cor3.dll"}
EXECUTABLE_EXT = {".exe", ".com", ".scr", ".bat", ".cmd", ".ps1", ".psm1", ".psd1", ".vbs", ".vbe", ".js", ".jse",
                  ".wsf", ".wsh", ".msi", ".msp", ".msix", ".appx", ".appinstaller", ".lnk", ".url", ".hta", ".cpl",
                  ".jar", ".reg", ".pif", ".sys", ".ocx", ".application", ".appref-ms"}
SUM_LINE = re.compile(r"^([0-9a-fA-F]{64}) [ *]([^\r\n]+)$")
THUMB = re.compile(r"^[0-9A-F]{40}$")
CODE_SIGNING_EKU = "1.3.6.1.5.5.7.3.3"


class Failed(Exception):
    pass


def http_get(url: str, *, api: bool = False, rewrite: tuple[str, str] | None = None):
    if rewrite and url.startswith(rewrite[0]):
        url = rewrite[1] + url[len(rewrite[0]):]
    headers = {"User-Agent": "snapforge-release-verifier"}
    if api:
        headers["Accept"] = "application/vnd.github+json"
        token = os.environ.get("GITHUB_TOKEN")
        if token:
            headers["Authorization"] = f"Bearer {token}"
    return urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=60)


def pe_certificate_table(data: bytes, label: str) -> tuple[int, int]:
    """(offset, size) of the PE Authenticode certificate table; (0, 0) when the file is not signed."""
    if data[:2] != b"MZ":
        raise Failed(f"{label} is not a Windows executable (no MZ header)")
    pe = struct.unpack_from("<I", data, 0x3C)[0]
    if data[pe:pe + 4] != b"PE\0\0":
        raise Failed(f"{label} has no PE header")
    opt = pe + 24
    magic = struct.unpack_from("<H", data, opt)[0]
    dirs = opt + (96 if magic == 0x10B else 112)           # PE32 / PE32+
    return struct.unpack_from("<II", data, dirs + 4 * 8)  # data directory 4 = security (certificate) table


# ----------------------------------------------------------------------------------------------- signatures

PS_AUTHENTICODE = r"""
$ErrorActionPreference = 'Stop'
$s = Get-AuthenticodeSignature -LiteralPath $env:SF_FILE
$c = $s.SignerCertificate
$t = $s.TimeStamperCertificate
$eku = @()
if ($c) { $eku = @($c.EnhancedKeyUsageList | ForEach-Object { $_.ObjectId }) }
$tsChain = $null
if ($t) { $ch = New-Object System.Security.Cryptography.X509Certificates.X509Chain; $tsChain = $ch.Build($t) }
[pscustomobject]@{
  Status = [string]$s.Status; StatusMessage = [string]$s.StatusMessage; SignatureType = [string]$s.SignatureType
  Subject = if ($c) { $c.Subject } else { $null }; Thumbprint = if ($c) { $c.Thumbprint } else { $null }
  Issuer = if ($c) { $c.Issuer } else { $null }; NotAfter = if ($c) { $c.NotAfter.ToString('o') } else { $null }
  Eku = $eku; TimeStamper = if ($t) { $t.Subject } else { $null }; TimeStamperChainOk = $tsChain
} | ConvertTo-Json -Compress
"""


def authenticode_on_windows(path: Path) -> dict:
    """Get-AuthenticodeSignature (WinVerifyTrust) plus signer/timestamp certificate details."""
    out = subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-Command", PS_AUTHENTICODE],
                         capture_output=True, text=True, env={**os.environ, "SF_FILE": str(path)}, check=True).stdout
    return json.loads(out)


def evaluate_signature(result: dict, publisher: dict, label: str) -> None:
    """Exact publisher identity, trusted chain and timestamp — raises Failed on any deviation (FA-012)."""
    if result.get("Status") != "Valid":
        raise Failed(f"{label}: Authenticode status is {result.get('Status')!r} ({result.get('StatusMessage')}), "
                     "not 'Valid' (untrusted chain, revoked, or hash mismatch)")
    if result.get("SignatureType") != "Authenticode":
        raise Failed(f"{label}: signature type is {result.get('SignatureType')!r}, not an embedded Authenticode "
                     "signature")
    if result.get("Subject") != publisher.get("subject"):
        raise Failed(f"{label}: signer subject {result.get('Subject')!r} is not exactly the expected publisher "
                     f"{publisher.get('subject')!r}")
    if (result.get("Thumbprint") or "").upper() not in [t.upper() for t in publisher.get("thumbprints") or []]:
        raise Failed(f"{label}: signer certificate thumbprint {result.get('Thumbprint')!r} is not in "
                     "release.publisher.thumbprints")
    if CODE_SIGNING_EKU not in (result.get("Eku") or []):
        raise Failed(f"{label}: signer certificate lacks the Code Signing EKU")
    if not result.get("TimeStamper"):
        raise Failed(f"{label}: no timestamp countersignature — the signature would stop validating when the "
                     "certificate expires")
    if result.get("TimeStamperChainOk") is not True:
        raise Failed(f"{label}: the timestamp certificate chain does not build to a trusted root")


def check_signing_claim(data: bytes, label: str, claimed: bool, publisher: dict, work: Path, *, platform: str,
                        authenticode) -> dict:
    _, size = pe_certificate_table(data, label)
    info = {"file": label, "claimedSigned": claimed, "certificateTablePresent": size > 0}
    if not claimed:
        if size:
            raise Failed(f"config says {label} is NOT signed, but it carries an Authenticode signature — correct the "
                         "asset's signed claim")
        return info
    if not size:
        raise Failed(f"config claims {label} is signed, but it has no Authenticode signature")
    if platform != "win32":
        raise Failed(f"a 'signed' claim for {label} can only be validated on Windows (Get-AuthenticodeSignature); "
                     "run this tool on a Windows PC")
    p = work / ("sig-" + hashlib.sha256(label.encode()).hexdigest()[:12] + Path(label).suffix)
    p.write_bytes(data)
    result = authenticode(p)
    evaluate_signature(result, publisher, label)
    info.update(windowsStatus=result.get("Status"), signerSubject=result.get("Subject"),
                signerThumbprint=result.get("Thumbprint"), issuer=result.get("Issuer"),
                timestamper=result.get("TimeStamper"), method="PowerShell Get-AuthenticodeSignature + certificate "
                "details (exact subject, thumbprint allow-list, Code Signing EKU, timestamp chain)")
    return info


# ----------------------------------------------------------------------------------------------- packages

def safe_name(name: str) -> bool:
    p = PurePosixPath(name)
    return (bool(name) and "\\" not in name and not name.startswith("/") and ":" not in name
            and ".." not in p.parts and all(part not in ("", ".") for part in p.parts))


def check_zip(path: Path) -> tuple[list[str], bytes]:
    """Portable ZIP rules (FA-012). Returns (sorted file list, SnapForge.exe bytes)."""
    try:
        z = zipfile.ZipFile(path)
    except zipfile.BadZipFile:
        raise Failed("the portable asset is not a valid ZIP file")
    with z:
        return _check_zip(z)


def _check_zip(z: zipfile.ZipFile) -> tuple[list[str], bytes]:
    try:
        bad = z.testzip()
    except zipfile.BadZipFile:
        raise Failed("the portable asset is not a valid ZIP file")
    if bad:
        raise Failed(f"ZIP entry {bad} is corrupt")
    names = [i.filename for i in z.infolist() if not i.is_dir()]
    for n in names:
        if not safe_name(n):
            raise Failed(f"ZIP entry {n!r} has an unsafe path")
    folded: dict[str, str] = {}
    for n in names:
        if n.lower() in folded:
            raise Failed(f"ZIP contains duplicate entries {folded[n.lower()]!r} and {n!r} (names must be unique, "
                         "also ignoring letter case)")
        folded[n.lower()] = n
    sums = [n for n in names if PurePosixPath(n).name == SUMS]
    if len(sums) != 1:
        raise Failed(f"expected exactly one {SUMS} in the ZIP, found {len(sums)}")
    prefix = sums[0][: -len(SUMS)]
    others = {n[len(prefix):] if n.startswith(prefix) else n for n in names if n != sums[0]}
    if any(not n.startswith(prefix) for n in names):
        raise Failed(f"files outside the folder of {SUMS} cannot be covered by it")
    listed: dict[str, str] = {}
    try:
        text = z.read(sums[0]).decode("ascii")
    except UnicodeDecodeError:
        raise Failed(f"{SUMS} is not plain ASCII")
    for lineno, line in enumerate(text.splitlines(), 1):
        if not line.strip():
            continue
        m = SUM_LINE.match(line)
        if not m:
            raise Failed(f"{SUMS} line {lineno} is not '<64 hex>  <name>': {line!r}")
        want, fname = m.group(1).lower(), m.group(2)
        if fname.lower() in {k.lower() for k in listed}:
            raise Failed(f"{SUMS} lists {fname!r} more than once (names compared ignoring letter case)")
        if fname == SUMS:
            raise Failed(f"{SUMS} must not list itself")
        listed[fname] = want
    missing = sorted(others - set(listed))
    extra = sorted(set(listed) - others)
    if missing:
        raise Failed(f"{SUMS} does not cover every file in the ZIP; unlisted: {missing}")
    if extra:
        raise Failed(f"{SUMS} lists files that are not in the ZIP: {extra}")
    for fname, want in listed.items():
        if hashlib.sha256(z.read(prefix + fname)).hexdigest() != want:
            raise Failed(f"{SUMS} checksum mismatch for {fname}")
    exe = None
    for n in names:
        base = PurePosixPath(n).name
        ext = PurePosixPath(base).suffix.lower()
        with z.open(n) as fh:
            head = fh.read(2)
        if base == APP_EXE and n == prefix + APP_EXE:
            exe = z.read(n)
            continue
        if ext in EXECUTABLE_EXT:
            raise Failed(f"unexpected executable or script in the ZIP: {n}")
        if ext == ".dll":
            if base.lower() not in EXPECTED_DLLS:
                raise Failed(f"unexpected DLL in the ZIP: {n} (expected only {sorted(EXPECTED_DLLS)})")
            continue
        if head == b"MZ":
            raise Failed(f"{n} is a Windows executable disguised by its extension")
    if exe is None:
        raise Failed(f"expected {APP_EXE} next to {SUMS} in the ZIP")
    return sorted(names), exe


def extract_installer_app(path: Path, work: Path) -> bytes:
    """{app}/SnapForge.exe from an Inno Setup installer, via innoextract (free, zlib licence)."""
    tool = shutil.which("innoextract")
    if not tool:
        raise Failed("innoextract is not on PATH: it is needed to check the application embedded in the installer "
                     "(https://constexpr.org/innoextract/)")
    out = work / "installer-extract"
    out.mkdir()
    r = subprocess.run([tool, "--silent", "--extract", "--output-dir", str(out), str(path)], capture_output=True,
                       text=True)
    if r.returncode != 0:
        raise Failed(f"innoextract could not read the installer: {r.stderr.strip()[:300]}")
    exe = out / "app" / APP_EXE
    if not exe.is_file():
        raise Failed(f"the installer does not contain {{app}}\\{APP_EXE}")
    return exe.read_bytes()


# ----------------------------------------------------------------------------------------------- verification

def check_config(rel: dict) -> list[dict]:
    for k in ("version", "tag", "date", "releaseNotesUrl"):
        if rel.get(k) in (None, ""):
            raise Failed(f"config.json release.{k} is empty")
    assets = rel.get("assets") or []
    if not assets:
        raise Failed("config.json release.assets is empty")
    kinds = [a.get("kind") for a in assets]
    if len(set(kinds)) != len(kinds) or not set(kinds) <= set(ASSET_NAMES):
        raise Failed(f"release.assets must be distinct kinds from {sorted(ASSET_NAMES)}, got {kinds}")
    pub = rel.get("publisher") or {}
    for a in assets:
        for k in ASSET_FIELDS:
            if a.get(k) in (None, "") and k != "signed":
                raise Failed(f"config.json release asset {a.get('kind')}: {k} is empty")
        want = ASSET_NAMES[a["kind"]].format(v=rel["version"])
        if a["fileName"] != want:
            raise Failed(f"{a['kind']} asset must be named {want!r} (same version for every asset), not "
                         f"{a['fileName']!r}")
        if a["downloadUrl"] != f"{DOWNLOAD_PREFIX}{rel['tag']}/{a['fileName']}":
            raise Failed(f"{a['kind']} downloadUrl must be {DOWNLOAD_PREFIX}{rel['tag']}/{a['fileName']} "
                         "(a GitHub Release asset; Actions artifacts are not accepted)")
        if not isinstance(a["assetId"], int) or a["assetId"] <= 0:
            raise Failed(f"{a['kind']} assetId must be GitHub's numeric release asset id")
        if a.get("signed") and not (pub.get("subject") and pub.get("thumbprints")
                                    and all(THUMB.match(t) for t in pub["thumbprints"])):
            raise Failed("a signed claim needs release.publisher.subject (full certificate subject) and "
                         "release.publisher.thumbprints (40 upper-case hex SHA-1 thumbprints)")
    return assets


def fetch_release(rel: dict, api_base: str) -> dict:
    try:
        with http_get(f"{api_base}/repos/{OWNER_REPO}/releases/tags/{rel['tag']}", api=True) as r:
            return json.load(r)
    except urllib.error.HTTPError as e:
        raise Failed(f"GitHub Release for tag {rel['tag']!r} not found or not public (HTTP {e.code})")


def github_asset(gh: dict, a: dict) -> dict:
    found = [x for x in gh.get("assets", []) if x.get("name") == a["fileName"]]
    if len(found) != 1:
        raise Failed(f"expected exactly one asset named {a['fileName']!r}, found {len(found)} "
                     f"(assets: {[x.get('name') for x in gh.get('assets', [])]})")
    x = found[0]
    if x.get("id") != a["assetId"]:
        raise Failed(f"{a['fileName']}: GitHub asset id is {x.get('id')}, config/record says {a['assetId']} — the "
                     "asset was replaced; the old record is stale. Update config.json and re-verify")
    if x.get("state") != "uploaded":
        raise Failed(f"{a['fileName']}: asset state is {x.get('state')!r}, not 'uploaded'")
    if x.get("browser_download_url") != a["downloadUrl"]:
        raise Failed(f"{a['fileName']}: asset download URL is {x.get('browser_download_url')!r}")
    if x.get("size") != a["sizeBytes"]:
        raise Failed(f"{a['fileName']}: GitHub reports {x.get('size')} bytes, config says {a['sizeBytes']}")
    if not x.get("digest"):
        raise Failed(f"{a['fileName']}: GitHub reports no SHA-256 digest for the asset")
    if x["digest"].lower() != "sha256:" + a["sha256"]:
        raise Failed(f"{a['fileName']}: GitHub's asset digest {x['digest']} differs from config sha256")
    return x


def verify(cfg: dict, *, api_base: str, rewrite, work: Path, platform: str = sys.platform,
           authenticode=authenticode_on_windows, extractor=extract_installer_app) -> dict:
    rel = cfg.get("release") or {}
    pub = rel.get("publisher") or {}
    checks: list[str] = []

    def ok(msg):
        checks.append(msg)
        print("PASS " + msg)

    assets = check_config(rel)

    # 1. the release
    gh = fetch_release(rel, api_base)
    if gh.get("draft"):
        raise Failed("the release is a draft (not published)")
    if gh.get("prerelease"):
        raise Failed("the release is marked as a pre-release")
    if gh.get("tag_name") not in (None, rel["tag"]):
        raise Failed(f"GitHub returned tag {gh.get('tag_name')!r}, expected {rel['tag']!r}")
    if gh.get("html_url") != rel["releaseNotesUrl"]:
        raise Failed(f"release page is {gh.get('html_url')!r}, config says {rel['releaseNotesUrl']!r}")
    published = (gh.get("published_at") or "")[:10]
    if published != rel["date"]:
        raise Failed(f"release published on {published!r} (UTC), config date is {rel['date']!r}")
    seen: dict[str, str] = {}
    for x in gh.get("assets", []):
        n = x.get("name") or ""
        if n.lower() in seen:
            raise Failed(f"release has assets {seen[n.lower()]!r} and {n!r} whose names differ only in case")
        seen[n.lower()] = n
    ok(f"release {rel['tag']} is published, not a draft or pre-release, page and date match ({published})")

    records = []
    portable_exe = None
    installer_app = None
    for a in sorted(assets, key=lambda a: a["kind"] != "portable"):   # portable first: installer compares with it
        x = github_asset(gh, a)
        ok(f"{a['fileName']}: GitHub asset id {x['id']}, uploaded, {x['size']} bytes, digest matches")

        # 3. the actual final bytes
        path = work / a["fileName"]
        h, n = hashlib.sha256(), 0
        with http_get(a["downloadUrl"], rewrite=rewrite) as r, open(path, "wb") as f:
            while chunk := r.read(1 << 20):
                h.update(chunk)
                n += len(chunk)
                f.write(chunk)
        if n != a["sizeBytes"]:
            raise Failed(f"{a['fileName']}: downloaded {n} bytes, config says {a['sizeBytes']}")
        if h.hexdigest() != a["sha256"]:
            raise Failed(f"{a['fileName']}: downloaded file SHA-256 is {h.hexdigest()}, config says {a['sha256']}")
        ok(f"{a['fileName']}: downloaded {n} bytes; SHA-256 {h.hexdigest()} matches")

        signing = []
        if a["kind"] == "portable":
            contents, exe = check_zip(path)
            portable_exe = exe
            ok(f"{a['fileName']}: ZIP valid; {SUMS} covers all {len(contents) - 1} other files exactly once and "
               "every hash matches; no unexpected executables")
            signing.append(check_signing_claim(exe, f"{a['fileName']}!{APP_EXE}", bool(a.get("signed")), pub, work,
                                               platform=platform, authenticode=authenticode))
            inner = True
        else:
            data = path.read_bytes()
            signing.append(check_signing_claim(data, a["fileName"], bool(a.get("signed")), pub, work,
                                               platform=platform, authenticode=authenticode))
            installer_app = extractor(path, work)
            signing.append(check_signing_claim(installer_app, f"{a['fileName']}!{{app}}\\{APP_EXE}",
                                               bool(a.get("signed")), pub, work, platform=platform,
                                               authenticode=authenticode))
            contents, inner = [a["fileName"]], False
        ok(f"{a['fileName']}: signing claim ({'signed' if a.get('signed') else 'not signed'}) holds for "
           + ", ".join(s["file"] for s in signing))
        records.append({**{k: a.get(k) for k in ASSET_FIELDS}, "githubUpdatedAt": x.get("updated_at"),
                        "packageContents": contents, "innerChecksumsVerified": inner, "signing": signing})
    if portable_exe is not None and installer_app is not None:
        if hashlib.sha256(portable_exe).digest() != hashlib.sha256(installer_app).digest():
            raise Failed(f"the installer's embedded {APP_EXE} differs from the portable ZIP's {APP_EXE}")
        ok(f"installer and portable ZIP contain the identical {APP_EXE}")

    return {
        "result": "verified",
        "checkedAt": dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat(),
        "checkedOn": platform,
        "release": {k: rel.get(k) for k in RELEASE_FIELDS},
        "assets": sorted(records, key=lambda r: r["kind"]),
        "checks": checks,
    }


def recheck(record: dict, cfg: dict, *, api_base: str) -> None:
    """The record still describes the live release: same assets (id, size, digest, updated_at) — FA-012."""
    rel = cfg.get("release") or {}
    if record.get("result") != "verified":
        raise Failed("record result is not 'verified'")
    for k in RELEASE_FIELDS:
        if (record.get("release") or {}).get(k) != rel.get(k):
            raise Failed(f"record is stale: release.{k} differs from config.json")
    want = {a.get("kind"): a for a in rel.get("assets") or []}
    have = {a.get("kind"): a for a in record.get("assets") or []}
    if set(want) != set(have):
        raise Failed(f"record is stale: it covers assets {sorted(have)}, config has {sorted(want)}")
    gh = fetch_release(rel, api_base)
    for kind, a in want.items():
        for k in ASSET_FIELDS:
            if have[kind].get(k) != a.get(k):
                raise Failed(f"record is stale: {kind}.{k} differs from config.json")
        x = github_asset(gh, a)
        if have[kind].get("githubUpdatedAt") != x.get("updated_at"):
            raise Failed(f"record is stale: {a['fileName']} was modified on GitHub after it was verified")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--config", default=str(ROOT / "config.json"))
    ap.add_argument("--out", default=str(ROOT / "release-verification.json"))
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--recheck", action="store_true", help="check that the existing record is still current")
    ap.add_argument("--api-base", default="https://api.github.com", help=argparse.SUPPRESS)       # tests only
    ap.add_argument("--download-rewrite", nargs=2, metavar=("FROM", "TO"), help=argparse.SUPPRESS)  # tests only
    args = ap.parse_args()
    cfg = json.loads(Path(args.config).read_text(encoding="utf-8"))
    if args.recheck:
        try:
            recheck(json.loads(Path(args.out).read_text(encoding="utf-8")), cfg, api_base=args.api_base.rstrip("/"))
        except (Failed, FileNotFoundError) as e:
            print(f"FAIL {e}", file=sys.stderr)
            return 1
        print("CURRENT — the record matches config.json and the live GitHub Release")
        return 0
    with tempfile.TemporaryDirectory(prefix="snapforge-verify-") as tmp:
        try:
            record = verify(cfg, api_base=args.api_base.rstrip("/"), rewrite=args.download_rewrite, work=Path(tmp))
        except Failed as e:
            print(f"FAIL {e}", file=sys.stderr)
            print("NOT VERIFIED — nothing written. The website keeps the download disabled.", file=sys.stderr)
            return 1
    if args.dry_run:
        print("VERIFIED (dry run, nothing written)")
        return 0
    Path(args.out).write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    print(f"VERIFIED — wrote {args.out}. Review it, set release.ownerApproved only with the owner's approval, "
          "rebuild the site and commit both files.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
