#!/usr/bin/env bash
set -euo pipefail

# -----------------------------------------------------------------------------
# run-UAT-smoke.sh
#
# Boots the canonical Rust overlay authority path for ASM-Lite UAT smoke sessions.
# Uses the same Rust-overlay-first transport contract as local visible smoke runs.
# -----------------------------------------------------------------------------

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
ARTIFACTS_DIR="${REPO_ROOT}/artifacts"
CANONICAL_PROJECT_PATH="${REPO_ROOT}/Tools/ci/unity-project"
CANONICAL_CATALOG_PATH="${REPO_ROOT}/Tools/ci/smoke/suite-catalog.json"
FIXED_DELAY_SECONDS="1.5"

OVERLAY_PID=""
OVERLAY_SESSION_ROOT=""
OVERLAY_LOG_PATH=""
RUST_OVERLAY_RUNNER_LABEL=""
declare -a RUST_OVERLAY_RUNNER_CMD=()

usage() {
  cat <<'EOF'
Usage: Tools/ci/run-UAT-smoke.sh

Boots the canonical Rust overlay authority path for ASM-Lite UAT smoke sessions.

Options:
  -h, --help                Show this help text

Notes:
  - Visible step delay is fixed at 1.5 seconds.
  - This command is a canonical Rust-overlay UAT smoke entrypoint.
EOF
}

cleanup() {
  set +e

  if [[ -n "${OVERLAY_PID}" ]]; then
    kill "${OVERLAY_PID}" >/dev/null 2>&1 || true
    wait "${OVERLAY_PID}" >/dev/null 2>&1 || true
  fi
}

resolve_rust_overlay_runner() {
  local candidate

  for candidate in \
    "${REPO_ROOT}/Tools/ci/rust-overlay/bin/asmlite_smoke_overlay" \
    "${REPO_ROOT}/Tools/ci/rust-overlay/bin/asmlite_smoke_overlay.exe"
  do
    if [[ -x "${candidate}" || ( "${candidate}" == *.exe && -f "${candidate}" ) ]]; then
      RUST_OVERLAY_RUNNER_CMD=("${candidate}")
      RUST_OVERLAY_RUNNER_LABEL="${candidate}"
      return 0
    fi
  done

  if ! command -v cargo >/dev/null 2>&1; then
    echo "error: neither checked-in Rust overlay executable nor cargo is available." >&2
    exit 1
  fi

  RUST_OVERLAY_RUNNER_CMD=(
    cargo
    run
    --manifest-path "${REPO_ROOT}/Tools/ci/rust-overlay/Cargo.toml"
    --bin asmlite_smoke_overlay
    --
  )
  RUST_OVERLAY_RUNNER_LABEL="cargo run --manifest-path Tools/ci/rust-overlay/Cargo.toml --bin asmlite_smoke_overlay --"
}

path_arg_for_rust_overlay_runner() {
  local path="$1"

  if [[ "${RUST_OVERLAY_RUNNER_CMD[0]:-}" == *.exe ]]; then
    if ! command -v wslpath >/dev/null 2>&1; then
      echo "error: wslpath is required to pass paths to a Windows Rust overlay executable." >&2
      exit 1
    fi
    wslpath -w "${path}"
    return 0
  fi

  printf '%s\n' "${path}"
}

start_rust_overlay_session() {
  local mode="$1"
  local timestamp repo_root_arg project_path_arg catalog_path_arg session_root_arg
  local -a overlay_cmd
  local overlay_exit_code

  resolve_rust_overlay_runner

  mkdir -p "${ARTIFACTS_DIR}/smoke-overlay"
  timestamp="$(date -u +%Y%m%dT%H%M%S)"
  OVERLAY_SESSION_ROOT="${ARTIFACTS_DIR}/smoke-overlay/session-${timestamp}-$$"
  mkdir -p "${OVERLAY_SESSION_ROOT}"
  OVERLAY_LOG_PATH="${OVERLAY_SESSION_ROOT}/overlay.log"

  repo_root_arg="$(path_arg_for_rust_overlay_runner "${REPO_ROOT}")"
  project_path_arg="$(path_arg_for_rust_overlay_runner "${CANONICAL_PROJECT_PATH}")"
  catalog_path_arg="$(path_arg_for_rust_overlay_runner "${CANONICAL_CATALOG_PATH}")"
  session_root_arg="$(path_arg_for_rust_overlay_runner "${OVERLAY_SESSION_ROOT}")"

  overlay_cmd=(
    "${RUST_OVERLAY_RUNNER_CMD[@]}"
    --repo-root "${repo_root_arg}"
    --project-path "${project_path_arg}"
    --catalog-path "${catalog_path_arg}"
    --session-root "${session_root_arg}"
    --mode "${mode}"
  )

  echo "Running canonical visible UAT smoke flow against:"
  echo "  Project: ${CANONICAL_PROJECT_PATH}"
  echo "  Package: ${REPO_ROOT}/Packages/com.staples.asm-lite"
  echo "  Mode:    ${mode}"
  echo "  Delay:   ${FIXED_DELAY_SECONDS}s"
  echo "  Runner:  ${RUST_OVERLAY_RUNNER_LABEL}"
  echo "  Session: ${OVERLAY_SESSION_ROOT}"

  set +e
  "${overlay_cmd[@]}" >"${OVERLAY_LOG_PATH}" 2>&1 &
  OVERLAY_PID=$!
  wait "${OVERLAY_PID}"
  overlay_exit_code=$?
  OVERLAY_PID=""
  set -e

  echo
  if [[ "${overlay_exit_code}" -eq 0 ]]; then
    echo "visible-smoke: PASS"
  else
    echo "visible-smoke: FAIL (exit code ${overlay_exit_code})"
  fi

  echo
  echo "Artifacts:"
  echo "  Rust session root: ${OVERLAY_SESSION_ROOT}"
  echo "  Overlay log: ${OVERLAY_LOG_PATH}"

  return "${overlay_exit_code}"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --help|-h)
      usage
      exit 0
      ;;
    --*)
      echo "error: unknown option: $1" >&2
      usage >&2
      exit 1
      ;;
    *)
      echo "error: unexpected positional argument: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

trap cleanup EXIT INT TERM HUP

start_rust_overlay_session "uat"
