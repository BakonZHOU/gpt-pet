$ErrorActionPreference='Stop'
$petRoot=Split-Path $PSScriptRoot -Parent
$petTemporary=Join-Path ([IO.Path]::GetTempPath()) ('pet-plugin-mcp-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $petTemporary -Force|Out-Null
[IO.File]::WriteAllText((Join-Path $petTemporary 'config.toml'),"[plugins.`"mcp-smoke@test`"]`nenabled = false`n")
$petInfo=[Diagnostics.ProcessStartInfo]::new()
$petInfo.FileName=Join-Path $petRoot 'native/CodexPetPlugin.exe'
$petInfo.UseShellExecute=$false
$petInfo.CreateNoWindow=$true
$petInfo.RedirectStandardInput=$true
$petInfo.RedirectStandardOutput=$true
$petInfo.RedirectStandardError=$true
$petInfo.Environment['CODEX_HOME']=$petTemporary
foreach($petArgument in @('--mcp','--plugin-root',$petRoot,'--plugin-id','mcp-smoke@test','--data-dir',(Join-Path $petTemporary 'data'))){$petInfo.ArgumentList.Add($petArgument)}
$petProcess=[Diagnostics.Process]::Start($petInfo)
$petRequestId=0
function Invoke-PetRequest([string]$Method,$Parameters=@{}){
 $script:petRequestId++
 $petProcess.StandardInput.WriteLine((@{jsonrpc='2.0';id=$script:petRequestId;method=$Method;params=$Parameters}|ConvertTo-Json -Depth 12 -Compress))
 $petProcess.StandardInput.Flush()
 $petRead=$petProcess.StandardOutput.ReadLineAsync()
 if(!$petRead.Wait(15000)){throw ('MCP timeout: '+$Method)}
 if(!$petRead.Result){throw ('MCP closed: '+$Method)}
 $petResult=$petRead.Result|ConvertFrom-Json
 if($petResult.error){throw $petResult.error.message}
 return $petResult.result
}
try{
 $petManifest=Get-Content -LiteralPath (Join-Path $petRoot 'plugin.json') -Raw|ConvertFrom-Json
 $petInit=Invoke-PetRequest initialize @{protocolVersion='2025-06-18';capabilities=@{};clientInfo=@{name='native-plugin-smoke';version='1'}}
 if($petInit.serverInfo.version -ne $petManifest.version){throw 'Manifest/server version mismatch'}
 $petTools=Invoke-PetRequest 'tools/list'
 if(@($petTools.tools).Count -ne 2 -or !($petTools.tools.name -contains 'pet_show')){throw 'Wrong plugin tools'}
 $petResources=Invoke-PetRequest 'resources/list'
 if(@($petResources.resources).Count -ne 0){throw 'Old browser control panel still registered'}
 $petStatus=Invoke-PetRequest 'tools/call' @{name='pet_status';arguments=@{}}
 if($petStatus.structuredContent.running){throw 'Disabled plugin launched a window'}
 $petTransport=Get-Content -LiteralPath (Join-Path $petRoot 'mcp.json') -Raw|ConvertFrom-Json
 if($petTransport.mcpServers.petapp.command -ne './native/CodexPetPlugin.exe'){throw 'Plugin must launch its bundled runtime'}
 $petHooks=Get-Content -LiteralPath (Join-Path $petRoot 'hooks-windows/hooks.json') -Raw|ConvertFrom-Json
 $petHookCommand=$petHooks.hooks.SessionStart[0].hooks[0].command
 $petPromptCommand=$petHooks.hooks.UserPromptSubmit[0].hooks[0].command
 if($petManifest.extensions.'com.openai'.hooks -ne './hooks-windows/hooks.json' -or $petHookCommand -notmatch '--ensure' -or $petPromptCommand -ne $petHookCommand){throw 'Session and prompt hooks are not wired to the same non-intrusive native launcher'}
 Write-Output 'PASS native stdio MCP, matching version, self-healing hooks, own-instance tools, no control panel and no startup while disabled'
}finally{
 $petProcess.StandardInput.Close()
 if(!$petProcess.WaitForExit(5000)){$petProcess.Kill($true)}
 $petProcess.Dispose()
}
