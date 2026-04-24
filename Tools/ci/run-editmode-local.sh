#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
WORKSPACE_ROOT="$(cd "${REPO_ROOT}/.." && pwd)"
REPO_DIR_NAME="$(basename "${REPO_ROOT}")"
PROJECT_REL_PATH="Tools/ci/unity-project"
PROJECT_PATH="${REPO_ROOT}/${PROJECT_REL_PATH}"
PROJECT_VERSION_FILE="${PROJECT_PATH}/ProjectSettings/ProjectVersion.txt"
CANONICAL_BATCH_RUNS_JSON_REL_PATH="Tools/ci/editmode-batch-runs.json"
CANONICAL_BATCH_RUNS_JSON_PATH="${REPO_ROOT}/${CANONICAL_BATCH_RUNS_JSON_REL_PATH}"
ARTIFACTS_DIR="${REPO_ROOT}/artifacts"
COVERAGE_DIR="${REPO_ROOT}/CodeCoverage"
DOTENV_FILE="${REPO_ROOT}/.env"

RUN_TIMESTAMP=""
RUN_ARTIFACT_BASENAME=""
EDITMODE_LOG_PATH=""
EDITMODE_RESULTS_PATH=""
VISIBLE_EDITOR_SMOKE_RESULTS_PATH=""
RUN_COVERAGE_DIR=""
VISIBLE_OVERLAY_DIR=""
VISIBLE_OVERLAY_STATE_PATH=""
VISIBLE_OVERLAY_ACK_PATH=""
VISIBLE_OVERLAY_LOG_PATH=""

RUN_TIMEOUT_SECONDS="${RUN_TIMEOUT_SECONDS:-2700}"
RUN_LOCK_DIR="${RUN_LOCK_DIR:-/tmp/asmlite-editmode-local.lock}"
RUN_LOCK_WAIT_SECONDS="${RUN_LOCK_WAIT_SECONDS:-0}"
RUN_LOCK_POLL_SECONDS="${RUN_LOCK_POLL_SECONDS:-5}"
LOCAL_DOCKER_CPUS="${LOCAL_DOCKER_CPUS:-}"
EDITMODE_RUNNER_MODE="${EDITMODE_RUNNER_MODE:-docker}"
EDITMODE_LOCAL_EXECUTION_STYLE="${EDITMODE_LOCAL_EXECUTION_STYLE:-headless}"
UNITY_EXECUTABLE="${UNITY_EXECUTABLE:-}"
EDITMODE_TEST_FILTER="${EDITMODE_TEST_FILTER:-}"
EDITMODE_VISIBLE_CATEGORY="${EDITMODE_VISIBLE_CATEGORY:-VisibleEditorAutomation}"
LOCAL_UNITY_MANAGE_LICENSE="${LOCAL_UNITY_MANAGE_LICENSE:-0}"
VISIBLE_SMOKE_STEP_DELAY_SECONDS="${ASMLITE_VISIBLE_SMOKE_STEP_DELAY_SECONDS:-1.0}"

RUN_LOCK_ACQUIRED=0
DOCKER_CPU_ARGS=()
SERIAL_FILE=""
RUNNER_SCRIPT=""
DOCKER_HELPER_SHIM_DIR=""
UNITY_VERSION=""
UNITY_IMAGE=""
UNITY_SERIAL="${UNITY_SERIAL:-}"
UNITY_EMAIL="${UNITY_EMAIL:-}"
UNITY_PASSWORD="${UNITY_PASSWORD:-}"
UNITY_LICENSE_SECRET="${UNITY_LICENSE_SECRET:-}"
UNITY_LICENSE_FILE="${UNITY_LICENSE_FILE:-}"
LOCAL_UNITY_RETURN_LICENSE=0

write_run_lock_metadata() {
  local started_at
  started_at="$(date -u +%s)"

  printf '%s\n' "$$" > "${RUN_LOCK_DIR}/pid"
  printf '%s\n' "${RUN_ARTIFACT_BASENAME}" > "${RUN_LOCK_DIR}/artifact_basename"
  printf '%s\n' "${EDITMODE_TEST_FILTER}" > "${RUN_LOCK_DIR}/test_filter"
  printf '%s\n' "${EDITMODE_VISIBLE_CATEGORY}" > "${RUN_LOCK_DIR}/visible_category"
  printf '%s\n' "${EDITMODE_RUNNER_MODE}" > "${RUN_LOCK_DIR}/runner_mode"
  printf '%s\n' "${EDITMODE_LOCAL_EXECUTION_STYLE}" > "${RUN_LOCK_DIR}/execution_style"
  printf '%s\n' "${started_at}" > "${RUN_LOCK_DIR}/started_at"
}

read_run_lock_metadata_value() {
  local name="$1"
  local value_file="${RUN_LOCK_DIR}/${name}"

  if [[ ! -f "${value_file}" ]]; then
    return 0
  fi

  tr -d '\r\n' < "${value_file}" || true
}

format_lock_duration() {
  local total_seconds="$1"
  local hours minutes seconds formatted=""

  if ! [[ "${total_seconds}" =~ ^[0-9]+$ ]]; then
    return 0
  fi

  hours=$((total_seconds / 3600))
  minutes=$(((total_seconds % 3600) / 60))
  seconds=$((total_seconds % 60))

  if [[ "${hours}" -gt 0 ]]; then
    formatted+="${hours}h"
  fi

  if [[ "${minutes}" -gt 0 || "${hours}" -gt 0 ]]; then
    formatted+="${minutes}m"
  fi

  formatted+="${seconds}s"
  printf '%s' "${formatted}"
}

build_lock_wait_message() {
  local existing_pid="$1"
  local artifact_basename test_filter visible_category runner_mode execution_style started_at context=""
  local active_for_seconds="" active_for_label=""

  artifact_basename="$(read_run_lock_metadata_value artifact_basename)"
  test_filter="$(read_run_lock_metadata_value test_filter)"
  visible_category="$(read_run_lock_metadata_value visible_category)"
  runner_mode="$(read_run_lock_metadata_value runner_mode)"
  execution_style="$(read_run_lock_metadata_value execution_style)"
  started_at="$(read_run_lock_metadata_value started_at)"

  if [[ -n "${started_at}" && "${started_at}" =~ ^[0-9]+$ ]]; then
    local now
    now="$(date -u +%s)"
    if [[ "${now}" -ge "${started_at}" ]]; then
      active_for_seconds="$((now - started_at))"
      active_for_label="$(format_lock_duration "${active_for_seconds}")"
    fi
  fi

  if [[ -n "${artifact_basename}" ]]; then
    context="artifact ${artifact_basename}"
  fi

  if [[ -n "${test_filter}" ]]; then
    if [[ -n "${context}" ]]; then
      context="${context}, filter ${test_filter}"
    else
      context="filter ${test_filter}"
    fi
  elif [[ "${runner_mode}" == "local" && "${execution_style}" == "visible_smoke" && -n "${visible_category}" ]]; then
    if [[ -n "${context}" ]]; then
      context="${context}, visible category ${visible_category}"
    else
      context="visible category ${visible_category}"
    fi
  fi

  if [[ -n "${active_for_label}" ]]; then
    if [[ -n "${context}" ]]; then
      context="${context}, active for ${active_for_label}"
    else
      context="active for ${active_for_label}"
    fi
  fi

  if [[ -n "${context}" ]]; then
    printf 'note: another EditMode run is active (pid %s, %s); ' "${existing_pid}" "${context}"
  else
    printf 'note: another EditMode run is active (pid %s); ' "${existing_pid}"
  fi

  if [[ "${RUN_LOCK_WAIT_SECONDS}" -gt 0 ]]; then
    printf 'waiting up to %ss for %s.\n' "${RUN_LOCK_WAIT_SECONDS}" "${RUN_LOCK_DIR}"
  else
    printf 'waiting for %s to clear.\n' "${RUN_LOCK_DIR}"
  fi
}

