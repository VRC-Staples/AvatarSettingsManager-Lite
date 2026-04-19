#!/usr/bin/env bash
set -euo pipefail

# -----------------------------------------------------------------------------
# run-UAT-smoke.sh
#
# Runs the full ASM-Lite visible UAT smoke suite in one path:
#   - discovers smoke categories from test source annotations
#   - generates a visible-suite batch plan
#   - executes the full run in one visible Unity session
#
# The script performs four major responsibilities:
# 1) Resolve working paths, executable selections, and environment-driven defaults.
# 2) Discover candidate UAT categories directly from source annotations.
# 3) Generate a batch JSON plan for Unity's batch runner.
# 4) Launch visible Unity smoke execution and report PASS/FAIL for the run.
# -----------------------------------------------------------------------------

# Repository-relative anchors for dependent scripts and output artifacts.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
ARTIFACTS_DIR="${REPO_ROOT}/artifacts"
RUN_EDITMODE_SCRIPT="${SCRIPT_DIR}/run-editmode-local.sh"
OVERLAY_SCRIPT="${SCRIPT_DIR}/asmlite-visible-overlay.py"

# Timing defaults for smoke execution.
FIXED_DELAY_SECONDS="1.5"

# Category discovery inputs; these drive which suites get included in the full visible smoke run.
# UAT_DISCOVERY_ROOT can be overridden if the source tree is relocated.
# UAT_CATEGORY_INCLUDE_REGEX can be overridden to broaden/narrow the discovered set.
UAT_DISCOVERY_ROOT="${REPO_ROOT}/Packages/com.staples.asm-lite/Tests/Editor"
UAT_CATEGORY_INCLUDE_REGEX="${UAT_CATEGORY_INCLUDE_REGEX:-^(Visible.*Automation|.*UAT.*)$}"

# Runtime state used for one visible smoke execution path.

# Overlay process tracking state.
OVERLAY_PID=""
OVERLAY_DIR=""
OVERLAY_STATE_PATH=""
OVERLAY_ACK_PATH=""
OVERLAY_LOG_PATH=""
PYTHON_BIN=""

# Emit usage and CLI help for the single visible smoke execution path.
usage() {
  cat <<'EOF'
Usage: Tools/ci/run-UAT-smoke.sh

Runs the full ASM-Lite visible UAT smoke suite.

Options:
  -h, --help                Show this help text

Notes:
  - Visible step delay is fixed at 1.5 seconds.
  - Test categories are discovered from Tests/Editor annotations.
  - Category discovery can be narrowed with UAT_CATEGORY_INCLUDE_REGEX.
EOF
}

# Resolve the active Python interpreter for non-Windows helper invocations.
# Prefers python3, then python, and fails loudly if neither is on PATH.
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

# Resolve the active Python interpreter for overlay process launch.
# Tries common Windows launchers first (pythonw.exe/python.exe/py.exe)
# and falls back to the same resolution used by ensure_python_bin.
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

# Convert a local filesystem path to the format expected by the selected
# overlay Python executable. This is required when using Windows Python binaries
# from WSL, where overlay expects Windows-style paths.
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

# Tear down the background overlay process on script exit/interrupt.
# Uses set +e around signal handling so cleanup is best-effort and never masks
# the original exit reason.
cleanup() {
  set +e

  if [[ -n "${OVERLAY_PID}" ]]; then
    kill "${OVERLAY_PID}" >/dev/null 2>&1 || true
    wait "${OVERLAY_PID}" >/dev/null 2>&1 || true
  fi
}

# Persist a single overlay state payload for the GUI to consume.
# Parameters:
#   $1 step text shown in the overlay
#   $2 zero-based/one-based step index currently being displayed
#   $3 total step count for progress context
#   $4 overlay state label (eg. "Running")
#   $5 overlay window title
#   $6 whether completion review UI should be visible
#
# The Python snippet writes the same JSON contract as the overlay renderer,
# including tick timestamps used for stale-data detection.
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

# Start the Python overlay helper in the background and capture its PID.
# The overlay runs in its own process so the Unity session can proceed while
# the state file is continuously polled by the overlay UI.
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

