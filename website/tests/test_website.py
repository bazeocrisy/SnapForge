"""Tests for website/build.py, website/tools/verify-release.py and website/tools/publish-gh-pages.sh
(standard library + git only).

    python3 -B -m unittest discover -s website/tests -v

The release verifier is exercised against a local fake of the GitHub API and release downloads (no network).
Finding IDs (FA-011 … FA-014, SF-011, SF-012) are in the test names.
"""
from __future__ import annotations

import hashlib
import http.server
import importlib.util
import io
import json
import os
import shutil
import struct
import subprocess
import sys
import tempfile
import threading
import unittest
import zipfile
import zlib
from pathlib import Path

WEBSITE = Path(__file__).resolve().parent.parent
VERSION = "1.0.0"
TAG = "v1.0.0"
PORTABLE = f"SnapForge-{VERSION}-win-x64-portable.zip"
INSTALLER = f"SnapForge-{VERSION}-win-x64-setup.exe"
DL = f"https://github.com/bazeocrisy/SnapForge/releases/download/{TAG}/"
NOTES = f"https://github.com/bazeocrisy/SnapForge/releases/tag/{TAG}"
SUBJECT = "CN=SignPath Foundation, O=SignPath Foundation, L=Lewes, S=Delaware, C=US"
THUMB = "A" * 40
IDS = {"portable": 1001, "installer": 1002}


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


build = load("sf_build", WEBSITE / "build.py")
verifier = load("sf_verify", WEBSITE / "tools" / "verify-release.py")


def make_exe(signed: bool, salt: bytes = b"") -> bytes:
    """Minimal PE32+ image: enough headers for the certificate-table check."""
    data = bytearray(0x200)
    data[0:2] = b"MZ"
    struct.pack_into("<I", data, 0x3C, 0x80)
    data[0x80:0x84] = b"PE\0\0"
    opt = 0x80 + 24
    struct.pack_into("<H", data, opt, 0x20B)
    struct.pack_into("<II", data, opt + 112 + 32, 0x180 if signed else 0, 0x40 if signed else 0)
    return bytes(data) + salt


def base_files(signed=False) -> dict:
    return {"SnapForge.exe": make_exe(signed), "LICENSE.txt": b"MIT License\n",
            "THIRD-PARTY-NOTICES.md": b"notices\n", "PenImc_cor3.dll": b"native dll\n"}


def make_zip(signed=False, files=None, sums_lines=None, tamper=False, extra_entries=()) -> bytes:
    files = dict(base_files(signed) if files is None else files)
    lines = sums_lines if sums_lines is not None else \
        [f"{hashlib.sha256(b).hexdigest()}  {n}" for n, b in sorted(files.items())]
    if tamper:
        lines[0] = "0" * 64 + lines[0][64:]
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w") as z:
        for n, b in files.items():
            z.writestr(n, b)
        for n, b in extra_entries:
            z.writestr(n, b)
        z.writestr("SHA256SUMS.txt", "\n".join(lines) + "\n")
    return buf.getvalue()


class FakeGitHub:
    def __init__(self, release: dict | None, downloads: dict[str, bytes]):
        self.release, self.downloads = release, downloads
        fake = self

        class H(http.server.BaseHTTPRequestHandler):
            def log_message(self, *a):
                pass

            def do_GET(self):
                name = self.path.rsplit("/", 1)[-1]
                if self.path == f"/repos/bazeocrisy/SnapForge/releases/tags/{TAG}" and fake.release is not None:
                    body = json.dumps(fake.release).encode()
                elif self.path.startswith(f"/dl/{TAG}/") and name in fake.downloads:
                    body = fake.downloads[name]
                else:
                    self.send_response(404)
                    self.end_headers()
                    return
                self.send_response(200)
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

        self.server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), H)
        self.base = f"http://127.0.0.1:{self.server.server_address[1]}"
        threading.Thread(target=self.server.serve_forever, daemon=True).start()

    def close(self):
        self.server.shutdown()
        self.server.server_close()


def asset_cfg(kind: str, data: bytes, **over) -> dict:
    name = PORTABLE if kind == "portable" else INSTALLER
    a = {"kind": kind, "fileName": name, "downloadUrl": DL + name, "assetId": IDS[kind], "sizeBytes": len(data),
         "sha256": hashlib.sha256(data).hexdigest(), "signed": False}
    a.update(over)
    return a


def config_for(assets: dict[str, bytes], signed=False, **over) -> dict:
    rel = {"ownerApproved": True, "version": VERSION, "tag": TAG, "date": "2027-01-15", "releaseNotesUrl": NOTES,
           "publisher": {"subject": SUBJECT, "thumbprints": [THUMB]} if signed else {"subject": None, "thumbprints": []},
           "assets": [asset_cfg(k, d, signed=signed) for k, d in assets.items()]}
    rel.update(over)
    return {"siteUrl": "https://bazeocrisy.github.io/SnapForge/", "repositoryUrl": "https://github.com/bazeocrisy/SnapForge",
            "sourceUrl": "https://github.com/bazeocrisy/SnapForge/tree/release/v1.0-candidate", "sourceIsCandidate": True,
            "security": {"privateReporting": {"url": build.PRIVATE_REPORTING_URL, "verified": True}},
            "testBuild": {"version": "1.0.0-rc.2", "runUrl": None, "commit": None},
            "release": rel, "donations": {}}


