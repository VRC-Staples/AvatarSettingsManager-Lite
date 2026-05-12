#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../../.." && pwd)"

if [[ ! -d "${REPO_ROOT}/.githooks" ]]; then
  echo "Missing .githooks directory at ${REPO_ROOT}/.githooks" >&2
  exit 1
fi

chmod +x "${REPO_ROOT}/.githooks/pre-commit" "${REPO_ROOT}/.githooks/commit-msg"

git -C "${REPO_ROOT}" config core.hooksPath .githooks

echo "Configured core.hooksPath to .githooks"
echo "Hooks enabled: pre-commit, commit-msg"
