param(
  [string]$WorkflowPath = ".github/workflows/release.yml"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $WorkflowPath)) {
  Write-Error "Invariant failed: workflow file not found at '$WorkflowPath'."
  exit 1
}

$content = Get-Content -LiteralPath $WorkflowPath -Raw
if ([string]::IsNullOrWhiteSpace($content)) {
  Write-Error "Invariant failed: workflow file '$WorkflowPath' is empty."
  exit 1
}

$failures = New-Object System.Collections.Generic.List[string]

function Assert-Regex {
  param(
    [string]$Pattern,
    [string]$FailureMessage
  )

  if ($content -notmatch $Pattern) {
    $failures.Add($FailureMessage)
  }
}

# --- Job structure ---
Assert-Regex '(?ms)^\s{2}setup:\s*$' "Invariant failed: missing 'setup' job block under jobs."
Assert-Regex '(?ms)^\s{2}gate:\s*$' "Invariant failed: missing 'gate' job block under jobs."
Assert-Regex '(?ms)^\s{2}build:\s*$' "Invariant failed: missing 'build' job block under jobs."

# --- Gate job: must depend on setup and fire only when should_release is true ---
Assert-Regex '(?ms)^\s{2}gate:\s*$.*?^\s{4}needs:\s*\[\s*setup\s*\]\s*$' "Invariant failed: gate job must declare 'needs: [setup]'."
Assert-Regex '(?ms)^\s{2}gate:\s*$.*?^\s{4}if:\s*needs\.setup\.outputs\.should_release\s*==\s*''true''\s*$' "Invariant failed: gate job must be conditioned on needs.setup.outputs.should_release == 'true'."

# --- Gate job: compatibility evaluator must run before check polling ---
Assert-Regex '(?ms)^\s{2}gate:\s*$.*?^\s{6}-\s*name:\s*Checkout\s*$' "Invariant failed: gate job must checkout repository contents before compatibility evaluation."
Assert-Regex '(?ms)^\s{2}gate:\s*$.*?^\s{6}-\s*name:\s*Evaluate\s+compatibility\s+contract\s*\(enforced\)\s*$.*?--mode\s+release.*?--contract\s+\.planning/compatibility\.contract\.json.*?^\s{6}-\s*name:\s*Validate\s+compile\s+and\s+lint\s+checks\s+for\s+release\s+SHA\s*$' "Invariant failed: gate job must run enforced compatibility evaluator before compile/lint/test check polling."
Assert-Regex '(?ms)^\s{2}gate:\s*$.*?^\s{6}-\s*name:\s*Append\s+compatibility\s+summary\s*$.*?^\s{8}if:\s*always\(\)\s*$' "Invariant failed: gate job must append compatibility markdown summary with if: always()."

# --- Gate job: must evaluate checks for the exact release SHA ---
Assert-Regex '(?m)^\s+TARGET_SHA:\s*\$\{\{\s*github\.sha\s*\}\}\s*$' 'Invariant failed: gate job must evaluate checks for the exact release SHA via TARGET_SHA: ${{ github.sha }}.'

# --- Build job: must depend on both setup AND gate (fail-closed) ---
Assert-Regex '(?ms)^\s{2}build:\s*$.*?^\s{4}needs:\s*\[\s*setup\s*,\s*gate\s*\]\s*$' "Invariant failed: build job must depend on setup and gate via needs: [setup, gate]."
Assert-Regex '(?ms)^\s{2}build:\s*$.*?^\s{4}if:\s*needs\.setup\.outputs\.should_release\s*==\s*''true''\s*$' "Invariant failed: build job must be conditioned on needs.setup.outputs.should_release == 'true'."

# --- Duplicate tag guard and Create Tag: conditioned on create_tag output ---
Assert-Regex '(?ms)^\s{6}-\s*name:\s*Check\s+for\s+existing\s+release\s+tag\s*$.*?^\s{8}if:\s*needs\.setup\.outputs\.create_tag\s*==\s*''true''\s*$' "Invariant failed: duplicate tag guard step must be conditioned on needs.setup.outputs.create_tag == 'true'."
Assert-Regex '(?ms)^\s{6}-\s*name:\s*Create\s+Tag\s*$.*?^\s{8}if:\s*needs\.setup\.outputs\.create_tag\s*==\s*''true''\s*$' "Invariant failed: Create Tag step must be conditioned on needs.setup.outputs.create_tag == 'true'."

# --- Gate script: required check aliases ---
Assert-Regex '(?s)required_checks\s*=\s*\{.*?"compile"\s*:\s*\[.*?"C# Compile \(Unity 2022\.3\.22f1\)".*?"C# Compile Check / C# Compile \(Unity 2022\.3\.22f1\)".*?\].*?\}' "Invariant failed: gate script must require the compile check aliases."
Assert-Regex '(?s)required_checks\s*=\s*\{.*?"lint"\s*:\s*\[.*?"Super-Linter".*?"Lint / Super-Linter".*?\].*?\}' "Invariant failed: gate script must require the lint check aliases."
Assert-Regex '(?s)required_checks\s*=\s*\{.*?"test"\s*:\s*\[.*?"EditMode Tests".*?"Unity Test Results / EditMode Tests".*?\].*?\}' "Invariant failed: gate script must require the test check aliases."

# --- Gate script: fail-closed logic ---
Assert-Regex '(?s)pending_states\s*=\s*\{.*?"queued".*?"in_progress".*?"pending".*?\}' "Invariant failed: gate script must define pending states for check polling."
Assert-Regex '(?s)if\s+matched_name\s+is\s+None\s*:\s*pending\.append\(' "Invariant failed: gate script must treat missing required aliases as pending while polling."
Assert-Regex '(?s)if\s+value\s*==\s*"success"\s*:\s*continue.*?if\s+value\s+in\s+pending_states\s*:\s*pending\.append\(.*?else\s*:\s*blocking\.append\(' "Invariant failed: gate script must classify check outcomes into success/pending/blocking."
Assert-Regex '(?s)while\s+True\s*:\s*.*?pending,\s*blocking\s*=\s*classify\(observed\)' "Invariant failed: gate script must poll check state until terminal outcome."
Assert-Regex '(?s)if\s+blocking\s*:\s*print\("::error::Release gate blocked\."\)\s*.*?sys\.exit\(1\)' "Invariant failed: gate script must fail closed when blocking reasons exist."
Assert-Regex '(?s)if\s+remaining\s*<=\s*0\s*:\s*print\("::error::Release gate timed out waiting for required checks\."\)\s*.*?sys\.exit\(1\)' "Invariant failed: gate script must timeout and fail when required checks never complete."

if ($failures.Count -gt 0) {
  Write-Host "Release gate invariant check: FAILED"
  for ($i = 0; $i -lt $failures.Count; $i++) {
    Write-Host ("{0}. {1}" -f ($i + 1), $failures[$i])
  }
  exit 1
}

Write-Host "Release gate invariant check: PASSED"
Write-Host "Verified file: $WorkflowPath"
exit 0
