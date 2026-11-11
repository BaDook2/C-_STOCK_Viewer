#!/usr/bin/env python3
"""Build, ad-hoc sign and ZIP a self-contained macOS application."""
import argparse
import plistlib
import re
import shutil
import subprocess
import sys
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", default="2.2.4")
    parser.add_argument("--runtime", choices=["osx-arm64", "osx-x64"], required=True)
    parser.add_argument("--output", type=Path, default=Path("artifacts/release"))
    args = parser.parse_args()
    if sys.platform != "darwin":
        parser.error("macOS is required for code signing and bundle ZIP creation")
    if not re.fullmatch(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", args.version):
        parser.error("version must have the form 2.2.4")
    root = Path(__file__).resolve().parents[1]
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    bundle = output / "StockViewer-macOS.app"
    archive = output / f"StockViewer-macOS-{args.runtime.removeprefix('osx-')}.zip"
    payload = output / ".payload"
    if bundle.exists() or archive.exists() or payload.exists():
        parser.error("output already contains this app or archive; choose an empty output directory")
    subprocess.run([
        "dotnet", "publish", str(root / "StockViewer.App/StockViewer.App.csproj"),
        "-c", "Release", "-r", args.runtime, "--self-contained", "true", "--disable-build-servers", "-m:1",
        "-p:PublishTrimmed=false", "-p:DebugType=None", "-p:DebugSymbols=false", f"-p:Version={args.version}",
        "-o", str(payload),
    ], check=True)
    contents = bundle / "Contents"
    shutil.copytree(payload, contents / "MacOS", copy_function=shutil.copy)
    (contents / "Resources").mkdir()
    with (contents / "Info.plist").open("wb") as stream:
        plistlib.dump({
            "CFBundleDevelopmentRegion": "ko", "CFBundleDisplayName": "StockViewer",
            "CFBundleName": "StockViewer", "CFBundleExecutable": "StockViewer",
            "CFBundleIdentifier": "com.badook2.stockviewer", "CFBundleInfoDictionaryVersion": "6.0",
            "CFBundlePackageType": "APPL", "CFBundleShortVersionString": args.version,
            "CFBundleVersion": args.version, "NSHighResolutionCapable": True, "NSPrincipalClass": "NSApplication",
        }, stream)
    (contents / "PkgInfo").write_bytes(b"APPL????")
    subprocess.run(["xattr", "-cr", str(bundle)], check=True)
    subprocess.run(["codesign", "--force", "--deep", "--sign", "-", str(bundle)], check=True)
    subprocess.run(["codesign", "--verify", "--deep", "--strict", str(bundle)], check=True)
    subprocess.run(["ditto", "-c", "-k", "--sequesterRsrc", "--keepParent", str(bundle), str(archive)], check=True)
    shutil.rmtree(payload)
    print(archive)


if __name__ == "__main__":
    main()
