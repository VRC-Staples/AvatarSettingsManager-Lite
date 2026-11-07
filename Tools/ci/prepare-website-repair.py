#!/usr/bin/env python3
"""Repair the published website dependency without rebuilding the VPM feed."""

import argparse
import hashlib
import json
from pathlib import Path
import re
from urllib.request import Request, urlopen


SITE_URL = "https://vrc-staples.github.io/AvatarSettingsManager-Lite/"
ASSETS = (
    "index.html", "app.js", "index.json", "styles.css",
    "favicon.ico", "banner.svg", "outfit.woff2",
)
UNPINNED = b"https://unpkg.com/@fluentui/web-components"
PINNED = UNPINNED + b"@2.6.1"
DEPENDENCY_URL = re.compile(
    rb"https://unpkg.com/@fluentui/web-components(?:@[^\s\"'<>;]+)?"
)


def patch_dependency(published, source):
    """Change only the known broken URL, or leave an already repaired page intact."""
    if DEPENDENCY_URL.findall(source) != [PINNED]:
        raise ValueError("Website source must pin Fluent UI to 2.6.1 exactly once")
    urls = DEPENDENCY_URL.findall(published)
    if urls == [PINNED]:
        return published
    if urls != [UNPINNED]:
        raise ValueError("Published dependency changed; inspect it before deploying")
    return published.replace(UNPINNED, PINNED, 1)


def download(name):
    request = Request(
        SITE_URL + name,
        headers={"User-Agent": "ASM-Lite-website-repair", "Cache-Control": "no-cache"},
    )
    with urlopen(request, timeout=30) as response:
        return response.read()


def prepare(destination, source_root):
    published = {name: download(name) for name in ASSETS}
    feed = json.loads(published["index.json"])
    if "com.staples.asm-lite" not in feed.get("packages", {}):
        raise ValueError("Published feed does not contain ASM-Lite")

    repaired = dict(published)
    for name in ("index.html", "app.js"):
        source = (source_root / "Website" / name).read_bytes()
        repaired[name] = patch_dependency(published[name], source)

    # Reuse generated pages so this repair cannot alter package versions or downloads.
    destination.mkdir(parents=True, exist_ok=False)
    for name, content in repaired.items():
        (destination / name).write_bytes(content)
        state = "changed" if content != published[name] else "preserved"
        print(f"{state}: {name} sha256={hashlib.sha256(content).hexdigest()}")
    return repaired


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    prepare(args.destination, Path(__file__).resolve().parents[2])


if __name__ == "__main__":
    main()
