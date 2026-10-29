param(
    [string]$OmniPath = 'D:\XIVLauncherCN\installedPlugins\OmniToolbox\1.1.8.7',
    [string]$DalamudPath = 'D:\XIVLauncherCN\Addon\Hooks\26-10-21-01',
    [string]$ReleaseRoot = 'E:\WorkPlace\Releases\OmniOnlineModules'
)
$ErrorActionPreference = 'Stop'
$moduleDirectory = $PSScriptRoot
$repositoryDirectory = Split-Path (Split-Path $moduleDirectory)
$projectFile = Join-Path $moduleDirectory 'Source\OmniShopOwned.csproj'
[xml]$projectXml = Get-Content -Raw -LiteralPath $projectFile
$version = [string]$projectXml.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Invalid module version.' }
$assemblyExpectedVersion = [version]$(if ($version.Split('.').Count -eq 3) { "$version.0" } else { $version })
dotnet build $projectFile -c Release --nologo "-p:OmniPath=$OmniPath" "-p:DalamudPath=$DalamudPath"
if ($LASTEXITCODE -ne 0) { throw 'Module build failed.' }
dotnet run --project (Join-Path $moduleDirectory 'ContractChecks') -c Release "-p:OmniPath=$OmniPath" "-p:DalamudPath=$DalamudPath" -- $OmniPath $DalamudPath
if ($LASTEXITCODE -ne 0) { throw 'API contract/lifecycle checks failed.' }
dotnet run --project (Join-Path $moduleDirectory 'Tests') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Export checks failed.' }
$builtDll = Join-Path $moduleDirectory 'Source\bin\Release\net10.0-windows\ShopCollectionExport.dll'
$versionDirectory = Join-Path $moduleDirectory $version
$publishedDll = Join-Path $versionDirectory 'ShopCollectionExport.dll'
$hash = (Get-FileHash -LiteralPath $builtDll -Algorithm SHA256).Hash
if ((Test-Path -LiteralPath $publishedDll) -and
    (Get-FileHash -LiteralPath $publishedDll -Algorithm SHA256).Hash -ne $hash) {
    throw 'This version already has different bytes. Increase the project version before publishing.'
}
New-Item -ItemType Directory -Force -Path $versionDirectory | Out-Null
Copy-Item -LiteralPath $builtDll -Destination $publishedDll -Force
$manifest = [ordered]@{ SchemaVersion = 1; Modules = @([ordered]@{
    InternalName = 'ShopCollectionExport'
    Author = 'Angelways'
    RepositoryURL = 'https://github.com/Angelways/OmniOnlineModules'
    Version = $version
    ApiVersion = 1
    MinimumOmniVersion = '1.1.8.2'
    File = "Modules/ShopCollectionExport/$version/ShopCollectionExport.dll"
    Sha256 = $hash
    Localization = [ordered]@{ Source = [ordered]@{
        Title = '商城物品收录导出'
        Description = '按角色导出库存与已解锁收录，供商城已获取标记油猴脚本导入。'
    } }
}) }
$json = ($manifest | ConvertTo-Json -Depth 7) + "`n"
# Omni resolves File against the actual manifest URL, including direct raw subscriptions.
[IO.File]::WriteAllText((Join-Path $repositoryDirectory 'TreeHouseModules.json'), $json, [Text.UTF8Encoding]::new($false))
$manifest.Modules[0].File = "$version/ShopCollectionExport.dll"
$moduleJson = ($manifest | ConvertTo-Json -Depth 7) + "`n"
[IO.File]::WriteAllText((Join-Path $moduleDirectory 'TreeHouseModules.json'), $moduleJson, [Text.UTF8Encoding]::new($false))
& (Join-Path $moduleDirectory 'Tests\ManifestChecks.ps1')
if (!$?) { throw 'Manifest URL/path checks failed.' }
$releaseDirectory = Join-Path $ReleaseRoot "ShopCollectionExport-$version"
$releaseDll = Join-Path $releaseDirectory 'ShopCollectionExport.dll'
if ((Test-Path -LiteralPath $releaseDll) -and (Get-FileHash $releaseDll -Algorithm SHA256).Hash -ne $hash) {
    throw 'Release folder already contains different bytes for this version.'
}
New-Item -ItemType Directory -Force -Path $releaseDirectory | Out-Null
Copy-Item -LiteralPath $publishedDll -Destination $releaseDll -Force
# Use the same repository-relative manifest paths in the offline backup.
$relativeDirectory = Join-Path $releaseDirectory "Modules\ShopCollectionExport\$version"
New-Item -ItemType Directory -Force -Path $relativeDirectory | Out-Null
Copy-Item -LiteralPath $publishedDll -Destination (Join-Path $relativeDirectory 'ShopCollectionExport.dll') -Force
[IO.File]::WriteAllText((Join-Path $releaseDirectory 'TreeHouseModules.json'), $json, [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $moduleDirectory 'README.md') -Destination (Join-Path $releaseDirectory 'README.md') -Force
$assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($releaseDll).Version
if ($assemblyVersion -ne $assemblyExpectedVersion) { throw 'Assembly version mismatch.' }
$packagedManifest = Get-Content -Raw (Join-Path $releaseDirectory 'TreeHouseModules.json') | ConvertFrom-Json
$packagedDll = Join-Path $releaseDirectory $packagedManifest.Modules[0].File
if ($packagedManifest.Modules[0].Version -ne $version -or
    (Get-FileHash -LiteralPath $packagedDll -Algorithm SHA256).Hash -ne $packagedManifest.Modules[0].Sha256 -or
    (Get-FileHash -LiteralPath $releaseDll -Algorithm SHA256).Hash -ne $hash) { throw 'Packaged manifest or DLL mismatch.' }
$archive = "$releaseDirectory.zip"
Compress-Archive -Path (Join-Path $releaseDirectory '*') -DestinationPath $archive -CompressionLevel Optimal -Force
Write-Output "Loading folder: $releaseDirectory"
Write-Output "DLL: $releaseDll"
Write-Output "Assembly: $assemblyVersion; SHA256: $hash"
Write-Output "Backup: $archive"
Write-Output 'Publication files ready; no Git operation performed.'
