# Security policy

## Reporting a vulnerability

Please report security problems **privately**:

- Use GitHub's **Report a vulnerability** button on the repository's *Security* tab
  (`https://github.com/bazeocrisy/SnapForge/security/advisories/new`) once private vulnerability reporting is enabled
  by the owner.
- Until it is enabled, open a regular issue that only says you have a security report and asks for a private contact —
  **do not include technical details or proof-of-concept files in public.**

Include the SnapForge version and commit (Help → About), your Windows version, steps to reproduce and the impact.
Do not include other people's personal data or real screenshots containing private information.

Reports are handled on a best-effort basis by volunteers; there is no bug bounty.

## Supported versions

| Version | Supported |
|---|---|
| 1.0.x (after release) | Yes |
| Release candidates and development builds | Best effort only |

## Design summary

SnapForge has no network code, no telemetry and no accounts; it runs with standard user rights. The detailed threat
model is in [docs/THREAT-MODEL.md](docs/THREAT-MODEL.md) and [docs/SECURITY.md](docs/SECURITY.md).
