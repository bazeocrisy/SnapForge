# Maintenance plan

Designed for one maintainer with no paid infrastructure: GitHub (free for public repositories) for code, issues, CI
and Pages.

| Activity | How | Cadence |
|---|---|---|
| .NET runtime / SDK updates | Bump `global.json` if needed; CI publishes with the SDK's latest runtime patch; release a patch build when a .NET security update ships | Monthly check (Patch Tuesday) |
| Dependency updates | No runtime packages; update test packages and pinned Actions SHAs; regenerate `packages.lock.json` with `--force-evaluate`; review diffs | Quarterly |
| Security monitoring | CI vulnerability audit on every push; GitHub Dependabot alerts (enable in Settings); private vulnerability reports | Continuous |
| Bug reports | GitHub Issues with the template in CONTRIBUTING.md; label `bug`, severity per DEFECT-REGISTER.md | Weekly triage |
| Feature requests | Issues labelled `enhancement`; roadmap on the website | Monthly |
| Regression testing | Full CI on every push (unit, Windows UI, packaged-build self-tests, installer); manual acceptance before releases | Every change / release |
| Versioning | SemVer; patch = fixes, minor = features, major = breaking Library format changes (avoid) | Per release |
| Compatibility policy | Supported: Windows 11 24H2+ x64. Others best-effort, documented | Review yearly |
| Backup and recovery | The repository is the source of truth; keep a local clone; release artifacts attached to GitHub Releases (permanent) | Per release |
| Release cadence | Patch releases as needed; feature releases when ready | — |
| Website updates | Edit `website/src`, `python3 website/build.py`, commit, `publish-gh-pages.sh`, push `gh-pages` | As needed |
| Artifact retention | CI artifacts expire after 14–30 days — never link them publicly; permanent files only on GitHub Releases | — |

Keep it simple: no servers, no databases, no paid services. Any new dependency or service requires a written
justification and owner approval.
