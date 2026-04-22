#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
ARTIFACTS_DIR="${REPO_ROOT}/artifacts"
CANONICAL_PROJECT_PATH="${REPO_ROOT}/Tools/ci/unity-project"
RUN_EDITMODE_SCRIPT="${SCRIPT_DIR}/run-editmode-local.sh"
OVERLAY_SCRIPT="${SCRIPT_DIR}/asmlite-visible-overlay.py"
FIXED_DELAY_SECONDS="1.5"
DEFAULT_EDITOR_FILTER="${DEFAULT_VISIBLE_FILTER:-ASMLiteVisibleEditorSmokeTests}"
DEFAULT_PLAYMODE_FILTER="${DEFAULT_VISIBLE_PLAYMODE_FILTER:-playmode}"
MODE="overlay"
TEST_FILTER=""
OVERLAY_PID=""
OVERLAY_DIR=""
OVERLAY_STATE_PATH=""
OVERLAY_ACK_PATH=""
OVERLAY_LOG_PATH=""
PYTHON_BIN=""

SMOKE_SUITE_CATALOG=(
  "install-scaffold|Install / Scaffold|ASMLiteEditorWorkflowAutomationTests"
  "workflow-lifecycle|Workflow Lifecycle|ASMLiteEditorWorkflowAutomationTests,ASMLiteCleanupTests"
  "customization|Customization|ASMLiteCustomizationScaffoldTests,ASMLiteCustomizationIntegrationTests"
  "migration-recovery|Migration / Recovery|ASMLiteMigrationTests"
  "runtime-upload|Runtime / Upload-Path|ASMLiteBuildIntegrationTests,ASMLiteVRCFuryPipelineTests"
  "regression-mixed-state|Regression / Mixed-State Edge Cases|ASMLiteBuildIntegrationTests,ASMLiteCleanupTests,ASMLiteEditorWorkflowAutomationTests"
)

usage() {
  cat <<'EOF'
Usage: Tools/ci/run-visible-smoke-local.sh [options]

Visible smoke options only:
  --overlay-smoke           Run the visible UAT overlay smoke suite (default)
  --editor-smoke            Run the visible editor smoke selector
  --playmode-smoke          Run the visible playmode smoke selector
  --test-filter <filter>    Override the visible selector for editor/playmode smoke
  -h, --help                Show this help text

Notes:
  - Visible step delay is fixed at 1.5 seconds.
  - There are no menu, headless, or delay-tuning options in this script.
EOF
}

ensure_python_bin() {
  if command -v python3 >/dev/null 2>&1; then
    printf 'python3\n'
    return 0
  fi

  if command -v python >/dev/null 2>&1; then
    printf 'python\n'
    return 0
  fi

  echo "error: python3/python not found in PATH." >&2
  exit 1
}

ensure_overlay_python_bin() {
  local candidate=""

  for candidate in pythonw.exe python.exe py.exe; do
    if command -v "${candidate}" >/dev/null 2>&1; then
      command -v "${candidate}"
      return 0
    fi
  done

  ensure_python_bin
}

path_arg_for_python() {
  local path="$1"

  if [[ "${PYTHON_BIN}" == *.exe ]]; then
    if ! command -v wslpath >/dev/null 2>&1; then
      echo "error: wslpath is required to launch the overlay with a Windows Python executable." >&2
      exit 1
    fi
    wslpath -w "${path}"
    return 0
  fi

  printf '%s\n' "${path}"
}

cleanup() {
  set +e

  if [[ -n "${OVERLAY_PID}" ]]; then
    kill "${OVERLAY_PID}" >/dev/null 2>&1 || true
    wait "${OVERLAY_PID}" >/dev/null 2>&1 || true
  fi
}

start_overlay() {
  local overlay_script_arg overlay_state_arg overlay_ack_arg overlay_log_arg
  local -a overlay_cmd

  mkdir -p "${ARTIFACTS_DIR}"
  OVERLAY_DIR="$(mktemp -d "${ARTIFACTS_DIR}/visible-smoke-overlay.XXXXXX")"
  OVERLAY_STATE_PATH="${OVERLAY_DIR}/state.json"
  OVERLAY_ACK_PATH="${OVERLAY_DIR}/ack.json"
  OVERLAY_LOG_PATH="${OVERLAY_DIR}/overlay.log"
  PYTHON_BIN="$(ensure_overlay_python_bin)"

  overlay_script_arg="$(path_arg_for_python "${OVERLAY_SCRIPT}")"
  overlay_state_arg="$(path_arg_for_python "${OVERLAY_STATE_PATH}")"
  overlay_ack_arg="$(path_arg_for_python "${OVERLAY_ACK_PATH}")"
  overlay_log_arg="$(path_arg_for_python "${OVERLAY_LOG_PATH}")"

  "$(ensure_python_bin)" - <<'PY' "${OVERLAY_STATE_PATH}"
import json
import sys
import time
from pathlib import Path

state_path = Path(sys.argv[1])
updated_ticks = int((time.time() + 62135596800) * 10_000_000)
payload = {
    "sessionId": "visible-smoke-local",
    "sessionActive": True,
    "state": "Running",
    "presentationMode": True,
    "title": "ASM-Lite visible smoke",
    "step": "Launching Unity visible smoke run",
    "stepIndex": 0,
    "totalSteps": 0,
    "checklist": [],
    "completionReviewVisible": False,
    "completionReviewRequestId": 0,
    "completionReviewTitle": "",
    "completionReviewMessage": "",
    "completionReviewAcknowledged": False,
    "updatedUtcTicks": updated_ticks,
    "meta": {
        "configuredStepDelaySeconds": "1.5",
    },
}
state_path.parent.mkdir(parents=True, exist_ok=True)
state_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")
PY

  if [[ "${PYTHON_BIN##*/}" == "py.exe" ]]; then
    overlay_cmd=(
      "${PYTHON_BIN}"
      -3
      "${overlay_script_arg}"
      --state-path "${overlay_state_arg}"
      --ack-path "${overlay_ack_arg}"
      --log-path "${overlay_log_arg}"
    )
  else
    overlay_cmd=(
      "${PYTHON_BIN}"
      "${overlay_script_arg}"
      --state-path "${overlay_state_arg}"
      --ack-path "${overlay_ack_arg}"
      --log-path "${overlay_log_arg}"
    )
  fi

  set +e
  "${overlay_cmd[@]}" >/dev/null 2>&1 &
  OVERLAY_PID=$!
  set -e
}

