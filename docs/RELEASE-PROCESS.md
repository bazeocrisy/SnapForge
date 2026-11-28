# Release process

Simple, owner-operated, no paid infrastructure. **Nothing here is automatic: a public release needs the owner's
explicit approval.**

## Versions

- `MAJOR.MINOR.PATCH` (SemVer). Release candidates: `1.0.0-rc.N`; final: `1.0.0`. Set in `Directory.Build.props`
  (`<Version>`), and set `<SnapForgeChannel>` to `Release` only for an approved release build.
- Windows file version: `MAJOR.MINOR.PATCH.<CI run number>`. Help → About shows version, build number, commit,
  branch, channel and architecture; the installed file's *Properties → Details* show the same version.
- Tags: `v1.0.0-rc.1`, `v1.0.0` on the exact tested commit.

## Steps

1. **Freeze**: all P0 requirements implemented with passing evidence; no open Critical/High defects
   (`docs/DEFECT-REGISTER.md`); manual acceptance done (`docs/MANUAL-ACCEPTANCE-1.0.md`).
2. **Build**: CI on the release branch produces `SnapForge-<ver>-win-x64-portable.zip`,
   `SnapForge-<ver>-win-x64-setup.exe`, `SnapForge-<ver>-sbom.cdx.json` and `SHA256SUMS.txt` (artifact
   `SnapForge-<ver>-packages-<sha>`). Clean runner, locked restore, warnings as errors.
3. **Verify the packages from that run**: CI already verifies checksums, runs the portable build and the installed app
   with .NET removed, installs/upgrades/uninstalls. Owner: download the artifact, verify `SHA256SUMS.txt`
   (`Get-FileHash`), run manual acceptance on a physical PC.
4. **Sign** (when available, see `docs/SIGNING.md`): sign exe → rebuild installer → sign installer → verify signatures.
5. **Release notes**: `docs/RELEASE-NOTES-<ver>.md` (features, fixes, known limitations, checksums, signing status).
6. **Owner approval** (written). Merge the release branch into `main` (owner), tag the commit.
7. **Publish**: create a GitHub Release for the tag; upload exactly the verified package files and `SHA256SUMS.txt`.
8. **Verify the published release**: `python website/tools/verify-release.py` (Windows if signed) — checks the real
   downloadable asset, size, checksum, contents and signing claim; writes `website/release-verification.json`.
9. **Website**: fill `website/config.json → release` (with `ownerApproved: true` only after approval), rebuild,
   `bash website/tools/publish-gh-pages.sh`, `git push origin gh-pages`.

## Rollback

See [ROLLBACK.md](ROLLBACK.md).
