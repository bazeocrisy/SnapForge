# Windows 10 / Windows 11 compatibility

Date: 2026-11-25. learn.microsoft.com was unreachable from the build environment; facts below come from the
official sources that were reachable: the .NET team's support documents on GitHub (dotnet/core),
dotnet.microsoft.com, microsoft.com, and Microsoft's documentation source repositories on GitHub
(MicrosoftDocs/sdk-api, MicrosoftDocs/win32, dotnet/docs).

## 1. What the .NET 10 runtime supports

Source: [dotnet/core — .NET 10 supported OS versions](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md) (last updated 2026-09-28).

| Windows | Supported by .NET 10 |
|---------|----------------------|
| Windows 11 26H1, 25H2, 24H2 (incl. Enterprise and IoT editions) | Yes |
| Windows 11 23H2 Enterprise | Yes |
| Windows 11 23H2 Home/Pro ("W") | **No** — out of support since 2025-11-11 |
| Windows 10 21H2 Enterprise LTSC / IoT LTSC, 1809 LTSC, 1607 LTSB | Yes |
| **Windows 10 22H2 Home/Pro** | **Not listed** (Windows 10 mainstream support ended; see §2) |
| Architectures | x64, Arm64, x86; x64 apps run under emulation on Windows 11 Arm64 |

.NET 10 is an LTS release supported until **14 November 2028**; self-contained apps (like SnapForge's
portable build) are responsible for shipping runtime updates themselves — Microsoft Update does not patch
them ([.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), updated 2026-11-10).
The .NET OS policy says support is typically removed when an OS leaves mainline support
([os-lifecycle-policy](https://github.com/dotnet/core/blob/main/os-lifecycle-policy.md)).

## 2. Windows lifecycle

Source: [microsoft.com — Windows 10 end of support](https://www.microsoft.com/en-us/windows/end-of-support).

* Windows 10 support ended on **14 October 2025**.
* Consumer Extended Security Updates (ESU) for Windows 10 22H2 provide security updates through
  **12 October 2027** (critical/important security fixes only, no technical support).
* Windows 11 per-version end dates: not verified (learn.microsoft.com release-information pages unreachable);
  the .NET list above reflects which Windows 11 versions are currently in support.

## 3. Windows APIs SnapForge uses and their documented minimums

| API / feature | Minimum (documented) | Source (Microsoft docs source on GitHub) |
|---------------|----------------------|-------------------------------------------|
| Per-Monitor V2 DPI awareness (manifest) | Windows 10 1703 | MicrosoftDocs/win32 `hidpi/high-dpi-desktop-application-development-on-windows.md` |
| WPF per-monitor DPI support | Windows 10 1607 | same |
| `GetDpiForMonitor` | Windows 8.1 | sdk-api `nf-shellscalingapi-getdpiformonitor.md` |
| `DwmGetWindowAttribute` / `DWMWA_EXTENDED_FRAME_BOUNDS`, `DWMWA_TRANSITIONS_FORCEDISABLED` | Windows Vista | sdk-api `ne-dwmapi-dwmwindowattribute.md` |
| `DWMWA_CLOAKED` | Windows 8 ("Windows 7 and earlier: not supported") | same |
| `DWMWA_USE_IMMERSIVE_DARK_MODE` (value 20) | **Windows 11 build 22000** | same |
| GDI `BitBlt`, `EnumDisplayMonitors`, `GetMonitorInfo`, `SetWindowPos`, `GetCursorPos` | Windows 2000/XP era | sdk-api |
| Self-contained single-file: native libraries are extracted to `%TEMP%\.net` only when `IncludeNativeLibrariesForSelfExtract` is set | — | dotnet/docs `core/deploying/single-file/overview.md` |

Consequences:

* **Dark title bar is Windows 11 only.** On Windows 10 the call is ignored and the standard light title bar
  appears; the rest of the UI is unaffected. (Undocumented attribute values for older builds are not used.)
* SnapForge no longer sets `IncludeNativeLibrariesForSelfExtract`: the five WPF native DLLs ship next to
  `SnapForge.exe`, so nothing is written to `%TEMP%` at start-up (relevant for locked-down PCs that block
  executables in temp folders).

## 4. SnapForge support statement (proposed for owner approval)

| Windows version | Status |
|-----------------|--------|
| Windows 11 24H2, 25H2, 26H1 (x64) | **Supported target** |
| Windows 11 on Arm64 | Runs via x64 emulation (supported by .NET); not tested; a native Arm64 build is possible later |
| Windows 10 Enterprise LTSC 2019/2021, IoT LTSC (x64) | Supported by .NET 10; not tested |
| Windows 10 22H2 Home/Pro (with or without ESU) | **Best effort** — not on .NET 10's supported list; APIs used are available since 1703 |
| Windows 10 earlier than 1703, Windows 8.1 and older | Not supported |

## 5. What has actually been run

| Environment | What ran | Result |
|-------------|----------|--------|
| GitHub Actions `windows-2025` (Windows Server 2025, build 10.0.26100 — same build family as Windows 11 24H2) | Build, all automated tests incl. real GDI capture, clipboard and real-input UI tests; self-test of the portable exe with .NET hidden | See latest CI run |
| Physical Windows 10 or Windows 11 client PC | **Nothing yet** | Manual acceptance testing pending |