# Discover UAT categories from source by scanning [Category("...")] attributes.
# The scan is constrained by:
#   - a discovery root directory (UAT_DISCOVERY_ROOT)
#   - a regex filter for category names (UAT_CATEGORY_INCLUDE_REGEX)
#
# The resulting category list preserves first-seen order and deduplicates by
# repeated annotation values.
discover_uat_categories() {
  UAT_DISCOVERY_ROOT="${UAT_DISCOVERY_ROOT}" \
  UAT_CATEGORY_INCLUDE_REGEX="${UAT_CATEGORY_INCLUDE_REGEX}" \
  "$(ensure_python_bin)" - <<'PY'
import os
import re
import sys
from pathlib import Path

root = Path(os.environ["UAT_DISCOVERY_ROOT"])
include_pattern = re.compile(os.environ["UAT_CATEGORY_INCLUDE_REGEX"])
category_pattern = re.compile(r'\[Category\("([^"]+)"\)\]')

if not root.exists():
    print(f"error: discovery root does not exist: {root}", file=sys.stderr)
    sys.exit(1)

categories = []
seen = set()
for path in sorted(root.rglob("*.cs")):
    try:
        content = path.read_text(encoding="utf-8")
    except UnicodeDecodeError:
        content = path.read_text(encoding="utf-8-sig")

    for category in category_pattern.findall(content):
        if not include_pattern.search(category):
            continue
        if category in seen:
            continue
        seen.add(category)
        categories.append(category)

if not categories:
    print(
        f"error: no UAT categories matched {include_pattern.pattern!r} under {root}",
        file=sys.stderr,
    )
    sys.exit(1)

for category in categories:
    print(category)
PY
}