usage() {
  cat <<'EOF'
Usage: Tools/ci/run-editmode-local.sh [options]

Run Unity EditMode tests using either Docker (default) or a locally installed Unity editor.

Visible smoke mode now launches the custom visible automation command-line harness via
-executeMethod instead of the Unity Test Runner. Pass --test-filter playmode (or any
selector containing playmode/runtime) to exercise the PlayMode-visible path.

Options:
  --mode <docker|local>     Choose execution mode (default: docker)
  --docker                  Shortcut for --mode docker
  --local                   Shortcut for --mode local
  --project-path <path>     Override Unity project path (default: Tools/ci/unity-project)
  --unity-path <path>       Path or command name for the local Unity executable
  --test-filter <filter>    Forward a Unity -testFilter value for targeted EditMode runs
  --manage-license          Activate/return a Unity license for local mode
  --no-manage-license       Skip local license activation/return (default for local mode)
  --visible-editor-smoke    Run visible local EditMode smoke tests in a Unity window (local mode only)
  --visible-editor-suite    Run visible local EditMode suite filters in a Unity window (local mode only)
  --timeout <seconds>       Override RUN_TIMEOUT_SECONDS for this invocation
  --lock-wait <seconds>     Queue behind an active run; 0 waits indefinitely (default)
  --lock-poll <seconds>     Poll interval while waiting on the run lock (default: 5)
  --docker-cpus <value>     Override LOCAL_DOCKER_CPUS for this invocation
  -h, --help                Show this help text

Environment overrides:
  EDITMODE_RUNNER_MODE      Default execution mode when no CLI mode is supplied
  UNITY_EXECUTABLE          Default local Unity executable path/command
  EDITMODE_TEST_FILTER      Default Unity -testFilter value
  EDITMODE_LOCAL_EXECUTION_STYLE
                            Local execution style: headless (default), visible_smoke, or visible_suite
  EDITMODE_VISIBLE_CATEGORY Visible smoke-test category selector (default: VisibleEditorAutomation)
  LOCAL_UNITY_MANAGE_LICENSE
                            Enable license activation/return in local mode (1/0, true/false)
  RUN_TIMEOUT_SECONDS       Timeout applied to Docker or local Unity execution
  RUN_LOCK_WAIT_SECONDS     Seconds to wait for the active run lock (0 = wait indefinitely)
  RUN_LOCK_POLL_SECONDS     Poll interval while waiting for the run lock
  LOCAL_DOCKER_CPUS         Optional Docker CPU limit for Docker mode

Docker mode keeps the existing CI-style activation/return flow and requires
UNITY_EMAIL, UNITY_PASSWORD, plus UNITY_SERIAL or UNITY_LICENSE_SECRET/UNITY_LICENSE_FILE.

When --test-filter is not supplied and no ASMLITE_BATCH_RUNS_JSON/ASMLITE_BATCH_RUNS_JSON_PATH
override is set, the run defaults to Tools/ci/editmode-batch-runs.json.

Local mode assumes an already activated Unity installation unless --manage-license
(or LOCAL_UNITY_MANAGE_LICENSE=1) is provided.
EOF
}

