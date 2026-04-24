#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
ARTIFACTS_DIR="${REPO_ROOT}/artifacts"
CANONICAL_PROJECT_PATH="${REPO_ROOT}/Tools/ci/unity-project"
CANONICAL_CATALOG_PATH="${REPO_ROOT}/Tools/ci/smoke/suite-catalog.json"
RUN_EDITMODE_SCRIPT="${SCRIPT_DIR}/run-editmode-local.sh"
FIXED_DELAY_SECONDS="1.5"
DEFAULT_EDITOR_FILTER="${DEFAULT_VISIBLE_FILTER:-ASMLiteVisibleEditorSmokeTests}"
DEFAULT_PLAYMODE_FILTER="${DEFAULT_VISIBLE_PLAYMODE_FILTER:-playmode}"
MODE="overlay"
TEST_FILTER=""
OVERLAY_PID=""
OVERLAY_SESSION_ROOT=""
OVERLAY_LOG_PATH=""
RUST_OVERLAY_RUNNER_LABEL=""
declare -a RUST_OVERLAY_RUNNER_CMD=()

usage() {
  cat <<'EOF'
Usage: Tools/ci/run-visible-smoke-local.sh [options]

Visible smoke options only:
  --overlay-smoke           Run the visible Rust overlay bootstrap path (default)
  --editor-smoke            Run the visible editor smoke selector
  --playmode-smoke          Run the visible playmode smoke selector
  --test-filter <filter>    Override the visible selector for editor/playmode smoke
  -h, --help                Show this help text

Notes:
  - Visible step delay is fixed at 1.5 seconds.
  - Overlay mode currently boots the Phase 07 suite-selection surface only (run-suite dispatch remains Phase 08 gated).
  - There are no menu, headless, or delay-tuning options in this script.
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

  echo "Running visible Rust overlay Phase 07 bootstrap against:"
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
  echo "Artifacts:"
  echo "  Rust session root: ${OVERLAY_SESSION_ROOT}"
  echo "  Overlay log: ${OVERLAY_LOG_PATH}"

  return "${overlay_exit_code}"
}

run_overlay_smoke() {
  start_rust_overlay_session "overlay"
}

run_selector_smoke() {
  local selector="$1"
  local mode_label="$2"

  echo "Running visible ${mode_label} smoke against:"
  echo "  Project: ${CANONICAL_PROJECT_PATH}"
  echo "  Package: ${REPO_ROOT}/Packages/com.staples.asm-lite"
  echo "  Selector: ${selector}"
  echo "  Delay:    ${FIXED_DELAY_SECONDS}s"

  ASMLITE_VISIBLE_SMOKE_STEP_DELAY_SECONDS="${FIXED_DELAY_SECONDS}" \
  bash "${RUN_EDITMODE_SCRIPT}" --local --project-path "${CANONICAL_PROJECT_PATH}" --visible-editor-smoke --test-filter "${selector}"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --overlay-smoke)
      MODE="overlay"
      shift
      ;;
    --editor-smoke)
      MODE="editor"
      shift
      ;;
    --playmode-smoke)
      MODE="playmode"
      shift
      ;;
    --test-filter)
      [[ $# -ge 2 ]] || { echo "error: --test-filter requires a value." >&2; exit 1; }
      TEST_FILTER="$2"
      shift 2
      ;;
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

if [[ ! -x "${RUN_EDITMODE_SCRIPT}" ]]; then
  echo "error: missing runner script: ${RUN_EDITMODE_SCRIPT}" >&2
  exit 1
fi

trap cleanup EXIT INT TERM HUP

case "${MODE}" in
  overlay)
    if [[ -n "${TEST_FILTER}" ]]; then
      echo "error: --test-filter is only supported with --editor-smoke or --playmode-smoke." >&2
      exit 1
    fi
    run_overlay_smoke
    ;;
  editor)
    run_selector_smoke "${TEST_FILTER:-${DEFAULT_EDITOR_FILTER}}" "editor"
    ;;
  playmode)
    run_selector_smoke "${TEST_FILTER:-${DEFAULT_PLAYMODE_FILTER}}" "playmode"
    ;;
  *)
    echo "error: unsupported mode '${MODE}'." >&2
    exit 1
    ;;
esac
