# generate-icon-metas.ps1
$icons = @('Save', 'Load', 'Reset', 'Presets')
foreach ($icon in $icons) {
    $guid = [System.Guid]::NewGuid().ToString('N')
    $metaPath = "Assets/ASM-Lite/Icons/$icon.png.meta"
    $content = "fileFormatVersion: 2`nguid: $guid`nTextureImporter:`n  serializedVersion: 12`n  mipmaps:`n    enableMipMap: 0`n  textureType: 2`n  textureShape: 1`n  maxTextureSize: 256`n  alphaUsage: 1`n  alphaIsTransparency: 1`n  userData:`n  assetBundleName:`n  assetBundleVariant:`n"
    Set-Content -Path $metaPath -Value $content -Encoding UTF8
    Write-Host ("Created: " + $metaPath + " (guid: " + $guid + ")")
}