def gh_release(assets: dict[str, bytes], **over) -> dict:
    r = {"tag_name": TAG, "draft": False, "prerelease": False, "html_url": NOTES, "published_at": "2027-01-15T10:00:00Z",
         "assets": [{"id": IDS[k], "name": PORTABLE if k == "portable" else INSTALLER, "state": "uploaded",
                     "size": len(d), "browser_download_url": DL + (PORTABLE if k == "portable" else INSTALLER),
                     "digest": "sha256:" + hashlib.sha256(d).hexdigest(), "updated_at": "2027-01-15T09:59:00Z"}
                    for k, d in assets.items()]}
    r.update(over)
    return r


def good_signature(**over) -> dict:
    r = {"Status": "Valid", "StatusMessage": "Signature verified.", "SignatureType": "Authenticode", "Subject": SUBJECT,
         "Thumbprint": THUMB, "Issuer": "CN=Example CA", "Eku": ["1.3.6.1.5.5.7.3.3"],
         "TimeStamper": "CN=Example TSA", "TimeStamperChainOk": True}
    r.update(over)
    return r


class VerifierTests(unittest.TestCase):
    """FA-012: tools/verify-release.py against a fake GitHub."""

    def run_verify(self, cfg, release, downloads, **kw):
        fake = FakeGitHub(release, downloads)
        try:
            with tempfile.TemporaryDirectory() as tmp:
                kw.setdefault("extractor", lambda path, work: make_exe(False))
                return verifier.verify(cfg, api_base=fake.base,
                                       rewrite=("https://github.com/bazeocrisy/SnapForge/releases/download/",
                                                fake.base + "/dl/"), work=Path(tmp), **kw)
        finally:
            fake.close()

    def assertFails(self, text, cfg, release, downloads, **kw):
        with self.assertRaises(verifier.Failed) as cm:
            self.run_verify(cfg, release, downloads, **kw)
        self.assertIn(text, str(cm.exception))

    def both(self, signed=False):
        z = make_zip(signed)
        return {"portable": z, "installer": make_exe(signed, b"setup")}

    def dl(self, assets):
        return {(PORTABLE if k == "portable" else INSTALLER): d for k, d in assets.items()}

    # --- the genuine cases
    def test_fa011_genuine_unsigned_release_with_two_assets_is_verified(self):
        assets = self.both()
        rec = self.run_verify(config_for(assets), gh_release(assets), self.dl(assets))
        self.assertEqual(rec["result"], "verified")
        kinds = {a["kind"]: a for a in rec["assets"]}
        self.assertEqual(set(kinds), {"installer", "portable"})
        self.assertEqual(kinds["portable"]["assetId"], IDS["portable"])
        self.assertEqual(kinds["installer"]["sha256"], hashlib.sha256(assets["installer"]).hexdigest())
        self.assertEqual(kinds["portable"]["packageContents"],
                         ["LICENSE.txt", "PenImc_cor3.dll", "SHA256SUMS.txt", "SnapForge.exe", "THIRD-PARTY-NOTICES.md"])
        self.assertTrue(kinds["portable"]["innerChecksumsVerified"])
        self.assertEqual(len(kinds["installer"]["signing"]), 2, "installer and its embedded app are both checked")

    def test_fa012_signed_release_verified_with_exact_publisher_on_windows(self):
        assets = self.both(signed=True)
        calls = []
        rec = self.run_verify(config_for(assets, signed=True), gh_release(assets), self.dl(assets),
                              platform="win32", authenticode=lambda p: calls.append(p) or good_signature(),
                              extractor=lambda path, work: make_exe(True))
        self.assertEqual(len(calls), 3, "portable SnapForge.exe, installer, installer's embedded SnapForge.exe")
        self.assertEqual(rec["assets"][0]["signing"][0]["signerSubject"], SUBJECT)

    # --- release and asset metadata
    def test_missing_release(self):
        assets = self.both()
        self.assertFails("not found", config_for(assets), None, self.dl(assets))

    def test_draft_and_prerelease_rejected(self):
        assets = self.both()
        self.assertFails("draft", config_for(assets), gh_release(assets, draft=True), self.dl(assets))
        self.assertFails("pre-release", config_for(assets), gh_release(assets, prerelease=True), self.dl(assets))

    def test_wrong_date_or_notes_page(self):
        assets = self.both()
        self.assertFails("published on", config_for(assets, date="2027-01-14"), gh_release(assets), self.dl(assets))
        self.assertFails("release page", config_for(assets), gh_release(assets, html_url=NOTES + "x"), self.dl(assets))

    def test_fa011_asset_names_must_carry_the_same_version(self):
        assets = self.both()
        cfg = config_for(assets)
        cfg["release"]["assets"][1]["fileName"] = "SnapForge-1.0.1-win-x64-setup.exe"
        self.assertFails("same version", cfg, gh_release(assets), self.dl(assets))

    def test_fa011_duplicate_asset_kinds_rejected(self):
        assets = self.both()
        cfg = config_for(assets)
        cfg["release"]["assets"][1] = dict(cfg["release"]["assets"][0])
        self.assertFails("distinct kinds", cfg, gh_release(assets), self.dl(assets))

    def test_asset_name_must_match_exactly(self):
        assets = self.both()
        rel = gh_release(assets)
        rel["assets"][0]["name"] = PORTABLE.lower()
        self.assertFails("exactly one asset", config_for(assets), rel, self.dl(assets))

    def test_fa012_release_assets_differing_only_in_case_rejected(self):
        assets = self.both()
        rel = gh_release(assets)
        rel["assets"].append(dict(rel["assets"][0], name=PORTABLE.upper(), id=9))
        self.assertFails("differ only in case", config_for(assets), rel, self.dl(assets))

    def test_asset_not_fully_uploaded(self):
        assets = self.both()
        rel = gh_release(assets)
        rel["assets"][0]["state"] = "starter"
        self.assertFails("not 'uploaded'", config_for(assets), rel, self.dl(assets))

    def test_fa012_replaced_asset_with_new_id_is_rejected(self):
        """A record/config for an older upload (asset id) must not verify against a re-uploaded asset."""
        assets = self.both()
        rel = gh_release(assets)
        rel["assets"][0]["id"] = 2002
        self.assertFails("asset was replaced", config_for(assets), rel, self.dl(assets))

    def test_fa012_digest_is_required_and_must_match(self):
        assets = self.both()
        rel = gh_release(assets)
        rel["assets"][0].pop("digest")
        self.assertFails("no SHA-256 digest", config_for(assets), rel, self.dl(assets))
        cfg = config_for(assets)
        cfg["release"]["assets"][0]["sha256"] = "0" * 64
        self.assertFails("digest", cfg, gh_release(assets), self.dl(assets))

    def test_fa012_actual_downloaded_bytes_are_checked(self):
        assets = self.both()
        served = self.dl(assets)
        served[PORTABLE] = assets["portable"] + b"x"        # API metadata right, final bytes served differ
        self.assertFails("downloaded", config_for(assets), gh_release(assets), served)
        served = self.dl(assets)
        served[INSTALLER] = bytes(len(assets["installer"]))  # same size, different bytes
        self.assertFails("downloaded file SHA-256", config_for(assets), gh_release(assets), served)

    def test_size_mismatch_in_api(self):
        assets = self.both()
        cfg = config_for(assets)
        cfg["release"]["assets"][0]["sizeBytes"] += 1
        self.assertFails("GitHub reports", cfg, gh_release(assets), self.dl(assets))

    def test_actions_artifact_url_rejected(self):
        assets = self.both()
        cfg = config_for(assets)
        cfg["release"]["assets"][0]["downloadUrl"] = "https://github.com/bazeocrisy/SnapForge/actions/runs/1/artifacts/2"
        self.assertFails("Actions artifacts are not accepted", cfg, gh_release(assets), self.dl(assets))

    # --- FA-012: the inner SHA256SUMS list must cover every file exactly once
    def zip_case(self, text, **zip_kw):
        z = make_zip(**zip_kw)
        assets = {"portable": z}
        self.assertFails(text, config_for(assets), gh_release(assets), self.dl(assets))

    def test_fa012_inner_checksum_mismatch(self):
        self.zip_case("SHA256SUMS.txt checksum mismatch", tamper=True)

    def test_fa012_inner_list_missing_a_file(self):
        f = base_files()
        lines = [f"{hashlib.sha256(b).hexdigest()}  {n}" for n, b in sorted(f.items()) if n != "LICENSE.txt"]
        self.zip_case("unlisted: ['LICENSE.txt']", sums_lines=lines)

    def test_fa012_inner_list_with_extra_name(self):
        f = base_files()
        lines = [f"{hashlib.sha256(b).hexdigest()}  {n}" for n, b in sorted(f.items())] + ["0" * 64 + "  ghost.txt"]
        self.zip_case("not in the ZIP: ['ghost.txt']", sums_lines=lines)

    def test_fa012_inner_list_duplicate_and_case_duplicate(self):
        f = base_files()
        lines = [f"{hashlib.sha256(b).hexdigest()}  {n}" for n, b in sorted(f.items())]
        self.zip_case("more than once", sums_lines=lines + [lines[0]])
        h = hashlib.sha256(f["LICENSE.txt"]).hexdigest()
        self.zip_case("more than once", sums_lines=lines + [f"{h}  license.txt"])

    def test_fa012_zip_entries_differing_only_in_case(self):
        self.zip_case("duplicate entries", extra_entries=[("license.TXT", b"MIT License\n")])

    def test_fa012_malformed_checksum_line(self):
        f = base_files()
        lines = [f"{hashlib.sha256(b).hexdigest()}  {n}" for n, b in sorted(f.items())] + ["not a checksum line"]
        self.zip_case("is not '<64 hex>  <name>'", sums_lines=lines)

    def test_fa012_missing_checksum_list(self):
        buf = io.BytesIO()
        with zipfile.ZipFile(buf, "w") as z:
            for n, b in base_files().items():
                z.writestr(n, b)
        assets = {"portable": buf.getvalue()}
        self.assertFails("exactly one SHA256SUMS.txt", config_for(assets), gh_release(assets), self.dl(assets))

    def test_fa012_unexpected_executables(self):
        for name, data, text in [("helper.exe", make_exe(False), "unexpected executable"),
                                 ("run.ps1", b"Write-Host hi", "unexpected executable"),
                                 ("evil.dll", b"x", "unexpected DLL"),
                                 ("readme.txt", make_exe(False), "disguised")]:
            with self.subTest(name=name):
                f = base_files()
                f[name] = data
                self.zip_case(text, files=f)

    def test_fa012_unsafe_zip_path(self):
        f = base_files()
        f["../outside.txt"] = b"x"
        self.zip_case("unsafe path", files=f)

    # --- FA-012: signing claims
    def test_unsigned_claim_but_executable_is_signed(self):
        assets = {"portable": make_zip(signed=True)}
        self.assertFails("carries an Authenticode signature", config_for(assets), gh_release(assets), self.dl(assets))

    def test_signed_claim_but_executable_is_unsigned(self):
        assets = {"portable": make_zip(signed=False)}
        self.assertFails("no Authenticode signature", config_for(assets, signed=True), gh_release(assets),
                         self.dl(assets), platform="win32", authenticode=lambda p: good_signature())

    def test_fa012_signed_claim_cannot_be_validated_off_windows(self):
        assets = {"portable": make_zip(signed=True)}
        self.assertFails("only be validated on Windows", config_for(assets, signed=True), gh_release(assets),
                         self.dl(assets), platform="linux")

    @unittest.skipIf(sys.platform == "win32", "on Windows the real Authenticode check runs instead")
    def test_fa012_default_platform_off_windows_refuses_signed_claim(self):
        assets = {"portable": make_zip(signed=True)}
        self.assertFails("only be validated on Windows", config_for(assets, signed=True), gh_release(assets),
                         self.dl(assets))

    def test_fa012_signature_policy_negative_cases(self):
        assets = {"portable": make_zip(signed=True)}
        cases = [
            (dict(Subject="CN=SignPath Foundation, O=SignPath Foundation, L=Lewes, S=Delaware, C=US, OU=Evil"),
             "not exactly the expected publisher"),
            (dict(Subject="CN=SignPath Foundation"), "not exactly the expected publisher"),   # substring only
            (dict(Thumbprint="B" * 40), "thumbprint"),
            (dict(Status="NotTrusted", StatusMessage="untrusted root"), "not 'Valid'"),
            (dict(Status="HashMismatch"), "not 'Valid'"),
            (dict(TimeStamper=None), "no timestamp countersignature"),
            (dict(TimeStamperChainOk=False), "timestamp certificate chain"),
            (dict(Eku=[]), "Code Signing EKU"),
            (dict(SignatureType="Catalog"), "signature type"),
        ]
        for over, text in cases:
            with self.subTest(over=over):
                self.assertFails(text, config_for(assets, signed=True), gh_release(assets), self.dl(assets),
                                 platform="win32", authenticode=lambda p, o=over: good_signature(**o))

    def test_fa012_signed_claim_needs_exact_publisher_policy(self):
        assets = {"portable": make_zip(signed=True)}
        cfg = config_for(assets, signed=True)
        cfg["release"]["publisher"]["thumbprints"] = []
        self.assertFails("release.publisher.thumbprints", cfg, gh_release(assets), self.dl(assets), platform="win32",
                         authenticode=lambda p: good_signature())

    def test_fa012_installer_embedded_app_must_be_signed_and_identical(self):
        assets = self.both(signed=True)
        self.assertFails("!{app}\\SnapForge.exe is signed, but it has no", config_for(assets, signed=True),
                         gh_release(assets), self.dl(assets),
                         platform="win32", authenticode=lambda p: good_signature(),
                         extractor=lambda path, work: make_exe(False))        # unsigned app inside a signed setup
        self.assertFails("differs from the portable", config_for(assets, signed=True), gh_release(assets),
                         self.dl(assets), platform="win32", authenticode=lambda p: good_signature(),
                         extractor=lambda path, work: make_exe(True, b"other build"))

    def test_fa012_installer_needs_innoextract(self):
        assets = {"installer": make_exe(False, b"setup")}
        with unittest.mock.patch.object(verifier.shutil, "which", return_value=None):
            self.assertFails("innoextract is not on PATH", config_for(assets), gh_release(assets), self.dl(assets),
                             extractor=verifier.extract_installer_app)

    # --- FA-012: stale records
    def test_fa012_recheck_detects_stale_record(self):
        assets = self.both()
        cfg = config_for(assets)
        rec = self.run_verify(cfg, gh_release(assets), self.dl(assets))
        fake = FakeGitHub(gh_release(assets), {})
        try:
            verifier.recheck(rec, cfg, api_base=fake.base)               # current: no exception
            changed = gh_release(assets)
            changed["assets"][1]["updated_at"] = "2027-02-01T00:00:00Z"
            fake.release = changed
            with self.assertRaisesRegex(verifier.Failed, "modified on GitHub"):
                verifier.recheck(rec, cfg, api_base=fake.base)
            replaced = gh_release(assets)
            replaced["assets"][0]["id"] = 4242
            fake.release = replaced
            with self.assertRaisesRegex(verifier.Failed, "asset was replaced"):
                verifier.recheck(rec, cfg, api_base=fake.base)
            fake.release = gh_release(assets)
            old = json.loads(json.dumps(rec))
            old["assets"][0]["assetId"] = 999
            with self.assertRaisesRegex(verifier.Failed, "stale"):
                verifier.recheck(old, cfg, api_base=fake.base)
        finally:
            fake.close()

    def test_fields_agree_between_build_and_verifier(self):
        self.assertEqual(build.RELEASE_FIELDS, verifier.RELEASE_FIELDS)
        self.assertEqual(build.ASSET_FIELDS, verifier.ASSET_FIELDS)
        self.assertEqual(build.ASSET_NAMES, verifier.ASSET_NAMES)