load_dotenv_defaults() {
  local file="$1"
  [[ -f "$file" ]] || return 0

  local line key raw value
  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%$'\r'}"

    [[ "$line" =~ ^[[:space:]]*$ ]] && continue
    [[ "$line" =~ ^[[:space:]]*# ]] && continue
    [[ "$line" =~ ^[[:space:]]*(export[[:space:]]+)?([A-Za-z_][A-Za-z0-9_]*)=(.*)$ ]] || continue

    key="${BASH_REMATCH[2]}"
    raw="${BASH_REMATCH[3]}"

    case "$key" in
      UNITY_LICENSE_SECRET|UNITY_LICENSE_FILE|UNITY_EMAIL|UNITY_PASSWORD|UNITY_SERIAL|LOCAL_DOCKER_CPUS|EDITMODE_RUNNER_MODE|EDITMODE_LOCAL_EXECUTION_STYLE|UNITY_EXECUTABLE|EDITMODE_TEST_FILTER|EDITMODE_VISIBLE_CATEGORY|LOCAL_UNITY_MANAGE_LICENSE) ;;
      *) continue ;;
    esac

    if [[ -n "${!key:-}" ]]; then
      continue
    fi

    value="${raw#"${raw%%[![:space:]]*}"}"
    value="${value%"${value##*[![:space:]]}"}"

    if [[ "$value" =~ ^\"(.*)\"$ ]]; then
      value="${BASH_REMATCH[1]}"
      value="${value//\\\"/\"}"
      value="${value//\\\\/\\}"
    elif [[ "$value" =~ ^\'(.*)\'$ ]]; then
      value="${BASH_REMATCH[1]}"
    fi

    printf -v "$key" '%s' "$value"
    export "$key"
  done < "$file"
}

require_env() {
  local name="$1"
  local hint="$2"
  if [[ -z "${!name:-}" ]]; then
    echo "error: ${name} is required. ${hint}" >&2
    exit 1
  fi
}

normalize_bool() {
  local raw="${1:-}"
  case "${raw,,}" in
    1|true|yes|on) printf '1' ;;
    0|false|no|off|'') printf '0' ;;
    *)
      echo "error: invalid boolean value '${raw}'. Use 1/0, true/false, yes/no, or on/off." >&2
      exit 1
      ;;
  esac
}

normalize_executable_path() {
  local value="$1"
  if [[ "$value" =~ ^[A-Za-z]:\\ ]]; then
    if ! command -v wslpath >/dev/null 2>&1; then
      echo "error: wslpath is required to convert Windows UNITY_EXECUTABLE paths." >&2
      exit 1
    fi
    wslpath -u "$value"
    return 0
  fi

  printf '%s\n' "$value"
}

resolve_repo_relative_path() {
  local value="$1"

  if [[ "$value" =~ ^[A-Za-z]:\\ ]]; then
    if ! command -v wslpath >/dev/null 2>&1; then
      echo "error: wslpath is required to convert Windows paths." >&2
      exit 1
    fi
    wslpath -u "$value"
    return 0
  fi

  if [[ "$value" == /* ]]; then
    printf '%s\n' "$value"
    return 0
  fi

  printf '%s\n' "${REPO_ROOT}/${value}"
}

to_docker_workspace_path() {
  local host_path="$1"

  if [[ "$host_path" == "/github/workspace" || "$host_path" == /github/workspace/* ]]; then
    printf '%s\n' "$host_path"
    return 0
  fi

  if [[ "$host_path" == "${WORKSPACE_ROOT}" ]]; then
    printf '/github/workspace\n'
    return 0
  fi

  if [[ "$host_path" == "${WORKSPACE_ROOT}/"* ]]; then
    printf '/github/workspace/%s\n' "${host_path#"${WORKSPACE_ROOT}/"}"
    return 0
  fi

  echo "error: path '${host_path}' is outside workspace root '${WORKSPACE_ROOT}' and is not available in Docker mode." >&2
  exit 1
}

is_windows_executable() {
  local executable="$1"
  [[ "$executable" == *.exe ]]
}

unity_path_arg() {
  local path="$1"
  if is_windows_executable "${UNITY_EXECUTABLE}"; then
    if ! command -v wslpath >/dev/null 2>&1; then
      echo "error: wslpath is required when running a Windows Unity executable from WSL." >&2
      exit 1
    fi
    wslpath -w "$path"
    return 0
  fi

  printf '%s\n' "$path"
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

initialize_visible_overlay_paths() {
  if [[ -n "${VISIBLE_OVERLAY_DIR}" && -d "${VISIBLE_OVERLAY_DIR}" ]]; then
    rm -rf "${VISIBLE_OVERLAY_DIR}"
  fi

  VISIBLE_OVERLAY_DIR="$(mktemp -d "${ARTIFACTS_DIR}/visible-overlay-${RUN_ARTIFACT_BASENAME}.XXXXXX")"
  VISIBLE_OVERLAY_STATE_PATH="${VISIBLE_OVERLAY_DIR}/overlay-state.payload"
  VISIBLE_OVERLAY_ACK_PATH="${VISIBLE_OVERLAY_DIR}/overlay-ack.payload"
  VISIBLE_OVERLAY_LOG_PATH="${VISIBLE_OVERLAY_DIR}/overlay.log"
}

load_license_secret_from_file_if_needed() {
  if [[ -n "${UNITY_SERIAL:-}" || -n "${UNITY_LICENSE_SECRET:-}" || -z "${UNITY_LICENSE_FILE:-}" ]]; then
    return 0
  fi

  if [[ -f "${UNITY_LICENSE_FILE}" ]]; then
    UNITY_LICENSE_SECRET="$(<"${UNITY_LICENSE_FILE}")"
    return 0
  fi

  if [[ "${UNITY_LICENSE_FILE}" =~ ^/mnt/([a-zA-Z])/(.+)$ ]]; then
    local windows_drive windows_tail windows_license_file windows_license_content
    windows_drive="${BASH_REMATCH[1]}"
    windows_tail="${BASH_REMATCH[2]}"
    windows_tail="${windows_tail//\//\\}"
    windows_license_file="${windows_drive^^}:\\${windows_tail}"

    windows_license_content="$(/mnt/c/Windows/System32/cmd.exe /c "type \"${windows_license_file}\"" 2>/dev/null || true)"
    if [[ -n "${windows_license_content}" ]]; then
      UNITY_LICENSE_SECRET="${windows_license_content}"
      return 0
    fi
  fi

  echo "error: UNITY_LICENSE_FILE does not exist: ${UNITY_LICENSE_FILE}" >&2
  exit 1
}

derive_unity_serial_if_needed() {
  if [[ -n "${UNITY_SERIAL:-}" ]]; then
    return 0
  fi

  load_license_secret_from_file_if_needed
  require_env "UNITY_LICENSE_SECRET" "Set raw .ulf text or base64-encoded .ulf content (or set UNITY_LICENSE_FILE in env/.env)."

  local python_bin
  python_bin="$(ensure_python_bin)"

  SERIAL_FILE="$(mktemp)"
  export UNITY_LICENSE_SECRET

  "${python_bin}" - <<'PY' "${SERIAL_FILE}"
import base64
import os
import sys

serial_path = sys.argv[1]
secret = os.environ.get("UNITY_LICENSE_SECRET", "")


def looks_like_ulf(value: str) -> bool:
    return '<DeveloperData Value="' in value and '/>' in value


normalized = secret
if not looks_like_ulf(secret):
    try:
        decoded = base64.b64decode(secret, validate=True).decode("utf-8")
    except Exception:
        decoded = ""

    if looks_like_ulf(decoded):
        normalized = decoded
        print("warning: UNITY_LICENSE_SECRET was base64-decoded at runtime.", file=sys.stderr)

if not looks_like_ulf(normalized):
    print("error: UNITY_LICENSE_SECRET is not a valid .ulf file.", file=sys.stderr)
    sys.exit(1)

start_key = '<DeveloperData Value="'
end_key = '"/>'
start = normalized.find(start_key)
if start < 0:
    print("error: missing DeveloperData entry.", file=sys.stderr)
    sys.exit(1)

start += len(start_key)
end = normalized.find(end_key, start)
if end < 0:
    print("error: missing DeveloperData closing marker.", file=sys.stderr)
    sys.exit(1)

serial = base64.b64decode(normalized[start:end]).decode("latin1")[4:]
if not serial:
    print("error: failed to derive UNITY_SERIAL.", file=sys.stderr)
    sys.exit(1)

with open(serial_path, "w", encoding="utf-8") as handle:
    handle.write(serial)
PY

  UNITY_SERIAL="$(tr -d '\r\n' < "${SERIAL_FILE}")"
  if [[ -z "${UNITY_SERIAL:-}" ]]; then
    echo "error: failed to derive UNITY_SERIAL from UNITY_LICENSE_SECRET." >&2
    exit 1
  fi

  echo "note: UNITY_SERIAL derived from UNITY_LICENSE_SECRET."
}

validate_timeout() {
  if ! [[ "${RUN_TIMEOUT_SECONDS}" =~ ^[0-9]+$ ]]; then
    echo "error: RUN_TIMEOUT_SECONDS must be a positive integer (seconds)." >&2
    exit 1
  fi

  if [[ "${RUN_TIMEOUT_SECONDS}" -le 0 ]]; then
    echo "error: RUN_TIMEOUT_SECONDS must be greater than zero." >&2
    exit 1
  fi

  if ! command -v timeout >/dev/null 2>&1; then
    echo "error: timeout command not found in PATH." >&2
    exit 1
  fi
}

validate_lock_wait_settings() {
  if ! [[ "${RUN_LOCK_WAIT_SECONDS}" =~ ^[0-9]+$ ]]; then
    echo "error: RUN_LOCK_WAIT_SECONDS must be a non-negative integer (seconds)." >&2
    exit 1
  fi

  if ! [[ "${RUN_LOCK_POLL_SECONDS}" =~ ^[0-9]+$ ]]; then
    echo "error: RUN_LOCK_POLL_SECONDS must be a positive integer (seconds)." >&2
    exit 1
  fi

  if [[ "${RUN_LOCK_POLL_SECONDS}" -le 0 ]]; then
    echo "error: RUN_LOCK_POLL_SECONDS must be greater than zero." >&2
    exit 1
  fi
}

validate_docker_cpus() {
  if [[ -z "${LOCAL_DOCKER_CPUS}" ]]; then
    DOCKER_CPU_ARGS=()
    return 0
  fi

  if ! [[ "${LOCAL_DOCKER_CPUS}" =~ ^[0-9]+([.][0-9]+)?$ ]]; then
    echo "error: LOCAL_DOCKER_CPUS must be a positive number (example: 4 or 3.5)." >&2
    exit 1
  fi

  if ! awk -v value="${LOCAL_DOCKER_CPUS}" 'BEGIN { exit (value > 0) ? 0 : 1 }'; then
    echo "error: LOCAL_DOCKER_CPUS must be greater than zero." >&2
    exit 1
  fi

  DOCKER_CPU_ARGS=(--cpus "${LOCAL_DOCKER_CPUS}")
  echo "note: limiting docker container CPUs to ${LOCAL_DOCKER_CPUS} for this run."
}

acquire_run_lock() {
  local started_waiting_at=0
  local wait_deadline=0
  local wait_message_printed=0

  if [[ "${RUN_LOCK_WAIT_SECONDS}" -gt 0 ]]; then
    started_waiting_at="$(date -u +%s)"
    wait_deadline=$((started_waiting_at + RUN_LOCK_WAIT_SECONDS))
  fi

  while true; do
    if mkdir "${RUN_LOCK_DIR}" 2>/dev/null; then
      RUN_LOCK_ACQUIRED=1
      write_run_lock_metadata
      return 0
    fi

    local existing_pid=""
    if [[ -f "${RUN_LOCK_DIR}/pid" ]]; then
      existing_pid="$(tr -d '\r\n' < "${RUN_LOCK_DIR}/pid" || true)"
    fi

    if [[ -n "${existing_pid}" && "${existing_pid}" =~ ^[0-9]+$ ]] && ps -p "${existing_pid}" >/dev/null 2>&1; then
      if [[ "${wait_message_printed}" -eq 0 ]]; then
        build_lock_wait_message "${existing_pid}" >&2
        wait_message_printed=1
      fi

      if [[ "${RUN_LOCK_WAIT_SECONDS}" -gt 0 ]]; then
        local now
        now="$(date -u +%s)"
        if [[ "${now}" -ge "${wait_deadline}" ]]; then
          echo "error: timed out waiting ${RUN_LOCK_WAIT_SECONDS}s for active EditMode run ${existing_pid} to release ${RUN_LOCK_DIR}." >&2
          exit 1
        fi
      fi

      sleep "${RUN_LOCK_POLL_SECONDS}"
      continue
    fi

    rm -rf "${RUN_LOCK_DIR}"
  done
}

setup_local_docker_credential_helper() {
  if command -v docker-credential-desktop >/dev/null 2>&1; then
    return 0
  fi

  local desktop_helper_exe="/mnt/c/Program Files/Docker/Docker/resources/bin/docker-credential-desktop.exe"
  if [[ ! -x "${desktop_helper_exe}" ]]; then
    return 0
  fi

  local shim_dir
  shim_dir="$(mktemp -d /tmp/docker-cred-shim.XXXXXX)"

  cat > "${shim_dir}/docker-credential-desktop" <<'EOF'
#!/usr/bin/env bash
"/mnt/c/Program Files/Docker/Docker/resources/bin/docker-credential-desktop.exe" "$@"
EOF

  chmod +x "${shim_dir}/docker-credential-desktop"
  export PATH="${shim_dir}:${PATH}"
  DOCKER_HELPER_SHIM_DIR="${shim_dir}"
  echo "note: using local docker-credential-desktop shim for this run."
}

resolve_unity_executable() {
  local candidate=""
  local normalized=""

  if [[ -n "${UNITY_EXECUTABLE}" ]]; then
    normalized="$(normalize_executable_path "${UNITY_EXECUTABLE}")"
    if [[ "${normalized}" == */* ]]; then
      if [[ -x "${normalized}" || ( "${normalized}" == *.exe && -f "${normalized}" ) ]]; then
        UNITY_EXECUTABLE="${normalized}"
        return 0
      fi
      echo "error: Unity executable not found or not runnable: ${UNITY_EXECUTABLE}" >&2
      exit 1
    fi

    if candidate="$(command -v "${normalized}" 2>/dev/null)"; then
      UNITY_EXECUTABLE="${candidate}"
      return 0
    fi

    echo "error: Unity command not found in PATH: ${UNITY_EXECUTABLE}" >&2
    exit 1
  fi

  for candidate in "$(command -v Unity.exe 2>/dev/null || true)" \
                   "/mnt/c/Program Files/Unity/Hub/Editor/${UNITY_VERSION}/Editor/Unity.exe" \
                   "/mnt/c/Program Files/Unity/Editor/Unity.exe" \
                   "$(command -v Unity 2>/dev/null || true)" \
                   "$(command -v unity-editor 2>/dev/null || true)"; do
    [[ -z "${candidate}" ]] && continue
    if [[ -x "${candidate}" || ( "${candidate}" == *.exe && -f "${candidate}" ) ]]; then
      UNITY_EXECUTABLE="${candidate}"
      echo "note: detected local Unity executable at ${UNITY_EXECUTABLE}"
      return 0
    fi
  done

  echo "error: failed to locate a local Unity executable for ${UNITY_VERSION}." >&2
  echo "hint: pass --unity-path <path> or set UNITY_EXECUTABLE in your environment/.env." >&2
  exit 1
}

return_local_license() {
  local return_log
  return_log="$(mktemp "${ARTIFACTS_DIR}/unity-return-license.XXXXXX.log")"
  if ! "${UNITY_EXECUTABLE}" \
    -batchmode \
    -quit \
    -returnlicense \
    -username "${UNITY_EMAIL}" \
    -password "${UNITY_PASSWORD}" \
    -logFile "$(unity_path_arg "${return_log}")"; then
    :
  fi
  cat "${return_log}" || true
  rm -f "${return_log}"
}

cleanup() {
  set +e

  if [[ -n "${VISIBLE_OVERLAY_DIR}" ]]; then
    rm -rf "${VISIBLE_OVERLAY_DIR}"
  fi

  if [[ "${LOCAL_UNITY_RETURN_LICENSE}" -eq 1 ]]; then
    return_local_license
  fi

  if [[ -n "${SERIAL_FILE}" ]]; then
    rm -f "${SERIAL_FILE}"
  fi

  if [[ -n "${RUNNER_SCRIPT}" ]]; then
    rm -f "${RUNNER_SCRIPT}"
  fi

  if [[ -n "${DOCKER_HELPER_SHIM_DIR}" ]]; then
    rm -rf "${DOCKER_HELPER_SHIM_DIR}"
  fi

  if [[ "${RUN_LOCK_ACQUIRED}" -eq 1 ]]; then
    local lock_pid=""
    if [[ -f "${RUN_LOCK_DIR}/pid" ]]; then
      lock_pid="$(tr -d '\r\n' < "${RUN_LOCK_DIR}/pid" || true)"
    fi

    if [[ -z "${lock_pid}" || "${lock_pid}" == "$$" ]]; then
      rm -rf "${RUN_LOCK_DIR}"
    fi
  fi
}

parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --mode)
        [[ $# -ge 2 ]] || { echo "error: --mode requires a value." >&2; exit 1; }
        EDITMODE_RUNNER_MODE="$2"
        shift 2
        ;;
      --docker)
        EDITMODE_RUNNER_MODE="docker"
        shift
        ;;
      --local)
        EDITMODE_RUNNER_MODE="local"
        shift
        ;;
      --project-path)
        [[ $# -ge 2 ]] || { echo "error: --project-path requires a value." >&2; exit 1; }
        PROJECT_PATH="$2"
        shift 2
        ;;
      --unity-path)
        [[ $# -ge 2 ]] || { echo "error: --unity-path requires a value." >&2; exit 1; }
        UNITY_EXECUTABLE="$2"
        shift 2
        ;;
      --test-filter)
        [[ $# -ge 2 ]] || { echo "error: --test-filter requires a value." >&2; exit 1; }
        EDITMODE_TEST_FILTER="$2"
        shift 2
        ;;
      --manage-license)
        LOCAL_UNITY_MANAGE_LICENSE=1
        shift
        ;;
      --no-manage-license)
        LOCAL_UNITY_MANAGE_LICENSE=0
        shift
        ;;
      --visible-editor-smoke)
        EDITMODE_RUNNER_MODE="local"
        EDITMODE_LOCAL_EXECUTION_STYLE="visible_smoke"
        shift
        ;;
      --visible-editor-suite)
        EDITMODE_RUNNER_MODE="local"
        EDITMODE_LOCAL_EXECUTION_STYLE="visible_suite"
        shift
        ;;
      --timeout)
        [[ $# -ge 2 ]] || { echo "error: --timeout requires a value." >&2; exit 1; }
        RUN_TIMEOUT_SECONDS="$2"
        shift 2
        ;;
      --lock-wait)
        [[ $# -ge 2 ]] || { echo "error: --lock-wait requires a value." >&2; exit 1; }
        RUN_LOCK_WAIT_SECONDS="$2"
        shift 2
        ;;
      --lock-poll)
        [[ $# -ge 2 ]] || { echo "error: --lock-poll requires a value." >&2; exit 1; }
        RUN_LOCK_POLL_SECONDS="$2"
        shift 2
        ;;
      --docker-cpus)
        [[ $# -ge 2 ]] || { echo "error: --docker-cpus requires a value." >&2; exit 1; }
        LOCAL_DOCKER_CPUS="$2"
        shift 2
        ;;
      --visible-overlay-state-path|--visible-overlay-ack-path|-asmliteVisibleAutomationExternalOverlayStatePath|-asmliteVisibleAutomationExternalOverlayAckPath)
        echo "error: legacy python overlay transport removed; use Tools/ci/run-visible-smoke-local.sh or Tools/ci/run-UAT-smoke.sh canonical rust-overlay commands." >&2
        exit 2
        ;;
      -h|--help)
        usage
        exit 0
        ;;
      *)
        echo "error: unknown argument: $1" >&2
        usage >&2
        exit 1
        ;;
    esac
  done
}

configure_batch_defaults() {
  PROJECT_PATH="$(resolve_repo_relative_path "${PROJECT_PATH}")"

  if [[ -n "${ASMLITE_BATCH_RUNS_JSON_PATH:-}" ]]; then
    ASMLITE_BATCH_RUNS_JSON_PATH="$(resolve_repo_relative_path "${ASMLITE_BATCH_RUNS_JSON_PATH}")"
  fi

  if [[ -z "${EDITMODE_TEST_FILTER:-}" && -z "${ASMLITE_BATCH_RUNS_JSON_PATH:-}" && -z "${ASMLITE_BATCH_RUNS_JSON:-}" ]]; then
    : "${ASMLITE_BATCH_RUNS_JSON_PATH:=${CANONICAL_BATCH_RUNS_JSON_PATH}}"
  fi

  if [[ -n "${ASMLITE_BATCH_RUNS_JSON_PATH:-}" && ! -f "${ASMLITE_BATCH_RUNS_JSON_PATH}" ]]; then
    echo "error: ASMLITE_BATCH_RUNS_JSON_PATH does not exist: ${ASMLITE_BATCH_RUNS_JSON_PATH}" >&2
    exit 1
  fi
}

ensure_project_version() {
  PROJECT_VERSION_FILE="${PROJECT_PATH}/ProjectSettings/ProjectVersion.txt"
  if [[ ! -f "${PROJECT_VERSION_FILE}" ]]; then
    echo "error: missing project version file: ${PROJECT_VERSION_FILE}" >&2
    exit 1
  fi

  UNITY_VERSION="$(awk -F': ' '/^m_EditorVersion:/ { print $2; exit }' "${PROJECT_VERSION_FILE}" | tr -d '\r\n')"
  if [[ -z "${UNITY_VERSION:-}" ]]; then
    echo "error: failed to read Unity version from ${PROJECT_VERSION_FILE}" >&2
    exit 1
  fi
}

ensure_license_credentials() {
  require_env "UNITY_EMAIL" "Set Unity account email used for license activation."
  require_env "UNITY_PASSWORD" "Set Unity account password used for license activation."
  derive_unity_serial_if_needed
}

activate_local_license() {
  local activate_log
  activate_log="$(mktemp "${ARTIFACTS_DIR}/unity-activate.XXXXXX.log")"

  if ! "${UNITY_EXECUTABLE}" \
    -batchmode \
    -quit \
    -nographics \
    -serial "${UNITY_SERIAL}" \
    -username "${UNITY_EMAIL}" \
    -password "${UNITY_PASSWORD}" \
    -logFile "$(unity_path_arg "${activate_log}")"; then
    :
  fi

  cat "${activate_log}"
  if ! grep -q "Successfully activated" "${activate_log}"; then
    rm -f "${activate_log}"
    echo "error: local Unity license activation failed. 'Successfully activated' not found." >&2
    exit 1
  fi

  rm -f "${activate_log}"
  LOCAL_UNITY_RETURN_LICENSE=1
}

build_test_filter_args() {
  if [[ -n "${EDITMODE_TEST_FILTER}" ]]; then
    printf '%s\n' "note: applying EditMode test filter: ${EDITMODE_TEST_FILTER}"
  fi
}

initialize_run_artifact_paths() {
  local stamp_source
  stamp_source="$(date -u +%s)"
  if command -v python3 >/dev/null 2>&1; then
    RUN_TIMESTAMP="$(python3 - <<'PY'
import time
print(int(time.time_ns()))
PY
)"
  elif command -v python >/dev/null 2>&1; then
    RUN_TIMESTAMP="$(python - <<'PY'
import time
print(int(time.time() * 1000000000))
PY
)"
  else
    RUN_TIMESTAMP="${stamp_source}000000000"
  fi

  RUN_TIMESTAMP="${RUN_TIMESTAMP//$'\r'/}"
  RUN_TIMESTAMP="${RUN_TIMESTAMP//$'\n'/}"
  if [[ -z "${RUN_TIMESTAMP}" ]]; then
    RUN_TIMESTAMP="${stamp_source}000000000"
  fi

  RUN_ARTIFACT_BASENAME="editmode-${RUN_TIMESTAMP}"
  EDITMODE_LOG_PATH="${ARTIFACTS_DIR}/${RUN_ARTIFACT_BASENAME}.log"
  EDITMODE_RESULTS_PATH="${ARTIFACTS_DIR}/${RUN_ARTIFACT_BASENAME}-results.xml"
  VISIBLE_EDITOR_SMOKE_RESULTS_PATH="${ARTIFACTS_DIR}/${RUN_ARTIFACT_BASENAME}-visible-editor-smoke.xml"
  RUN_COVERAGE_DIR="${COVERAGE_DIR}/${RUN_ARTIFACT_BASENAME}"
}

