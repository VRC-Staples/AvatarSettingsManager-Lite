#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=Tools/ci/lib/worktree-wave-common.sh
source "${SCRIPT_DIR}/lib/worktree-wave-common.sh"

usage() {
  cat <<'EOF'
Usage: Tools/ci/worktree-wave-status.sh --wave <name>

Show frozen-base, branch, commit, and dirty-state details for a worktree wave.

Options:
  --wave <name>     Wave name.
  --help, -h        Show this help text.
EOF
}

ensure_git_repo

wave_name=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --wave)
      [[ $# -ge 2 ]] || fail "--wave requires a value."
      wave_name="$2"
      shift 2
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    --*)
      fail "unknown option: $1"
      ;;
    *)
      fail "unexpected positional argument: $1"
      ;;
  esac
done

[[ -n "${wave_name}" ]] || fail "--wave is required."
load_wave_registry "${wave_name}"
print_registry_summary
log
log "Agent status:"
iter_agent_records "$(agents_path "${wave_name}")" | while IFS=$'\t' read -r agent_id branch_name worktree_path pathspec_csv; do
  branch_state="present"
  if ! branch_exists "${branch_name}"; then
    branch_state="missing"
  fi

  dirty_state="$(worktree_dirty_state "${worktree_path}")"
  commits_since_base="0"
  pending_for_integration="0"
  if [[ "${branch_state}" == "present" ]]; then
    commits_since_base="$(count_branch_commits_since_base "${BASE_SHA}" "${branch_name}")"
    if branch_exists "${INTEGRATION_BRANCH}"; then
      pending_for_integration="$(count_equivalent_commits_for_integration "${INTEGRATION_BRANCH}" "${branch_name}")"
    fi
  fi

  log "- ${agent_id}"
  log "  branch:                ${branch_name} (${branch_state})"
  log "  worktree:              ${worktree_path} (${dirty_state})"
  log "  commits since base:    ${commits_since_base}"
  log "  pending for integrate: ${pending_for_integration}"
  if [[ -n "${pathspec_csv}" ]]; then
    log "  owns:                  ${pathspec_csv}"
  fi
done
