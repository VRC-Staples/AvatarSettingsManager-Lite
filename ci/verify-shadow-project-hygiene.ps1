#!/usr/bin/env pwsh
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$manifestPath = Join-Path $repoRoot 'ci/unity-project/Packages/manifest.json'
$lockPath = Join-Path $repoRoot 'ci/unity-project/Packages/packages-lock.json'
$lockRepoPath = 'ci/unity-project/Packages/packages-lock.json'
$requiredTrackedFiles = @(
    'ci/unity-project/Packages/com.vrchat.base/Runtime/VRCSDK/Plugins/VRCSDKBase.dll',
    'ci/unity-project/Packages/com.vrchat.base/Runtime/VRCSDK/Plugins/VRCSDKBase.dll.meta',
    'ci/unity-project/Packages/com.vrchat.base/Runtime/VRCSDK/Dependencies/Managed/System.Collections.Immutable.dll',
    'ci/unity-project/Packages/com.vrchat.base/Runtime/VRCSDK/Dependencies/Managed/System.Collections.Immutable.dll.meta',
    'ci/unity-project/Packages/com.vrchat.avatars/Runtime/VRCSDK/Plugins/VRCSDK3A.dll',
    'ci/unity-project/Packages/com.vrchat.avatars/Runtime/VRCSDK/Plugins/VRCSDK3A.dll.meta'
)

function Assert-True {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$Condition,
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

try {
    $manifest = Get-Content -Raw -Path $manifestPath | ConvertFrom-Json
    $lock = Get-Content -Raw -Path $lockPath | ConvertFrom-Json

    $manifestVersion = [string]$manifest.dependencies.'com.unity.test-framework'
    $lockVersion = [string]$lock.dependencies.'com.unity.test-framework'.version

    Assert-True -Condition (-not [string]::IsNullOrWhiteSpace($manifestVersion)) -Message "Invariant failed: manifest is missing dependencies.com.unity.test-framework in $manifestPath"
    Assert-True -Condition (-not [string]::IsNullOrWhiteSpace($lockVersion)) -Message "Invariant failed: lockfile is missing dependencies.com.unity.test-framework.version in $lockPath"
    Assert-True -Condition ($manifestVersion -eq $lockVersion) -Message "Invariant failed: com.unity.test-framework mismatch. manifest=$manifestVersion lock=$lockVersion"

    # packages-lock.json must be tracked so UPM does not need network access in CI.
    $null = & git ls-files --error-unmatch $lockRepoPath
    Assert-True -Condition ($LASTEXITCODE -eq 0) -Message "Invariant failed: $lockRepoPath is not tracked by git. It must be committed so UPM can resolve packages without network access in CI."

    foreach ($file in $requiredTrackedFiles) {
        $null = & git ls-files --error-unmatch $file
        Assert-True -Condition ($LASTEXITCODE -eq 0) -Message "Invariant failed: required SDK file is not tracked: $file"
    }

    Write-Host "PASS: shadow project hygiene invariants hold."
    Write-Host "PASS: com.unity.test-framework manifest and lock both resolve to $manifestVersion"
    Write-Host "PASS: packages-lock.json is tracked (required for offline UPM resolution in CI)"
    Write-Host "PASS: required VRChat SDK DLLs remain tracked"
    exit 0
}
catch {
    Write-Error $_
    exit 1
}
