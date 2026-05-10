#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
ARTIFACTS_DIR="${REPO_ROOT}/artifacts"
CANONICAL_PROJECT_PATH="${REPO_ROOT}/Tools/ci/unity-project"
CANONICAL_CATALOG_PATH="${REPO_ROOT}/Tools/ci/smoke/suite-catalog.json"
DEFAULT_RUST_OVERLAY_ROOT="/mnt/f/Workspace/VAUST"
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
  --overlay-smoke           Run canonical visible Rust overlay smoke flow (default)
  --editor-smoke            Run the visible editor smoke selector
  --playmode-smoke          Run the visible playmode smoke selector
  --test-filter <filter>    Override the visible selector for editor/playmode smoke
  --self-test               Run lightweight wrapper self-tests without Unity or cargo
  -h, --help                Show this help text

Notes:
  - Visible step delay is fixed at 1.5 seconds.
  - Overlay mode is the canonical local Rust-overlay smoke entrypoint.
  - Rust overlay resolution order: ASMLITE_RUST_OVERLAY_BIN, ASMLITE_RUST_OVERLAY_MANIFEST,
    ASMLITE_RUST_OVERLAY_ROOT, /mnt/f/Workspace/VAUST, then legacy Tools/ci/rust-overlay.
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

resolve_cargo_overlay_runner() {
  local manifest_path="$1"
  local label_manifest="$2"

  if ! command -v cargo >/dev/null 2>&1; then
    echo "error: Rust overlay manifest found at ${manifest_path}, but no overlay executable was found and cargo is unavailable." >&2
    echo "error: build the VAUST overlay, set ASMLITE_RUST_OVERLAY_BIN, or install cargo." >&2
    exit 1
  fi

  if cargo +stable-x86_64-unknown-linux-gnu --version >/dev/null 2>&1; then
    RUST_OVERLAY_RUNNER_CMD=(
      cargo
      +stable-x86_64-unknown-linux-gnu
      run
      --manifest-path "${manifest_path}"
      --bin asmlite_smoke_overlay
      --
    )
    RUST_OVERLAY_RUNNER_LABEL="cargo +stable-x86_64-unknown-linux-gnu run --manifest-path ${label_manifest} --bin asmlite_smoke_overlay --"
    return 0
  fi

  RUST_OVERLAY_RUNNER_CMD=(
    cargo
    run
    --manifest-path "${manifest_path}"
    --bin asmlite_smoke_overlay
    --
  )
  RUST_OVERLAY_RUNNER_LABEL="cargo run --manifest-path ${label_manifest} --bin asmlite_smoke_overlay --"
}

resolve_overlay_root_runner() {
  local overlay_root="$1"
  local candidate manifest

  for candidate in \
    "${overlay_root}/bin/asmlite_smoke_overlay" \
    "${overlay_root}/bin/asmlite_smoke_overlay.exe"
  do
    if [[ -x "${candidate}" || ( "${candidate}" == *.exe && -f "${candidate}" ) ]]; then
      RUST_OVERLAY_RUNNER_CMD=("${candidate}")
      RUST_OVERLAY_RUNNER_LABEL="${candidate}"
      return 0
    fi
  done

  manifest="${overlay_root}/Cargo.toml"
  if [[ -f "${manifest}" ]]; then
    resolve_cargo_overlay_runner "${manifest}" "${manifest}"
    return 0
  fi

  return 1
}

resolve_rust_overlay_runner() {
  local candidate overlay_root manifest
  local -a overlay_roots=()

  if [[ -n "${ASMLITE_RUST_OVERLAY_BIN:-}" ]]; then
    candidate="${ASMLITE_RUST_OVERLAY_BIN}"
    if [[ -x "${candidate}" || ( "${candidate}" == *.exe && -f "${candidate}" ) ]]; then
      RUST_OVERLAY_RUNNER_CMD=("${candidate}")
      RUST_OVERLAY_RUNNER_LABEL="${candidate}"
      return 0
    fi
    echo "error: ASMLITE_RUST_OVERLAY_BIN points to a missing or non-executable overlay: ${candidate}" >&2
    exit 1
  fi

  if [[ -n "${ASMLITE_RUST_OVERLAY_MANIFEST:-}" ]]; then
    manifest="${ASMLITE_RUST_OVERLAY_MANIFEST}"
    if [[ ! -f "${manifest}" ]]; then
      echo "error: ASMLITE_RUST_OVERLAY_MANIFEST points to a missing Cargo.toml: ${manifest}" >&2
      exit 1
    fi
    resolve_cargo_overlay_runner "${manifest}" "${manifest}"
    return 0
  fi

  if [[ -n "${ASMLITE_RUST_OVERLAY_ROOT:-}" ]]; then
    overlay_roots+=("${ASMLITE_RUST_OVERLAY_ROOT}")
  fi
  overlay_roots+=("${DEFAULT_RUST_OVERLAY_ROOT}" "${REPO_ROOT}/Tools/ci/rust-overlay")

  for overlay_root in "${overlay_roots[@]}"; do
    if resolve_overlay_root_runner "${overlay_root}"; then
      return 0
    fi
  done

  echo "error: no Rust overlay executable or Cargo.toml was found." >&2
  echo "error: tried ASMLITE_RUST_OVERLAY_ROOT, ${DEFAULT_RUST_OVERLAY_ROOT}, and legacy ${REPO_ROOT}/Tools/ci/rust-overlay." >&2
  echo "error: set ASMLITE_RUST_OVERLAY_ROOT=/mnt/f/Workspace/VAUST, ASMLITE_RUST_OVERLAY_BIN, or ASMLITE_RUST_OVERLAY_MANIFEST." >&2
  exit 1
}

