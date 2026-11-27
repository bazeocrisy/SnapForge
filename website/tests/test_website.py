"""Tests for website/build.py and website/tools/verify-release.py (standard library only).

    python3 -B -m unittest discover -s website/tests -v

The release verifier is exercised against a local fake of the GitHub API and release download (no network).
"""
from __future__ import annotations

import hashlib
import http.server
import importlib.util
import io
import json
import shutil
import struct
import subprocess
import sys
import tempfile
import threading
import unittest
import zipfile
from pathlib import Path

WEBSITE = Path(__file__).resolve().parent.parent
TAG = "v1.0.0"
NAME = "SnapForge-1.0.0-win-x64.zip"
DL = f"https://github.com/bazeocrisy/SnapForge/releases/download/{TAG}/{NAME}"
NOTES = f"https://github.com/bazeocrisy/SnapForge/releases/tag/{TAG}"


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


build = load("sf_build", WEBSITE / "build.py")
verifier = load("sf_verify", WEBSITE / "tools" / "verify-release.py")


def make_exe(signed: bool) -> bytes:
    """Minimal PE32+ image: enough headers for the certificate-table check."""
    data = bytearray(0x200)
    data[0:2] = b"MZ"
    struct.pack_into("<I", data, 0x3C, 0x80)
    data[0x80:0x84] = b"PE\0\0"
    opt = 0x80 + 24
    struct.pack_into("<H", data, opt, 0x20B)
    struct.pack_into("<II", data, opt + 112 + 32, 0x180 if signed else 0, 0x40 if signed else 0)
    return bytes(data)


def make_zip(signed=False, sums=True, tamper=False) -> bytes:
    files = {"SnapForge.exe": make_exe(signed), "LICENSE.txt": b"MIT License\n", "THIRD-PARTY-NOTICES.md": b"notices\n"}
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w") as z:
        for n, b in files.items():
            z.writestr(n, b)
        if sums:
            lines = [f"{hashlib.sha256(b).hexdigest()}  {n}" for n, b in sorted(files.items())]
            if tamper:
                lines[0] = "0" * 64 + lines[0][64:]
            z.writestr("SHA256SUMS.txt", "\n".join(lines) + "\n")
    return buf.getvalue()


class FakeGitHub:
    def __init__(self, release: dict, asset_bytes: bytes):
        self.release, self.asset = release, asset_bytes
        fake = self

        class H(http.server.BaseHTTPRequestHandler):
            def log_message(self, *a):
                pass

            def do_GET(self):
                if self.path == f"/repos/bazeocrisy/SnapForge/releases/tags/{TAG}" and fake.release is not None:
                    body = json.dumps(fake.release).encode()
                elif self.path == f"/dl/{TAG}/{NAME}":
                    body = fake.asset
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


def config_for(asset: bytes, **over) -> dict:
    rel = {"ownerApproved": True, "version": "1.0.0", "date": "2027-01-15", "downloadUrl": DL, "fileName": NAME,
           "sizeBytes": len(asset), "sha256": hashlib.sha256(asset).hexdigest(), "signed": False, "signer": None,
           "releaseNotesUrl": NOTES, "packageType": "portable"}
    rel.update(over)
    return {"siteUrl": "https://bazeocrisy.github.io/SnapForge/", "repositoryUrl": "https://github.com/bazeocrisy/SnapForge",
            "security": {"privateReporting": {"url": build.PRIVATE_REPORTING_URL, "verified": True}},
            "release": rel, "donations": {}}


def gh_release(asset: bytes, **over) -> dict:
    r = {"tag_name": TAG, "draft": False, "prerelease": False, "html_url": NOTES, "published_at": "2027-01-15T10:00:00Z",
         "assets": [{"name": NAME, "state": "uploaded", "size": len(asset), "browser_download_url": DL,
                     "digest": "sha256:" + hashlib.sha256(asset).hexdigest()}]}
    r.update(over)
    return r


