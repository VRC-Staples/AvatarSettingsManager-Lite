#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
PROJECT_REL_PATH="Tools/ci/unity-project"
PROJECT_PATH="${REPO_ROOT}/${PROJECT_REL_PATH}"
PROJECT_VERSION_FILE="${PROJECT_PATH}/ProjectSettings/ProjectVersion.txt"
ARTIFACTS_DIR="${REPO_ROOT}/artifacts"
COVERAGE_DIR="${REPO_ROOT}/CodeCoverage"
DOTENV_FILE="${REPO_ROOT}/.env"

RUN_TIMEOUT_SECONDS="${RUN_TIMEOUT_SECONDS:-2700}"
RUN_LOCK_DIR="${RUN_LOCK_DIR:-/tmp/asmlite-editmode-local.lock}"
LOCAL_DOCKER_CPUS="${LOCAL_DOCKER_CPUS:-}"
EDITMODE_RUNNER_MODE="${EDITMODE_RUNNER_MODE:-docker}"
UNITY_EXECUTABLE="${UNITY_EXECUTABLE:-}"
EDITMODE_TEST_FILTER="${EDITMODE_TEST_FILTER:-}"
LOCAL_UNITY_MANAGE_LICENSE="${LOCAL_UNITY_MANAGE_LICENSE:-0}"

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

