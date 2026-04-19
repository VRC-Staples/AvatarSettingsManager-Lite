#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
ARTIFACTS_DIR="${REPO_ROOT}/artifacts"
RUN_EDITMODE_SCRIPT="${SCRIPT_DIR}/run-editmode-local.sh"
OVERLAY_SCRIPT="${SCRIPT_DIR}/asmlite-visible-overlay.py"
FIXED_DELAY_SECONDS="1.5"
DEFAULT_EDITOR_FILTER="${DEFAULT_UAT_EDITOR_FILTER:-launch-unity}"
DEFAULT_PLAYMODE_FILTER="${DEFAULT_UAT_PLAYMODE_FILTER:-UATPlayModeSmokeScaffold}"
MODE="overlay"
TEST_FILTER=""
OVERLAY_PID=""
OVERLAY_DIR=""
OVERLAY_STATE_PATH=""
OVERLAY_ACK_PATH=""
OVERLAY_LOG_PATH=""
PYTHON_BIN=""

UAT_SUITE_CATALOG=(
  "launch-unity|Launch Unity|launch-unity"
)

usage() {
  cat <<'EOF'
Usage: Tools/ci/run-UAT-smoke.sh [options]

Visible UAT smoke options:
  --overlay-smoke           Run visible UAT overlay smoke suites (default)
  --editor-smoke            Run a visible editor UAT selector
  --playmode-smoke          Run a visible playmode UAT selector
  --test-filter <filter>    Override selector for editor/playmode smoke
  -h, --help                Show this help text

Notes:
  - Visible step delay is fixed at 1.5 seconds.
  - Default editor selector is launch-unity.
  - Overlay mode runs configured UAT suite catalog in a single visible Unity session.
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

write_overlay_state() {
  local step="$1"
  local step_index="$2"
  local total_steps="$3"
  local state_label="$4"
  local title="$5"
  local completion_review_visible="$6"

  "$(ensure_python_bin)" - <<'PY' \
    "${OVERLAY_STATE_PATH}" \
    "${step}" \
    "${step_index}" \
    "${total_steps}" \
    "${state_label}" \
    "${title}" \
    "${completion_review_visible}" \
    "${FIXED_DELAY_SECONDS}"
import json
import sys
import time
from pathlib import Path

state_path = Path(sys.argv[1])
step = sys.argv[2]
step_index = int(sys.argv[3])
total_steps = int(sys.argv[4])
state_label = sys.argv[5]
title = sys.argv[6]
completion_review_visible = sys.argv[7].lower() == "true"
step_delay_seconds = sys.argv[8]
updated_ticks = int((time.time() + 62135596800) * 10_000_000)

payload = {
    "sessionId": "visible-uat-local",
    "sessionActive": True,
    "state": state_label,
    "presentationMode": True,
    "title": title,
    "step": step,
    "stepIndex": step_index,
    "totalSteps": total_steps,
    "checklist": [],
    "completionReviewVisible": completion_review_visible,
    "completionReviewRequestId": 0,
    "completionReviewTitle": "",
    "completionReviewMessage": "",
    "completionReviewAcknowledged": False,
    "updatedUtcTicks": updated_ticks,
    "meta": {
        "configuredStepDelaySeconds": step_delay_seconds,
        "scaffoldOnly": "true",
    },
}
state_path.parent.mkdir(parents=True, exist_ok=True)
state_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")
PY
}

start_overlay() {
  local overlay_script_arg overlay_state_arg overlay_ack_arg overlay_log_arg
  local -a overlay_cmd

  mkdir -p "${ARTIFACTS_DIR}"
  OVERLAY_DIR="$(mktemp -d "${ARTIFACTS_DIR}/uat-smoke-overlay.XXXXXX")"
  OVERLAY_STATE_PATH="${OVERLAY_DIR}/state.json"
  OVERLAY_ACK_PATH="${OVERLAY_DIR}/ack.json"
  OVERLAY_LOG_PATH="${OVERLAY_DIR}/overlay.log"
  PYTHON_BIN="$(ensure_overlay_python_bin)"

  overlay_script_arg="$(path_arg_for_python "${OVERLAY_SCRIPT}")"
  overlay_state_arg="$(path_arg_for_python "${OVERLAY_STATE_PATH}")"
  overlay_ack_arg="$(path_arg_for_python "${OVERLAY_ACK_PATH}")"
  overlay_log_arg="$(path_arg_for_python "${OVERLAY_LOG_PATH}")"

  write_overlay_state \
    "Preparing ASM-Lite UAT smoke scaffold" \
    0 \
    0 \
    "Running" \
    "ASM-Lite UAT smoke scaffold" \
    "false"

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

  catalog_blob="$(printf '%s\n' "${UAT_SUITE_CATALOG[@]:-}")"

  UAT_SUITE_CATALOG_BLOB="${catalog_blob}" \
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
catalog_entries = [line for line in os.environ.get("UAT_SUITE_CATALOG_BLOB", "").splitlines() if line.strip()]

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
    "selection": "ASM-Lite UAT smoke scaffold",
    "runnerStrategy": "single_unity_instance",
    "stepDelaySeconds": 1.5,
    "overlayStatePath": overlay_state_path,
    "overlaySessionId": "visible-uat-local",
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

count_catalog_runs() {
  local entry filters_csv
  local count=0

  for entry in "${UAT_SUITE_CATALOG[@]:-}"; do
    [[ -n "${entry}" ]] || continue
    filters_csv="${entry##*|}"
    IFS=',' read -r -a filters <<< "${filters_csv}"
    count=$((count + ${#filters[@]}))
  done

  printf '%s\n' "${count}"
}

run_overlay_smoke() {
  local selector_override="$1"
  local selector first_entry filters_csv
  local -a first_filters=()

  if [[ -n "${selector_override}" ]]; then
    selector="${selector_override}"
  else
    selector="${DEFAULT_EDITOR_FILTER}"
    if [[ ${#UAT_SUITE_CATALOG[@]} -gt 0 ]]; then
      first_entry="${UAT_SUITE_CATALOG[0]}"
      filters_csv="${first_entry##*|}"
      IFS=',' read -r -a first_filters <<< "${filters_csv}"
      if [[ ${#first_filters[@]} -gt 0 && -n "${first_filters[0]}" ]]; then
        selector="${first_filters[0]}"
      fi
    fi
  fi

  echo "Running visible UAT overlay smoke against:"
  echo "  Project: ${REPO_ROOT}/../Test Project/TestUnityProject"
  echo "  Package: ${REPO_ROOT}/Packages/com.staples.asm-lite"
  echo "  Mode:    overlay_smoke"
  echo "  Selector: ${selector}"
  echo "  Delay:   ${FIXED_DELAY_SECONDS}s"

  ASMLITE_VISIBLE_SMOKE_STEP_DELAY_SECONDS="${FIXED_DELAY_SECONDS}" \
  bash "${RUN_EDITMODE_SCRIPT}" --local --visible-editor-smoke --test-filter "${selector}"
}

run_selector_smoke() {
  local selector="$1"
  local mode_label="$2"

  start_overlay
  write_overlay_state \
    "Launching visible ${mode_label} UAT selector in Unity" \
    0 \
    1 \
    "Running" \
    "ASM-Lite ${mode_label} smoke" \
    "false"

  echo "Running visible ${mode_label} smoke against:"
  echo "  Project: ${REPO_ROOT}/../Test Project/TestUnityProject"
  echo "  Package: ${REPO_ROOT}/Packages/com.staples.asm-lite"
  echo "  Selector: ${selector}"
  echo "  Delay:    ${FIXED_DELAY_SECONDS}s"
  echo "  Overlay:  ${OVERLAY_STATE_PATH}"

  ASMLITE_VISIBLE_SMOKE_STEP_DELAY_SECONDS="${FIXED_DELAY_SECONDS}" \
  bash "${RUN_EDITMODE_SCRIPT}" --local --visible-editor-smoke --test-filter "${selector}"
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
  echo "error: missing runner script scaffold dependency: ${RUN_EDITMODE_SCRIPT}" >&2
  exit 1
fi

if [[ ! -f "${OVERLAY_SCRIPT}" ]]; then
  echo "error: missing overlay script: ${OVERLAY_SCRIPT}" >&2
  exit 1
fi

trap cleanup EXIT INT TERM HUP

case "${MODE}" in
  overlay)
    run_overlay_smoke "${TEST_FILTER:-}"
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
