#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=Tools/ci/lib/worktree-wave-common.sh
source "${SCRIPT_DIR}/lib/worktree-wave-common.sh"

usage() {
  cat <<'EOF'
Usage: Tools/ci/worktree-wave-cleanup.sh --wave <name> [options]

Remove local worktree directories after a wave is finished. Branch deletion is optional.

Options:
  --wave <name>       Wave name.
  --drop-branches     Delete worker branches after removing worktrees.
  --drop-integration  Delete the integration branch too.
  --drop-registry     Delete the saved wave registry files after cleanup.
  --force             Remove dirty worktrees too.
  --help, -h          Show this help text.
EOF
}

ensure_git_repo

wave_name=""
drop_branches="false"
drop_integration="false"
drop_registry="false"
force_remove="false"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --wave)
      [[ $# -ge 2 ]] || fail "--wave requires a value."
      wave_name="$2"
      shift 2
      ;;
    --drop-branches)
      drop_branches="true"
      shift
      ;;
    --drop-integration)
      drop_integration="true"
      shift
      ;;
    --drop-registry)
      drop_registry="true"
      shift
      ;;
    --force)
      force_remove="true"
      shift
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
log "Cleanup actions:"
while IFS=$'\t' read -r agent_id branch_name worktree_path pathspec_csv; do
  dirty_state="$(worktree_dirty_state "${worktree_path}")"
  if [[ "${dirty_state}" == "dirty" && "${force_remove}" != "true" ]]; then
    fail "worktree ${worktree_path} is dirty. Commit, stash, or re-run with --force."
  fi

  if [[ -d "${worktree_path}" ]]; then
    if [[ "${force_remove}" == "true" ]]; then
      git -C "${REPO_ROOT}" worktree remove --force "${worktree_path}"
    else
      git -C "${REPO_ROOT}" worktree remove "${worktree_path}"
    fi
    log "- removed worktree ${worktree_path}"
  else
    log "- worktree already absent: ${worktree_path}"
  fi

  if [[ "${drop_branches}" == "true" ]]; then
    if branch_exists "${branch_name}"; then
      git -C "${REPO_ROOT}" branch -D "${branch_name}" >/dev/null
      log "  deleted branch ${branch_name}"
    else
      log "  branch already absent: ${branch_name}"
    fi
  fi
done < <(iter_agent_records "$(agents_path "${wave_name}")")

if [[ "${drop_integration}" == "true" ]]; then
  if branch_exists "${INTEGRATION_BRANCH}"; then
    git -C "${REPO_ROOT}" branch -D "${INTEGRATION_BRANCH}" >/dev/null
    log "- deleted integration branch ${INTEGRATION_BRANCH}"
  else
    log "- integration branch already absent: ${INTEGRATION_BRANCH}"
  fi
fi

if [[ "${drop_registry}" == "true" ]]; then
  rm -f "$(metadata_path "${wave_name}")" "$(agents_path "${wave_name}")"
  log "- deleted registry for ${wave_name}"
fi
