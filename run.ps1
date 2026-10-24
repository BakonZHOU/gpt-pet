$ErrorActionPreference = 'Stop'
$petExe = Join-Path $PSScriptRoot 'dist\CodexPet.exe'
if (-not (Test-Path -LiteralPath $petExe)) { & (Join-Path $PSScriptRoot 'build.ps1') }
Start-Process -FilePath $petExe -WorkingDirectory (Split-Path $petExe) -WindowStyle Normal
