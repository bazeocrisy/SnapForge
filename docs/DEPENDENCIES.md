# Dependency inventory

Machine-readable SBOM: `SnapForge-<version>-sbom.cdx.json` (CycloneDX 1.5) in every CI package artifact, generated
by `tools/sbom/generate_sbom.py` from the actual build (runtime version = the SDK's bundled runtime pack, package
hashes included).

## Shipped in the application

| Component | Version | Source | Licence | Purpose | Redistribution obligations | Security notes | Maintenance |
|---|---|---|---|---|---|---|---|
| SnapForge | 1.0.0-rc.1 | this repository | MIT | the app | keep LICENSE | no network code | this project |
| Microsoft.NETCore.App.Runtime.win-x64 | 10.0.15 (from .NET SDK 10.0.1xx; `global.json` rolls forward to the latest feature band) | nuget.org (signed by Microsoft) | MIT | self-contained runtime | THIRD-PARTY-NOTICES | patched via new SnapForge builds; CI vulnerability audit | Microsoft, LTS/STS per .NET policy |
| Microsoft.WindowsDesktop.App.Runtime.win-x64 (WPF, `*_cor3.dll`) | 10.0.15 | nuget.org | MIT | UI framework | same | same | Microsoft |

## Installer only

| Component | Version | Licence | Purpose |
|---|---|---|---|
| Inno Setup | 6.x (runner image or pinned Chocolatey package 6.4.3 fallback) | Inno Setup License | setup/uninstall engine in `*-setup.exe` |

## Build and test only (not shipped)

| Package | Licence | Pinning |
|---|---|---|
| xunit, xunit.runner.visualstudio, xunit.analyzers | Apache-2.0 | `packages.lock.json`, locked restore in CI, nuget.org signature required (`nuget.config`) |
| Microsoft.NET.Test.Sdk, coverlet.collector | MIT | same |
| Playwright 1.63.0, axe-core 4.10.2 (website tests) | Apache-2.0 / MPL-2.0 | `website/tests/package-lock.json` (integrity hashes) |
| GitHub Actions: checkout, setup-dotnet, upload/download-artifact, setup-node, Pages actions | MIT | pinned to full commit SHAs in workflows |

## Windows components used (not shipped)

Windows.Graphics.Capture, Media Foundation (H.264/AAC), Windows.Media.Ocr, WASAPI, GDI/User32/Shell32. Updated by
Windows Update.

## Supply-chain controls

- Locked, signature-verified NuGet restore; vulnerability audit step (`dotnet list package --vulnerable`).
- Workflows run with `contents: read`; third-party actions pinned by SHA; no secrets used by build or test jobs.
- Package checksums (`SHA256SUMS.txt`) and SBOM per build; website release verifier checks the real published asset.
