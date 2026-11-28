#!/usr/bin/env python3
"""Generates a CycloneDX 1.5 SBOM (JSON) for a SnapForge Windows package (standard library only).

    python tools/sbom/generate_sbom.py --version 1.0.0-rc.1 --commit <sha> --runtime 10.0.15 \
        --package artifacts/release/SnapForge-1.0.0-rc.1-win-x64-portable.zip --out artifacts/release/SnapForge-1.0.0-rc.1.cdx.json

Components listed are what the package actually contains: SnapForge itself (MIT), the self-contained .NET runtime and
Windows Desktop runtime (WPF) packs that `dotnet publish --self-contained` bundles (MIT), and — for the installer —
the Inno Setup installer engine. SnapForge references no third-party NuGet packages at run time (packages.lock.json
of the shipped projects); test-only packages are listed as excluded for transparency. Windows components that are
used but not shipped (Media Foundation, Windows.Media.Ocr, Windows.Graphics.Capture, WASAPI) are listed as
operating-system dependencies, not as redistributed code.
"""
import argparse, hashlib, json, pathlib, uuid, datetime

def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", required=True)
    ap.add_argument("--commit", required=True)
    ap.add_argument("--runtime", required=True, help=".NET runtime pack version bundled by publish")
    ap.add_argument("--inno", default="6", help="Inno Setup version used for the installer")
    ap.add_argument("--package", action="append", default=[], help="package file(s) to hash")
    ap.add_argument("--out", required=True)
    a = ap.parse_args()
    lic = lambda i: [{"license": {"id": i}}]
    components = [
        {"type": "framework", "bom-ref": "dotnet-runtime", "name": "Microsoft.NETCore.App.Runtime.win-x64", "version": a.runtime,
         "supplier": {"name": "Microsoft"}, "licenses": lic("MIT"), "purl": f"pkg:nuget/Microsoft.NETCore.App.Runtime.win-x64@{a.runtime}",
         "description": "Self-contained .NET runtime bundled into SnapForge.exe (third-party notices: https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT)"},
        {"type": "framework", "bom-ref": "windowsdesktop-runtime", "name": "Microsoft.WindowsDesktop.App.Runtime.win-x64", "version": a.runtime,
         "supplier": {"name": "Microsoft"}, "licenses": lic("MIT"), "purl": f"pkg:nuget/Microsoft.WindowsDesktop.App.Runtime.win-x64@{a.runtime}",
         "description": "WPF runtime incl. D3DCompiler_47_cor3.dll, PenImc_cor3.dll, PresentationNative_cor3.dll, vcruntime140_cor3.dll, wpfgfx_cor3.dll (third-party notices: https://github.com/dotnet/wpf/blob/main/THIRD-PARTY-NOTICES.TXT)"},
        {"type": "application", "bom-ref": "inno-setup", "name": "Inno Setup (installer engine)", "version": a.inno,
         "supplier": {"name": "Jordan Russell / Martijn Laan"}, "licenses": [{"license": {"name": "Inno Setup License", "url": "https://jrsoftware.org/files/is/license.txt"}}],
         "description": "Only in the -setup.exe installer: the setup/uninstall engine. Not part of the portable ZIP."},
    ]
    os_deps = ["Windows.Graphics.Capture", "Media Foundation (H.264/AAC encoders and decoders)", "Windows.Media.Ocr", "WASAPI (Core Audio)", "GDI / User32 / Shell32"]
    for name in os_deps:
        components.append({"type": "library", "bom-ref": "os-" + name.split()[0].lower(), "name": name, "scope": "excluded",
                           "supplier": {"name": "Microsoft (Windows)"}, "description": "Part of Windows; used, not redistributed."})
    for test_pkg, l in [("xunit", "Apache-2.0"), ("xunit.runner.visualstudio", "Apache-2.0"), ("Microsoft.NET.Test.Sdk", "MIT"), ("coverlet.collector", "MIT")]:
        components.append({"type": "library", "name": test_pkg, "scope": "excluded", "licenses": lic(l), "description": "Test-only; not shipped."})
    for p in a.package:
        path = pathlib.Path(p)
        components.append({"type": "file", "name": path.name, "hashes": [{"alg": "SHA-256", "content": sha256(path)}]})
    bom = {
        "bomFormat": "CycloneDX", "specVersion": "1.5", "serialNumber": f"urn:uuid:{uuid.uuid4()}", "version": 1,
        "metadata": {
            "timestamp": datetime.datetime.now(datetime.timezone.utc).replace(microsecond=0).isoformat(),
            "component": {"type": "application", "bom-ref": "snapforge", "name": "SnapForge", "version": a.version,
                          "licenses": lic("MIT"), "supplier": {"name": "bazeocrisy"},
                          "externalReferences": [{"type": "vcs", "url": f"https://github.com/bazeocrisy/SnapForge/tree/{a.commit}"}]},
            "properties": [{"name": "snapforge:commit", "value": a.commit}],
        },
        "components": components,
        "dependencies": [{"ref": "snapforge", "dependsOn": ["dotnet-runtime", "windowsdesktop-runtime"]}],
    }
    pathlib.Path(a.out).write_text(json.dumps(bom, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {a.out} with {len(components)} components")

if __name__ == "__main__":
    main()