import unittest.mock  # noqa: E402  (used above)


class LinkNameTests(unittest.TestCase):
    """An image link is named by its alt text, whether written <img> or <img/>; an empty link is reported."""

    def count(self, html: str) -> int:
        p = build.PageParser()
        p.feed(html)
        return p.empty_links

    def test_image_alt_names_the_link(self):
        self.assertEqual(self.count('<a href="x.png"><img src="x.png" alt="Editor"></a>'), 0)
        self.assertEqual(self.count('<a href="x.png"><img src="x.png" alt="Editor"/></a>'), 0)

    def test_empty_links_are_reported(self):
        self.assertEqual(self.count('<a href="x.png"><img src="x.png" alt=""></a>'), 1)
        self.assertEqual(self.count('<a href="x.html"></a>'), 1)


def png_bytes(w: int, h: int) -> bytes:
    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)
    raw = b"".join(b"\0" + b"\0\0\0" * w for _ in range(h))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b""))


class ScreenshotTests(unittest.TestCase):
    """SF-011: native-size PNG screenshots, real width/height, each linked to its full-resolution file."""

    def setUp(self):
        self.out = Path(tempfile.mkdtemp())
        (self.out / "assets" / "screenshots").mkdir(parents=True)
        (self.out / "assets" / "screenshots" / "a.png").write_bytes(png_bytes(40, 20))

    def tearDown(self):
        shutil.rmtree(self.out)

    def page(self, html):
        p = build.PageParser()
        p.feed(html)
        return build.check_page_images(self.out, "x.html", p.imgs)

    def test_sf011_png_size_read_from_ihdr(self):
        self.assertEqual(build.png_size(self.out / "assets/screenshots/a.png"), (40, 20))

    def test_sf011_correct_markup_passes(self):
        self.assertEqual(self.page('<a href="assets/screenshots/a.png"><img src="assets/screenshots/a.png" '
                                   'width="40" height="20" alt="A"></a>'), [])

    def test_sf011_wrong_declared_size_fails(self):
        probs = self.page('<a href="assets/screenshots/a.png"><img src="assets/screenshots/a.png" width="80" '
                          'height="40" alt="A"></a>')
        self.assertTrue(any("must equal the real size" in p for p in probs), probs)

    def test_sf011_unlinked_or_wrongly_linked_image_fails(self):
        self.assertTrue(self.page('<img src="assets/screenshots/a.png" width="40" height="20" alt="A">'))
        self.assertTrue(self.page('<a href="features.html"><img src="assets/screenshots/a.png" width="40" '
                                  'height="20" alt="A"></a>'))

    def test_sf011_jpeg_fake_png_and_upscaled_files_fail(self):
        (self.out / "assets/screenshots/b.jpg").write_bytes(b"\xff\xd8\xff")
        (self.out / "assets/screenshots/c.png").write_bytes(b"\xff\xd8\xff not a png")
        (self.out / "assets/screenshots/d.png").write_bytes(png_bytes(3840, 2))
        probs = "\n".join(build.check_screenshots(self.out))
        self.assertIn("b.jpg: screenshots must be lossless PNG", probs)
        self.assertIn("c.png: has a .png name but is not a PNG", probs)
        self.assertIn("d.png: 3840×2 is larger than", probs)

    def test_sf011_committed_site_screenshots_all_pass(self):
        self.assertEqual(build.check_screenshots(WEBSITE / "public"), [])
        for page in sorted((WEBSITE / "public").glob("*.html")):
            p = build.PageParser()
            p.feed(page.read_text(encoding="utf-8"))
            self.assertEqual(build.check_page_images(WEBSITE / "public", page.name, p.imgs), [], page.name)


