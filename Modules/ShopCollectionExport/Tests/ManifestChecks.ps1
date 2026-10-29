param([switch]$Online, [string]$Revision = 'main')
$ErrorActionPreference = 'Stop'
$repositoryDirectory = Split-Path (Split-Path (Split-Path $PSScriptRoot))
$rawBase = "https://raw.githubusercontent.com/Angelways/OmniOnlineModules/$Revision/"
$manifestPaths = @('TreeHouseModules.json', 'Modules/ShopCollectionExport/TreeHouseModules.json')
$resolvedUrls = @()
$identities = @()
foreach ($relativePath in $manifestPaths) {
    $manifestPath = Join-Path $repositoryDirectory $relativePath
    $manifestUrl = [uri]::new([uri]$rawBase, $relativePath)
    if ($Online) {
        $response = & curl.exe --fail --silent --show-error --connect-timeout 10 --max-time 30 $manifestUrl.AbsoluteUri
        if ($LASTEXITCODE -ne 0) { throw "Manifest download failed: $manifestUrl" }
        $manifest = ($response -join "`n") | ConvertFrom-Json
    } else {
        $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    }
    if ($manifest.SchemaVersion -ne 1 -or $manifest.Modules.Count -ne 1) { throw 'Unexpected manifest schema/modules.' }
    $module = $manifest.Modules[0]
    $downloadUrl = [uri]::new($manifestUrl, [string]$module.File)
    $expectedPath = "Modules/ShopCollectionExport/$($module.Version)/ShopCollectionExport.dll"
    if ($downloadUrl.AbsoluteUri -ne [uri]::new([uri]$rawBase, $expectedPath).AbsoluteUri) {
        throw "Manifest resolves to wrong module URL: $relativePath -> $downloadUrl"
    }
    $localDll = [IO.Path]::GetFullPath((Join-Path (Split-Path $manifestPath) $module.File))
    if ($localDll -ne [IO.Path]::GetFullPath((Join-Path $repositoryDirectory $expectedPath))) {
        throw "Manifest resolves to wrong local file: $relativePath -> $localDll"
    }
    if ((Get-FileHash -LiteralPath $localDll -Algorithm SHA256).Hash -ne $module.Sha256) { throw 'Published DLL hash mismatch.' }
    if ($Online) {
        $temporaryDll = Join-Path ([IO.Path]::GetTempPath()) ("omni-manifest-" + [guid]::NewGuid() + '.dll')
        try {
            & curl.exe --fail --silent --show-error --connect-timeout 10 --max-time 30 --output $temporaryDll $downloadUrl.AbsoluteUri
            if ($LASTEXITCODE -ne 0) { throw "Module download failed: $downloadUrl" }
            if ((Get-FileHash -LiteralPath $temporaryDll -Algorithm SHA256).Hash -ne $module.Sha256) { throw 'Downloaded DLL hash mismatch.' }
        } finally { if (Test-Path -LiteralPath $temporaryDll) { Remove-Item -LiteralPath $temporaryDll } }
    }
    $resolvedUrls += $downloadUrl.AbsoluteUri
    $identities += ($module | Select-Object * -ExcludeProperty File | ConvertTo-Json -Depth 7 -Compress)
    Write-Output "Verified $relativePath -> $downloadUrl; SHA256 $($module.Sha256)"
}
if ($resolvedUrls[0] -ne $resolvedUrls[1] -or $identities[0] -ne $identities[1]) { throw 'Subscription entries disagree.' }
Write-Output 'Both subscriptions resolve to the same verified DLL.'
