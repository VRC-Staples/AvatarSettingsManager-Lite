# export-package.ps1 — Headless Unity batch-mode export for ASM-Lite
# Produces Dist/ASM-Lite.unitypackage via ExportPackageEditor.Export()
# Requires Unity 2022.3.22f1 installed at the default Hub path.

$UnityExe  = "C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe"
$LogFile   = "unity-export.log"
$OutputPkg = "Dist\ASM-Lite.unitypackage"

if (-not (Test-Path $UnityExe)) {
    Write-Error "Unity not found at: $UnityExe"
    Write-Error "Install Unity 2022.3.22f1 via Unity Hub and try again."
    exit 1
}

Write-Host "Starting headless Unity export..."
Write-Host "Log file: $LogFile"

& $UnityExe `
    -batchmode `
    -quit `
    -logFile $LogFile `
    -projectPath . `
    -executeMethod ASMLite.Editor.ExportPackageEditor.Export

if ($LASTEXITCODE -ne 0) {
    Write-Error "Unity exited with code $LASTEXITCODE. Check $LogFile for details."
    exit 1
}

if (-not (Test-Path $OutputPkg)) {
    Write-Error "Export appeared to succeed but $OutputPkg was not created."
    Write-Error "Check $LogFile for details."
    exit 1
}

Write-Host ""
Write-Host "Export complete: $OutputPkg"
