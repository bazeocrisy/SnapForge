# Code signing — preparation and status (SF-001)

**Status: not signed. Blocked on an owner decision** (requirement PKG-04). Release candidates (rc.1, rc.2) are
unsigned; Windows SmartScreen will probably warn and Smart App Control may block them. SnapForge never tells users to
disable Microsoft Defender, SmartScreen or Smart App Control and gives no bypass instructions; users are told to run
only builds from the project's CI page whose SHA-256 they verified, or to wait for the signed release. **No
self-signed certificate is used or presented as trusted**, now or as a stop-gap.

Code signing identifies the publisher; it does **not** guarantee that SmartScreen never warns. SmartScreen also uses
reputation that builds up with downloads over time — no option below promises warning-free installs.

## Sources (accessed 27 November 2026)

Direct page fetches of `signpath.org` and `about.signpath.io` were **blocked by the network policy of the build
environment** used for this research, so the statements below come from those official pages as returned by web
search on 27 Nov 2026 (titles/excerpts), not from a full read. **The owner must re-read the live pages before
applying**; the SignPath terms page itself says parts (code of conduct) are still a draft.

| # | Source (official unless noted) | Used for |
|---|---|---|
| S1 | SignPath Foundation — Terms / conditions for OSS projects: https://signpath.org/terms | Eligibility, team/MFA rules, code signing policy page, attribution, privacy |
| S2 | SignPath Foundation home / about: https://signpath.org/ , https://signpath.org/about | Programme overview, Foundation is the certificate holder |
| S3 | SignPath.io — open-source product page: https://about.signpath.io/product/open-source (also https://signpath.io/product/open-source) | OSS offering |
| S4 | SignPath docs — GitHub trusted build system: https://docs.signpath.io/trusted-build-systems/github | GitHub Actions integration (`submit-signing-request` action) |
| S5 | SignPath docs — Origin verification: https://docs.signpath.io/origin-verification ; Trusted build systems: https://docs.signpath.io/trusted-build-systems ; Projects / signing policies: https://docs.signpath.io/projects | Origin verification, signing policies, approvals |
| S6 | Microsoft — Artifact Signing (formerly Trusted Signing) pricing: https://azure.microsoft.com/pricing/details/artifact-signing/ and product page https://azure.microsoft.com/products/artifact-signing | Alternative: cost model |
| S7 | Example (non-authoritative) published policy pages of SignPath-signed OSS projects, e.g. https://pandoc.org/code-signing-policy.html , https://inkstitch.org/code-signing-policy | Shape of a policy page (roles list, attribution line) |
| S8 | GitHub Docs — Downloading workflow artifacts: https://docs.github.com/en/actions/how-tos/manage-workflow-runs/download-workflow-artifacts | Test-build wording: artifacts need sign-in + read access; expire |

## Option 1 (recommended to evaluate first): SignPath Foundation — free for OSS

What the official pages say (S1–S5, as retrieved by search; verify):

- **Eligibility:** an open-source project under an **OSI-approved licence** (SnapForge: MIT), source in a
  **public repository**, and **no proprietary components** in what is signed (system libraries are allowed — SnapForge
  uses only Windows APIs and the MIT-licensed .NET runtime; no third-party run-time packages).
- **Publisher:** the certificate is issued to **SignPath Foundation**, which therefore appears as the publisher in
  Windows (not the owner). The Foundation sets technical constraints and project rules.
- **Team:** the team that signs must be the team that develops and maintains the repository (owns the repository).
  **MFA for all team members** on SignPath and on the source repository (GitHub). Code signing **roles** must be
  assigned (in practice published as "committers/reviewers" and "approvers").
- **Code signing policy page:** the project's home page must have a page titled "Code signing policy" with the
  attribution **"Free code signing provided by SignPath.io, certificate by SignPath Foundation"**, the team roles, and
  privacy information (the terms ask projects to respect user privacy and to state the privacy policies of other
  components/services the app uses, if users are affected). Draft below and in
  `website/src/pages/code-signing.html` (published as a noindex draft, not linked, and stating SnapForge is **not**
  signed).
- **Build origin:** OSS signing requires **origin verification**: signing requests must come from a **trusted build
  system** (GitHub Actions) for the configured repository/branch; for OSS, all workflow jobs leading to the signing
  request must run on **GitHub-hosted runners** (S4/S5). This rules out signing anything built on a personal PC.
- **Conduct:** if complaints arise, the project must help investigate. Malware, potentially unwanted behaviour and
  undisclosed data collection are not allowed (SnapForge has no network code).
- Some third-party guides mention the Foundation looks for an existing release/user base; this is not confirmed from
  the official terms text — **ask SignPath** if a project without a public release is accepted.

GitHub Actions integration (S4): upload the unsigned files with `actions/upload-artifact`, then a step
`signpath/github-action-submit-signing-request` (current major version per the docs; the search showed `@v3` in current
docs and `@v1` in an older copy — pin by commit SHA as `ci.yml` does for other actions) with `api-token` (a repository
secret created by the owner), `organization-id`, `project-slug`, `signing-policy-slug` (e.g. `release-signing`),
optional `artifact-configuration-slug`, the GitHub artifact id, and `wait-for-completion` + an output directory to
download the signed artifact. The **artifact configuration** (defined in SignPath) describes which files inside the
uploaded artifact are signed — here `SnapForge.exe` in the portable folder, and in a second request the installer.

### Step-by-step plan

