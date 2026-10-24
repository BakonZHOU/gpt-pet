param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$petProject = Join-Path $PSScriptRoot 'src\CodexPet\CodexPet.csproj'
$petOutput = Join-Path $PSScriptRoot 'dist'
if ($SelfContained) {
    dotnet publish $petProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $petOutput --nologo
} else {
    dotnet publish $petProject -c Release --self-contained false -o $petOutput --nologo
}
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE.whale-widget'),(Join-Path $PSScriptRoot 'README.md') -Destination $petOutput
Write-Host "Ready: $petOutput\CodexPet.exe"
