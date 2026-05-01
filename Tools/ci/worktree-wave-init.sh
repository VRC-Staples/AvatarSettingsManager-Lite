#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=Tools/ci/lib/worktree-wave-common.sh
source "${SCRIPT_DIR}/lib/worktree-wave-common.sh"

usage() {
  cat <<'EOF'
Usage: Tools/ci/worktree-wave-init.sh --wave <name> --agent <id[:path1,path2]> [options]

Create one integration branch plus one worktree branch per agent from a frozen base SHA.
Worktrees are created sequentially to avoid git config.lock collisions.

Required:
  --wave <name>                 Wave name. Used in branch and registry names.
  --agent <id[:path1,path2]>    Agent id, optionally with owned pathspec CSV. Repeat per agent.

Optional:
  --base-ref <ref>              Base ref to freeze. Default: HEAD
  --integration-branch <name>   Integration branch name. Default: int/<wave>
  --worktrees-root <path>       Parent directory for created worktrees.
                                Default: ../.worktrees/<repo>/<wave>
  --allow-dirty                 Skip clean-repo guard.
  --reuse-existing              Reuse pre-existing integration branch / worktree branch / path.
  --help, -h                    Show this help text.

Examples:
  bash Tools/ci/worktree-wave-init.sh \
    --wave overlay-phase6 \
    --agent gui:Packages/com.staples.asm-lite/Editor,Packages/com.staples.asm-lite/Tests/Editor \
    --agent docs:README.md,Tools/ci/WORKTREE-AGENTS.md

  bash Tools/ci/worktree-wave-init.sh \
    --wave phase5-wave1 \
    --base-ref dev \
    --agent catalog:Packages/com.staples.asm-lite/Editor/Catalog \
    --agent protocol:Packages/com.staples.asm-lite/Runtime
EOF
}

ensure_git_repo

wave_name=""
base_ref="HEAD"
integration_branch=""
worktrees_root=""
allow_dirty="false"
reuse_existing="false"
declare -a agent_specs=()

after_parse() {
  [[ -n "${wave_name}" ]] || fail "--wave is required."
  [[ ${#agent_specs[@]} -gt 0 ]] || fail "at least one --agent is required."

  if [[ -z "${integration_branch}" ]]; then
    integration_branch="int/${wave_name}"
  fi
  require_branch_name "${integration_branch}"

  if [[ -z "${worktrees_root}" ]]; then
    worktrees_root="$(resolve_default_worktrees_root "${wave_name}")"
  fi
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --wave)
      [[ $# -ge 2 ]] || fail "--wave requires a value."
      wave_name="$2"
      shift 2
      ;;
    --agent)
      [[ $# -ge 2 ]] || fail "--agent requires a value."
      agent_specs+=("$2")
      shift 2
      ;;
    --base-ref)
      [[ $# -ge 2 ]] || fail "--base-ref requires a value."
      base_ref="$2"
      shift 2
      ;;
    --integration-branch)
      [[ $# -ge 2 ]] || fail "--integration-branch requires a value."
      integration_branch="$2"
      shift 2
      ;;
    --worktrees-root)
      [[ $# -ge 2 ]] || fail "--worktrees-root requires a value."
      worktrees_root="$2"
      shift 2
      ;;
    --allow-dirty)
      allow_dirty="true"
      shift
      ;;
    --reuse-existing)
      reuse_existing="true"
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

after_parse

if [[ "${allow_dirty}" != "true" ]]; then
  require_clean_repo
fi

base_sha="$(git -C "${REPO_ROOT}" rev-parse "${base_ref}")"
base_branch="$(current_branch)"
created_at_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
metadata_file="$(metadata_path "${wave_name}")"
agents_file="$(agents_path "${wave_name}")"

mkdir -p "${worktrees_root}"

if branch_exists "${integration_branch}"; then
  if [[ "${reuse_existing}" != "true" ]]; then
    fail "integration branch ${integration_branch} already exists. Re-run with --reuse-existing if that is intentional."
  fi
else
  git -C "${REPO_ROOT}" branch "${integration_branch}" "${base_sha}"
fi

write_wave_metadata "${metadata_file}" "${wave_name}" "${base_ref}" "${base_sha}" "${base_branch}" "${integration_branch}" "${worktrees_root}" "${created_at_utc}"
write_agents_header "${agents_file}"
load_wave_registry "${wave_name}"

declare -A seen_agents=()
for spec in "${agent_specs[@]}"; do
  parsed_spec="$(parse_agent_spec "${spec}")"
  agent_id="${parsed_spec%%$'\t'*}"
  pathspec_csv="${parsed_spec#*$'\t'}"

  [[ -z "${seen_agents[${agent_id}]:-}" ]] || fail "duplicate agent id: ${agent_id}"
  seen_agents["${agent_id}"]=1

  branch_name="$(agent_branch_name "${wave_name}" "${agent_id}")"
  worktree_path="$(agent_worktree_path "${worktrees_root}" "${agent_id}")"
  require_branch_name "${branch_name}"

  if branch_exists "${branch_name}"; then
    if [[ "${reuse_existing}" != "true" ]]; then
      fail "worker branch ${branch_name} already exists. Re-run with --reuse-existing if that is intentional."
    fi
  fi

  if [[ -d "${worktree_path}" ]]; then
    if [[ "${reuse_existing}" != "true" ]]; then
      fail "worktree path ${worktree_path} already exists. Re-run with --reuse-existing if that is intentional."
    fi
  else
    mkdir -p "$(dirname "${worktree_path}")"
    if branch_exists "${branch_name}"; then
      git -C "${REPO_ROOT}" worktree add "${worktree_path}" "${branch_name}"
    else
      git -C "${REPO_ROOT}" worktree add "${worktree_path}" -b "${branch_name}" "${base_sha}"
    fi
  fi

  actual_base="$(git -C "${worktree_path}" rev-parse HEAD)"
  [[ "${actual_base}" == "${base_sha}" ]] || fail "worktree ${worktree_path} did not start from frozen base ${base_sha}."

  append_agent_record "${agents_file}" "${agent_id}" "${branch_name}" "${worktree_path}" "${pathspec_csv}"
done

log "Created worktree wave ${wave_name}."
log
print_registry_summary
log
log "Agents:"
iter_agent_records "${agents_file}" | while IFS=$'\t' read -r agent_id branch_name worktree_path pathspec_csv; do
  log "- ${agent_id}"
  log "  branch:   ${branch_name}"
  log "  worktree: ${worktree_path}"
  if [[ -n "${pathspec_csv}" ]]; then
    log "  owns:     ${pathspec_csv}"
  fi
  log "  start:    cd ${worktree_path}"
  log "            git branch --show-current"
  log "            hermes --worktree"
done
log
log "Next:"
log "  1. Dispatch one agent per worktree path."
log "  2. Keep commits tiny and local to each worker branch."
log "  3. Integrate with: bash Tools/ci/worktree-wave-integrate.sh --wave ${wave_name}"
