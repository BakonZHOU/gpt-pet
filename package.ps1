param([switch]$ReuseBuild)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'build.ps1') -ReuseBuild:$ReuseBuild
$petVersion=(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'plugin.json') -Raw|ConvertFrom-Json).version
$petStage=Join-Path $PSScriptRoot ('artifacts/plugin-'+$petVersion)
$petZip=Join-Path $PSScriptRoot ('artifacts/CodexPet-Plugin-'+$petVersion+'-Windows.zip')
if(Test-Path -LiteralPath $petZip){Remove-Item -LiteralPath $petZip}
[IO.Compression.ZipFile]::CreateFromDirectory($petStage,$petZip,[IO.Compression.CompressionLevel]::Optimal,$false)
((Get-FileHash -LiteralPath $petZip -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($petZip))|Set-Content -LiteralPath ($petZip+'.sha256') -Encoding ascii
Write-Output ('PLUGIN_ZIP '+$petZip)
