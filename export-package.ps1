# export-package.ps1
# Produces Dist/ASM-Lite.unitypackage in two stages:
#   1. CreatePrefab via -executeMethod  (ensures prefab asset is up-to-date)
#   2. -exportPackage                   (Unity built-in flag, no custom C# needed)
#
# Package resolution: scoped registries (packages.vrchat.com + package.openupm.com).
# Open the project through VCC at least once so auth tokens are cached, then this
# script can run in headless mode without further interaction.
#
# Unity discovery order:
#   1. -UnityExe parameter (explicit override)
#   2. Unity Hub install matching ProjectSettings/ProjectVersion.txt
#   3. Unity Hub install — any 2022.3.x version (fallback)
#
# Usage:
#   .\export-package.ps1
#   .\export-package.ps1 -UnityExe "D:\Unity\2022.3.22f1\Editor\Unity.exe"

param(
    [string]$UnityExe = ""
)

$ProjectPath = (Resolve-Path ".").Path
$LogFile     = Join-Path $ProjectPath "unity-export.log"
$OutputPkg   = Join-Path $ProjectPath "Dist\ASM-Lite.unitypackage"

# ── Unity discovery ───────────────────────────────────────────────────────────

function Find-UnityExe {
    $hubBase = "C:\Program Files\Unity\Hub\Editor"

    # Read the exact version from the project
    $versionFile = Join-Path $ProjectPath "ProjectSettings\ProjectVersion.txt"
    $projectVersion = $null
    if (Test-Path $versionFile) {
        $line = Get-Content $versionFile | Where-Object { $_ -match "^m_EditorVersion:" } | Select-Object -First 1
        if ($line) { $projectVersion = ($line -split ":\s*")[1].Trim() }
    }

    if ($projectVersion -and (Test-Path $hubBase)) {
        # Exact match first
        $exact = Join-Path $hubBase "$projectVersion\Editor\Unity.exe"
        if (Test-Path $exact) { return $exact }
    }

    if (Test-Path $hubBase) {
        # Fallback: any 2022.3.x
        $fallback = Get-ChildItem $hubBase -Directory |
            Where-Object { $_.Name -match "^2022\.3\." } |
            Sort-Object Name -Descending |
            Select-Object -First 1
        if ($fallback) {
            $candidate = Join-Path $fallback.FullName "Editor\Unity.exe"
            if (Test-Path $candidate) { return $candidate }
        }
    }

    return $null
}

if ($UnityExe -eq "") {
    $UnityExe = Find-UnityExe
    if ($null -eq $UnityExe) {
        Write-Error "Could not locate Unity editor automatically."
        Write-Error "Install Unity 2022.3.x via Unity Hub, or pass -UnityExe explicitly:"
        Write-Error "  .\export-package.ps1 -UnityExe 'D:\Unity\Editor\Unity.exe'"
        exit 1
    }
    Write-Host "Discovered Unity: $UnityExe"
}
elseif (-not (Test-Path $UnityExe)) {
    Write-Error ("Unity not found at: {0}" -f $UnityExe)
    Write-Error "Pass -UnityExe with the correct path, or leave it empty for auto-discovery."
    exit 1
}

New-Item -ItemType Directory -Force -Path "Dist" | Out-Null

# ── VCC lockfile guard ────────────────────────────────────────────────────────
# packages.vrchat.com requires VCC-injected auth tokens. Unity batch mode cannot
# authenticate against it on its own. The lockfile is written by VCC when it
# opens and resolves the project — if it's missing or lacks the VRChat entries,
# the export will fail with "Access denied" before any code runs.

$LockFile         = Join-Path $ProjectPath "Packages\packages-lock.json"
$RequiredPackages = @("com.vrchat.avatars", "com.vrchat.base")
$lockOk           = $false

