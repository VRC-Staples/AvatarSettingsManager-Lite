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
$DefaultRustOverlayRoot = if ($RepoRoot -match '^/' -or -not [string]::IsNullOrWhiteSpace($env:WSL_DISTRO_NAME)) { '/mnt/f/Workspace/VAUST' } else { 'F:\Workspace\VAUST' }

function Show-Usage {
    @'
Usage: Tools/ci/run-UAT-smoke.ps1

Boots the canonical Rust overlay authority path for ASM-Lite UAT smoke sessions.

Options:
  -h, -Help, --help        Show this help text
  -SelfTest                Run lightweight wrapper self-tests without Unity or cargo
  -SuiteId <id>[,<id>...]  Select one or more suites for the batch in supplied order
  --suite-id <id>[,<id>...] Same as -SuiteId

Examples:
  pwsh -NoProfile -File Tools/ci/run-UAT-smoke.ps1 -SuiteId setup-scene-avatar,lifecycle-roundtrip
  pwsh -NoProfile -File Tools/ci/run-UAT-smoke.ps1 --suite-id setup-scene-avatar,lifecycle-roundtrip

Notes:
  - Step sleep is configured in the Rust overlay UI; it defaults off.
  - Enabling step sleep starts at 1.5 seconds and can be changed while no suite is running.
  - This command is a canonical Rust-overlay UAT smoke entrypoint.
  - Rust overlay resolution order: ASMLITE_RUST_OVERLAY_BIN, ASMLITE_RUST_OVERLAY_MANIFEST,
    ASMLITE_RUST_OVERLAY_ROOT, /mnt/f/Workspace/VAUST (or F:\Workspace\VAUST on Windows),
    then legacy Tools/ci/rust-overlay.
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
    $preferNativeCargo = $RepoRoot -match '^/' -or -not [string]::IsNullOrWhiteSpace($env:WSL_DISTRO_NAME)
    if ($preferNativeCargo) {
        $command = Get-Command cargo -ErrorAction SilentlyContinue
        if ($null -ne $command -and -not $command.Source.EndsWith('.exe', [StringComparison]::OrdinalIgnoreCase)) {
            return $command.Source
        }
    }

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

function New-RustOverlayCargoRunner {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ManifestPath,
        [Parameter(Mandatory = $true)]
        [string]$LabelManifestPath
    )

    $script:CargoCommand = Resolve-CargoCommand
    if ([string]::IsNullOrWhiteSpace($script:CargoCommand)) {
        throw "error: Rust overlay manifest found at $ManifestPath, but no overlay executable was found and no cargo/cargo.exe is available. Build the VAUST overlay, set ASMLITE_RUST_OVERLAY_BIN, or install Rust."
    }

    if (Test-CargoToolchain '+stable-x86_64-pc-windows-msvc') {
        return @{
            Command = $script:CargoCommand
            PrefixArgs = @(
                '+stable-x86_64-pc-windows-msvc',
                'run',
                '--manifest-path', $ManifestPath,
                '--bin', 'asmlite_smoke_overlay',
                '--'
            )
            Label = "cargo +stable-x86_64-pc-windows-msvc run --manifest-path $LabelManifestPath --bin asmlite_smoke_overlay --"
            Entrypoint = $false
        }
    }

    if (Test-CargoToolchain '+stable-x86_64-unknown-linux-gnu') {
        return @{
            Command = $script:CargoCommand
            PrefixArgs = @(
                '+stable-x86_64-unknown-linux-gnu',
                'run',
                '--manifest-path', $ManifestPath,
                '--bin', 'asmlite_smoke_overlay',
                '--'
            )
            Label = "cargo +stable-x86_64-unknown-linux-gnu run --manifest-path $LabelManifestPath --bin asmlite_smoke_overlay --"
            Entrypoint = $false
        }
    }

    return @{
        Command = $script:CargoCommand
        PrefixArgs = @(
            'run',
            '--manifest-path', $ManifestPath,
            '--bin', 'asmlite_smoke_overlay',
            '--'
        )
        Label = "cargo run --manifest-path $LabelManifestPath --bin asmlite_smoke_overlay --"
        Entrypoint = $false
    }
}

function Resolve-RustOverlayRootRunner {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OverlayRoot
    )

    $exeCandidate = Join-Path $OverlayRoot 'bin/asmlite_smoke_overlay.exe'
    $bareCandidate = Join-Path $OverlayRoot 'bin/asmlite_smoke_overlay'
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

    $manifestPath = Join-Path $OverlayRoot 'Cargo.toml'
    if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        return New-RustOverlayCargoRunner -ManifestPath $manifestPath -LabelManifestPath $manifestPath
    }

    return $null
}

