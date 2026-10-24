param([switch]$ReuseBuild, [switch]$Rebuild)
$ErrorActionPreference = 'Stop'
$petProjectInfo = [xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\CodexPet\CodexPet.csproj') -Raw)
$petVersion = $petProjectInfo.Project.PropertyGroup.Version
$petRelease = Join-Path $PSScriptRoot "release\CodexWhalePet-$petVersion-win-x64"
if ((Test-Path -LiteralPath $petRelease) -and !$ReuseBuild -and !$Rebuild) { throw "发布目录已存在：$petRelease；请换新版本号或先检查旧产物。" }
New-Item -ItemType Directory -Path $petRelease -Force | Out-Null
if (!$ReuseBuild) {
dotnet publish (Join-Path $PSScriptRoot 'src\CodexPet\CodexPet.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $petRelease --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
}
if (!(Test-Path -LiteralPath (Join-Path $petRelease 'CodexPet.exe'))) { throw '缺少发布程序。' }
foreach ($petAsset in @('Assets/DSniang1.png','Assets/DSniang1-no-ahoge.png','Assets/Skins/Mutsumi.png','Assets/Skins/WhiteDragon.png','语录.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $petRelease $petAsset))) { throw "发布资源缺失：$petAsset" }
}
$petAssets = Get-Content (Join-Path $PSScriptRoot 'src\CodexPet\obj\project.assets.json') -Raw | ConvertFrom-Json
$petLicenses = Join-Path $petRelease 'licenses'
New-Item -ItemType Directory -Path $petLicenses -Force | Out-Null
foreach ($petDependency in $petAssets.project.frameworks.PSObject.Properties.Value.downloadDependencies) {
    if ($petDependency.name -notin @('Microsoft.NETCore.App.Runtime.win-x64', 'Microsoft.WindowsDesktop.App.Runtime.win-x64')) { continue }
    $petRuntimeVersion = (($petDependency.version -replace '[\[\]]','') -split ',')[0].Trim()
    $petFoundLicense = $false
    foreach ($petPackageRoot in $petAssets.packageFolders.PSObject.Properties.Name) {
        $petPackage = Join-Path $petPackageRoot ($petDependency.name.ToLowerInvariant() + '/' + $petRuntimeVersion)
        foreach ($petLicenseName in @('LICENSE','LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
            $petLicenseSource = Join-Path $petPackage $petLicenseName
            if (Test-Path -LiteralPath $petLicenseSource) {
                Copy-Item -LiteralPath $petLicenseSource -Destination (Join-Path $petLicenses ($petDependency.name + '-' + $petLicenseName))
                $petFoundLicense = $true
            }
        }
    }
    if (!$petFoundLicense) { throw "缺少运行时许可：$($petDependency.name)" }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE.whale-widget') -Destination $petRelease
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'packaging\使用说明.txt') -Destination $petRelease
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\SKIN-MUTSUMI.txt') -Destination $petRelease
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\SKIN-WHITEDRAGON.txt') -Destination $petRelease
[IO.File]::WriteAllText((Join-Path $petRelease 'version.txt'), $petVersion + "`r`n", [Text.Encoding]::ASCII)
$petSetup = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'packaging\setup.ps1') -Raw -Encoding utf8
[IO.File]::WriteAllText((Join-Path $petRelease 'setup.ps1'), $petSetup, [Text.UTF8Encoding]::new($true))
foreach ($petEntry in @(@('安装.cmd','Install'), @('卸载.cmd','Uninstall'), @('开启随GPT启动.cmd','Enable'), @('关闭随GPT启动.cmd','Disable'))) {
    $petCmd = '@echo off' + "`r`n" + 'powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1" -Action ' + $petEntry[1] + "`r`npause`r`n"
    [IO.File]::WriteAllText((Join-Path $petRelease $petEntry[0]), $petCmd, [Text.Encoding]::ASCII)
}
[IO.File]::WriteAllText((Join-Path $petRelease '直接启动.cmd'), "@echo off`r`nstart `"`" `"%~dp0CodexPet.exe`" --show`r`n", [Text.Encoding]::ASCII)
[IO.File]::WriteAllText((Join-Path $petRelease '关闭桌宠.cmd'), "@echo off`r`nstart `"`" `"%~dp0CodexPet.exe`" --quit`r`n", [Text.Encoding]::ASCII)
$petZip = "$petRelease.zip"
Compress-Archive -LiteralPath $petRelease -DestinationPath $petZip -CompressionLevel Optimal -Force
$petHash = Get-FileHash -LiteralPath $petZip -Algorithm SHA256
[IO.File]::WriteAllText("$petZip.sha256", $petHash.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($petZip) + "`n", [Text.Encoding]::ASCII)
Write-Host "Ready: $petZip"