class VerifierTests(unittest.TestCase):
    def run_verify(self, cfg, release, asset):
        fake = FakeGitHub(release, asset)
        try:
            with tempfile.TemporaryDirectory() as tmp:
                return verifier.verify(cfg, api_base=fake.base,
                                       rewrite=("https://github.com/bazeocrisy/SnapForge/releases/download/",
                                                fake.base + "/dl/"), work=Path(tmp))
        finally:
            fake.close()

    def assertFails(self, text, cfg, release, asset):
        with self.assertRaises(verifier.Failed) as cm:
            self.run_verify(cfg, release, asset)
        self.assertIn(text, str(cm.exception))

    def test_genuine_unsigned_release_is_verified(self):
        asset = make_zip()
        rec = self.run_verify(config_for(asset), gh_release(asset), asset)
        self.assertEqual(rec["result"], "verified")
        self.assertEqual(rec["packageContents"], ["LICENSE.txt", "SHA256SUMS.txt", "SnapForge.exe", "THIRD-PARTY-NOTICES.md"])
        self.assertTrue(rec["innerChecksumsVerified"])
        self.assertFalse(rec["signing"]["certificateTablePresent"])

    def test_missing_release(self):
        asset = make_zip()
        self.assertFails("not found", config_for(asset), None, asset)

    def test_draft_and_prerelease_rejected(self):
        asset = make_zip()
        self.assertFails("draft", config_for(asset), gh_release(asset, draft=True), asset)
        self.assertFails("pre-release", config_for(asset), gh_release(asset, prerelease=True), asset)

    def test_wrong_date_or_notes_page(self):
        asset = make_zip()
        self.assertFails("published on", config_for(asset, date="2027-01-14"), gh_release(asset), asset)
        self.assertFails("release page", config_for(asset), gh_release(asset, html_url=NOTES + "x"), asset)

    def test_asset_name_must_match_exactly(self):
        asset = make_zip()
        rel = gh_release(asset)
        rel["assets"][0]["name"] = NAME.lower()
        self.assertFails("exactly one asset", config_for(asset), rel, asset)

    def test_asset_not_fully_uploaded(self):
        asset = make_zip()
        rel = gh_release(asset)
        rel["assets"][0]["state"] = "starter"
        self.assertFails("not 'uploaded'", config_for(asset), rel, asset)

    def test_size_mismatch_in_api_and_in_bytes(self):
        asset = make_zip()
        self.assertFails("GitHub reports", config_for(asset, sizeBytes=len(asset) + 1), gh_release(asset), asset)
        rel = gh_release(asset)
        rel["assets"][0].pop("digest")
        served = asset + b"x"                      # API metadata right, bytes served differ
        self.assertFails("downloaded", config_for(asset), rel, served)

    def test_checksum_mismatch(self):
        asset = make_zip()
        cfg = config_for(asset, sha256="0" * 64)
        self.assertFails("digest", cfg, gh_release(asset), asset)
        rel = gh_release(asset)
        rel["assets"][0].pop("digest")
        self.assertFails("SHA-256", cfg, rel, asset)

    def test_inner_checksum_list_must_match(self):
        asset = make_zip(tamper=True)
        self.assertFails("SHA256SUMS.txt checksum mismatch", config_for(asset), gh_release(asset), asset)

    def test_unsigned_claim_but_executable_is_signed(self):
        asset = make_zip(signed=True)
        self.assertFails("carries an Authenticode signature", config_for(asset), gh_release(asset), asset)

    def test_signed_claim_but_executable_is_unsigned(self):
        asset = make_zip(signed=False)
        cfg = config_for(asset, signed=True, signer="Example Publisher")
        self.assertFails("no Authenticode signature", cfg, gh_release(asset), asset)

    @unittest.skipIf(sys.platform == "win32", "on Windows the real Authenticode check runs instead")
    def test_signed_claim_cannot_be_validated_off_windows(self):
        asset = make_zip(signed=True)
        cfg = config_for(asset, signed=True, signer="Example Publisher")
        self.assertFails("only be validated on Windows", cfg, gh_release(asset), asset)

    def test_actions_artifact_url_rejected(self):
        asset = make_zip()
        cfg = config_for(asset, downloadUrl="https://github.com/bazeocrisy/SnapForge/actions/runs/1/artifacts/2")
        self.assertFails("not a GitHub Release asset URL", cfg, gh_release(asset), asset)


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
        return {"result": "verified", "checkedAt": "2027-01-15T12:00:00+00:00",
                "release": {k: cfg["release"][k] for k in verifier.FIELDS},
                "packageContents": ["LICENSE.txt", "SHA256SUMS.txt", "SnapForge.exe", "THIRD-PARTY-NOTICES.md"],
                "innerChecksumsVerified": True}

    def test_owner_approved_alone_is_refused(self):
        cfg = config_for(make_zip())
        c, v = self.write(cfg)
        with self.assertRaises(build.ConfigError) as cm:
            build.load_config(c, v)
        self.assertIn("release-verification.json is missing", str(cm.exception))

    def test_record_must_match_config(self):
        cfg = config_for(make_zip())
        rec = self.record(cfg)
        rec["release"]["sha256"] = "f" * 64
        c, v = self.write(cfg, rec)
        with self.assertRaises(build.ConfigError) as cm:
            build.load_config(c, v)
        self.assertIn("does not match config.json for release.sha256", str(cm.exception))

    def test_private_reporting_required_before_release(self):
        cfg = config_for(make_zip())
        cfg["security"]["privateReporting"]["verified"] = False
        c, v = self.write(cfg, self.record(cfg))
        with self.assertRaises(build.ConfigError) as cm:
            build.load_config(c, v)
        self.assertIn("private vulnerability reporting", str(cm.exception))

    def test_verified_release_builds_and_lists_only_verified_contents(self):
        cfg = config_for(make_zip())
        c, v = self.write(cfg, self.record(cfg))
        loaded = build.load_config(c, v)
        out = self.tmp / "site"
        build.build(loaded, out)
        page = (out / "download.html").read_text()
        self.assertIn("<code>THIRD-PARTY-NOTICES.md</code>", page)
        self.assertIn("Download for Windows</a>", page)
        self.assertIn("Report a vulnerability form", (out / "support.html").read_text())
        self.assertEqual(build.validate(out), [])

    def test_unconfigured_site_promises_no_package_contents(self):
        loaded = build.load_config(WEBSITE / "config.json", self.tmp / "none.json")
        out = self.tmp / "site"
        build.build(loaded, out)
        page = (out / "download.html").read_text()
        self.assertIn("will be listed here once a release has been published and verified", page)
        self.assertNotIn("SHA256SUMS.txt</code> with the checksum", page)
        self.assertIn("disabled", page)
        self.assertIn("Private reporting is not set up yet", (out / "support.html").read_text())

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


if __name__ == "__main__":
    unittest.main()
