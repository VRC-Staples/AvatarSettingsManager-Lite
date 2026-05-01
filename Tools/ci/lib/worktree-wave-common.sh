#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../../.." && pwd)"
REPO_NAME="$(basename "${REPO_ROOT}")"
REPO_PARENT="$(dirname "${REPO_ROOT}")"
REGISTRY_DIR="${REPO_ROOT}/.git/worktree-waves"

mkdir -p "${REGISTRY_DIR}"

log() {
  printf '%s\n' "$*"
}

warn() {
  printf 'warn: %s\n' "$*" >&2
}

fail() {
  printf 'error: %s\n' "$*" >&2
  exit 1
}

ensure_git_repo() {
  git -C "${REPO_ROOT}" rev-parse --show-toplevel >/dev/null 2>&1 || fail "${REPO_ROOT} is not a git repository."
}

require_branch_name() {
  local branch_name="$1"
  git check-ref-format --branch "${branch_name}" >/dev/null 2>&1 || fail "invalid branch name: ${branch_name}"
}

require_clean_repo() {
  local status_output
  status_output="$(git -C "${REPO_ROOT}" status --porcelain --untracked-files=all)"
  [[ -z "${status_output}" ]] || fail "repo must be clean before freezing a worktree wave. Commit, stash, or move local changes first."
}

current_branch() {
  git -C "${REPO_ROOT}" branch --show-current
}

branch_exists() {
  local branch_name="$1"
  git -C "${REPO_ROOT}" show-ref --verify --quiet "refs/heads/${branch_name}"
}

resolve_default_worktrees_root() {
  local wave_name="$1"
  printf '%s/.worktrees/%s/%s\n' "${REPO_PARENT}" "${REPO_NAME}" "${wave_name}"
}

metadata_path() {
  local wave_name="$1"
  printf '%s/%s.env\n' "${REGISTRY_DIR}" "${wave_name}"
}

agents_path() {
  local wave_name="$1"
  printf '%s/%s.agents.tsv\n' "${REGISTRY_DIR}" "${wave_name}"
}

require_wave_registry() {
  local wave_name="$1"
  [[ -f "$(metadata_path "${wave_name}")" ]] || fail "missing wave metadata for ${wave_name}. Run worktree-wave-init.sh first."
  [[ -f "$(agents_path "${wave_name}")" ]] || fail "missing agent registry for ${wave_name}. Run worktree-wave-init.sh first."
}

load_wave_registry() {
  local wave_name="$1"
  require_wave_registry "${wave_name}"
  # shellcheck disable=SC1090
  source "$(metadata_path "${wave_name}")"
}

write_wave_metadata() {
  local output_path="$1"
  local wave_name="$2"
  local base_ref="$3"
  local base_sha="$4"
  local base_branch="$5"
  local integration_branch="$6"
  local worktrees_root="$7"
  local created_at_utc="$8"

  {
    printf 'WAVE_NAME=%q\n' "${wave_name}"
    printf 'REPO_ROOT=%q\n' "${REPO_ROOT}"
    printf 'REPO_NAME=%q\n' "${REPO_NAME}"
    printf 'BASE_REF=%q\n' "${base_ref}"
    printf 'BASE_SHA=%q\n' "${base_sha}"
    printf 'BASE_BRANCH=%q\n' "${base_branch}"
    printf 'INTEGRATION_BRANCH=%q\n' "${integration_branch}"
    printf 'WORKTREES_ROOT=%q\n' "${worktrees_root}"
    printf 'CREATED_AT_UTC=%q\n' "${created_at_utc}"
  } >"${output_path}"
}

normalize_agent_id() {
  local agent_id="$1"
  [[ "${agent_id}" =~ ^[A-Za-z0-9._-]+$ ]] || fail "invalid agent id '${agent_id}'. Use letters, numbers, dot, underscore, or dash only."
  printf '%s\n' "${agent_id}"
}

parse_agent_spec() {
  local spec="$1"
  local agent_id="${spec%%:*}"
  local raw_pathspecs=""
  if [[ "${spec}" == *:* ]]; then
    raw_pathspecs="${spec#*:}"
  fi
  agent_id="$(normalize_agent_id "${agent_id}")"
  printf '%s\t%s\n' "${agent_id}" "${raw_pathspecs}"
}

agent_branch_name() {
  local wave_name="$1"
  local agent_id="$2"
  printf 'wt/%s/%s\n' "${wave_name}" "${agent_id}"
}

agent_worktree_path() {
  local worktrees_root="$1"
  local agent_id="$2"
  printf '%s/%s\n' "${worktrees_root}" "${agent_id}"
}

write_agents_header() {
  local output_path="$1"
  printf 'agent_id\tbranch_name\tworktree_path\tpathspec_csv\n' >"${output_path}"
}

append_agent_record() {
  local output_path="$1"
  local agent_id="$2"
  local branch_name="$3"
  local worktree_path="$4"
  local pathspec_csv="$5"
  printf '%s\t%s\t%s\t%s\n' "${agent_id}" "${branch_name}" "${worktree_path}" "${pathspec_csv}" >>"${output_path}"
}

iter_agent_records() {
  local agents_file="$1"
  tail -n +2 "${agents_file}"
}

count_branch_commits_since_base() {
  local base_sha="$1"
  local branch_name="$2"
  git -C "${REPO_ROOT}" rev-list --count "${base_sha}..${branch_name}"
}

count_equivalent_commits_for_integration() {
  local integration_branch="$1"
  local branch_name="$2"
  git -C "${REPO_ROOT}" cherry "${integration_branch}" "${branch_name}" | awk '$1 == "+" { count += 1 } END { print count + 0 }'
}

worktree_dirty_state() {
  local worktree_path="$1"
  if [[ ! -d "${worktree_path}" ]]; then
    printf 'missing\n'
    return 0
  fi

  local status_output
  status_output="$(git -C "${worktree_path}" status --porcelain --untracked-files=all 2>/dev/null || true)"
  if [[ -z "${status_output}" ]]; then
    printf 'clean\n'
  else
    printf 'dirty\n'
  fi
}

print_registry_summary() {
  log "Wave: ${WAVE_NAME}"
  log "Repo: ${REPO_ROOT}"
  log "Base ref: ${BASE_REF}"
  log "Base sha: ${BASE_SHA}"
  log "Base branch: ${BASE_BRANCH}"
  log "Integration branch: ${INTEGRATION_BRANCH}"
  log "Worktrees root: ${WORKTREES_ROOT}"
  log "Created at (UTC): ${CREATED_AT_UTC}"
}
