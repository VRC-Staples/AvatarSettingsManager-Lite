#!/usr/bin/env python3
"""Extract a Unity serial from UNITY_LICENSE_SECRET without printing secrets."""

from __future__ import annotations

import base64
import os
import sys


def looks_like_ulf(value: str) -> bool:
    return '<DeveloperData Value="' in value and '/>' in value


def normalize_license_secret(secret: str) -> str:
    if looks_like_ulf(secret):
        return secret

    try:
        decoded = base64.b64decode(secret, validate=True).decode("utf-8")
    except Exception:
        decoded = ""

    if looks_like_ulf(decoded):
        print("::warning::UNITY_LICENSE was base64-decoded at runtime.", file=sys.stderr)
        return decoded

    raise RuntimeError("UNITY_LICENSE is not a valid .ulf file.")


def extract_serial(ulf_text: str) -> str:
    start_key = '<DeveloperData Value="'
    end_key = '"/>'
    start = ulf_text.find(start_key)
    if start < 0:
        raise RuntimeError("Missing DeveloperData entry.")
    start += len(start_key)
    end = ulf_text.find(end_key, start)
    if end < 0:
        raise RuntimeError("Missing DeveloperData closing marker.")

    serial = base64.b64decode(ulf_text[start:end]).decode("latin1")[4:]
    if not serial:
        raise RuntimeError("Failed to derive serial.")
    return serial


def main() -> int:
    secret = os.environ.get("UNITY_LICENSE_SECRET", "")
    try:
        print(extract_serial(normalize_license_secret(secret)))
        return 0
    except RuntimeError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
