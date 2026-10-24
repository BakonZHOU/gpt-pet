param([switch]$Disable)
$ErrorActionPreference = 'Stop'
$petExe = Join-Path $PSScriptRoot 'dist\CodexPet.exe'
$petStartup = Join-Path ([Environment]::GetFolderPath('Startup')) 'Codex 鲸鱼娘联动.lnk'
if ($Disable) {
    if (Test-Path -LiteralPath $petStartup) { Remove-Item -LiteralPath $petStartup }
    if (Test-Path -LiteralPath $petExe) { Start-Process -FilePath $petExe -ArgumentList '--stop-watch' -WindowStyle Hidden -Wait }
    Write-Host '已关闭随GPT启动，桌宠仍可手动打开。'
    exit
}
if (!(Test-Path -LiteralPath $petExe)) { throw '请先运行 build.ps1。' }
$petShell = New-Object -ComObject WScript.Shell
$petShortcut = $petShell.CreateShortcut($petStartup)
$petShortcut.TargetPath = $petExe
$petShortcut.Arguments = '--watch-gpt'
$petShortcut.WorkingDirectory = Split-Path -Parent $petExe
$petShortcut.WindowStyle = 7
$petShortcut.Description = '检测GPT桌面程序，自动开关鲸鱼娘；不调用模型。'
$petShortcut.Save()
Start-Process -FilePath $petExe -ArgumentList '--watch-gpt' -WindowStyle Hidden
Write-Host '已开启随GPT自动开关。'

