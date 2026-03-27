# export-package.ps1
# Produces Dist/ASM-Lite.unitypackage in two stages:
#   1. CreatePrefab via -executeMethod  (ensures prefab asset is up-to-date)
#   2. -exportPackage                   (Unity built-in flag, no custom C# needed)
#
# Packages resolve from local file: paths in manifest.json — no network required.
#
# Usage:
#   .\export-package.ps1
#   .\export-package.ps1 -UnityExe "C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe"

param(
    [string]$UnityExe = "C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe"
)

$ProjectPath = (Resolve-Path ".").Path
$LogFile     = Join-Path $ProjectPath "unity-export.log"
$OutputPkg   = Join-Path $ProjectPath "Dist\ASM-Lite.unitypackage"

if (-not (Test-Path $UnityExe)) {
    Write-Error ("Unity not found at: {0}" -f $UnityExe)
    Write-Error "Pass -UnityExe with the correct path, e.g.:"
    Write-Error "  .\export-package.ps1 -UnityExe 'C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe'"
    exit 1
}

New-Item -ItemType Directory -Force -Path "Dist" | Out-Null

Write-Host "Unity:      $UnityExe"
Write-Host "Project:    $ProjectPath"
Write-Host "Output:     $OutputPkg"
Write-Host "Log:        $LogFile"
Write-Host ""

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
# Unity's built-in -exportPackage: no custom C# required.
# Appends to the same log file for a single combined record.
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
