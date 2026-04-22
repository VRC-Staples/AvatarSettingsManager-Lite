#!/usr/bin/env pwsh
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
Assert-Regex '(?ms)^\s{2}gate:\s*$.*?^\s{4}name:\s*Release\s+Gate\s*$' "Invariant failed: gate job name must remain 'Release Gate'."

# --- Gate job: must depend on setup and fire only when should_release is true ---
Assert-Regex '(?ms)^\s{2}gate:\s*$.*?^\s{4}needs:\s*\[\s*setup\s*\]\s*$' "Invariant failed: gate job must declare 'needs: [setup]'."
Assert-Regex '(?ms)^\s{2}gate:\s*$.*?^\s{4}if:\s*needs\.setup\.outputs\.should_release\s*==\s*''true''\s*$' "Invariant failed: gate job must be conditioned on needs.setup.outputs.should_release == 'true'."
Assert-Regex '(?ms)^\s{2}gate:\s*$.*?^\s{4}permissions:\s*$.*?^\s{6}checks:\s*read\s*$.*?^\s{6}statuses:\s*read\s*$' "Invariant failed: gate job must keep checks/statuses read permissions."

# --- Gate job ordering and invariant checker wiring ---
Assert-Regex '(?ms)^\s{2}gate:\s*$.*?^\s{6}-\s*name:\s*Checkout\s*$.*?^\s{6}-\s*name:\s*Verify\s+release\s+gate\s+invariants\s*$.*?^\s{6}-\s*name:\s*Evaluate\s+compatibility\s+contract\s*\(enforced\)\s*$.*?^\s{6}-\s*name:\s*Validate\s+compile\s+and\s+lint\s+checks\s+for\s+release\s+SHA\s*$' "Invariant failed: gate job must run checkout -> invariant checker -> compatibility evaluation -> required-check polling in order."
Assert-Regex '(?ms)^\s{2}gate:\s*$.*?^\s{6}-\s*name:\s*Verify\s+release\s+gate\s+invariants\s*$.*?^\s{8}run:\s*pwsh\s+-File\s+Tools/ci/verify-release-gate\.ps1\s*$' "Invariant failed: gate job must execute pwsh -File Tools/ci/verify-release-gate.ps1."
Assert-Regex '(?ms)^\s{2}gate:\s*$.*?^\s{6}-\s*name:\s*Evaluate\s+compatibility\s+contract\s*\(enforced\)\s*$.*?--mode\s+release.*?--contract\s+compatibility\.contract\.json' "Invariant failed: gate job must run enforced compatibility evaluation."
Assert-Regex '(?ms)^\s{2}gate:\s*$.*?^\s{6}-\s*name:\s*Append\s+compatibility\s+summary\s*$.*?^\s{8}if:\s*always\(\)\s*$' "Invariant failed: gate job must append compatibility markdown summary with if: always()."

# --- Gate job: exact-SHA helper wiring + shared required-check source ---
Assert-Regex '(?m)^\s+TARGET_SHA:\s*\$\{\{\s*github\.sha\s*\}\}\s*$' 'Invariant failed: gate job must evaluate checks for the exact release SHA via TARGET_SHA: ${{ github.sha }}.'
Assert-Regex '(?ms)^\s{2}gate:\s*$.*?Tools/ci/check-required-statuses\.py' "Invariant failed: gate job must invoke Tools/ci/check-required-statuses.py."
Assert-Regex '(?ms)^\s{2}gate:\s*$.*?--required-checks\s+Tools/ci/release-required-checks\.json' "Invariant failed: gate job must read shared aliases from Tools/ci/release-required-checks.json."
Assert-Regex '(?ms)^\s{2}gate:\s*$.*?--github-token-env\s+GITHUB_TOKEN' "Invariant failed: gate job must pass --github-token-env GITHUB_TOKEN to helper."
Assert-Regex '(?ms)^\s{2}gate:\s*$.*?--max-wait-seconds\s+1800' "Invariant failed: gate job must keep 1800s max wait for required-check polling."
Assert-Regex '(?ms)^\s{2}gate:\s*$.*?--poll-interval-seconds\s+20' "Invariant failed: gate job must keep 20s polling interval for required-check polling."
Assert-Regex '(?ms)^\s{2}gate:\s*$.*?--repo\s+"\$\{\{\s*github\.repository\s*\}\}"' "Invariant failed: gate helper invocation must target the current repository."
Assert-Regex '(?ms)^\s{2}gate:\s*$.*?--sha\s+"\$TARGET_SHA"' "Invariant failed: gate helper invocation must evaluate TARGET_SHA."

if ($content -match '(?s)required_checks\s*=\s*\{') {
  $failures.Add("Invariant failed: release workflow still contains inline required_checks map; use Tools/ci/release-required-checks.json via helper.")
}

# --- Build job: must depend on both setup AND gate (fail-closed) ---
Assert-Regex '(?ms)^\s{2}build:\s*$.*?^\s{4}needs:\s*\[\s*setup\s*,\s*gate\s*\]\s*$' "Invariant failed: build job must depend on setup and gate via needs: [setup, gate]."
Assert-Regex '(?ms)^\s{2}build:\s*$.*?^\s{4}if:\s*needs\.setup\.outputs\.should_release\s*==\s*''true''\s*$' "Invariant failed: build job must be conditioned on needs.setup.outputs.should_release == 'true'."

# --- Duplicate tag guard and Create Tag: conditioned on create_tag output ---
Assert-Regex '(?ms)^\s{6}-\s*name:\s*Check\s+for\s+existing\s+release\s+tag\s*$.*?^\s{8}if:\s*needs\.setup\.outputs\.create_tag\s*==\s*''true''\s*$' "Invariant failed: duplicate tag guard step must be conditioned on needs.setup.outputs.create_tag == 'true'."
Assert-Regex '(?ms)^\s{6}-\s*name:\s*Create\s+Tag\s*$.*?^\s{8}if:\s*needs\.setup\.outputs\.create_tag\s*==\s*''true''\s*$' "Invariant failed: Create Tag step must be conditioned on needs.setup.outputs.create_tag == 'true'."

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
