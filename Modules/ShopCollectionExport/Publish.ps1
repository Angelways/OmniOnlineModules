param(
    [string]$OmniPath = 'D:\XIVLauncherCN\installedPlugins\OmniToolbox\1.1.8.1',
    [string]$DalamudPath = 'D:\XIVLauncherCN\Addon\Hooks\26-10-21-01'
)
$ErrorActionPreference = 'Stop'
$moduleDirectory = $PSScriptRoot
$projectFile = Join-Path $moduleDirectory 'Source\OmniShopOwned.csproj'
[xml]$projectXml = Get-Content -Raw -LiteralPath $projectFile
$version = [string]$projectXml.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Invalid module version.' }
dotnet build $projectFile -c Release --nologo "-p:OmniPath=$OmniPath" "-p:DalamudPath=$DalamudPath"
if ($LASTEXITCODE -ne 0) { throw 'Module build failed.' }
$builtDll = Join-Path $moduleDirectory 'Source\bin\Release\net10.0-windows\OmniShopOwned.dll'
$versionDirectory = Join-Path $moduleDirectory $version
$publishedDll = Join-Path $versionDirectory 'OmniShopOwned.dll'
$hash = (Get-FileHash -LiteralPath $builtDll -Algorithm SHA256).Hash
if ((Test-Path -LiteralPath $publishedDll) -and
    (Get-FileHash -LiteralPath $publishedDll -Algorithm SHA256).Hash -ne $hash) {
    throw 'This version already has different bytes. Increase the project version before publishing.'
}
New-Item -ItemType Directory -Force -Path $versionDirectory | Out-Null
Copy-Item -LiteralPath $builtDll -Destination $publishedDll -Force
$manifest = [ordered]@{ SchemaVersion = 1; Modules = @([ordered]@{
    InternalName = 'ShopCollectionExport'
    Name = '商城物品收录导出'
    Author = 'Angelways'
    Description = '按角色导出库存与已解锁收录，供商城已获取标记油猴脚本导入。'
    Version = $version
    MinimumOmniVersion = '1.1.8.1'
    File = "$version/OmniShopOwned.dll"
    Sha256 = $hash
}) }
$json = ($manifest | ConvertTo-Json -Depth 5) + "`n"
[IO.File]::WriteAllText((Join-Path $moduleDirectory 'TreeHouseModules.json'), $json, [Text.UTF8Encoding]::new($false))
Write-Output "Version: $version"
Write-Output "SHA256: $hash"
Write-Output 'Local publication files ready; no Git operation performed.'