copy_visible_smoke_artifacts_to_timestamped_paths() {
  local visible_log_alias visible_results_alias
  visible_log_alias="${ARTIFACTS_DIR}/editmode.log"
  visible_results_alias="${ARTIFACTS_DIR}/visible-editor-smoke.xml"

  if [[ -f "${visible_log_alias}" && "${visible_log_alias}" != "${EDITMODE_LOG_PATH}" ]]; then
    cp "${visible_log_alias}" "${EDITMODE_LOG_PATH}"
  fi

  if [[ -f "${visible_results_alias}" && "${visible_results_alias}" != "${VISIBLE_EDITOR_SMOKE_RESULTS_PATH}" ]]; then
    cp "${visible_results_alias}" "${VISIBLE_EDITOR_SMOKE_RESULTS_PATH}"
  fi
}

run_local_visible_smoke_mode() {
  local log_path results_path project_path exit_code=0 visible_log_alias visible_results_alias visible_selector visible_mode
  local -a unity_cmd

  resolve_unity_executable
  echo "note: running local visible Unity editor smoke flow with ${UNITY_EXECUTABLE}"

  if [[ -n "${ASMLITE_VISIBLE_SMOKE_EXTERNAL_OVERLAY_STATE_PATH:-}" || -n "${ASMLITE_VISIBLE_SMOKE_EXTERNAL_OVERLAY_ACK_PATH:-}" ]]; then
    echo "error: legacy python overlay transport removed; unset ASMLITE_VISIBLE_SMOKE_EXTERNAL_OVERLAY_STATE_PATH / ASMLITE_VISIBLE_SMOKE_EXTERNAL_OVERLAY_ACK_PATH and use canonical rust-overlay wrappers." >&2
    exit 2
  fi

  if [[ "${LOCAL_UNITY_MANAGE_LICENSE}" == "1" ]]; then
    ensure_license_credentials
    activate_local_license
  fi

  rm -rf "${PROJECT_PATH}/Temp"
  visible_log_alias="${ARTIFACTS_DIR}/editmode.log"
  visible_results_alias="${ARTIFACTS_DIR}/visible-editor-smoke.xml"
  rm -f "${visible_log_alias}" "${visible_results_alias}" "${EDITMODE_LOG_PATH}" "${VISIBLE_EDITOR_SMOKE_RESULTS_PATH}"

  visible_selector="${EDITMODE_TEST_FILTER:-ASMLiteVisibleEditorSmokeTests}"
  if [[ -n "${EDITMODE_TEST_FILTER}" ]]; then
    echo "note: applying visible automation selector: ${visible_selector}"
  else
    echo "note: visible automation defaulting to selector: ${visible_selector}"
  fi

  visible_mode="editor"
  if [[ "${visible_selector,,}" == *launch-unity* || "${visible_selector,,}" == *launchunity* ]]; then
    visible_mode="launch-unity"
  elif [[ "${visible_selector,,}" == *playmode* || "${visible_selector,,}" == *runtime* ]]; then
    visible_mode="playmode"
  fi

  echo "note: visible smoke step delay: ${VISIBLE_SMOKE_STEP_DELAY_SECONDS}s"

  initialize_visible_overlay_paths
  echo "note: run-editmode-local visible smoke no longer launches legacy Python overlay transport."

  log_path="$(unity_path_arg "${visible_log_alias}")"
  results_path="$(unity_path_arg "${visible_results_alias}")"
  project_path="$(unity_path_arg "${PROJECT_PATH}")"

  unity_cmd=(
    "${UNITY_EXECUTABLE}"
    -logFile "${log_path}"
    -projectPath "${project_path}"
    -executeMethod ASMLite.Tests.Editor.ASMLiteVisibleAutomationCommandLine.RunFromCommandLine
    -asmliteVisibleAutomationResultsPath "${results_path}"
    -asmliteVisibleAutomationSelector "${visible_selector}"
    -asmliteVisibleAutomationMode "${visible_mode}"
    -asmliteVisibleAutomationStepDelaySeconds "${VISIBLE_SMOKE_STEP_DELAY_SECONDS}"
    -asmliteVisibleAutomationExternalOverlayStatePath "$(unity_path_arg "${VISIBLE_OVERLAY_STATE_PATH}")"
    -asmliteVisibleAutomationExternalOverlayAckPath "$(unity_path_arg "${VISIBLE_OVERLAY_ACK_PATH}")"
  )

  echo "note: visible smoke uses the command-line automation harness; timeout disabled while waiting for manual acceptance."

  set +e
  "${unity_cmd[@]}"
  exit_code=$?
  set -e

  copy_visible_smoke_artifacts_to_timestamped_paths
  return "${exit_code}"
}

