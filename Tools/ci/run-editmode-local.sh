#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
PROJECT_REL_PATH="Tools/ci/unity-project"
PROJECT_VERSION_FILE="${REPO_ROOT}/${PROJECT_REL_PATH}/ProjectSettings/ProjectVersion.txt"
ARTIFACTS_DIR="${REPO_ROOT}/artifacts"
COVERAGE_DIR="${REPO_ROOT}/CodeCoverage"
DOTENV_FILE="${REPO_ROOT}/.env"

RUN_TIMEOUT_SECONDS="${RUN_TIMEOUT_SECONDS:-2700}"
RUN_LOCK_DIR="${RUN_LOCK_DIR:-/tmp/asmlite-editmode-local.lock}"
LOCAL_DOCKER_CPUS="${LOCAL_DOCKER_CPUS:-}"
RUN_LOCK_ACQUIRED=0
DOCKER_CPU_ARGS=()

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
      UNITY_LICENSE_SECRET|UNITY_LICENSE_FILE|UNITY_EMAIL|UNITY_PASSWORD|UNITY_SERIAL|LOCAL_DOCKER_CPUS) ;;
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

load_dotenv_defaults "${DOTENV_FILE}"

if [[ -z "${UNITY_SERIAL:-}" ]]; then
  if [[ -z "${UNITY_LICENSE_SECRET:-}" && -n "${UNITY_LICENSE_FILE:-}" ]]; then
    if [[ ! -f "${UNITY_LICENSE_FILE}" ]]; then
      if [[ "${UNITY_LICENSE_FILE}" =~ ^/mnt/([a-zA-Z])/(.+)$ ]]; then
        windows_drive="${BASH_REMATCH[1]}"
        windows_tail="${BASH_REMATCH[2]}"
        windows_tail="${windows_tail//\//\\}"
        windows_license_file="${windows_drive^^}:\\${windows_tail}"

        windows_license_content="$(/mnt/c/Windows/System32/cmd.exe /c "type \"${windows_license_file}\"" 2>/dev/null || true)"
        if [[ -n "${windows_license_content}" ]]; then
          UNITY_LICENSE_SECRET="${windows_license_content}"
        fi
      fi

      if [[ -z "${UNITY_LICENSE_SECRET:-}" ]]; then
        echo "error: UNITY_LICENSE_FILE does not exist: ${UNITY_LICENSE_FILE}" >&2
        exit 1
      fi
    else
      UNITY_LICENSE_SECRET="$(<"${UNITY_LICENSE_FILE}")"
    fi
  fi

  require_env "UNITY_LICENSE_SECRET" "Set raw .ulf text or base64-encoded .ulf content (or set UNITY_LICENSE_FILE in env/.env)."
fi

require_env "UNITY_EMAIL" "Set Unity account email used for license activation."
require_env "UNITY_PASSWORD" "Set Unity account password used for license activation."

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

if ! command -v docker >/dev/null 2>&1; then
  echo "error: docker not found in PATH." >&2
  exit 1
fi

if [[ -n "${LOCAL_DOCKER_CPUS}" ]]; then
  if ! [[ "${LOCAL_DOCKER_CPUS}" =~ ^[0-9]+([.][0-9]+)?$ ]]; then
    echo "error: LOCAL_DOCKER_CPUS must be a positive number (example: 4 or 3.5)." >&2
    exit 1
  fi

  if ! awk -v value="${LOCAL_DOCKER_CPUS}" 'BEGIN { exit (value > 0) ? 0 : 1 }'; then
    echo "error: LOCAL_DOCKER_CPUS must be greater than zero." >&2
    exit 1
  fi

  DOCKER_CPU_ARGS=(--cpus "${LOCAL_DOCKER_CPUS}")
  echo "note: limiting docker container CPUs to ${LOCAL_DOCKER_CPUS} for this local run."
fi

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

acquire_run_lock

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

setup_local_docker_credential_helper

