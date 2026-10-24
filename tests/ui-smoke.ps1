$ErrorActionPreference='Stop'
$petRoot=Split-Path $PSScriptRoot -Parent
$petTemporary=Join-Path ([IO.Path]::GetTempPath()) ('pet-plugin-ui-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $petTemporary -Force|Out-Null
$petResult=Join-Path $petTemporary 'ui.txt'
$petInfo=[Diagnostics.ProcessStartInfo]::new()
$petInfo.FileName=Join-Path $petRoot 'native/CodexPetPlugin.exe'
$petInfo.UseShellExecute=$false
$petInfo.CreateNoWindow=$true
foreach($petArgument in @('--plugin-root',$petRoot,'--plugin-id','ui-smoke@test','--data-dir',(Join-Path $petTemporary 'data'),'--ui-test',$petResult)){$petInfo.ArgumentList.Add($petArgument)}
$petProcess=[Diagnostics.Process]::Start($petInfo)
try{
 if(!$petProcess.WaitForExit(30000)){throw 'Native UI timeout'}
 $petChecks=Get-Content -LiteralPath $petResult
 if($petProcess.ExitCode -ne 0 -or @($petChecks|Where-Object {$_ -notlike 'PASS *'}).Count){$petChecks|Write-Output;throw 'Native UI regression failed'}
 Write-Output ('PASS native UI: '+$petChecks.Count+' checks; three-click cycle, skins, bubbles, quotas and taskbar movement')
}finally{if(!$petProcess.HasExited){$petProcess.Kill($true)};$petProcess.Dispose()}