run_self_test() {
  local tmp_root explicit_root default_root legacy_root explicit_bin explicit_manifest fake_bin expected

  assert_label() {
    local name="$1"
    local expected_label="$2"
    if [[ "${RUST_OVERLAY_RUNNER_LABEL}" != "${expected_label}" ]]; then
      echo "SelfTest FAIL: ${name}: expected runner label '${expected_label}', got '${RUST_OVERLAY_RUNNER_LABEL}'" >&2
      return 1
    fi
  }

  tmp_root="$(mktemp -d)"
  trap "rm -rf '${tmp_root}'" EXIT

  explicit_root="${tmp_root}/explicit-root"
  default_root="${tmp_root}/default-root"
  legacy_root="${tmp_root}/legacy-root"
  explicit_bin="${tmp_root}/explicit-bin/asmlite_smoke_overlay"
  explicit_manifest="${tmp_root}/explicit-manifest/Cargo.toml"
  fake_bin="${tmp_root}/fake-bin"

  mkdir -p "${explicit_root}" "${default_root}/bin" "${legacy_root}/bin" "$(dirname "${explicit_bin}")" "$(dirname "${explicit_manifest}")" "${fake_bin}"
  : >"${explicit_root}/Cargo.toml"
  : >"${explicit_manifest}"
  cat >"${fake_bin}/cargo" <<'EOF'
#!/usr/bin/env bash
if [[ "${1:-}" == +* && "${2:-}" == "--version" ]]; then
  exit 1
fi
exit 0
EOF
  chmod +x "${fake_bin}/cargo"
  printf '#!/usr/bin/env bash\nexit 0\n' >"${default_root}/bin/asmlite_smoke_overlay"
  printf '#!/usr/bin/env bash\nexit 0\n' >"${legacy_root}/bin/asmlite_smoke_overlay"
  printf '#!/usr/bin/env bash\nexit 0\n' >"${explicit_bin}"
  chmod +x "${default_root}/bin/asmlite_smoke_overlay" "${legacy_root}/bin/asmlite_smoke_overlay" "${explicit_bin}"

  DEFAULT_RUST_OVERLAY_ROOT="${default_root}"
  PATH="${fake_bin}:${PATH}"

  RUST_OVERLAY_RUNNER_CMD=()
  RUST_OVERLAY_RUNNER_LABEL=""
  ASMLITE_RUST_OVERLAY_BIN="${explicit_bin}" \
  ASMLITE_RUST_OVERLAY_MANIFEST="${explicit_manifest}" \
  ASMLITE_RUST_OVERLAY_ROOT="${explicit_root}" \
    resolve_rust_overlay_runner
  assert_label "ASMLITE_RUST_OVERLAY_BIN outranks manifest/root/default" "${explicit_bin}"

  RUST_OVERLAY_RUNNER_CMD=()
  RUST_OVERLAY_RUNNER_LABEL=""
  ASMLITE_RUST_OVERLAY_BIN="" \
  ASMLITE_RUST_OVERLAY_MANIFEST="${explicit_manifest}" \
  ASMLITE_RUST_OVERLAY_ROOT="${explicit_root}" \
    resolve_rust_overlay_runner
  expected="cargo run --manifest-path ${explicit_manifest} --bin asmlite_smoke_overlay --"
  assert_label "ASMLITE_RUST_OVERLAY_MANIFEST outranks root/default" "${expected}"

  RUST_OVERLAY_RUNNER_CMD=()
  RUST_OVERLAY_RUNNER_LABEL=""
  ASMLITE_RUST_OVERLAY_BIN="" \
  ASMLITE_RUST_OVERLAY_MANIFEST="" \
  ASMLITE_RUST_OVERLAY_ROOT="${explicit_root}" \
    resolve_rust_overlay_runner
  expected="cargo run --manifest-path ${explicit_root}/Cargo.toml --bin asmlite_smoke_overlay --"
  assert_label "ASMLITE_RUST_OVERLAY_ROOT manifest outranks default executable" "${expected}"

  echo "SelfTest PASS"
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
    --exit-on-ready
  )

  echo "Running canonical visible Rust overlay smoke flow against:"
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
