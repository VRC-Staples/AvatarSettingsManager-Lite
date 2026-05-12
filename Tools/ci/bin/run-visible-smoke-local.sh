#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../../.." && pwd)"
CANONICAL_PROJECT_PATH="${REPO_ROOT}/Tools/ci/unity-project"
RUN_EDITMODE_SCRIPT="${SCRIPT_DIR}/run-editmode-local.sh"
FIXED_DELAY_SECONDS="1.5"
DEFAULT_EDITOR_FILTER="${DEFAULT_VISIBLE_FILTER:-ASMLiteVisibleEditorSmokeTests}"
DEFAULT_PLAYMODE_FILTER="${DEFAULT_VISIBLE_PLAYMODE_FILTER:-playmode}"
MODE="editor"
TEST_FILTER=""

usage() {
  cat <<'EOF'
Usage: Tools/ci/bin/run-visible-smoke-local.sh [options]

Visible smoke options:
  --editor-smoke            Run the visible editor smoke selector (default)
  --playmode-smoke          Run the visible playmode smoke selector
  --test-filter <filter>    Override the visible selector
  --self-test               Run lightweight wrapper self-tests without Unity
  -h, --help                Show this help text

Notes:
  - Visible step delay is fixed at 1.5 seconds.
  - There are no menu, headless, external launcher, or delay-tuning options in this script.
EOF
}

run_self_test() {
  if [[ ! -f "${RUN_EDITMODE_SCRIPT}" ]]; then
    echo "SelfTest FAIL: missing runner script: ${RUN_EDITMODE_SCRIPT}" >&2
    return 1
  fi
  echo "SelfTest PASS"
}

run_selector_smoke() {
  local selector="$1"
  local mode_label="$2"

  echo "Running visible ${mode_label} smoke against:"
  echo "  Project: ${CANONICAL_PROJECT_PATH}"
  echo "  Package: ${REPO_ROOT}/Packages/com.staples.asm-lite"
  echo "  Selector: ${selector}"
  echo "  Delay:    ${FIXED_DELAY_SECONDS}s"

  ASMLITE_VISIBLE_SMOKE_STEP_DELAY_SECONDS="${FIXED_DELAY_SECONDS}"   bash "${RUN_EDITMODE_SCRIPT}" --local --project-path "${CANONICAL_PROJECT_PATH}" --visible-editor-smoke --test-filter "${selector}"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
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
    --self-test)
      run_self_test
      exit 0
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

case "${MODE}" in
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
