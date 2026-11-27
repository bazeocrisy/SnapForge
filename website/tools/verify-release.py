#!/usr/bin/env python3
"""Verify a published SnapForge GitHub Release against website/config.json (read-only; standard library only).

    python3 website/tools/verify-release.py                     # check, then write website/release-verification.json
    python3 website/tools/verify-release.py --dry-run           # check only

It never creates, edits or publishes a release: it only reads the GitHub API and downloads the asset.
`release.ownerApproved: true` in config.json is not accepted as proof; the website build refuses to show a release
unless the record written here matches config.json exactly.

Checks (all must pass, otherwise nothing is written and the exit code is 1):
  1. The release for the tag in release.downloadUrl exists in bazeocrisy/SnapForge, is published (not a draft) and is
     not a pre-release; its page is release.releaseNotesUrl and its publication date is release.date (UTC).
  2. It has exactly one asset named release.fileName, fully uploaded, whose download URL is release.downloadUrl and
     whose size is release.sizeBytes; where GitHub reports a SHA-256 digest for the asset it equals release.sha256.
  3. The bytes actually downloaded from release.downloadUrl: count == release.sizeBytes, SHA-256 == release.sha256.
  4. ZIP packages: the archive is valid, contains SnapForge.exe, and every entry listed in an included SHA256SUMS.txt
     matches; the file list is recorded (the website lists only these files, never assumed ones).
  5. Signing claim (release.signed / release.signer) for SnapForge.exe (inside the ZIP, or the asset itself):
     - signed: false -> the executable must have no Authenticode signature at all (PE certificate table empty);
     - signed: true  -> it must carry a signature AND, on Windows, Get-AuthenticodeSignature must report Status
       "Valid" with a signer subject containing release.signer. Off Windows a "signed" claim cannot be validated and
       fails: run this tool on a Windows PC for a signed release.
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import io
import json
import os
import re
import struct
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OWNER_REPO = "bazeocrisy/SnapForge"
FIELDS = ("version", "date", "downloadUrl", "fileName", "sizeBytes", "sha256", "signed", "signer",
          "releaseNotesUrl", "packageType")
URL_RX = re.compile(r"^https://github\.com/bazeocrisy/SnapForge/releases/download/([A-Za-z0-9._-]+)/([A-Za-z0-9._-]+)$")


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


def pe_certificate_table(data: bytes) -> tuple[int, int]:
    """(offset, size) of the PE Authenticode certificate table; (0, 0) when the file is not signed."""
    if data[:2] != b"MZ":
        raise Failed("SnapForge.exe is not a Windows executable (no MZ header)")
    pe = struct.unpack_from("<I", data, 0x3C)[0]
    if data[pe:pe + 4] != b"PE\0\0":
        raise Failed("SnapForge.exe has no PE header")
    opt = pe + 24
    magic = struct.unpack_from("<H", data, opt)[0]
    dirs = opt + (96 if magic == 0x10B else 112)           # PE32 / PE32+
    return struct.unpack_from("<II", data, dirs + 4 * 8)  # data directory 4 = security (certificate) table


def authenticode_on_windows(exe: Path) -> dict:
    ps = ("$s = Get-AuthenticodeSignature -LiteralPath $env:SF_EXE; "
          "[pscustomobject]@{Status=[string]$s.Status; Signer=[string]$s.SignerCertificate.Subject} | ConvertTo-Json")
    out = subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-Command", ps], capture_output=True,
                         text=True, env={**os.environ, "SF_EXE": str(exe)}, check=True).stdout
    return json.loads(out)


def verify(cfg: dict, *, api_base: str, rewrite, work: Path) -> dict:
    rel = cfg.get("release") or {}
    checks: list[str] = []

    def ok(msg):
        checks.append(msg)
        print("PASS " + msg)

    for k in ("version", "date", "downloadUrl", "fileName", "sizeBytes", "sha256", "releaseNotesUrl"):
        if rel.get(k) in (None, ""):
            raise Failed(f"config.json release.{k} is empty")
    m = URL_RX.match(rel["downloadUrl"])
    if not m:
        raise Failed("release.downloadUrl is not a GitHub Release asset URL of " + OWNER_REPO)
    tag, name = m.groups()
    if name != rel["fileName"]:
        raise Failed(f"release.fileName {rel['fileName']!r} differs from the URL's file name {name!r}")

    # 1. the release
    try:
        with http_get(f"{api_base}/repos/{OWNER_REPO}/releases/tags/{tag}", api=True) as r:
            gh = json.load(r)
    except urllib.error.HTTPError as e:
        raise Failed(f"GitHub Release for tag {tag!r} not found or not public (HTTP {e.code})")
    if gh.get("draft"):
        raise Failed("the release is a draft (not published)")
    if gh.get("prerelease"):
        raise Failed("the release is marked as a pre-release")
    if gh.get("html_url") != rel["releaseNotesUrl"]:
        raise Failed(f"release page is {gh.get('html_url')!r}, config says {rel['releaseNotesUrl']!r}")
    published = (gh.get("published_at") or "")[:10]
    if published != rel["date"]:
        raise Failed(f"release published on {published!r} (UTC), config date is {rel['date']!r}")
    ok(f"release {tag} is published, not a draft or pre-release, page and date match ({published})")

    # 2. the asset as GitHub describes it
    assets = [a for a in gh.get("assets", []) if a.get("name") == rel["fileName"]]
    if len(assets) != 1:
        raise Failed(f"expected exactly one asset named {rel['fileName']!r}, found {len(assets)} "
                     f"(assets: {[a.get('name') for a in gh.get('assets', [])]})")
    asset = assets[0]
    if asset.get("state") != "uploaded":
        raise Failed(f"asset state is {asset.get('state')!r}, not 'uploaded'")
    if asset.get("browser_download_url") != rel["downloadUrl"]:
        raise Failed(f"asset download URL is {asset.get('browser_download_url')!r}")
    if asset.get("size") != rel["sizeBytes"]:
        raise Failed(f"GitHub reports {asset.get('size')} bytes, config says {rel['sizeBytes']}")
    digest = asset.get("digest")
    if digest:
        if digest.lower() != "sha256:" + rel["sha256"]:
            raise Failed(f"GitHub's asset digest {digest} differs from config sha256")
        ok("GitHub's own asset digest matches release.sha256")
    ok(f"exactly one uploaded asset {rel['fileName']} of {asset['size']} bytes")

    # 3. the actual bytes
    path = work / rel["fileName"]
    h, n = hashlib.sha256(), 0
    with http_get(rel["downloadUrl"], rewrite=rewrite) as r, open(path, "wb") as f:
        while chunk := r.read(1 << 20):
            h.update(chunk)
            n += len(chunk)
            f.write(chunk)
    if n != rel["sizeBytes"]:
        raise Failed(f"downloaded {n} bytes, config says {rel['sizeBytes']}")
    if h.hexdigest() != rel["sha256"]:
        raise Failed(f"downloaded file SHA-256 is {h.hexdigest()}, config says {rel['sha256']}")
    ok(f"downloaded {n} bytes; SHA-256 {h.hexdigest()} matches")

    # 4. package contents
    contents: list[str] = []
    inner_ok = False
    if rel["fileName"].lower().endswith(".zip"):
        try:
            z = zipfile.ZipFile(path)
            bad = z.testzip()
        except zipfile.BadZipFile:
            raise Failed("the asset is not a valid ZIP file")
        if bad:
            raise Failed(f"ZIP entry {bad} is corrupt")
        contents = sorted(i.filename for i in z.infolist() if not i.is_dir())
        exe_names = [c for c in contents if c.split("/")[-1] == "SnapForge.exe"]
        if len(exe_names) != 1:
            raise Failed(f"expected one SnapForge.exe in the ZIP, found {exe_names}")
        exe_bytes = z.read(exe_names[0])
        sums = [c for c in contents if c.split("/")[-1] == "SHA256SUMS.txt"]
        if sums:
            prefix = sums[0][: -len("SHA256SUMS.txt")]
            listed = 0
            for line in z.read(sums[0]).decode("ascii").splitlines():
                if not line.strip():
                    continue
                want, fname = line.split(None, 1)
                fname = fname.strip().lstrip("*")
                if prefix + fname not in contents:
                    raise Failed(f"SHA256SUMS.txt lists {fname}, which is not in the ZIP")
                if hashlib.sha256(z.read(prefix + fname)).hexdigest() != want.lower():
                    raise Failed(f"SHA256SUMS.txt checksum mismatch for {fname}")
                listed += 1
            inner_ok = listed > 0
            ok(f"ZIP valid; {len(contents)} files; SHA256SUMS.txt matches all {listed} listed files")
        else:
            ok(f"ZIP valid; {len(contents)} files (no SHA256SUMS.txt)")
    elif rel["fileName"].lower().endswith(".exe"):
        exe_bytes = path.read_bytes()
        contents = [rel["fileName"]]
    else:
        raise Failed("only .zip and .exe release assets can be verified by this tool")

    # 5. signing claim
    cert_off, cert_size = pe_certificate_table(exe_bytes)
    has_sig = cert_size > 0
    signing = {"claimedSigned": bool(rel.get("signed")), "certificateTablePresent": has_sig}
    if not rel.get("signed"):
        if has_sig:
            raise Failed("config says the release is NOT signed, but SnapForge.exe carries an Authenticode signature "
                         "— correct release.signed/signer")
        ok("SnapForge.exe carries no Authenticode signature, as the 'not code-signed' claim says")
    else:
        if not has_sig:
            raise Failed("config claims a signed release, but SnapForge.exe has no Authenticode signature")
        if sys.platform != "win32":
            raise Failed("a 'signed' claim can only be validated on Windows (Get-AuthenticodeSignature); run this "
                         "tool on a Windows PC")
        exe_path = work / "SnapForge.exe"
        exe_path.write_bytes(exe_bytes)
        result = authenticode_on_windows(exe_path)
        signing.update(windowsStatus=result.get("Status"), signerSubject=result.get("Signer"))
        if result.get("Status") != "Valid":
            raise Failed(f"Authenticode status is {result.get('Status')!r}, not 'Valid'")
        if (rel.get("signer") or "").lower() not in (result.get("Signer") or "").lower():
            raise Failed(f"signer {result.get('Signer')!r} does not contain release.signer {rel.get('signer')!r}")
        ok(f"Authenticode signature Valid; signer {result.get('Signer')}")

    return {
        "result": "verified",
        "checkedAt": dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat(),
        "checkedOn": sys.platform,
        "workflowRun": (f"{os.environ['GITHUB_SERVER_URL']}/{os.environ['GITHUB_REPOSITORY']}/actions/runs/"
                        f"{os.environ['GITHUB_RUN_ID']}") if os.environ.get("GITHUB_RUN_ID") else None,
        "release": {k: rel.get(k) for k in FIELDS},
        "packageContents": contents,
        "innerChecksumsVerified": inner_ok,
        "signing": signing,
        "checks": checks,
    }


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--config", default=str(ROOT / "config.json"))
    ap.add_argument("--out", default=str(ROOT / "release-verification.json"))
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--api-base", default="https://api.github.com", help=argparse.SUPPRESS)       # tests only
    ap.add_argument("--download-rewrite", nargs=2, metavar=("FROM", "TO"), help=argparse.SUPPRESS)  # tests only
    args = ap.parse_args()
    cfg = json.loads(Path(args.config).read_text(encoding="utf-8"))
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