if (Test-Path $LockFile) {
    try {
        $lock = Get-Content $LockFile -Raw | ConvertFrom-Json
        $lockOk = $true
        foreach ($pkg in $RequiredPackages) {
            if (-not $lock.dependencies.PSObject.Properties[$pkg]) {
                $lockOk = $false
                Write-Warning ("packages-lock.json is missing '{0}'." -f $pkg)
            }
        }
    }
    catch {
        $lockOk = $false
        Write-Warning "packages-lock.json could not be parsed."
    }
}

if (-not $lockOk) {
    Write-Host ""
    Write-Host "┌─────────────────────────────────────────────────────────────┐" -ForegroundColor Yellow
    Write-Host "│  VCC prerequisite check failed                              │" -ForegroundColor Yellow
    Write-Host "├─────────────────────────────────────────────────────────────┤" -ForegroundColor Yellow
    Write-Host "│  Packages/packages-lock.json is missing or does not contain │" -ForegroundColor Yellow
    Write-Host "│  resolved entries for the VRChat SDK packages.              │" -ForegroundColor Yellow
    Write-Host "│                                                             │" -ForegroundColor Yellow
    Write-Host "│  packages.vrchat.com requires auth tokens that only VCC     │" -ForegroundColor Yellow
    Write-Host "│  can inject. Unity batch mode cannot authenticate on its    │" -ForegroundColor Yellow
    Write-Host "│  own and will fail with 'Access denied'.                    │" -ForegroundColor Yellow
    Write-Host "│                                                             │" -ForegroundColor Yellow
    Write-Host "│  Fix: open this project in the VRChat Creator Companion     │" -ForegroundColor Yellow
    Write-Host "│  (VCC) and let it fully resolve packages, then re-run this  │" -ForegroundColor Yellow
    Write-Host "│  script.                                                    │" -ForegroundColor Yellow
    Write-Host "└─────────────────────────────────────────────────────────────┘" -ForegroundColor Yellow
    Write-Host ""
    exit 1
}

Write-Host "Unity:      $UnityExe"
Write-Host "Project:    $ProjectPath"
Write-Host "Output:     $OutputPkg"
Write-Host "Log:        $LogFile"
Write-Host ""

# ── Invoke helper ─────────────────────────────────────────────────────────────

function Invoke-Unity {
    param([string[]]$Arguments, [string]$Stage)

    Write-Host ("{0}..." -f $Stage)

    $proc = Start-Process `
        -FilePath $UnityExe `
        -ArgumentList $Arguments `
        -Wait `
        -PassThru `
        -NoNewWindow

    $code = $proc.ExitCode
    if ($code -ne 0) {
        Write-Host ""
        Write-Error ("{0} failed (Unity exit code: {1}). Last 30 lines of log:" -f $Stage, $code)
        if (Test-Path $LogFile) {
            Get-Content $LogFile -Tail 30 | ForEach-Object { Write-Host "  $_" }
        }
        exit 1
    }
    Write-Host ("  {0} complete (exit 0)." -f $Stage)
    Write-Host ""
}

# ── Stage 1: Create the prefab ────────────────────────────────────────────────
Invoke-Unity -Stage "Stage 1/2: Creating prefab" -Arguments @(
    "-batchmode", "-quit",
    "-logFile", $LogFile,
    "-projectPath", $ProjectPath,
    "-executeMethod", "ASMLite.Editor.ASMLitePrefabCreator.CreatePrefab"
)

# ── Stage 2: Export the package ───────────────────────────────────────────────
Invoke-Unity -Stage "Stage 2/2: Exporting package" -Arguments @(
    "-batchmode", "-quit",
    "-logFile", $LogFile,
    "-projectPath", $ProjectPath,
    "-exportPackage", "Assets/ASM-Lite", $OutputPkg
)

if (-not (Test-Path $OutputPkg)) {
    Write-Error ("Unity exited cleanly but {0} was not created. Check {1}." -f $OutputPkg, $LogFile)
    exit 1
}

$size = (Get-Item $OutputPkg).Length
Write-Host "========================================"
Write-Host ("Export complete: Dist\ASM-Lite.unitypackage ({0:N0} KB)" -f [math]::Round($size / 1KB))
Write-Host "========================================"