run_local_batch_suite_mode() {
  local presentation_mode="$1"
  local log_path project_path exit_code=0
  local -a unity_cmd

  resolve_unity_executable
  echo "note: running local Unity batch EditMode suite flow with ${UNITY_EXECUTABLE}"

  if [[ "${LOCAL_UNITY_MANAGE_LICENSE}" == "1" ]]; then
    ensure_license_credentials
    activate_local_license
  fi

  rm -rf "${PROJECT_PATH}/Temp"

  : "${ASMLITE_BATCH_RESULTS_DIR:=${ARTIFACTS_DIR}}"
  : "${ASMLITE_BATCH_CANONICAL_RESULTS_PATH:=${EDITMODE_RESULTS_PATH}}"
  : "${ASMLITE_BATCH_RUNS_JSON_PATH:=}"
  : "${ASMLITE_BATCH_OVERLAY_STATE_PATH:=}"
  : "${ASMLITE_BATCH_STEP_DELAY_SECONDS:=}"
  : "${ASMLITE_BATCH_SELECTION_LABEL:=}"
  : "${ASMLITE_BATCH_SESSION_ID:=}"
  : "${ASMLITE_BATCH_OVERLAY_TITLE:=}"
  export ASMLITE_BATCH_RESULTS_DIR
  export ASMLITE_BATCH_CANONICAL_RESULTS_PATH

  mkdir -p "${ASMLITE_BATCH_RESULTS_DIR}"

  log_path="$(unity_path_arg "${EDITMODE_LOG_PATH}")"
  project_path="$(unity_path_arg "${PROJECT_PATH}")"

  unity_cmd=(
    "${UNITY_EXECUTABLE}"
    -logFile "${log_path}"
    -projectPath "${project_path}"
    -executeMethod ASMLite.Tests.Editor.ASMLiteBatchTestRunner.RunFromCommandLine
    -asmliteBatchResultsDir "$(unity_path_arg "${ASMLITE_BATCH_RESULTS_DIR}")"
    -asmliteBatchCanonicalResultsPath "$(unity_path_arg "${ASMLITE_BATCH_CANONICAL_RESULTS_PATH}")"
  )

  if [[ -n "${ASMLITE_BATCH_RUNS_JSON_PATH}" ]]; then
    unity_cmd+=( -asmliteBatchRunsJsonPath "$(unity_path_arg "${ASMLITE_BATCH_RUNS_JSON_PATH}")" )
  fi

  if [[ -n "${ASMLITE_BATCH_OVERLAY_STATE_PATH}" ]]; then
    unity_cmd+=( -asmliteBatchOverlayStatePath "$(unity_path_arg "${ASMLITE_BATCH_OVERLAY_STATE_PATH}")" )
  fi

  if [[ -n "${ASMLITE_BATCH_STEP_DELAY_SECONDS}" ]]; then
    unity_cmd+=( -asmliteBatchStepDelaySeconds "${ASMLITE_BATCH_STEP_DELAY_SECONDS}" )
  fi

  if [[ -n "${ASMLITE_BATCH_SELECTION_LABEL}" ]]; then
    unity_cmd+=( -asmliteBatchSelectionLabel "${ASMLITE_BATCH_SELECTION_LABEL}" )
  fi

  if [[ -n "${ASMLITE_BATCH_SESSION_ID}" ]]; then
    unity_cmd+=( -asmliteBatchSessionId "${ASMLITE_BATCH_SESSION_ID}" )
  fi

  if [[ -n "${ASMLITE_BATCH_OVERLAY_TITLE}" ]]; then
    unity_cmd+=( -asmliteBatchOverlayTitle "${ASMLITE_BATCH_OVERLAY_TITLE}" )
  fi

  if [[ "${presentation_mode}" == "headless" ]]; then
    unity_cmd=(
      "${UNITY_EXECUTABLE}"
      -batchmode
      -nographics
      -logFile "${log_path}"
      -projectPath "${project_path}"
      -executeMethod ASMLite.Tests.Editor.ASMLiteBatchTestRunner.RunFromCommandLine
      -asmliteBatchResultsDir "$(unity_path_arg "${ASMLITE_BATCH_RESULTS_DIR}")"
      -asmliteBatchCanonicalResultsPath "$(unity_path_arg "${ASMLITE_BATCH_CANONICAL_RESULTS_PATH}")"
    )
    if [[ -n "${ASMLITE_BATCH_RUNS_JSON_PATH}" ]]; then
      unity_cmd+=( -asmliteBatchRunsJsonPath "$(unity_path_arg "${ASMLITE_BATCH_RUNS_JSON_PATH}")" )
    fi
    if [[ -n "${ASMLITE_BATCH_OVERLAY_STATE_PATH}" ]]; then
      unity_cmd+=( -asmliteBatchOverlayStatePath "$(unity_path_arg "${ASMLITE_BATCH_OVERLAY_STATE_PATH}")" )
    fi
    if [[ -n "${ASMLITE_BATCH_STEP_DELAY_SECONDS}" ]]; then
      unity_cmd+=( -asmliteBatchStepDelaySeconds "${ASMLITE_BATCH_STEP_DELAY_SECONDS}" )
    fi
    if [[ -n "${ASMLITE_BATCH_SELECTION_LABEL}" ]]; then
      unity_cmd+=( -asmliteBatchSelectionLabel "${ASMLITE_BATCH_SELECTION_LABEL}" )
    fi
    if [[ -n "${ASMLITE_BATCH_SESSION_ID}" ]]; then
      unity_cmd+=( -asmliteBatchSessionId "${ASMLITE_BATCH_SESSION_ID}" )
    fi
    if [[ -n "${ASMLITE_BATCH_OVERLAY_TITLE}" ]]; then
      unity_cmd+=( -asmliteBatchOverlayTitle "${ASMLITE_BATCH_OVERLAY_TITLE}" )
    fi
    echo "note: single-instance batch suite mode is running headless."
  else
    echo "note: single-instance batch suite mode leaves the Unity editor window open while the full batch executes."
  fi

  set +e
  timeout --signal=TERM --kill-after=30 "${RUN_TIMEOUT_SECONDS}" "${unity_cmd[@]}"
  exit_code=$?
  set -e

  return "${exit_code}"
}

