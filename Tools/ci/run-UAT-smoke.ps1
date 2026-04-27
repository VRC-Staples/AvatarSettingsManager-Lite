#!/usr/bin/env pwsh
param(
    [Alias('h')]
    [switch]$Help,

    [switch]$SelfTest,

    [Alias('suite-id')]
    [string[]]$SuiteId,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir '../..')).Path
$ArtifactsDir = Join-Path $RepoRoot 'artifacts'
$CanonicalProjectPath = Join-Path (Split-Path -Parent $RepoRoot) 'Test Project/TestUnityProject'
$CanonicalCatalogPath = Join-Path $RepoRoot 'Tools/ci/smoke/suite-catalog.json'

function Show-Usage {
    @'
Usage: Tools/ci/run-UAT-smoke.ps1

Boots the canonical Rust overlay authority path for ASM-Lite UAT smoke sessions.

Options:
  -h, -Help, --help        Show this help text
  -SuiteId <id>[,<id>...]  Select one or more suites for the batch in supplied order
  --suite-id <id>[,<id>...] Same as -SuiteId

Examples:
  pwsh -NoProfile -File Tools/ci/run-UAT-smoke.ps1 -SuiteId setup-scene-avatar,lifecycle-roundtrip
  pwsh -NoProfile -File Tools/ci/run-UAT-smoke.ps1 --suite-id setup-scene-avatar,lifecycle-roundtrip

Notes:
  - Step sleep is configured in the Rust overlay UI; it defaults off.
  - Enabling step sleep starts at 1.5 seconds and can be changed while no suite is running.
  - This command is a canonical Rust-overlay UAT smoke entrypoint.
'@ | Write-Host
}

function Stop-WithUsageError {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-Host $Message
    Show-Usage
    exit 1
}

function Resolve-CargoCommand {
    $command = Get-Command cargo.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $command = Get-Command cargo -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
        $userCargo = Join-Path $env:USERPROFILE '.cargo/bin/cargo.exe'
        if (Test-Path -LiteralPath $userCargo -PathType Leaf) {
            return $userCargo
        }
    }

    return $null
}

function Resolve-UnityExecutable {
    $projectVersionPath = Join-Path $CanonicalProjectPath 'ProjectSettings/ProjectVersion.txt'
    if (-not (Test-Path -LiteralPath $projectVersionPath -PathType Leaf)) {
        return $null
    }

    $versionLine = Get-Content -LiteralPath $projectVersionPath | Where-Object { $_ -match '^m_EditorVersion:' } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($versionLine)) {
        return $null
    }

    $unityVersion = ($versionLine -replace '^m_EditorVersion:\s*', '').Trim()
    if ([string]::IsNullOrWhiteSpace($unityVersion)) {
        return $null
    }

    if ([string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        return $null
    }

    $candidate = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$unityVersion/Editor/Unity.exe"
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        return $candidate
    }

    return $null
}

function ConvertTo-SuiteIdBatch {
    param(
        [string[]]$RawSuiteIds
    )

    $normalized = @()
    if ($null -eq $RawSuiteIds) {
        return $normalized
    }
    foreach ($raw in @($RawSuiteIds)) {
        if ([string]::IsNullOrWhiteSpace($raw)) {
            throw 'suite id values must not be empty'
        }
        foreach ($entry in ($raw -split ',')) {
            $suiteId = $entry.Trim()
            if ([string]::IsNullOrWhiteSpace($suiteId)) {
                throw 'suite id values must not be empty'
            }
            $normalized += $suiteId
        }
    }
    return $normalized
}

function Get-KnownSuiteIds {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CatalogPath
    )

    $catalog = Get-Content -LiteralPath $CatalogPath -Raw | ConvertFrom-Json
    $known = @()
    foreach ($group in @($catalog.groups)) {
        foreach ($suite in @($group.suites)) {
            if (-not [string]::IsNullOrWhiteSpace($suite.suiteId)) {
                $known += [string]$suite.suiteId
            }
        }
    }
    return $known
}

function Test-SuiteIdsKnown {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$SuiteIds,
        [Parameter(Mandatory = $true)]
        [string[]]$KnownSuiteIds
    )

    foreach ($suiteId in @($SuiteIds)) {
        if ($KnownSuiteIds -notcontains $suiteId) {
            throw "unknown suite id '$suiteId'; expected one of: $($KnownSuiteIds -join ', ')"
        }
    }
}

