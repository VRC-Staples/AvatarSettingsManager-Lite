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

Assert-Regex '(?ms)^\s{2}gate:\s*$' "Invariant failed: missing 'gate' job block under jobs."
Assert-Regex '(?ms)^\s{4}needs:\s*config\s*$' "Invariant failed: gate job must declare 'needs: config'."
Assert-Regex '(?ms)^\s{4}if:\s*needs\.config\.outputs\.config_package\s*==\s*''true''\s*$' "Invariant failed: gate job must be conditioned on config_package == 'true'."
Assert-Regex '(?ms)^\s{2}tag-check:\s*$' "Invariant failed: missing 'tag-check' job block under jobs."
Assert-Regex '(?ms)^\s{2}tag-check:\s*$.*?^\s{4}needs:\s*config\s*$' "Invariant failed: tag-check job must declare 'needs: config'."
Assert-Regex '(?ms)^\s{2}tag-check:\s*$.*?^\s{4}if:\s*needs\.config\.outputs\.config_package\s*==\s*''true''\s*$' "Invariant failed: tag-check job must be conditioned on config_package == 'true'."
Assert-Regex '(?m)^\s+TARGET_SHA:\s*\$\{\{\s*github\.sha\s*\}\}\s*$' 'Invariant failed: gate job must evaluate checks for the exact release SHA via TARGET_SHA: ${{ github.sha }}.'
Assert-Regex '(?ms)^\s{2}build:\s*$' "Invariant failed: missing 'build' job block."
Assert-Regex '(?ms)^\s{4}needs:\s*\[\s*config\s*,\s*gate\s*,\s*tag-check\s*\]\s*$' "Invariant failed: build job must depend on config, gate, and tag-check via needs: [config, gate, tag-check]."

Assert-Regex '(?s)required_checks\s*=\s*\{.*?"compile"\s*:\s*\[.*?"C# Compile \(Unity 2022\.3\.22f1\)".*?"C# Compile Check / C# Compile \(Unity 2022\.3\.22f1\)".*?\].*?\}' "Invariant failed: gate script must require the compile check aliases."
Assert-Regex '(?s)required_checks\s*=\s*\{.*?"lint"\s*:\s*\[.*?"Super-Linter".*?"Lint / Super-Linter".*?\].*?\}' "Invariant failed: gate script must require the lint check aliases."
Assert-Regex '(?s)required_checks\s*=\s*\{.*?"test"\s*:\s*\[.*?"EditMode Tests".*?"Unity Test Results / EditMode Tests".*?\].*?\}' "Invariant failed: gate script must require the test check aliases."

Assert-Regex '(?s)if\s+matched_name\s+is\s+None\s*:\s*blocking\.append\(' "Invariant failed: gate script must fail when a required check alias is missing."
Assert-Regex '(?s)if\s+matched_value\.lower\(\)\s*!=\s*"success"\s*:\s*blocking\.append\(' "Invariant failed: gate script must enforce success verdicts, not only check-name presence."
Assert-Regex '(?s)if\s+blocking\s*:\s*print\("::error::Release gate blocked\."\)\s*.*?sys\.exit\(1\)' "Invariant failed: gate script must fail closed when blocking reasons exist."

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
