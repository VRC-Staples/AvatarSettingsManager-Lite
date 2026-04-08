param(
  [string]$WorkflowPath = ".github/workflows/secret-scan.yml"
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

# R066 criterion 1: weekly cron schedule present
Assert-Regex '(?ms)schedule:.*cron:' "Invariant failed: secret-scan.yml must have a schedule.cron trigger (R066 weekly schedule)."

# R066 criterion 2: workflow_dispatch trigger present
Assert-Regex '(?ms)workflow_dispatch:' "Invariant failed: secret-scan.yml must include workflow_dispatch trigger (R066 independent of push cadence)."

# R066 criterion 3: no push trigger present
if ($content -match '(?ms)^on:[\s\S]*?push:') {
  $failures.Add("Invariant failed: secret-scan.yml must NOT have a push: trigger (R066 runs independently of push cadence).")
}

# R066 criterion 4: gitleaks action is SHA-pinned (40-char hex SHA)
Assert-Regex 'gitleaks/gitleaks-action@[0-9a-f]{40}' "Invariant failed: gitleaks/gitleaks-action must be pinned to a full 40-char SHA (R066 SHA-pinned action)."

# R066 criterion 5: permissions: contents: read present
Assert-Regex '(?ms)permissions:[\s\S]*?contents:\s*read' "Invariant failed: secret-scan.yml must declare permissions: contents: read (R066 expected permissions)."

if ($failures.Count -gt 0) {
  Write-Host "Secret scan invariant check: FAILED"
  for ($i = 0; $i -lt $failures.Count; $i++) {
    Write-Host ("{0}. {1}" -f ($i + 1), $failures[$i])
  }
  exit 1
}

Write-Host "Secret scan invariant check: PASSED"
Write-Host "Verified file: $WorkflowPath"
exit 0