function Get-SuiteIdsFromRemainingArgs {
    param(
        [string[]]$RawArgs
    )

    $suiteIds = @()
    $passthrough = @()
    $index = 0
    while ($index -lt @($RawArgs).Count) {
        $arg = $RawArgs[$index]
        if ($arg -eq '--suite-id') {
            $index++
            if ($index -ge @($RawArgs).Count) {
                throw '--suite-id requires a value'
            }
            $suiteIds += $RawArgs[$index]
        }
        else {
            $passthrough += $arg
        }
        $index++
    }

    return @{
        SuiteIds = $suiteIds
        RemainingArgs = $passthrough
    }
}

function Join-SuiteIdInputs {
    param(
        [string[]]$BoundSuiteIds,
        [string[]]$RemainingSuiteIds
    )

    $joined = @()
    if ($null -ne $BoundSuiteIds) {
        $joined += @($BoundSuiteIds)
    }
    if ($null -ne $RemainingSuiteIds) {
        $joined += @($RemainingSuiteIds)
    }
    return $joined
}

function Test-CargoToolchain {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Toolchain
    )

    try {
        & $script:CargoCommand $Toolchain --version *> $null
        return ($LASTEXITCODE -eq 0)
    }
    catch {
        return $false
    }
}

function Resolve-WslCommand {
    $command = Get-Command wsl.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $command = Get-Command wsl -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $candidates = @(
        (Join-Path $env:WINDIR 'System32/wsl.exe'),
        (Join-Path $env:WINDIR 'Sysnative/wsl.exe')
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    return $null
}

function ConvertTo-WslPathFallback {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WindowsPath
    )

    if ($WindowsPath -match '^([A-Za-z]):\\(.*)$') {
        $drive = $matches[1].ToLowerInvariant()
        $rest = $matches[2] -replace '\\', '/'
        return "/mnt/$drive/$rest"
    }

    if ($WindowsPath -match '^/') {
        return $WindowsPath
    }

    return $null
}

function Resolve-WslRepoRoot {
    $wslCommand = Resolve-WslCommand
    if ([string]::IsNullOrWhiteSpace($wslCommand)) {
        return $null
    }

    try {
        $converted = (& $wslCommand wslpath -a $RepoRoot 2>$null | Select-Object -First 1)
        if (-not [string]::IsNullOrWhiteSpace($converted)) {
            return @{
                Command = $wslCommand
                Path = $converted.Trim()
            }
        }
    }
    catch {
        # Fall through to manual drive-letter conversion.
    }

    $fallback = ConvertTo-WslPathFallback $RepoRoot
    if (-not [string]::IsNullOrWhiteSpace($fallback)) {
        return @{
            Command = $wslCommand
            Path = $fallback
        }
    }

    return $null
}

function Resolve-RustOverlayRunner {
    $exeCandidate = Join-Path $RepoRoot 'Tools/ci/rust-overlay/bin/asmlite_smoke_overlay.exe'
    $bareCandidate = Join-Path $RepoRoot 'Tools/ci/rust-overlay/bin/asmlite_smoke_overlay'

    foreach ($candidate in @($exeCandidate, $bareCandidate)) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return @{
                Command = $candidate
                PrefixArgs = @()
                Label = $candidate
                Entrypoint = $false
            }
        }
    }

    $script:CargoCommand = Resolve-CargoCommand
    if ([string]::IsNullOrWhiteSpace($script:CargoCommand)) {
        throw 'error: no Windows cargo.exe found and no checked-in Rust overlay executable exists. Install Windows Rust or build Tools/ci/rust-overlay/bin/asmlite_smoke_overlay.exe.'
    }

    $manifestPath = Join-Path $RepoRoot 'Tools/ci/rust-overlay/Cargo.toml'

    if (Test-CargoToolchain '+stable-x86_64-pc-windows-msvc') {
        return @{
            Command = $script:CargoCommand
            PrefixArgs = @(
                '+stable-x86_64-pc-windows-msvc',
                'run',
                '--manifest-path', $manifestPath,
                '--bin', 'asmlite_smoke_overlay',
                '--'
            )
            Label = 'cargo +stable-x86_64-pc-windows-msvc run --manifest-path Tools/ci/rust-overlay/Cargo.toml --bin asmlite_smoke_overlay --'
            Entrypoint = $false
        }
    }

    if (Test-CargoToolchain '+stable-x86_64-unknown-linux-gnu') {
        return @{
            Command = $script:CargoCommand
            PrefixArgs = @(
                '+stable-x86_64-unknown-linux-gnu',
                'run',
                '--manifest-path', $manifestPath,
                '--bin', 'asmlite_smoke_overlay',
                '--'
            )
            Label = 'cargo +stable-x86_64-unknown-linux-gnu run --manifest-path Tools/ci/rust-overlay/Cargo.toml --bin asmlite_smoke_overlay --'
            Entrypoint = $false
        }
    }

    return @{
        Command = $script:CargoCommand
        PrefixArgs = @(
            'run',
            '--manifest-path', $manifestPath,
            '--bin', 'asmlite_smoke_overlay',
            '--'
        )
        Label = 'cargo run --manifest-path Tools/ci/rust-overlay/Cargo.toml --bin asmlite_smoke_overlay --'
        Entrypoint = $false
    }
}

