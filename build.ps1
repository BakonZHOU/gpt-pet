param([switch]$ReuseBuild)
$ErrorActionPreference='Stop'
$petRoot=$PSScriptRoot
$petManifest=Get-Content -LiteralPath (Join-Path $petRoot 'plugin.json') -Raw|ConvertFrom-Json
$petProject=Join-Path $petRoot 'native-src/CodexPetPlugin.csproj'
$petNative=Join-Path $petRoot 'native'
if(!$ReuseBuild){
 dotnet publish $petProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $petNative --nologo
 if($LASTEXITCODE -ne 0){throw 'Native plugin build failed'}
}
foreach($petRequired in @('CodexPetPlugin.exe','Assets/DSniang1.png','Assets/Skins/Mutsumi.png','Assets/Skins/WhiteDragon.png')){
 if(!(Test-Path -LiteralPath (Join-Path $petNative $petRequired))){throw ('Missing native asset: '+$petRequired)}
}
$petStage=Join-Path $petRoot ('artifacts/plugin-'+$petManifest.version)
New-Item -ItemType Directory -Path $petStage -Force|Out-Null
foreach($petName in @('plugin.json','mcp.json','quotes.txt','README.md','LICENSE','LICENSE.whale-widget')){
 if(Test-Path -LiteralPath (Join-Path $petRoot $petName)){Copy-Item -LiteralPath (Join-Path $petRoot $petName) -Destination (Join-Path $petStage $petName) -Force}
}
New-Item -ItemType Directory -Path (Join-Path $petStage 'native') -Force|Out-Null
Copy-Item -Path (Join-Path $petNative '*') -Destination (Join-Path $petStage 'native') -Recurse -Force
New-Item -ItemType Directory -Path (Join-Path $petStage 'assets/Skins') -Force|Out-Null
Copy-Item -LiteralPath (Join-Path $petRoot 'assets/Skins/WhiteDragon.png') -Destination (Join-Path $petStage 'assets/Skins/WhiteDragon.png') -Force
if(Test-Path -LiteralPath (Join-Path $petRoot 'docs')){Copy-Item -LiteralPath (Join-Path $petRoot 'docs') -Destination $petStage -Recurse -Force}
$petAssets=Get-Content -LiteralPath (Join-Path $petRoot 'native-src/obj/project.assets.json') -Raw|ConvertFrom-Json
$petLicenses=Join-Path $petStage 'licenses'
New-Item -ItemType Directory -Path $petLicenses -Force|Out-Null
foreach($petDependency in $petAssets.project.frameworks.PSObject.Properties.Value.downloadDependencies){
 if($petDependency.name -notin @('Microsoft.NETCore.App.Runtime.win-x64','Microsoft.WindowsDesktop.App.Runtime.win-x64')){continue}
 $petRuntimeVersion=(($petDependency.version -replace '[\[\]]','') -split ',')[0].Trim()
 $petFound=$false
 foreach($petPackageRoot in $petAssets.packageFolders.PSObject.Properties.Name){
  $petPackage=Join-Path $petPackageRoot ($petDependency.name.ToLowerInvariant()+'/'+$petRuntimeVersion)
  foreach($petLicense in @('LICENSE','LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')){
   $petSource=Join-Path $petPackage $petLicense
   if(Test-Path -LiteralPath $petSource){Copy-Item -LiteralPath $petSource -Destination (Join-Path $petLicenses ($petDependency.name+'-'+$petLicense)) -Force;$petFound=$true}
  }
 }
 if(!$petFound){throw ('Missing runtime license: '+$petDependency.name)}
}
Write-Output ('PLUGIN_STAGE '+$petStage)
