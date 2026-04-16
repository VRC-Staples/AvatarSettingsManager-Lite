#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ ! -d "$repo_root/.githooks" ]]; then
  echo "Missing .githooks directory at $repo_root/.githooks" >&2
  exit 1
fi

chmod +x "$repo_root/.githooks/pre-commit" "$repo_root/.githooks/commit-msg"

git -C "$repo_root" config core.hooksPath .githooks

echo "Configured core.hooksPath to .githooks"
echo "Hooks enabled: pre-commit, commit-msg"