function Invoke-SelfTest {
    function Assert-EqualArray {
        param(
            [string[]]$Actual,
            [string[]]$Expected,
            [Parameter(Mandatory = $true)]
            [string]$Name
        )

        if ($null -eq $Actual) {
            $Actual = @()
        }
        else {
            $Actual = @($Actual)
        }
        if ($null -eq $Expected) {
            $Expected = @()
        }
        else {
            $Expected = @($Expected)
        }
        if ($Actual.Count -ne $Expected.Count) {
            throw "$Name failed: expected $($Expected.Count) item(s), got $($Actual.Count)"
        }
        for ($index = 0; $index -lt $Expected.Count; $index++) {
            if ($Actual[$index] -ne $Expected[$index]) {
                throw "$Name failed at index ${index}: expected '$($Expected[$index])', got '$($Actual[$index])'"
            }
        }
    }

    Assert-EqualArray `
        -Name 'Empty suite inputs stay empty' `
        -Actual (Join-SuiteIdInputs -BoundSuiteIds $null -RemainingSuiteIds $null) `
        -Expected @()

    Assert-EqualArray `
        -Name 'SuiteId CSV normalization preserves order' `
        -Actual (ConvertTo-SuiteIdBatch @('setup-scene-avatar,lifecycle-roundtrip')) `
        -Expected @('setup-scene-avatar', 'lifecycle-roundtrip')

    $parsedRemaining = Get-SuiteIdsFromRemainingArgs -RawArgs @('--suite-id', 'setup-scene-avatar,lifecycle-roundtrip')
    Assert-EqualArray `
        -Name '--suite-id remaining arg normalization preserves order' `
        -Actual (ConvertTo-SuiteIdBatch @($parsedRemaining['SuiteIds'])) `
        -Expected @('setup-scene-avatar', 'lifecycle-roundtrip')

    $known = @('setup-scene-avatar', 'lifecycle-roundtrip', 'playmode-runtime-validation')
    Test-SuiteIdsKnown -SuiteIds @('setup-scene-avatar', 'lifecycle-roundtrip') -KnownSuiteIds $known

    $rejected = $false
    try {
        Test-SuiteIdsKnown -SuiteIds @('setup-scene-avatar', 'synthetic-all') -KnownSuiteIds $known
    }
    catch {
        $rejected = $_.Exception.Message.Contains('unknown suite id')
    }
    if (-not $rejected) {
        throw 'Unknown suite id validation failed'
    }

    Write-Host 'SelfTest PASS'
}

if ($Help) {
    Show-Usage
    exit 0
}

if ($SelfTest) {
    Invoke-SelfTest
    exit 0
}

if ($null -eq $RemainingArgs) {
    $RemainingArgs = @()
}
else {
    $RemainingArgs = @($RemainingArgs)
}

try {
    $remainingParse = Get-SuiteIdsFromRemainingArgs -RawArgs $RemainingArgs
    $SuiteId = Join-SuiteIdInputs -BoundSuiteIds $SuiteId -RemainingSuiteIds $remainingParse.SuiteIds
    $RemainingArgs = @($remainingParse.RemainingArgs)
}
catch {
    Stop-WithUsageError "error: $($_.Exception.Message)"
}

if ($RemainingArgs.Count -gt 0) {
    if ($RemainingArgs.Count -eq 1 -and ($RemainingArgs[0] -eq '--help' -or $RemainingArgs[0] -eq '-h')) {
        Show-Usage
        exit 0
    }

    if ($RemainingArgs[0].StartsWith('--')) {
        Stop-WithUsageError "error: unknown option: $($RemainingArgs[0])"
    }

    Stop-WithUsageError "error: unexpected positional argument: $($RemainingArgs[0])"
}

if (-not (Test-Path -LiteralPath $CanonicalProjectPath -PathType Container)) {
    Write-Host "error: UAT smoke requires the external test project path: $CanonicalProjectPath"
    Write-Host 'error: do not use Tools/ci/unity-project for UAT smoke; that harness is CI-only.'
    exit 1
}

if (-not (Test-Path -LiteralPath $CanonicalCatalogPath -PathType Leaf)) {
    Write-Host "error: expected suite catalog not found: $CanonicalCatalogPath"
    exit 1
}

try {
    $selectedSuiteIds = @(ConvertTo-SuiteIdBatch -RawSuiteIds $SuiteId)
    if ($selectedSuiteIds.Count -gt 0) {
        $knownSuiteIds = @(Get-KnownSuiteIds -CatalogPath $CanonicalCatalogPath)
        Test-SuiteIdsKnown -SuiteIds $selectedSuiteIds -KnownSuiteIds $knownSuiteIds
    }
}
catch {
    Write-Host "error: $($_.Exception.Message)"
    exit 1
}

$runner = Resolve-RustOverlayRunner

if ($runner.Entrypoint) {
    Write-Host "Windows cargo not found; using WSL fallback: $($runner.Label)"
    & $runner.Command @($runner.PrefixArgs)
    $fallbackExitCode = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
    exit $fallbackExitCode
}

$smokeOverlayRoot = Join-Path $ArtifactsDir 'smoke-overlay'
$null = New-Item -ItemType Directory -Force -Path $smokeOverlayRoot

$timestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmss')
$overlaySessionRoot = Join-Path $smokeOverlayRoot ("session-{0}-{1}" -f $timestamp, $PID)
$null = New-Item -ItemType Directory -Force -Path $overlaySessionRoot
$overlayLogPath = Join-Path $overlaySessionRoot 'overlay.log'

$overlayArgs = @()
$overlayArgs += $runner.PrefixArgs
$overlayArgs += @(
    '--repo-root', $RepoRoot,
    '--project-path', $CanonicalProjectPath,
    '--catalog-path', $CanonicalCatalogPath,
    '--session-root', $overlaySessionRoot,
    '--mode', 'uat'
)

$unityExecutable = Resolve-UnityExecutable
if (-not [string]::IsNullOrWhiteSpace($unityExecutable)) {
    $overlayArgs += @('--unity-executable', $unityExecutable)
}

foreach ($suiteId in @($selectedSuiteIds)) {
    $overlayArgs += @('--suite-id', $suiteId)
}

Write-Host 'Running canonical visible UAT smoke flow against:'
Write-Host "  Project: $CanonicalProjectPath"
Write-Host "  Package: $(Join-Path $RepoRoot 'Packages/com.staples.asm-lite')"
Write-Host '  Mode:    uat'
if ($selectedSuiteIds.Count -gt 0) { Write-Host "  Suites:  $($selectedSuiteIds -join ', ')" }
Write-Host '  Step sleep: configured in overlay UI (default off; 1.5s when enabled)'
Write-Host "  Runner:  $($runner.Label)"
if (-not [string]::IsNullOrWhiteSpace($unityExecutable)) { Write-Host "  Unity:   $unityExecutable" }
Write-Host "  Session: $overlaySessionRoot"

$overlayExitCode = 1
$previousErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'

try {
    & $runner.Command @overlayArgs *> $overlayLogPath
    $overlayExitCode = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
}
catch {
    $message = $_.Exception.Message
    Add-Content -LiteralPath $overlayLogPath -Value "`nlauncher-error: $message"
    Write-Host "error: $message"
    $overlayExitCode = 1
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}

Write-Host ''
if ($overlayExitCode -eq 0) {
    Write-Host 'visible-smoke: BOOTSTRAP PASS (Rust overlay CLI exited 0)'
}
else {
    Write-Host "visible-smoke: BOOTSTRAP FAIL (exit code $overlayExitCode)"
}

Write-Host ''
Write-Host 'Artifacts:'
Write-Host "  Rust session root: $overlaySessionRoot"
Write-Host "  Overlay log: $overlayLogPath"

exit $overlayExitCode