# Build the overlay batch plan JSON consumed by run-editmode-local.sh's batch
# runner mode. Each discovered category becomes one run with a category-based
# filter so newly added tests in that category are automatically included.
build_overlay_batch_plan() {
  local batch_plan_path="$1"
  local batch_results_dir="$2"
  local canonical_results_path="$3"
  local discovered_categories

  discovered_categories="$(discover_uat_categories)"

  UAT_DISCOVERED_CATEGORIES="${discovered_categories}" \
  "$(ensure_python_bin)" - <<'PY' \
    "${batch_plan_path}" \
    "${batch_results_dir}" \
    "${canonical_results_path}" \
    "${OVERLAY_STATE_PATH}"
import json
import os
import re
import sys
from pathlib import Path

batch_plan_path = Path(sys.argv[1])
batch_results_dir = Path(sys.argv[2])
canonical_results_path = Path(sys.argv[3])
overlay_state_path = sys.argv[4]
categories = [line.strip() for line in os.environ.get("UAT_DISCOVERED_CATEGORIES", "").splitlines() if line.strip()]

runs = []
for step_index, category_name in enumerate(categories, start=1):
    suite_id = re.sub(r"[^A-Za-z0-9]+", "-", category_name).strip("-").lower() or f"category-{step_index:02d}"
    runs.append(
        {
            "name": category_name,
            "suiteId": suite_id,
            "suiteLabel": category_name,
            "resultFile": str(batch_results_dir / f"{step_index:02d}-{suite_id}.xml"),
            "filters": [
                {
                    "testNames": [],
                    "groupNames": [],
                    "categoryNames": [category_name],
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

# Execute the full visible smoke run:
# 1) discover categories in source and write a batch plan
# 2) launch the overlay and Unity in visible suite mode
# 3) print the discovered suite list for traceability
# 4) return Unity exit code for final status reporting
run_overlay_smoke() {
  local batch_plan_path batch_results_dir canonical_results_path total_runs discovered_categories

  mkdir -p "${ARTIFACTS_DIR}"
  batch_plan_path="${ARTIFACTS_DIR}/.uat-smoke-suite-plan.json"
  batch_results_dir="${ARTIFACTS_DIR}/uat-smoke-suite-runs"
  canonical_results_path="${ARTIFACTS_DIR}/uat-smoke-suite-results.xml"

  rm -rf "${batch_results_dir}"
  rm -f "${batch_plan_path}" "${canonical_results_path}"

  discovered_categories="$(discover_uat_categories)"
  total_runs="$(CATEGORY_BLOB="${discovered_categories}" "$(ensure_python_bin)" - <<'PY'
import os
print(sum(1 for line in os.environ.get("CATEGORY_BLOB", "").splitlines() if line.strip()))
PY
)"

  start_overlay
  build_overlay_batch_plan "${batch_plan_path}" "${batch_results_dir}" "${canonical_results_path}"

  echo "Running full visible UAT smoke suite against:"
  echo "  Project: ${REPO_ROOT}/../Test Project/TestUnityProject"
  echo "  Package: ${REPO_ROOT}/Packages/com.staples.asm-lite"
  echo "  Mode:    full_visible_smoke"
  echo "  Suites:  ${total_runs} discovered category run(s)"
  while IFS= read -r category_name; do
    [[ -n "${category_name}" ]] || continue
    echo "  Category: ${category_name}"
  done <<< "${discovered_categories}"
  echo "  Delay:   ${FIXED_DELAY_SECONDS}s"
  echo "  Overlay: ${OVERLAY_STATE_PATH}"
  echo "  Overlay ack: ${OVERLAY_ACK_PATH}"

  set +e
  ASMLITE_BATCH_RUNS_JSON_PATH="${batch_plan_path}" \
  ASMLITE_BATCH_RESULTS_DIR="${batch_results_dir}" \
  ASMLITE_BATCH_CANONICAL_RESULTS_PATH="${canonical_results_path}" \
  ASMLITE_BATCH_STEP_DELAY_SECONDS="${FIXED_DELAY_SECONDS}" \
  ASMLITE_BATCH_SELECTION_LABEL="ASM-Lite UAT smoke scaffold" \
  ASMLITE_BATCH_SESSION_ID="visible-uat-local" \
  ASMLITE_BATCH_OVERLAY_TITLE="ASM-Lite UAT smoke suites" \
  ASMLITE_VISIBLE_SMOKE_STEP_DELAY_SECONDS="${FIXED_DELAY_SECONDS}" \
  ASMLITE_VISIBLE_SMOKE_EXTERNAL_OVERLAY_STATE_PATH="${OVERLAY_STATE_PATH}" \
  ASMLITE_VISIBLE_SMOKE_EXTERNAL_OVERLAY_ACK_PATH="${OVERLAY_ACK_PATH}" \
  bash "${RUN_EDITMODE_SCRIPT}" --local --visible-editor-suite
  run_exit_code=$?
  set -e

  echo
  print_run_result "visible-smoke" "${run_exit_code}"

  echo
  echo "Artifacts:"
  echo "  Overlay log: ${OVERLAY_LOG_PATH}"
  echo "  Overlay state: ${OVERLAY_STATE_PATH}"
  echo "  Batch plan: ${batch_plan_path}"
  echo "  Batch results: ${batch_results_dir}"
  echo "  Canonical results: ${canonical_results_path}"

  return "${run_exit_code}"
}

# Standardized status line formatter for the visible smoke execution path.
# Keeps PASS/FAIL output stable for this run.
print_run_result() {
  local mode_label="$1"
  local exit_code="$2"

  if [[ "${exit_code}" -eq 0 ]]; then
    echo "${mode_label}: PASS"
  else
    echo "${mode_label}: FAIL (exit code ${exit_code})"
  fi
}
# Parse CLI arguments for the single execution path.
# Only --help is accepted; any other flag is rejected.
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

if [[ ! -x "${RUN_EDITMODE_SCRIPT}" ]]; then
  echo "error: missing runner script scaffold dependency: ${RUN_EDITMODE_SCRIPT}" >&2
  exit 1
fi

if [[ ! -f "${OVERLAY_SCRIPT}" ]]; then
  echo "error: missing overlay script: ${OVERLAY_SCRIPT}" >&2
  exit 1
fi

# Validate required dependencies (runner and overlay scripts) before invoking any
# long-running work, then install cleanup trap.
trap cleanup EXIT INT TERM HUP

run_overlay_smoke