run_local_visible_suite_mode() {
  local log_path results_path coverage_path project_path exit_code=0
  local -a unity_cmd
  local -a test_filter_args=()

  if [[ -n "${ASMLITE_BATCH_RUNS_JSON_PATH:-}" || -n "${ASMLITE_BATCH_RUNS_JSON:-}" ]]; then
    run_local_batch_suite_mode visible
    return $?
  fi

  resolve_unity_executable
  echo "note: running local visible Unity EditMode suite flow with ${UNITY_EXECUTABLE}"

  if [[ "${LOCAL_UNITY_MANAGE_LICENSE}" == "1" ]]; then
    ensure_license_credentials
    activate_local_license
  fi

  build_test_filter_args

  rm -rf "${PROJECT_PATH}/Temp"

  if [[ -n "${EDITMODE_TEST_FILTER}" ]]; then
    test_filter_args=(-testFilter "${EDITMODE_TEST_FILTER}")
  fi

  log_path="$(unity_path_arg "${EDITMODE_LOG_PATH}")"
  results_path="$(unity_path_arg "${EDITMODE_RESULTS_PATH}")"
  coverage_path="$(unity_path_arg "${RUN_COVERAGE_DIR}")"
  project_path="$(unity_path_arg "${PROJECT_PATH}")"

  unity_cmd=(
    "${UNITY_EXECUTABLE}"
    -logFile "${log_path}"
    -projectPath "${project_path}"
    -coverageResultsPath "${coverage_path}"
    -runTests
    -testPlatform editmode
    "${test_filter_args[@]}"
    -testResults "${results_path}"
    -enableCodeCoverage
    -debugCodeOptimization
    -coverageOptions "generateAdditionalMetrics;generateHtmlReport;generateBadgeReport;dontClear"
  )

  echo "note: visible suite mode leaves the Unity editor window open while the filtered EditMode run executes."

  set +e
  timeout --signal=TERM --kill-after=30 "${RUN_TIMEOUT_SECONDS}" "${unity_cmd[@]}"
  exit_code=$?
  set -e

  return "${exit_code}"
}

