#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "Usage: $0 <base_sha> <head_sha>" >&2
  exit 1
fi

base_sha="$1"
head_sha="$2"

if [[ -z "$head_sha" ]]; then
  echo "head_sha is required" >&2
  exit 1
fi

# On first push to a branch, GitHub passes all-zero base.
if [[ -z "$base_sha" || "$base_sha" =~ ^0+$ ]]; then
  range="$head_sha"
else
  range="$base_sha..$head_sha"
fi

mapfile -t commits < <(git rev-list "$range")

if [[ ${#commits[@]} -eq 0 ]]; then
  echo "No commits in range $range"
  exit 0
fi

fail=0

for sha in "${commits[@]}"; do
  author_name="$(git show -s --format='%an' "$sha")"
  author_email="$(git show -s --format='%ae' "$sha")"
  committer_name="$(git show -s --format='%cn' "$sha")"
  committer_email="$(git show -s --format='%ce' "$sha")"
  body="$(git show -s --format='%B' "$sha")"

  if [[ "$author_name" == "rhiltner" || "$committer_name" == "rhiltner" ]]; then
    echo "::error::Commit $sha uses banned name 'rhiltner' in metadata." >&2
    fail=1
  fi

  if [[ "$author_email" == "ryan.hiltner@gmail.com" || "$committer_email" == "ryan.hiltner@gmail.com" ]]; then
    echo "::error::Commit $sha uses banned personal email in metadata." >&2
    fail=1
  fi

  if grep -Eiq '^Co-Authored-By:[[:space:]]*Claude\b' <<<"$body"; then
    echo "::error::Commit $sha contains blocked Co-Authored-By trailer for Claude." >&2
    fail=1
  fi

  if grep -Eiq 'noreply@anthropic\.com' <<<"$body"; then
    echo "::error::Commit $sha contains blocked anthropic noreply trailer." >&2
    fail=1
  fi

done

if [[ $fail -ne 0 ]]; then
  echo "Commit identity guard failed." >&2
  exit 1
fi

echo "Commit identity guard passed for ${#commits[@]} commit(s)."
