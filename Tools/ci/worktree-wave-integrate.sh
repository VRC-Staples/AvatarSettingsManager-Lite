#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=Tools/ci/lib/worktree-wave-common.sh
source "${SCRIPT_DIR}/lib/worktree-wave-common.sh"

usage() {
  cat <<'EOF'
Usage: Tools/ci/worktree-wave-integrate.sh --wave <name> [options]

Cherry-pick worker commits from each agent branch onto the integration branch.
This preserves the worker's small atomic commits instead of collapsing them.

Options:
  --wave <name>            Wave name.
  --agent <id>             Restrict integration to one or more specific agents.
  --verify-cmd <command>   Run a repo-root verification command after each cherry-pick.
  --dry-run                Print the pending commit plan without changing git state.
  --help, -h               Show this help text.

Examples:
  bash Tools/ci/worktree-wave-integrate.sh --wave overlay-phase6 --dry-run

  bash Tools/ci/worktree-wave-integrate.sh \
    --wave overlay-phase6 \
    --verify-cmd 'bash Tools/ci/run-editmode-local.sh --test-filter ASMLiteSmokeOverlayHostTests'
EOF
}

ensure_git_repo

wave_name=""
verify_cmd=""
dry_run="false"
declare -a include_agents=()
declare -A include_agent_lookup=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --wave)
      [[ $# -ge 2 ]] || fail "--wave requires a value."
      wave_name="$2"
      shift 2
      ;;
    --agent)
      [[ $# -ge 2 ]] || fail "--agent requires a value."
      include_agents+=("$2")
      shift 2
      ;;
    --verify-cmd)
      [[ $# -ge 2 ]] || fail "--verify-cmd requires a value."
      verify_cmd="$2"
      shift 2
      ;;
    --dry-run)
      dry_run="true"
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
branch_exists "${INTEGRATION_BRANCH}" || fail "integration branch ${INTEGRATION_BRANCH} is missing."

for agent_id in "${include_agents[@]}"; do
  include_agent_lookup["$(normalize_agent_id "${agent_id}")"]=1
done

if [[ "${dry_run}" != "true" ]]; then
  repo_status="$(git -C "${REPO_ROOT}" status --porcelain --untracked-files=all)"
  [[ -z "${repo_status}" ]] || fail "repo must be clean before integration."
  git -C "${REPO_ROOT}" switch "${INTEGRATION_BRANCH}" >/dev/null
fi

print_registry_summary
log
log "Integration plan:"

action_taken="false"
while IFS=$'\t' read -r agent_id branch_name worktree_path pathspec_csv; do
  if [[ ${#include_agent_lookup[@]} -gt 0 && -z "${include_agent_lookup[${agent_id}]:-}" ]]; then
    continue
  fi

  branch_exists "${branch_name}" || fail "worker branch ${branch_name} is missing."

  mapfile -t pending_commits < <(git -C "${REPO_ROOT}" cherry "${INTEGRATION_BRANCH}" "${branch_name}" | awk '$1 == "+" { print $2 }')
  if [[ ${#pending_commits[@]} -eq 0 ]]; then
    log "- ${agent_id}: no new commits to integrate"
    continue
  fi

  log "- ${agent_id}: ${#pending_commits[@]} commit(s)"
  if [[ -n "${pathspec_csv}" ]]; then
    log "  owns: ${pathspec_csv}"
  fi

  for commit_sha in "${pending_commits[@]}"; do
    subject="$(git -C "${REPO_ROOT}" log -1 --format=%s "${commit_sha}")"
    log "  - ${commit_sha} ${subject}"
    if [[ "${dry_run}" == "true" ]]; then
      continue
    fi

    if ! git -C "${REPO_ROOT}" cherry-pick "${commit_sha}"; then
      warn "cherry-pick stopped on ${commit_sha}. Resolve conflicts in ${REPO_ROOT}, then run git cherry-pick --continue or --abort manually."
      exit 1
    fi

    if [[ -n "${verify_cmd}" ]]; then
      log "    verify: ${verify_cmd}"
      if ! bash -lc "cd \"${REPO_ROOT}\" && ${verify_cmd}"; then
        warn "verification failed after cherry-picking ${commit_sha}. Inspect the repo, then use git cherry-pick --abort or fix-forward manually."
        exit 1
      fi
    fi
  done

  action_taken="true"
done < <(iter_agent_records "$(agents_path "${wave_name}")")

if [[ "${dry_run}" == "true" ]]; then
  log
  log "Dry run only. No git state changed."
else
  log
  log "Integration branch now contains the selected worker commits."
  if [[ "${action_taken}" == "false" ]]; then
    log "No new commits were required."
  fi
fi