| # | Step | Who | Notes |
|---|---|---|---|
| 1 | Decide: SignPath Foundation (Foundation is publisher) vs. paid options below | **Owner** | Free, but publisher name is "SignPath Foundation" |
| 2 | Enable MFA on GitHub for every maintainer | Owner | Required (S1) |
| 3 | Merge `release/v1.0-candidate` into `main` after approval; signed releases only from tags on `main` | Owner / reviewer | Policy says what is signed |
| 4 | Complete the code signing policy page (roles, attribution only once accepted) and link it from the site footer | Maintainer, owner approves text | Draft: `website/src/pages/code-signing.html` |
| 5 | Apply via the form linked on signpath.org with the repository URL, licence, build description and policy URL | **Owner** (submits; accepts the Foundation's terms) | Owner's identity/contact goes to SignPath, not into this repo |
| 6 | After acceptance: in SignPath, create the project, link the GitHub trusted build system, define artifact configurations and a `release-signing` policy with **manual approval** by the owner | Owner (approver) + maintainer | Owner creates the API token as a GitHub secret; nothing secret in the repo |
| 7 | CI: a separate, tag-triggered release job on GitHub-hosted runners: build → upload unsigned artifact → submit signing request for `SnapForge.exe` → download signed exe → build installer from it (Inno Setup embeds the signed exe) → submit installer for signing → download → ZIP the signed portable folder → SHA256SUMS + SBOM | Maintainer (PR), owner reviews | Order matters: exe first, installer last. Inno's uninstaller is generated inside the installer; check whether it is signed too (Inno `SignTool`/`SignedUninstaller`), else document it |
| 8 | CI verification on Windows: `Get-AuthenticodeSignature` = Valid for setup.exe and SnapForge.exe, exact subject, thumbprint, timestamp present; record in the release notes | Maintainer | Same checks as `website/tools/verify-release.py` |
| 9 | Owner approves each signing request in SignPath (checks run URL + commit), then publishes the GitHub Release | **Owner** | |
| 10 | Website: fill `config.json → release` (publisher subject + thumbprints, per-asset `signed: true`); run `verify-release.py` **on Windows** with `innoextract`; rebuild; publish | Maintainer, owner sets `ownerApproved` | Verifier refuses signed claims off Windows |

### Draft "Code signing policy" text (for the project website; not in effect)

> **Code signing policy**
>
> Free code signing provided by SignPath.io, certificate by SignPath Foundation. *(Only once the project has been
> accepted.)*
>
> **What is signed.** Only `SnapForge.exe` and the installer `SnapForge-<version>-win-x64-setup.exe`, built by the
> project's GitHub Actions release workflow on GitHub-hosted runners from a tagged commit on `main` of the public
> repository https://github.com/bazeocrisy/SnapForge. Nothing built elsewhere, and no third-party program, is signed.
>
> **Team roles.** Committers and reviewers: bazeocrisy (repository owner); outside contributions are reviewed before
> merging. Approvers: bazeocrisy. Every signing request is approved manually after checking the workflow run and
> commit. All members use multi-factor authentication on GitHub and SignPath.
>
> **Privacy.** SnapForge does not transfer any information to other networked systems: it has no network code, no
> telemetry, accounts, update checks or uploads. Captures stay on the user's PC. See the privacy page.
>
> Signing identifies the publisher; Windows SmartScreen may still warn until the app has built up reputation.

## Option 2: Microsoft Artifact Signing (formerly Trusted Signing) — **requires spending and identity verification: owner decision**

- Azure subscription; Basic tier reported at about **US$9.99/month** for up to 5,000 signatures (+$0.005 each beyond),
  billed for the full month from account creation (S6 — confirm current price in the owner's region/currency).
- **Identity validation** of the owner (individual) or organisation by Microsoft; eligibility for **individual
  developers** and supported countries are restricted and change over time — not confirmed by this research; check the
  current Artifact Signing documentation/FAQ.
- Publisher shown = the validated owner name. Integrates with GitHub Actions via Microsoft's signing action/`signtool`
  dlib; same signing order and verification as above.
- Recurring cost needs explicit owner approval (no paid infrastructure without authorisation).

## Option 3: buy an OV code-signing certificate — **requires spending and identity verification: owner decision**

- Yearly cost from a commercial CA; since 2023 private keys must live on a hardware token or a cloud HSM, which
  complicates CI signing (cloud HSM services cost extra). Owner identity (and address) is validated and appears as the
  publisher. EV certificates no longer give instant SmartScreen reputation.

## What is already prepared

- Reproducible CI build from the source commit (`ci.yml`), self-contained publish, package checksums, SBOM.
- `installer/SnapForge.iss` (Inno Setup) can sign installer/uninstaller via its `SignTool` directive once a service is
  configured.
- `website/tools/verify-release.py` (FA-012) verifies signed claims on Windows only: PowerShell
  `Get-AuthenticodeSignature` status **Valid** (WinVerifyTrust: hash, trusted chain, not revoked), signature type
  Authenticode, **exact** signer subject = `release.publisher.subject`, thumbprint in
  `release.publisher.thumbprints`, Code Signing EKU, a **timestamp countersignature** whose chain builds; for the
  installer both the setup file and its embedded `SnapForge.exe` (via `innoextract`), which must equal the portable
  ZIP's. An optional manual cross-check is `signtool verify /pa /all /tw /v <file>`.
- `website/src/pages/code-signing.html`: draft policy page (noindex, not in navigation, states "not code-signed").

## What must not happen

- No self-signed certificate presented as trusted; no "disable SmartScreen/Defender" or "Run anyway" instructions.
- No claim on the website that anything is signed until `verify-release.py` has verified it on Windows.
- No public production executable while signing is unresolved without the owner's explicit acceptance of the risk.