if command -v python3 >/dev/null 2>&1; then
  PYTHON_BIN="python3"
elif command -v python >/dev/null 2>&1; then
  PYTHON_BIN="python"
else
  echo "error: python3/python not found in PATH." >&2
  exit 1
fi

if [[ ! -f "${PROJECT_VERSION_FILE}" ]]; then
  echo "error: missing project version file: ${PROJECT_VERSION_FILE}" >&2
  exit 1
fi

mkdir -p "${ARTIFACTS_DIR}" "${COVERAGE_DIR}"

UNITY_VERSION="$(awk -F': ' '/^m_EditorVersion:/ { print $2; exit }' "${PROJECT_VERSION_FILE}" | tr -d '\r\n')"
if [[ -z "${UNITY_VERSION:-}" ]]; then
  echo "error: failed to read Unity version from ${PROJECT_VERSION_FILE}" >&2
  exit 1
fi

UNITY_IMAGE="unityci/editor:ubuntu-${UNITY_VERSION}-linux-il2cpp-3"
printf 'Using Unity image %q\n' "${UNITY_IMAGE}"

SERIAL_FILE="$(mktemp)"
RUNNER_SCRIPT="$(mktemp /tmp/unity-runner.XXXXXX.sh)"
cleanup() {
  rm -f "${SERIAL_FILE}" "${RUNNER_SCRIPT}"
  if [[ -n "${DOCKER_HELPER_SHIM_DIR:-}" ]]; then
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
trap cleanup EXIT

export UNITY_EMAIL
export UNITY_PASSWORD

if [[ -z "${UNITY_SERIAL:-}" ]]; then
  export UNITY_LICENSE_SECRET

  "${PYTHON_BIN}" - <<'PY' "${SERIAL_FILE}"
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
else
  echo "note: using UNITY_SERIAL from environment/.env."
fi

cat > "${RUNNER_SCRIPT}" <<'RUNNER'
set -euo pipefail
export HOME="${HOME:-/root}"
mkdir -p "$HOME/.cache/unity3d" /github/workspace/artifacts /github/workspace/CodeCoverage

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
  -nographics \
  -logFile /github/workspace/artifacts/editmode.log \
  -projectPath Tools/ci/unity-project \
  -coverageResultsPath /github/workspace/CodeCoverage \
  -runTests \
  -testPlatform editmode \
  -testResults /github/workspace/artifacts/editmode-results.xml \
  -enableCodeCoverage \
  -debugCodeOptimization \
  -coverageOptions "generateAdditionalMetrics;generateHtmlReport;generateBadgeReport;dontClear"
RUNNER

docker_run_exit=0
set +e
timeout --signal=TERM --kill-after=30 "${RUN_TIMEOUT_SECONDS}" docker run --rm \
  --workdir /github/workspace \
  --env HOME=/root \
  --env UNITY_EMAIL \
  --env UNITY_PASSWORD \
  --env UNITY_SERIAL="${UNITY_SERIAL}" \
  "${DOCKER_CPU_ARGS[@]}" \
  --volume "${REPO_ROOT}:/github/workspace" \
  --volume "${RUNNER_SCRIPT}:/tmp/unity-runner.sh" \
  "${UNITY_IMAGE}" \
  /bin/bash /tmp/unity-runner.sh
docker_run_exit=$?
set -e
if [[ "${docker_run_exit}" -eq 124 ]]; then
  echo "error: EditMode run timed out after ${RUN_TIMEOUT_SECONDS}s (set RUN_TIMEOUT_SECONDS to adjust)." >&2
fi
if [[ -f "${ARTIFACTS_DIR}/editmode.log" ]]; then
  echo "done: artifacts/editmode.log"
fi
if [[ -f "${ARTIFACTS_DIR}/editmode-results.xml" ]]; then
  echo "done: artifacts/editmode-results.xml"
fi
exit "${docker_run_exit}"