run_local_mode() {
  local log_path results_path coverage_path project_path exit_code=0
  local -a unity_cmd
  local -a test_filter_args=()

  if [[ "${EDITMODE_LOCAL_EXECUTION_STYLE}" == "visible_smoke" ]]; then
    run_local_visible_smoke_mode
    return $?
  fi

  if [[ "${EDITMODE_LOCAL_EXECUTION_STYLE}" == "visible_suite" ]]; then
    run_local_visible_suite_mode
    return $?
  fi

  if [[ -n "${ASMLITE_BATCH_RUNS_JSON_PATH:-}" || -n "${ASMLITE_BATCH_RUNS_JSON:-}" ]]; then
    run_local_batch_suite_mode headless
    return $?
  fi

  resolve_unity_executable
  echo "note: running local Unity EditMode tests with ${UNITY_EXECUTABLE}"

  if [[ "${LOCAL_UNITY_MANAGE_LICENSE}" == "1" ]]; then
    ensure_license_credentials
    activate_local_license
  fi

  build_test_filter_args

  rm -rf "${PROJECT_PATH}/Temp"

  if [[ -n "${EDITMODE_TEST_FILTER}" ]]; then
    test_filter_args=(-testFilter "${EDITMODE_TEST_FILTER}")
  fi

  log_path="$(unity_path_arg "${EDITMODE_LOG_PATH}")"
  results_path="$(unity_path_arg "${EDITMODE_RESULTS_PATH}")"
  coverage_path="$(unity_path_arg "${RUN_COVERAGE_DIR}")"
  project_path="$(unity_path_arg "${PROJECT_PATH}")"

  unity_cmd=(
    "${UNITY_EXECUTABLE}"
    -batchmode
    -nographics
    -logFile "${log_path}"
    -projectPath "${project_path}"
    -coverageResultsPath "${coverage_path}"
    -runTests
    -testPlatform editmode
    "${test_filter_args[@]}"
    -testResults "${results_path}"
    -enableCodeCoverage
    -debugCodeOptimization
    -coverageOptions "generateAdditionalMetrics;generateHtmlReport;generateBadgeReport;dontClear"
  )

  set +e
  timeout --signal=TERM --kill-after=30 "${RUN_TIMEOUT_SECONDS}" "${unity_cmd[@]}"
  exit_code=$?
  set -e

  return "${exit_code}"
}