function Resolve-RustOverlayRunner {
    if (-not [string]::IsNullOrWhiteSpace($env:ASMLITE_RUST_OVERLAY_BIN)) {
        $candidate = $env:ASMLITE_RUST_OVERLAY_BIN
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return @{
                Command = $candidate
                PrefixArgs = @()
                Label = $candidate
                Entrypoint = $false
            }
        }
        throw "error: ASMLITE_RUST_OVERLAY_BIN points to a missing overlay executable: $candidate"
    }

    if (-not [string]::IsNullOrWhiteSpace($env:ASMLITE_RUST_OVERLAY_MANIFEST)) {
        $manifestPath = $env:ASMLITE_RUST_OVERLAY_MANIFEST
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
            throw "error: ASMLITE_RUST_OVERLAY_MANIFEST points to a missing Cargo.toml: $manifestPath"
        }
        return New-RustOverlayCargoRunner -ManifestPath $manifestPath -LabelManifestPath $manifestPath
    }

    $overlayRoots = @()
    if (-not [string]::IsNullOrWhiteSpace($env:ASMLITE_RUST_OVERLAY_ROOT)) {
        $overlayRoots += $env:ASMLITE_RUST_OVERLAY_ROOT
    }
    $overlayRoots += $DefaultRustOverlayRoot
    $overlayRoots += (Join-Path $RepoRoot 'Tools/ci/rust-overlay')

    foreach ($overlayRoot in @($overlayRoots)) {
        $runner = Resolve-RustOverlayRootRunner -OverlayRoot $overlayRoot
        if ($null -ne $runner) {
            return $runner
        }
    }

    throw "error: no Rust overlay executable or Cargo.toml was found. Tried ASMLITE_RUST_OVERLAY_ROOT, $DefaultRustOverlayRoot, and legacy $(Join-Path $RepoRoot 'Tools/ci/rust-overlay'). Set ASMLITE_RUST_OVERLAY_ROOT=/mnt/f/Workspace/VAUST, ASMLITE_RUST_OVERLAY_BIN, or ASMLITE_RUST_OVERLAY_MANIFEST."
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

    function Assert-EqualValue {
        param(
            [AllowNull()]
            [object]$Actual,
            [AllowNull()]
            [object]$Expected,
            [Parameter(Mandatory = $true)]
            [string]$Name
        )

        if ($Actual -ne $Expected) {
            throw "$Name failed: expected '$Expected', got '$Actual'"
        }
    }

    $previousBin = $env:ASMLITE_RUST_OVERLAY_BIN
    $previousManifest = $env:ASMLITE_RUST_OVERLAY_MANIFEST
    $previousRoot = $env:ASMLITE_RUST_OVERLAY_ROOT
    $previousPath = $env:PATH
    $previousUserProfile = $env:USERPROFILE
    $previousDefaultRoot = $script:DefaultRustOverlayRoot
    $tmpRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("asmlite-overlay-selftest-{0}-{1}" -f $PID, [System.Guid]::NewGuid().ToString('N'))

    try {
        $explicitRoot = Join-Path $tmpRoot 'explicit-root'
        $defaultRoot = Join-Path $tmpRoot 'default-root'
        $legacyRoot = Join-Path $tmpRoot 'legacy-root'
        $explicitBin = Join-Path $tmpRoot 'explicit-bin/asmlite_smoke_overlay'
        $explicitManifest = Join-Path $tmpRoot 'explicit-manifest/Cargo.toml'

        $null = New-Item -ItemType Directory -Force -Path $explicitRoot, (Join-Path $defaultRoot 'bin'), (Join-Path $legacyRoot 'bin'), (Split-Path -Parent $explicitBin), (Split-Path -Parent $explicitManifest)
        $null = New-Item -ItemType File -Force -Path (Join-Path $explicitRoot 'Cargo.toml'), $explicitManifest, (Join-Path $defaultRoot 'bin/asmlite_smoke_overlay'), (Join-Path $legacyRoot 'bin/asmlite_smoke_overlay'), $explicitBin

        $script:DefaultRustOverlayRoot = $defaultRoot
        $env:PATH = ''
        $env:USERPROFILE = ''

        $env:ASMLITE_RUST_OVERLAY_BIN = $explicitBin
        $env:ASMLITE_RUST_OVERLAY_MANIFEST = $explicitManifest
        $env:ASMLITE_RUST_OVERLAY_ROOT = $explicitRoot
        $runner = Resolve-RustOverlayRunner
        Assert-EqualValue -Name 'ASMLITE_RUST_OVERLAY_BIN outranks manifest/root/default' -Actual $runner.Label -Expected $explicitBin

        $env:ASMLITE_RUST_OVERLAY_BIN = ''
        $env:ASMLITE_RUST_OVERLAY_MANIFEST = $explicitManifest
        $env:ASMLITE_RUST_OVERLAY_ROOT = $explicitRoot
        $manifestSelected = $false
        try {
            $null = Resolve-RustOverlayRunner
        }
        catch {
            $manifestSelected = $_.Exception.Message.Contains($explicitManifest)
        }
        if (-not $manifestSelected) {
            throw 'ASMLITE_RUST_OVERLAY_MANIFEST did not outrank root/default executable before cargo lookup'
        }

        $env:ASMLITE_RUST_OVERLAY_BIN = ''
        $env:ASMLITE_RUST_OVERLAY_MANIFEST = ''
        $env:ASMLITE_RUST_OVERLAY_ROOT = $explicitRoot
        $rootManifestSelected = $false
        $expectedRootManifest = Join-Path $explicitRoot 'Cargo.toml'
        try {
            $null = Resolve-RustOverlayRunner
        }
        catch {
            $rootManifestSelected = $_.Exception.Message.Contains($expectedRootManifest)
        }
        if (-not $rootManifestSelected) {
            throw 'ASMLITE_RUST_OVERLAY_ROOT manifest did not outrank default executable before cargo lookup'
        }
    }
    finally {
        $env:ASMLITE_RUST_OVERLAY_BIN = $previousBin
        $env:ASMLITE_RUST_OVERLAY_MANIFEST = $previousManifest
        $env:ASMLITE_RUST_OVERLAY_ROOT = $previousRoot
        $env:PATH = $previousPath
        $env:USERPROFILE = $previousUserProfile
        $script:DefaultRustOverlayRoot = $previousDefaultRoot
        if (Test-Path -LiteralPath $tmpRoot) {
            Remove-Item -LiteralPath $tmpRoot -Recurse -Force
        }
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
