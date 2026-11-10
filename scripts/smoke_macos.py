#!/usr/bin/env python3
"""Verify architecture, signature and startup after extracting the release ZIP."""
import argparse
import plistlib
import subprocess
import tempfile
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", required=True)
    parser.add_argument("--runtime", choices=["osx-arm64", "osx-x64"], required=True)
    parser.add_argument("--directory", type=Path, required=True)
    args = parser.parse_args()
    archive = args.directory.resolve() / f"StockViewer-macOS-{args.runtime.removeprefix('osx-')}.zip"
    with tempfile.TemporaryDirectory(prefix="StockViewer-release-") as temporary:
        directory = Path(temporary)
        subprocess.run(["ditto", "-x", "-k", str(archive), str(directory)], check=True)
        bundle = directory / "StockViewer-macOS.app"
        with (bundle / "Contents/Info.plist").open("rb") as stream:
            info = plistlib.load(stream)
        if info["CFBundleShortVersionString"] != args.version:
            raise SystemExit("Bundle version mismatch")
        executable = bundle / "Contents/MacOS" / info["CFBundleExecutable"]
        architecture = "arm64" if args.runtime == "osx-arm64" else "x86_64"
        subprocess.run(["lipo", str(executable), "-verify_arch", architecture], check=True)
        subprocess.run(["codesign", "--verify", "--deep", "--strict", str(bundle)], check=True)
        if not (bundle / "Contents/MacOS/libhostfxr.dylib").is_file():
            raise SystemExit("Self-contained runtime is missing")
        with (directory / "startup.log").open("w+") as log:
            process = subprocess.Popen([str(executable)], cwd=directory, stdout=log, stderr=log)
            try:
                try:
                    status = process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    print(f"PASS: {args.runtime} ZIP extracted, signature valid, standalone app started")
                else:
                    log.seek(0)
                    raise SystemExit(f"App exited early ({status}):\n{log.read()}")
            finally:
                if process.poll() is None:
                    process.terminate()
                    try:
                        process.wait(timeout=10)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait()


if __name__ == "__main__":
    main()