build_overlay_batch_plan() {
  local batch_plan_path="$1"
  local batch_results_dir="$2"
  local canonical_results_path="$3"
  local catalog_blob

  catalog_blob="$(printf '%s\n' "${SMOKE_SUITE_CATALOG[@]}")"

  SMOKE_SUITE_CATALOG_BLOB="${catalog_blob}" \
  "$(ensure_python_bin)" - <<'PY' \
    "${batch_plan_path}" \
    "${batch_results_dir}" \
    "${canonical_results_path}" \
    "${OVERLAY_STATE_PATH}"
import json
import os
import sys
from pathlib import Path

batch_plan_path = Path(sys.argv[1])
batch_results_dir = Path(sys.argv[2])
canonical_results_path = Path(sys.argv[3])
overlay_state_path = sys.argv[4]
catalog_entries = [line for line in os.environ.get("SMOKE_SUITE_CATALOG_BLOB", "").splitlines() if line.strip()]

runs = []
step_index = 0
for entry in catalog_entries:
    suite_id, suite_label, suite_filters = entry.split("|", 2)
    filters = [item.strip() for item in suite_filters.split(",") if item.strip()]
    for filter_name in filters:
        step_index += 1
        runs.append(
            {
                "name": filter_name,
                "suiteId": suite_id,
                "suiteLabel": suite_label,
                "resultFile": str(batch_results_dir / f"{step_index:02d}-{suite_id}-{filter_name}.xml"),
                "filters": [
                    {
                        "testNames": [filter_name],
                        "groupNames": [],
                        "categoryNames": [],
                        "assemblyNames": [],
                    }
                ],
                "runSynchronously": False,
                "allowEmptySelection": False,
            }
        )

payload = {
    "selection": "UAT checklist smoke suites",
    "runnerStrategy": "single_unity_instance",
    "stepDelaySeconds": 1.5,
    "overlayStatePath": overlay_state_path,
    "overlaySessionId": "visible-smoke-local",
    "resultsDir": str(batch_results_dir),
    "canonicalResultsPath": str(canonical_results_path),
    "batchPlanPath": str(batch_plan_path),
    "runs": runs,
}

batch_plan_path.parent.mkdir(parents=True, exist_ok=True)
batch_results_dir.mkdir(parents=True, exist_ok=True)
batch_plan_path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
PY
}

run_overlay_smoke() {
  local batch_plan_path batch_results_dir canonical_results_path

  mkdir -p "${ARTIFACTS_DIR}"
  batch_plan_path="${ARTIFACTS_DIR}/.visible-smoke-suite-plan.json"
  batch_results_dir="${ARTIFACTS_DIR}/visible-smoke-suite-runs"
  canonical_results_path="${ARTIFACTS_DIR}/visible-smoke-suite-results.xml"

  rm -rf "${batch_results_dir}"
  rm -f "${batch_plan_path}" "${canonical_results_path}"

  start_overlay
  build_overlay_batch_plan "${batch_plan_path}" "${batch_results_dir}" "${canonical_results_path}"

  echo "Running visible UAT overlay smoke suite against:"
  echo "  Project: ${CANONICAL_PROJECT_PATH}"
  echo "  Package: ${REPO_ROOT}/Packages/com.staples.asm-lite"
  echo "  Mode:    overlay_smoke"
  echo "  Delay:   ${FIXED_DELAY_SECONDS}s"
  echo "  Overlay: ${OVERLAY_STATE_PATH}"

  ASMLITE_BATCH_RUNS_JSON_PATH="${batch_plan_path}" \
  ASMLITE_BATCH_RESULTS_DIR="${batch_results_dir}" \
  ASMLITE_BATCH_CANONICAL_RESULTS_PATH="${canonical_results_path}" \
  ASMLITE_BATCH_OVERLAY_STATE_PATH="${OVERLAY_STATE_PATH}" \
  ASMLITE_BATCH_STEP_DELAY_SECONDS="${FIXED_DELAY_SECONDS}" \
  ASMLITE_BATCH_SELECTION_LABEL="UAT checklist smoke suites" \
  ASMLITE_BATCH_SESSION_ID="visible-smoke-local" \
  ASMLITE_BATCH_OVERLAY_TITLE="ASM-Lite UAT smoke suites" \
  bash "${RUN_EDITMODE_SCRIPT}" --local --project-path "${CANONICAL_PROJECT_PATH}" --visible-editor-suite

  echo
  echo "Artifacts:"
  echo "  Overlay log: ${OVERLAY_LOG_PATH}"
  echo "  Overlay state: ${OVERLAY_STATE_PATH}"
  echo "  Batch plan: ${batch_plan_path}"
  echo "  Batch results: ${batch_results_dir}"
  echo "  Canonical results: ${canonical_results_path}"
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
