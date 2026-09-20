#!/usr/bin/env python3
"""Packages the plugin publish output into a zip and updates manifest.json.

Usage: python scripts/package.py --version 1.0.0.0 --tag v1.0.0 --repo zlx64/JellyPoll

- Copies docs/logo.png into ./publish and generates a pre-packaged meta.json
  (plugin identity + imagePath, so manual installs show the logo and keep the
  stable GUID instead of the folder-name MD5 fallback)
- Zips everything in ./publish (files at zip root, jprm-compatible layout)
  to artifacts/jellypoll_<version>.zip
- Computes MD5 checksum
- Inserts/replaces the version entry in ./manifest.json
"""

import argparse
import hashlib
import json
import shutil
import zipfile
from datetime import datetime, timezone
from pathlib import Path

GUID = "3734440f-4b20-4c3a-86a3-d931b45b7248"
NAME = "Jelly Poll"
DESCRIPTION = "Group polls to choose what to watch together: suggest titles from the library, rank them, and crown the top 3."
OVERVIEW = "Group movie polling with ranked ballots and a gold/silver/bronze podium."
OWNER = "zlx64"
CATEGORY = "General"
TARGET_ABI = "12.1.0.0"
FRAMEWORK = "net10.0"
LOGO_SOURCE = Path("docs/logo.png")
LOGO_NAME = "logo.png"


def write_meta_json(publish: Path, version: str, repo: str, tag: str) -> None:
    """Pre-packaged meta.json so manually installed zips keep the stable GUID,
    the display name, and the plugin logo (imagePath is relative to the plugin
    folder; the server serves it via /api/Plugins/{id}/{version}/Image)."""
    meta = {
        "category": CATEGORY,
        "changelog": f"See release notes at https://github.com/{repo}/releases/tag/{tag}",
        "description": DESCRIPTION,
        "guid": GUID,
        "name": NAME,
        "overview": OVERVIEW,
        "owner": OWNER,
        "targetAbi": TARGET_ABI,
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "version": version,
        "status": 0,  # PluginStatus.Active
        "autoUpdate": True,
        "imagePath": LOGO_NAME,
    }
    (publish / "meta.json").write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", required=True, help="4-part plugin version, e.g. 1.0.0.0")
    parser.add_argument("--tag", required=True, help="git tag, e.g. v1.0.0")
    parser.add_argument("--repo", required=True, help="GitHub repository, e.g. zlx64/JellyPoll")
    parser.add_argument("--publish-dir", default="publish")
    parser.add_argument("--artifacts-dir", default="artifacts")
    args = parser.parse_args()

    publish = Path(args.publish_dir)
    if not publish.is_dir():
        raise SystemExit(f"Publish directory not found: {publish}")

    if not LOGO_SOURCE.is_file():
        raise SystemExit(f"Plugin logo not found: {LOGO_SOURCE}")
    shutil.copy2(LOGO_SOURCE, publish / LOGO_NAME)
    write_meta_json(publish, args.version, args.repo, args.tag)

    artifacts = Path(args.artifacts_dir)
    artifacts.mkdir(exist_ok=True)
    zip_path = artifacts / f"jellypoll_{args.version}.zip"

    files = sorted(f for f in publish.rglob("*") if f.is_file())
    # Exclude native runtime assets: the Jellyfin PluginManager scans every .dll in the
    # plugin folder and chokes on native binaries (BadImageFormatException -> plugin disabled).
    # The server process already initializes the e_sqlite3 provider globally.
    files = [f for f in files if "runtimes" not in f.relative_to(publish).parts]
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for f in files:
            zf.write(f, f.relative_to(publish).as_posix())

    checksum = hashlib.md5(zip_path.read_bytes()).hexdigest().upper()  # Jellyfin verifies MD5 (uppercase), not SHA-256

    manifest_path = Path("manifest.json")
    manifest: list = json.loads(manifest_path.read_text(encoding="utf-8-sig")) if manifest_path.exists() else []

    entry = next((p for p in manifest if p.get("guid") == GUID), None)
    if entry is None:
        entry = {
            "guid": GUID,
            "name": NAME,
            "description": DESCRIPTION,
            "overview": OVERVIEW,
            "owner": OWNER,
            "category": CATEGORY,
            "versions": [],
        }
        manifest.append(entry)
    entry["name"] = NAME
    entry["imageUrl"] = f"https://raw.githubusercontent.com/{args.repo}/main/docs/{LOGO_NAME}"

    source_url = f"https://github.com/{args.repo}/releases/download/{args.tag}/jellypoll_{args.version}.zip"

    # Replace same-version entry if present, else prepend (newest first).
    entry["versions"] = [v for v in entry["versions"] if v.get("version") != args.version]
    entry["versions"].insert(
        0,
        {
            "version": args.version,
            "changelog": f"See release notes at https://github.com/{args.repo}/releases/tag/{args.tag}",
            "targetAbi": TARGET_ABI,
            "framework": FRAMEWORK,
            "sourceUrl": source_url,
            "checksum": checksum,
            "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        },
    )

    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Packaged {len(files)} files -> {zip_path}")
    print(f"Checksum: {checksum}")
    print(f"Manifest updated: {manifest_path}")


if __name__ == "__main__":
    main()