usage() {
  cat <<'EOF'
Usage: Tools/ci/run-editmode-local.sh [options]

Run Unity EditMode tests using either Docker (default) or a locally installed Unity editor.

Options:
  --mode <docker|local>     Choose execution mode (default: docker)
  --docker                  Shortcut for --mode docker
  --local                   Shortcut for --mode local
  --unity-path <path>       Path or command name for the local Unity executable
  --test-filter <filter>    Forward a Unity -testFilter value for targeted EditMode runs
  --manage-license          Activate/return a Unity license for local mode
  --no-manage-license       Skip local license activation/return (default for local mode)
  --timeout <seconds>       Override RUN_TIMEOUT_SECONDS for this invocation
  --docker-cpus <value>     Override LOCAL_DOCKER_CPUS for this invocation
  -h, --help                Show this help text

Environment overrides:
  EDITMODE_RUNNER_MODE      Default execution mode when no CLI mode is supplied
  UNITY_EXECUTABLE          Default local Unity executable path/command
  EDITMODE_TEST_FILTER      Default Unity -testFilter value
  LOCAL_UNITY_MANAGE_LICENSE
                            Enable license activation/return in local mode (1/0, true/false)
  RUN_TIMEOUT_SECONDS       Timeout applied to Docker or local Unity execution
  LOCAL_DOCKER_CPUS         Optional Docker CPU limit for Docker mode

Docker mode keeps the existing CI-style activation/return flow and requires
UNITY_EMAIL, UNITY_PASSWORD, plus UNITY_SERIAL or UNITY_LICENSE_SECRET/UNITY_LICENSE_FILE.

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
      UNITY_LICENSE_SECRET|UNITY_LICENSE_FILE|UNITY_EMAIL|UNITY_PASSWORD|UNITY_SERIAL|LOCAL_DOCKER_CPUS|EDITMODE_RUNNER_MODE|UNITY_EXECUTABLE|EDITMODE_TEST_FILTER|LOCAL_UNITY_MANAGE_LICENSE) ;;
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
  if mkdir "${RUN_LOCK_DIR}" 2>/dev/null; then
    RUN_LOCK_ACQUIRED=1
    printf '%s\n' "$$" > "${RUN_LOCK_DIR}/pid"
    return 0
  fi

  local existing_pid=""
  if [[ -f "${RUN_LOCK_DIR}/pid" ]]; then
    existing_pid="$(tr -d '\r\n' < "${RUN_LOCK_DIR}/pid" || true)"
  fi

  if [[ -n "${existing_pid}" && "${existing_pid}" =~ ^[0-9]+$ ]] && ps -p "${existing_pid}" >/dev/null 2>&1; then
    echo "error: another local EditMode run is already active (pid ${existing_pid})." >&2
    echo "hint: wait for it to finish or remove stale lock: ${RUN_LOCK_DIR}" >&2
    exit 1
  fi

  rm -rf "${RUN_LOCK_DIR}"
  if mkdir "${RUN_LOCK_DIR}" 2>/dev/null; then
    RUN_LOCK_ACQUIRED=1
    printf '%s\n' "$$" > "${RUN_LOCK_DIR}/pid"
    return 0
  fi

  echo "error: failed to acquire run lock at ${RUN_LOCK_DIR}." >&2
  exit 1
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

  for candidate in "$(command -v Unity 2>/dev/null || true)" \
                   "$(command -v unity-editor 2>/dev/null || true)" \
                   "$(command -v Unity.exe 2>/dev/null || true)" \
                   "/mnt/c/Program Files/Unity/Hub/Editor/${UNITY_VERSION}/Editor/Unity.exe" \
                   "/mnt/c/Program Files/Unity/Editor/Unity.exe"; do
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
      --timeout)
        [[ $# -ge 2 ]] || { echo "error: --timeout requires a value." >&2; exit 1; }
        RUN_TIMEOUT_SECONDS="$2"
        shift 2
        ;;
      --docker-cpus)
        [[ $# -ge 2 ]] || { echo "error: --docker-cpus requires a value." >&2; exit 1; }
        LOCAL_DOCKER_CPUS="$2"
        shift 2
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

ensure_project_version() {
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

run_local_mode() {
  local log_path results_path coverage_path project_path exit_code=0
  local -a unity_cmd
  local -a test_filter_args=()

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

  log_path="$(unity_path_arg "${ARTIFACTS_DIR}/editmode.log")"
  results_path="$(unity_path_arg "${ARTIFACTS_DIR}/editmode-results.xml")"
  coverage_path="$(unity_path_arg "${COVERAGE_DIR}")"
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

  if ! command -v docker >/dev/null 2>&1; then
    echo "error: docker not found in PATH." >&2
    exit 1
  fi

  ensure_license_credentials
  validate_docker_cpus
  setup_local_docker_credential_helper

  UNITY_IMAGE="unityci/editor:ubuntu-${UNITY_VERSION}-linux-il2cpp-3"
  printf 'Using Unity image %q\n' "${UNITY_IMAGE}"

  RUNNER_SCRIPT="$(mktemp /tmp/unity-runner.XXXXXX.sh)"

  cat > "${RUNNER_SCRIPT}" <<'RUNNER'
set -euo pipefail
export HOME="${HOME:-/root}"
mkdir -p "$HOME/.cache/unity3d" /github/workspace/artifacts /github/workspace/CodeCoverage

test_filter_args=()
if [[ -n "${EDITMODE_TEST_FILTER:-}" ]]; then
  test_filter_args=(-testFilter "$EDITMODE_TEST_FILTER")
  printf 'note: applying EditMode test filter: %s\n' "$EDITMODE_TEST_FILTER"
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

rm -rf /github/workspace/Tools/ci/unity-project/Temp

unity-editor \
  -batchmode \
  -nographics \
  -logFile /github/workspace/artifacts/editmode.log \
  -projectPath Tools/ci/unity-project \
  -coverageResultsPath /github/workspace/CodeCoverage \
  -runTests \
  -testPlatform editmode \
  "${test_filter_args[@]}" \
  -testResults /github/workspace/artifacts/editmode-results.xml \
  -enableCodeCoverage \
  -debugCodeOptimization \
  -coverageOptions "generateAdditionalMetrics;generateHtmlReport;generateBadgeReport;dontClear"
RUNNER

  set +e
  timeout --signal=TERM --kill-after=30 "${RUN_TIMEOUT_SECONDS}" docker run --rm \
    --workdir /github/workspace \
    --env HOME=/root \
    --env UNITY_EMAIL \
    --env UNITY_PASSWORD \
    --env UNITY_SERIAL="${UNITY_SERIAL}" \
    --env EDITMODE_TEST_FILTER="${EDITMODE_TEST_FILTER}" \
    "${DOCKER_CPU_ARGS[@]}" \
    --volume "${REPO_ROOT}:/github/workspace" \
    --volume "${RUNNER_SCRIPT}:/tmp/unity-runner.sh" \
    "${UNITY_IMAGE}" \
    /bin/bash /tmp/unity-runner.sh
  docker_run_exit=$?
  set -e

  return "${docker_run_exit}"
}

print_artifact_summary() {
  if [[ -f "${ARTIFACTS_DIR}/editmode.log" ]]; then
    echo "done: artifacts/editmode.log"
  fi

  if [[ -f "${ARTIFACTS_DIR}/editmode-results.xml" ]]; then
    echo "done: artifacts/editmode-results.xml"
  fi
}

load_dotenv_defaults "${DOTENV_FILE}"
parse_args "$@"
LOCAL_UNITY_MANAGE_LICENSE="$(normalize_bool "${LOCAL_UNITY_MANAGE_LICENSE}")"

case "${EDITMODE_RUNNER_MODE}" in
  docker|local) ;;
  *)
    echo "error: EDITMODE_RUNNER_MODE must be 'docker' or 'local'." >&2
    exit 1
    ;;
esac

validate_timeout
ensure_project_version
mkdir -p "${ARTIFACTS_DIR}" "${COVERAGE_DIR}"
acquire_run_lock
trap cleanup EXIT

run_exit=0
case "${EDITMODE_RUNNER_MODE}" in
  docker)
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