class BuildGateTests(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())

    def tearDown(self):
        shutil.rmtree(self.tmp)

    def write(self, cfg, record=None):
        c = self.tmp / "config.json"
        c.write_text(json.dumps(cfg))
        v = self.tmp / "release-verification.json"
        if record is not None:
            v.write_text(json.dumps(record))
        return c, v

    def record(self, cfg):
        rel = cfg["release"]
        return {"result": "verified", "checkedAt": "2027-01-15T12:00:00+00:00",
                "release": {k: rel[k] for k in verifier.RELEASE_FIELDS},
                "assets": [{**{k: a[k] for k in verifier.ASSET_FIELDS},
                            "packageContents": (["LICENSE.txt", "SHA256SUMS.txt", "SnapForge.exe", "THIRD-PARTY-NOTICES.md"]
                                                if a["kind"] == "portable" else [a["fileName"]]),
                            "innerChecksumsVerified": a["kind"] == "portable",
                            "signing": [{"file": a["fileName"], "claimedSigned": a["signed"]}]}
                           for a in rel["assets"]]}

    def cfg(self):
        return config_for({"installer": make_exe(False, b"setup"), "portable": make_zip()})

    def refused(self, cfg, rec, text):
        c, v = self.write(cfg, rec)
        with self.assertRaises(build.ConfigError) as cm:
            build.load_config(c, v)
        self.assertIn(text, str(cm.exception))

    def test_owner_approved_alone_is_refused(self):
        self.refused(self.cfg(), None, "release-verification.json is missing")

    def test_fa011_record_must_match_each_asset(self):
        cfg = self.cfg()
        rec = self.record(cfg)
        rec["assets"][1]["sha256"] = "f" * 64
        self.refused(cfg, rec, "the portable asset's sha256")

    def test_fa011_each_asset_needs_its_own_record(self):
        cfg = self.cfg()
        rec = self.record(cfg)
        rec["assets"] = [a for a in rec["assets"] if a["kind"] != "installer"]
        self.refused(cfg, rec, "no verification record for the installer asset")

    def test_fa012_record_for_older_asset_id_is_stale(self):
        cfg = self.cfg()
        rec = self.record(cfg)
        rec["assets"][0]["assetId"] = 1
        self.refused(cfg, rec, "installer asset's assetId")
        rec = self.record(cfg)
        rec["assets"].append(dict(rec["assets"][0], kind="msix"))
        self.refused(cfg, rec, "stale record")

    def test_fa011_asset_must_have_release_version_and_tag(self):
        cfg = self.cfg()
        cfg["release"]["assets"][0]["fileName"] = "SnapForge-0.9.0-win-x64-setup.exe"
        self.refused(cfg, self.record(cfg), "every asset has the release's version")
        cfg = self.cfg()
        cfg["release"]["assets"][1]["downloadUrl"] = "https://github.com/bazeocrisy/SnapForge/actions/runs/1/artifacts/2"
        self.refused(cfg, self.record(cfg), "Actions artifacts expire")

    def test_fa012_signed_asset_needs_publisher_identity(self):
        cfg = self.cfg()
        cfg["release"]["assets"][0]["signed"] = True
        self.refused(cfg, self.record(cfg), "release.publisher.subject")

    def test_private_reporting_required_before_release(self):
        cfg = self.cfg()
        cfg["security"]["privateReporting"]["verified"] = False
        self.refused(cfg, self.record(cfg), "private vulnerability reporting")

    def test_fa011_verified_release_shows_each_asset_separately(self):
        cfg = self.cfg()
        c, v = self.write(cfg, self.record(cfg))
        loaded = build.load_config(c, v)
        out = self.tmp / "site"
        build.build(loaded, out)
        page = (out / "download.html").read_text()
        self.assertIn('id="asset-installer"', page)
        self.assertIn('id="asset-portable"', page)
        self.assertIn(INSTALLER, page)
        self.assertIn(PORTABLE, page)
        self.assertIn(DL + INSTALLER, page)
        self.assertIn(DL + PORTABLE, page)
        self.assertIn("<code>THIRD-PARTY-NOTICES.md</code>", page)
        self.assertIn("Report a vulnerability form", (out / "support.html").read_text())
        self.assertEqual(build.validate(out), [])

    def test_fa014_unconfigured_site_disables_public_download(self):
        loaded = build.load_config(WEBSITE / "config.json", self.tmp / "none.json")
        self.assertFalse(loaded["release"]["ownerApproved"])
        out = self.tmp / "site"
        build.build(loaded, out)
        page = (out / "download.html").read_text()
        self.assertIn('<h2 id="public">Public download</h2>', page)
        self.assertIn("no public release of SnapForge yet", page)
        self.assertIn("disabled", page)
        self.assertNotIn("releases/download/", page)
        self.assertIn("will be listed here once a release has been published and verified", page)
        self.assertIn("Private reporting is not set up yet", (out / "support.html").read_text())

    def test_sf012_test_build_section_is_honest(self):
        # The neutral state (no CI run checked yet), whatever the shipped config currently links to.
        cfg = json.loads((WEBSITE / "config.json").read_text())
        cfg["testBuild"] = {"version": "1.0.0-rc.2", "runUrl": None, "commit": None}
        c, v = self.write(cfg)
        out = self.tmp / "site"
        build.build(build.load_config(c, v), out)
        page = (out / "download.html").read_text()
        self.assertIn('id="test-builds"', page)
        self.assertIn("1.0.0-rc.2", page)
        self.assertIn("Sign-in is not owner-only access", page)
        self.assertIn("anyone with a free GitHub", page)
        self.assertIn("expire", page)
        self.assertIn("not code-signed", page)
        self.assertIn("will be added here", page)            # runUrl is null: neutral state, no invented URL
        self.assertNotIn("/actions/runs/", page)
        self.assertIn("Get-FileHash", page)
        self.assertIn("SHA256SUMS.txt", page)
        for bad in ("Run anyway", "turn off SmartScreen", "disable SmartScreen", "disable Defender"):
            self.assertNotIn(bad, page)
        self.assertIn("Program Files", page)
        self.assertIn("administrator approval", page)
        self.assertIn("%LOCALAPPDATA%\\SnapForge", page)
        self.assertIn("24H2", page)

    def test_sf012_test_build_link_rendered_when_configured(self):
        cfg = json.loads((WEBSITE / "config.json").read_text())
        cfg["testBuild"].update(runUrl="https://github.com/bazeocrisy/SnapForge/actions/runs/123", commit="a" * 40,
                                artifactUrl=None)  # the run-page-only mode
        c, v = self.write(cfg)
        out = self.tmp / "site"
        build.build(build.load_config(c, v), out)
        page = (out / "download.html").read_text()
        self.assertIn('href="https://github.com/bazeocrisy/SnapForge/actions/runs/123"', page)
        self.assertIn("a" * 40, page)
        self.assertEqual(build.validate(out), [])

    def _rc4_test_build(self) -> dict:
        return {"version": "1.0.0-rc.4", "build": 161, "commit": "b" * 40,
                "runUrl": "https://github.com/bazeocrisy/SnapForge/actions/runs/456",
                "artifactUrl": "https://github.com/bazeocrisy/SnapForge/actions/runs/456/artifacts/789",
                "fileName": "SnapForge-1.0.0-rc.4-win-x64-setup.exe", "sha256": "c" * 64, "signed": False,
                "expires": "2026-12-30"}

    def test_rc4_download_windows_test_build_link_shows_every_detail(self):
        cfg = json.loads((WEBSITE / "config.json").read_text())
        cfg["testBuild"] = self._rc4_test_build()
        c, v = self.write(cfg)
        out = self.tmp / "site"
        build.build(build.load_config(c, v), out)
        page = (out / "download.html").read_text()
        home = (out / "index.html").read_text()
        for p in (page, home):
            self.assertIn('href="https://github.com/bazeocrisy/SnapForge/actions/runs/456/artifacts/789"', p)
            self.assertIn("Download Windows test build", p)
            self.assertIn("1.0.0-rc.4", p)
            self.assertIn("build 161", p)
            self.assertIn("SnapForge-1.0.0-rc.4-win-x64-setup.exe", p)
            self.assertIn("not code-signed", p)
            self.assertIn("sign-in", p)
        self.assertIn("c" * 64, page)
        self.assertIn("2026-12-30", page)
        self.assertIn("release candidate 4", home)
        # The public download stays separate and disabled.
        self.assertIn('disabled aria-describedby="dl-status-note"', home)
        self.assertNotIn("releases/download", page + home)
        self.assertEqual(build.validate(out), [])

    def test_rc4_test_build_link_must_be_the_runs_own_package_with_all_details(self):
        cfg = json.loads((WEBSITE / "config.json").read_text())
        for change, message in (
            ({"artifactUrl": "https://example.com/SnapForge.exe"}, "testBuild.artifactUrl must be"),
            ({"artifactUrl": "https://github.com/bazeocrisy/SnapForge/actions/runs/999/artifacts/1"}, "must belong to the run"),
            ({"fileName": "setup.exe"}, "testBuild.fileName must be"),
            ({"sha256": "abc"}, "testBuild.sha256 must be"),
            ({"signed": True}, "testBuild.signed must be false"),
            ({"build": None}, "testBuild.build must be"),
            ({"expires": None}, "testBuild.expires must be"),
        ):
            cfg["testBuild"] = {**self._rc4_test_build(), **change}
            self.refused(cfg, None, message)

    def test_sf012_test_build_url_must_be_a_run_page(self):
        cfg = json.loads((WEBSITE / "config.json").read_text())
        cfg["testBuild"].update(runUrl="https://bazeocrisy.github.io/SnapForge/SnapForge.zip", commit="a" * 40)
        self.refused(cfg, None, "testBuild.runUrl must be a run page")

    def test_source_links_point_to_candidate_branch(self):
        loaded = build.load_config(WEBSITE / "config.json", self.tmp / "none.json")
        out = self.tmp / "site"
        build.build(loaded, out)
        for name in ("index.html", "download.html", "about.html"):
            self.assertIn('href="https://github.com/bazeocrisy/SnapForge/tree/release/v1.0-candidate"',
                          (out / name).read_text(), name)
        self.assertIn("becomes the main branch", (out / "download.html").read_text())
        cfg = json.loads((WEBSITE / "config.json").read_text())
        cfg["sourceUrl"] = "https://example.com/fork"
        self.refused(cfg, None, "sourceUrl must be")

    def test_code_signing_policy_is_a_draft_outside_navigation(self):
        loaded = build.load_config(WEBSITE / "config.json", self.tmp / "none.json")
        out = self.tmp / "site"
        build.build(loaded, out)
        page = (out / "code-signing.html").read_text()
        self.assertIn('<meta name="robots" content="noindex">', page)
        self.assertIn("not code-signed today", page)
        self.assertNotIn("code-signing.html", (out / "index.html").read_text())
        self.assertNotIn("code-signing.html", (out / "sitemap.xml").read_text())

    def test_check_detects_stale_or_hand_edited_or_extra_generated_files(self):
        site = self.tmp / "public"
        shutil.copytree(WEBSITE / "public", site)
        script = [sys.executable, "-B", "-c",
                  "import importlib.util,sys;from pathlib import Path;"
                  f"s=importlib.util.spec_from_file_location('b',{str(WEBSITE / 'build.py')!r});b=importlib.util.module_from_spec(s);"
                  f"s.loader.exec_module(b);b.PUBLIC=Path({str(site)!r});sys.argv=['build.py','--check'];sys.exit(b.main())"]
        self.assertEqual(subprocess.run(script, capture_output=True).returncode, 0, "fresh copy should pass")
        (site / "about.html").write_text((site / "about.html").read_text().replace("MIT", "GPL"))
        r = subprocess.run(script, capture_output=True, text=True)
        self.assertEqual(r.returncode, 1)
        self.assertIn("differs: about.html", r.stderr)
        shutil.rmtree(site)
        shutil.copytree(WEBSITE / "public", site)
        (site / "extra.html").write_text("<p>stray</p>")
        r = subprocess.run(script, capture_output=True, text=True)
        self.assertIn("only in one tree: extra.html", r.stderr)

    def test_refuses_to_delete_unrelated_folder(self):
        other = self.tmp / "important"
        other.mkdir()
        (other / "notes.txt").write_text("keep me")
        with self.assertRaises(SystemExit):
            build.build(build.load_config(WEBSITE / "config.json", self.tmp / "none.json"), other)
        self.assertTrue((other / "notes.txt").exists())


