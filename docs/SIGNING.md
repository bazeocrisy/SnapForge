# Code signing — preparation and status

**Status: not signed. Blocked on owner action** (requirement PKG-04). The 1.0 release candidate packages are
unsigned; Windows SmartScreen may warn. SnapForge never tells users to disable Microsoft Defender or SmartScreen;
users are told to verify the SHA-256 checksum instead. No self-signed certificate is used or presented as trusted.

## Options evaluated (researched 27 Nov 2026 — verify current terms before applying)

| Option | Cost | Who is the publisher | Fit for SnapForge | Notes |
|---|---|---|---|---|
| **SignPath Foundation** (free OSS programme, certificate issued to SignPath Foundation, signing via SignPath.io) | Free for OSS projects (reported) | **SignPath Foundation** (not the owner) | Good: MIT licence, public repo, fully automated CI build, no proprietary components | Reported requirements: OSI licence, public source, automated build from the repository, no proprietary code (system libraries allowed), MFA for all team members on GitHub and SignPath, attribution line "Free code signing provided by SignPath.io, certificate by SignPath Foundation", a published code-signing policy; some guides mention a prior release. Application: signpath.org (check the current form and criteria). |
| **Microsoft Artifact Signing** (formerly Trusted Signing, Azure) | Paid monthly tiers (Basic/Premium; billing starts when the account is created) | The verified individual/organisation | Possible; recurring cost needs owner approval (non-negotiable: no paid infrastructure without authorisation) | Identity validation; eligibility for individuals varies by region — check Microsoft's documentation. |
| Commercial OV/EV certificate (hardware token / cloud HSM) | Paid (yearly) | Owner | Needs purchase approval | EV no longer guarantees instant SmartScreen reputation. |

Code signing alone does **not** guarantee that SmartScreen never warns: reputation builds over time and downloads.

## What is already prepared

- Reproducible CI build from the tagged source commit (`ci.yml`), self-contained publish, package checksums and SBOM.
- `installer/SnapForge.iss` builds the installer from the same files; Inno Setup supports a `SignTool` directive, so
  the installer and uninstaller can be signed once a signing service is configured.
- `website/tools/verify-release.py` already verifies an Authenticode signature claim on Windows (`Get-AuthenticodeSignature`,
  status *Valid*, signer subject) and refuses a "signed" claim if the executable carries no signature.

## Steps once the owner chooses an option (SignPath Foundation example)

1. Owner: enable MFA on GitHub; apply at SignPath Foundation with the repository URL; publish a short code-signing
   policy (who may approve releases; that only CI builds from `main` tags are signed).
2. Add the SignPath GitHub integration: upload the unsigned release artifact from CI, request signing for
   `SnapForge.exe` (inside the ZIP) and the installer, download the signed artifact.
3. Sign in this order: `SnapForge.exe` → build installer (installer embeds signed exe) → sign installer.
4. Verify in CI on Windows: `Get-AuthenticodeSignature` = Valid for every signed file, certificate chain to a trusted
   root, RFC 3161 timestamp present, signer = expected publisher. Record the result in the release notes.
5. Website: set `release.signed = true` and `release.signer` in `website/config.json`; the release verifier checks the
   real downloaded asset before the site will show it.

## What must not happen

- No self-signed certificate presented as trusted; no "disable SmartScreen" advice.
- No public production executable while signing is unresolved without the owner's explicit acceptance of the risk.
