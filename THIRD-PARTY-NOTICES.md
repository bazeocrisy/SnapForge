# Third-party notices

SnapForge is licensed under the MIT License (see `LICENSE`).

SnapForge itself has **no third-party runtime dependencies**. It uses only the .NET runtime and
Windows Presentation Foundation (WPF), plus Win32 APIs that ship with Windows.

## Redistributed in the portable (self-contained) build

The portable build bundles the .NET runtime and WPF so that it runs without installing .NET.

| Component | License | Source |
|-----------|---------|--------|
| .NET Runtime (Microsoft.NETCore.App) | MIT | https://github.com/dotnet/runtime |
| Windows Desktop Runtime / WPF (Microsoft.WindowsDesktop.App) | MIT | https://github.com/dotnet/wpf |

The .NET runtime includes components under other permissive licenses; see
https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT and
https://github.com/dotnet/wpf/blob/main/THIRD-PARTY-NOTICES.TXT.

Note: the WPF build bundles `D3DCompiler_47_cor3.dll`, `PenImc_cor3.dll`, `PresentationNative_cor3.dll`,
`vcruntime140_cor3.dll` and `wpfgfx_cor3.dll`, which Microsoft distributes as part of the Windows Desktop
runtime package under the .NET license terms above.

## Development and test only (not shipped)

| Package | License |
|---------|---------|
| xunit, xunit.runner.visualstudio | Apache-2.0 |
| Microsoft.NET.Test.Sdk | MIT |
| coverlet.collector | MIT |