@unittest.skipUnless(shutil.which("git") and shutil.which("bash"), "needs git and bash")
class PublishScriptTests(unittest.TestCase):
    """FA-013: publish-gh-pages.sh source guards (run with --preflight-only in throw-away repositories)."""

    BRANCH = "release/v1.0-candidate"

    def git(self, cwd, *args, check=True):
        env = {**os.environ, "GIT_AUTHOR_NAME": "t", "GIT_AUTHOR_EMAIL": "t@example.invalid",
               "GIT_COMMITTER_NAME": "t", "GIT_COMMITTER_EMAIL": "t@example.invalid", "GIT_CONFIG_GLOBAL": os.devnull,
               "GIT_CONFIG_NOSYSTEM": "1"}
        return subprocess.run(["git", "-c", "init.defaultBranch=main", "-c", "commit.gpgsign=false", *args],
                              cwd=cwd, env=env, capture_output=True, text=True, check=check)

    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.origin = self.tmp / "origin.git"
        self.git(self.tmp, "init", "-q", "--bare", str(self.origin))
        self.repo = self.tmp / "repo"
        self.git(self.tmp, "clone", "-q", str(self.origin), str(self.repo))
        (self.repo / "website" / "tools").mkdir(parents=True)
        shutil.copy(WEBSITE / "tools" / "publish-gh-pages.sh", self.repo / "website" / "tools")
        self.git(self.repo, "checkout", "-q", "-b", self.BRANCH)
        self.commit("first")
        self.git(self.repo, "push", "-q", "origin", self.BRANCH)

    def tearDown(self):
        shutil.rmtree(self.tmp)

    def commit(self, msg):
        (self.repo / "f.txt").write_text(msg)
        self.git(self.repo, "add", "-A")
        self.git(self.repo, "commit", "-q", "-m", msg)
        return self.git(self.repo, "rev-parse", "HEAD").stdout.strip()

    def run_script(self, *args):
        return subprocess.run(["bash", "website/tools/publish-gh-pages.sh", "--preflight-only", *args], cwd=self.repo,
                              capture_output=True, text=True,
                              env={**os.environ, "GIT_CONFIG_GLOBAL": os.devnull, "GIT_CONFIG_NOSYSTEM": "1"})

    def gh_pages_record(self, commit):
        """Create origin/gh-pages whose build-info.json records `commit`."""
        pages = self.tmp / "pages"
        self.git(self.tmp, "clone", "-q", str(self.origin), str(pages))
        self.git(pages, "checkout", "-q", "--orphan", "gh-pages")
        self.git(pages, "rm", "-q", "-r", "--cached", ".", check=False)
        for p in pages.iterdir():
            if p.name != ".git":
                shutil.rmtree(p) if p.is_dir() else p.unlink()
        (pages / ".nojekyll").write_text("")
        (pages / "build-info.json").write_text(json.dumps({"sourceBranch": self.BRANCH, "sourceCommit": commit}))
        self.git(pages, "add", "-A")
        self.git(pages, "commit", "-q", "-m", "publish")
        self.git(pages, "push", "-q", "origin", "gh-pages")

    def test_fa013_clean_canonical_branch_passes(self):
        r = self.run_script()
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertIn("Preflight OK", r.stdout)

    def test_fa013_other_branch_refused(self):
        self.git(self.repo, "checkout", "-q", "-b", "claude/website")
        r = self.run_script()
        self.assertEqual(r.returncode, 1)
        self.assertIn("published only from 'release/v1.0-candidate'", r.stderr)

    def test_fa013_dirty_tree_refused(self):
        (self.repo / "untracked.txt").write_text("x")
        r = self.run_script()
        self.assertEqual(r.returncode, 1)
        self.assertIn("uncommitted or untracked", r.stderr)

    def test_fa013_behind_or_unpushed_refused(self):
        self.commit("second")
        self.git(self.repo, "push", "-q", "origin", self.BRANCH)
        self.git(self.repo, "reset", "-q", "--hard", "HEAD~1")
        r = self.run_script()
        self.assertEqual(r.returncode, 1)
        self.assertIn("is behind", r.stderr)
        self.git(self.repo, "reset", "-q", "--hard", f"origin/{self.BRANCH}")
        self.commit("third")
        r = self.run_script()
        self.assertEqual(r.returncode, 1)
        self.assertIn("not pushed", r.stderr)

    def test_fa013_older_source_than_recorded_refused_unless_forced(self):
        old = self.git(self.repo, "rev-parse", "HEAD").stdout.strip()
        new = self.commit("second")
        self.git(self.repo, "push", "-q", "origin", self.BRANCH)
        self.gh_pages_record(new)
        # roll the canonical branch back to the older commit (remote too), then try to publish it
        self.git(self.repo, "reset", "-q", "--hard", old)
        self.git(self.repo, "push", "-q", "-f", "origin", f"{old}:refs/heads/{self.BRANCH}")
        r = self.run_script()
        self.assertEqual(r.returncode, 1)
        self.assertIn("does not contain", r.stderr)
        r = self.run_script("--force-older")
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertIn("--force-older", r.stderr)

    def test_fa013_newer_source_than_recorded_passes(self):
        old = self.git(self.repo, "rev-parse", "HEAD").stdout.strip()
        self.gh_pages_record(old)
        self.commit("second")
        self.git(self.repo, "push", "-q", "origin", self.BRANCH)
        r = self.run_script()
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertIn(old, r.stdout)

    def test_fa013_script_uses_no_actions_deployment(self):
        text = (WEBSITE / "tools" / "publish-gh-pages.sh").read_text()
        self.assertIn('CANONICAL_BRANCH="release/v1.0-candidate"', text)
        self.assertIn(".nojekyll", text)
        commands = [l for l in text.splitlines() if not l.lstrip().startswith("#") and "echo" not in l]
        self.assertFalse([l for l in commands if "git push" in l], "the script must never push")
        workflow = (WEBSITE.parent / ".github" / "workflows" / "website.yml")
        if workflow.exists():
            self.assertNotIn("deploy-pages", workflow.read_text())


if __name__ == "__main__":
    unittest.main()
