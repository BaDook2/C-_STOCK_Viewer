#!/usr/bin/env python3
"""Build the small macOS mouse observer for the selected application runtime."""
import argparse
import platform
import subprocess
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--runtime", default="")
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args()
architecture = {"osx-arm64": "arm64", "osx-x64": "x86_64"}.get(args.runtime, platform.machine())
if architecture not in ("arm64", "x86_64"):
    parser.error("unsupported macOS architecture")
source = Path(__file__).resolve().parents[1] / "StockViewer.App/Native/DesktopSelection.m"
args.output.parent.mkdir(parents=True, exist_ok=True)
subprocess.run([
    "xcrun", "clang", "-dynamiclib", "-fobjc-arc", "-O2", "-arch", architecture,
    "-mmacosx-version-min=12.0", "-framework", "Cocoa", "-framework", "CoreGraphics",
    "-install_name", "@rpath/libStockViewerDesktop.dylib", str(source), "-o", str(args.output),
], check=True)
