$ErrorActionPreference='Stop'
$petManifest=Get-Content plugin.json -Raw|ConvertFrom-Json
$petStage=Join-Path $PSScriptRoot 'artifacts/codex-pet-app'
if(!(Test-Path -LiteralPath ui.bundle.js) -or !(Test-Path -LiteralPath runtime/node.exe)){throw 'Run npm ci and npm run build first.'}
New-Item -ItemType Directory -Path $petStage -Force|Out-Null
foreach($petName in @('plugin.json','mcp.json','server.cjs','quota.cjs','core.mjs','ui.js','ui.html','ui.bundle.js','quotes.txt','README.md','LICENSE','LICENSE.whale-widget','MCP-APPS-LICENSE.txt','assets','licenses','runtime','docs')){
 if(Test-Path -LiteralPath $petName){Copy-Item -LiteralPath $petName -Destination $petStage -Recurse -Force}
}
$petZip=Join-Path $PSScriptRoot ('artifacts/CodexPet-App-'+$petManifest.version+'-Windows-plugin.zip')
Compress-Archive -LiteralPath $petStage -DestinationPath $petZip -Force
((Get-FileHash -LiteralPath $petZip -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($petZip))|Set-Content -LiteralPath ($petZip+'.sha256') -Encoding ascii
Write-Host $petZip