run_docker_mode() {
  local docker_run_exit=0
  local docker_project_path
  local docker_batch_runs_json_path=""
  local docker_batch_results_dir=""
  local docker_batch_canonical_results_path=""

  if ! command -v docker >/dev/null 2>&1; then
    echo "error: docker not found in PATH." >&2
    exit 1
  fi

  ensure_license_credentials
  validate_docker_cpus
  setup_local_docker_credential_helper

  UNITY_IMAGE="unityci/editor:ubuntu-${UNITY_VERSION}-linux-il2cpp-3"
  printf 'Using Unity image %q\n' "${UNITY_IMAGE}"

  EDITMODE_LOG_PATH="${ARTIFACTS_DIR}/editmode.log"
  EDITMODE_RESULTS_PATH="${ARTIFACTS_DIR}/editmode-results.xml"

  docker_project_path="$(to_docker_workspace_path "${PROJECT_PATH}")"

  if [[ -n "${ASMLITE_BATCH_RUNS_JSON_PATH:-}" ]]; then
    docker_batch_runs_json_path="$(to_docker_workspace_path "${ASMLITE_BATCH_RUNS_JSON_PATH}")"
  fi

  if [[ -n "${ASMLITE_BATCH_RESULTS_DIR:-}" ]]; then
    ASMLITE_BATCH_RESULTS_DIR="$(resolve_repo_relative_path "${ASMLITE_BATCH_RESULTS_DIR}")"
    docker_batch_results_dir="$(to_docker_workspace_path "${ASMLITE_BATCH_RESULTS_DIR}")"
  fi

  if [[ -n "${ASMLITE_BATCH_CANONICAL_RESULTS_PATH:-}" ]]; then
    ASMLITE_BATCH_CANONICAL_RESULTS_PATH="$(resolve_repo_relative_path "${ASMLITE_BATCH_CANONICAL_RESULTS_PATH}")"
    docker_batch_canonical_results_path="$(to_docker_workspace_path "${ASMLITE_BATCH_CANONICAL_RESULTS_PATH}")"
  fi

  RUNNER_SCRIPT="$(mktemp /tmp/unity-runner.XXXXXX.sh)"

  cat > "${RUNNER_SCRIPT}" <<'RUNNER'
set -euo pipefail
export HOME="${HOME:-/root}"
WORKSPACE_REPO_ROOT="/github/workspace/${REPO_DIR_NAME}"
mkdir -p "$HOME/.cache/unity3d" "${WORKSPACE_REPO_ROOT}/artifacts" "${WORKSPACE_REPO_ROOT}/CodeCoverage"

EDITMODE_LOG_PATH="${WORKSPACE_REPO_ROOT}/artifacts/editmode.log"
EDITMODE_RESULTS_PATH="${WORKSPACE_REPO_ROOT}/artifacts/editmode-results.xml"
RUN_COVERAGE_DIR="${WORKSPACE_REPO_ROOT}/CodeCoverage"
mkdir -p "${RUN_COVERAGE_DIR}"

if [[ -z "${DOCKER_PROJECT_PATH:-}" ]]; then
  echo "error: DOCKER_PROJECT_PATH is required." >&2
  exit 1
fi

return_license() {
  unity-editor \
    -batchmode \
    -quit \
    -returnlicense \
    -username "$UNITY_EMAIL" \
    -password "$UNITY_PASSWORD" \
    -logFile /dev/stdout || true
}
trap return_license EXIT

ACTIVATE_LOG="$(mktemp)"
unity-editor \
  -batchmode \
  -quit \
  -nographics \
  -serial "$UNITY_SERIAL" \
  -username "$UNITY_EMAIL" \
  -password "$UNITY_PASSWORD" \
  -logFile "${ACTIVATE_LOG}" || true
cat "${ACTIVATE_LOG}"
if ! grep -q "Successfully activated" "${ACTIVATE_LOG}"; then
  echo "error: license activation failed. 'Successfully activated' not found." >&2
  exit 1
fi
rm -f "${ACTIVATE_LOG}"

rm -rf "${DOCKER_PROJECT_PATH}/Temp"

if [[ -n "${EDITMODE_TEST_FILTER:-}" ]]; then
  printf 'note: applying EditMode test filter override: %s\n' "$EDITMODE_TEST_FILTER"
  unity-editor \
    -batchmode \
    -nographics \
    -logFile "${EDITMODE_LOG_PATH}" \
    -projectPath "${DOCKER_PROJECT_PATH}" \
    -coverageResultsPath "${RUN_COVERAGE_DIR}" \
    -runTests \
    -testPlatform editmode \
    -testFilter "${EDITMODE_TEST_FILTER}" \
    -testResults "${EDITMODE_RESULTS_PATH}" \
    -enableCodeCoverage \
    -debugCodeOptimization \
    -coverageOptions "generateAdditionalMetrics;generateHtmlReport;generateBadgeReport;dontClear"
else
  : "${ASMLITE_BATCH_RESULTS_DIR:=${WORKSPACE_REPO_ROOT}/artifacts}"
  : "${ASMLITE_BATCH_CANONICAL_RESULTS_PATH:=${WORKSPACE_REPO_ROOT}/artifacts/editmode-results.xml}"

  mkdir -p "${ASMLITE_BATCH_RESULTS_DIR}"

  unity_cmd=(
    unity-editor
    -batchmode
    -nographics
    -logFile "${EDITMODE_LOG_PATH}"
    -projectPath "${DOCKER_PROJECT_PATH}"
    -coverageResultsPath "${RUN_COVERAGE_DIR}"
    -executeMethod ASMLite.Tests.Editor.ASMLiteBatchTestRunner.RunFromCommandLine
    -asmliteBatchResultsDir "${ASMLITE_BATCH_RESULTS_DIR}"
    -asmliteBatchCanonicalResultsPath "${ASMLITE_BATCH_CANONICAL_RESULTS_PATH}"
    -enableCodeCoverage
    -debugCodeOptimization
    -coverageOptions "generateAdditionalMetrics;generateHtmlReport;generateBadgeReport;dontClear"
  )

  if [[ -n "${ASMLITE_BATCH_RUNS_JSON_PATH:-}" ]]; then
    unity_cmd+=( -asmliteBatchRunsJsonPath "${ASMLITE_BATCH_RUNS_JSON_PATH}" )
  fi

  "${unity_cmd[@]}"
fi
RUNNER

  set +e
  timeout --signal=TERM --kill-after=30 "${RUN_TIMEOUT_SECONDS}" docker run --rm \
    --workdir /github/workspace \
    --env HOME=/root \
    --env UNITY_EMAIL \
    --env UNITY_PASSWORD \
    --env UNITY_SERIAL="${UNITY_SERIAL}" \
    --env EDITMODE_TEST_FILTER="${EDITMODE_TEST_FILTER}" \
    --env ASMLITE_BATCH_RUNS_JSON="${ASMLITE_BATCH_RUNS_JSON:-}" \
    --env ASMLITE_BATCH_RUNS_JSON_PATH="${docker_batch_runs_json_path}" \
    --env ASMLITE_BATCH_RESULTS_DIR="${docker_batch_results_dir}" \
    --env ASMLITE_BATCH_CANONICAL_RESULTS_PATH="${docker_batch_canonical_results_path}" \
    --env DOCKER_PROJECT_PATH="${docker_project_path}" \
    --env REPO_DIR_NAME="${REPO_DIR_NAME}" \
    "${DOCKER_CPU_ARGS[@]}" \
    --volume "${WORKSPACE_ROOT}:/github/workspace" \
    --volume "${RUNNER_SCRIPT}:/tmp/unity-runner.sh" \
    "${UNITY_IMAGE}" \
    /bin/bash /tmp/unity-runner.sh
  docker_run_exit=$?
  set -e

  return "${docker_run_exit}"
}

print_artifact_summary() {
  if [[ -f "${EDITMODE_LOG_PATH}" ]]; then
    echo "done: ${EDITMODE_LOG_PATH#"${REPO_ROOT}/"}"
  fi

  if [[ "${EDITMODE_RUNNER_MODE}" == "local" && "${EDITMODE_LOCAL_EXECUTION_STYLE}" == "visible_smoke" ]]; then
    if [[ -f "${VISIBLE_EDITOR_SMOKE_RESULTS_PATH}" ]]; then
      echo "done: ${VISIBLE_EDITOR_SMOKE_RESULTS_PATH#"${REPO_ROOT}/"}"
    elif [[ -f "${ARTIFACTS_DIR}/visible-editor-smoke.xml" ]]; then
      echo "done: artifacts/visible-editor-smoke.xml"
    else
      echo "warn: visible smoke results are not available yet; this is expected until the user accepts and closes the visible smoke run." >&2
    fi

    return 0
  fi

  if [[ -f "${EDITMODE_RESULTS_PATH}" ]]; then
    echo "done: ${EDITMODE_RESULTS_PATH#"${REPO_ROOT}/"}"
  else
    echo "warn: ${EDITMODE_RESULTS_PATH#"${REPO_ROOT}/"} was not created" >&2
  fi

  if [[ -f "${ARTIFACTS_DIR}/editmode-core-results.xml" ]]; then
    echo "done: artifacts/editmode-core-results.xml"
  fi

  if [[ -f "${ARTIFACTS_DIR}/editmode-integration-results.xml" ]]; then
    echo "done: artifacts/editmode-integration-results.xml"
  fi

  if [[ -f "${ARTIFACTS_DIR}/asmlite-generation-wiring-summary.json" ]]; then
    echo "done: artifacts/asmlite-generation-wiring-summary.json"
  fi
}

load_dotenv_defaults "${DOTENV_FILE}"
parse_args "$@"
configure_batch_defaults
LOCAL_UNITY_MANAGE_LICENSE="$(normalize_bool "${LOCAL_UNITY_MANAGE_LICENSE}")"

case "${EDITMODE_LOCAL_EXECUTION_STYLE}" in
  headless|visible_smoke|visible_suite) ;;
  *)
    echo "error: EDITMODE_LOCAL_EXECUTION_STYLE must be 'headless', 'visible_smoke', or 'visible_suite'." >&2
    exit 1
    ;;
esac

case "${EDITMODE_RUNNER_MODE}" in
  docker|local) ;;
  *)
    echo "error: EDITMODE_RUNNER_MODE must be 'docker' or 'local'." >&2
    exit 1
    ;;
esac

validate_timeout
validate_lock_wait_settings
ensure_project_version
mkdir -p "${ARTIFACTS_DIR}" "${COVERAGE_DIR}"
initialize_run_artifact_paths
mkdir -p "${RUN_COVERAGE_DIR}"
acquire_run_lock
trap cleanup EXIT INT TERM HUP

run_exit=0
case "${EDITMODE_RUNNER_MODE}" in
  docker)
    if [[ "${EDITMODE_LOCAL_EXECUTION_STYLE}" != "headless" ]]; then
      echo "error: visible smoke or visible suite execution is only supported in local mode." >&2
      exit 1
    fi
    run_docker_mode || run_exit=$?
    ;;
  local)
    run_local_mode || run_exit=$?
    ;;
esac

if [[ "${run_exit}" -eq 124 ]]; then
  echo "error: EditMode run timed out after ${RUN_TIMEOUT_SECONDS}s (set RUN_TIMEOUT_SECONDS to adjust)." >&2
fi

print_artifact_summary
exit "${run_exit}"
