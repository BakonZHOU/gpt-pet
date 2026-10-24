param([ValidateSet('Install','Uninstall','Enable','Disable')][string]$Action = 'Install')
$ErrorActionPreference = 'Stop'
$petDestination = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\CodexWhalePet'
$petExe = Join-Path $petDestination 'CodexPet.exe'
$petStartup = Join-Path ([Environment]::GetFolderPath('Startup')) 'Codex 鲸鱼娘联动.lnk'
$petDesktop = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) '鲸鱼娘桌宠.lnk'
$petRegistry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexWhalePet'

function Stop-Pet {
    if (!(Test-Path -LiteralPath $petExe)) { return }
    Start-Process -FilePath $petExe -ArgumentList '--stop-watch' -WindowStyle Hidden -Wait
    Start-Process -FilePath $petExe -ArgumentList '--quit' -WindowStyle Hidden -Wait
    for ($petAttempt = 0; $petAttempt -lt 20; $petAttempt++) {
        $petRunning = @(Get-Process -Name CodexPet -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $petExe })
        if ($petRunning.Count -eq 0) { return }
        Start-Sleep -Milliseconds 250
    }
    throw '桌宠尚未退出，请关闭后重试。'
}
function Set-PetShortcut([string]$ShortcutPath, [string]$Arguments) {
    $petShell = New-Object -ComObject WScript.Shell
    $petLink = $petShell.CreateShortcut($ShortcutPath)
    $petLink.TargetPath = $petExe
    $petLink.Arguments = $Arguments
    $petLink.WorkingDirectory = $petDestination
    $petLink.WindowStyle = 7
    $petLink.Save()
}
function Register-PetUninstaller {
    $petVersion = (Get-Content -LiteralPath (Join-Path $petDestination 'version.txt') -Raw).Trim()
    if ($petVersion -notmatch '^\d+\.\d+\.\d+$') { throw '发布版本号无效。' }
    $petPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $petUninstall = '"' + $petPowerShell + '" -NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $petDestination 'setup.ps1') + '" -Action Uninstall'
    New-Item -Path $petRegistry -Force | Out-Null
    foreach ($petValue in @{
        DisplayName = '鲸鱼娘桌宠'
        DisplayVersion = $petVersion
        Publisher = '鲸鱼娘桌宠项目（非官方）'
        InstallLocation = $petDestination
        DisplayIcon = $petExe + ',0'
        UninstallString = $petUninstall
        InstallDate = (Get-Date -Format yyyyMMdd)
    }.GetEnumerator()) {
        New-ItemProperty -LiteralPath $petRegistry -Name $petValue.Key -Value $petValue.Value -PropertyType String -Force | Out-Null
    }
    $petKilobytes = [int][Math]::Ceiling((Get-ChildItem -LiteralPath $petDestination -File -Recurse | Measure-Object Length -Sum).Sum / 1KB)
    New-ItemProperty -LiteralPath $petRegistry -Name EstimatedSize -Value $petKilobytes -PropertyType DWord -Force | Out-Null
    New-ItemProperty -LiteralPath $petRegistry -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -LiteralPath $petRegistry -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null
}
if ($Action -eq 'Uninstall') {
    Stop-Pet
    foreach ($petShortcut in @($petStartup, $petDesktop)) {
        if (Test-Path -LiteralPath $petShortcut) {
            $petLink = (New-Object -ComObject WScript.Shell).CreateShortcut($petShortcut)
            if ($petLink.TargetPath -eq $petExe) { Remove-Item -LiteralPath $petShortcut }
        }
    }
    # Delete only this fixed, per-user application directory. Keep user preferences.
    $petResolved = [IO.Path]::GetFullPath($petDestination)
    $petExpected = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\CodexWhalePet'))
    if ($petResolved -ne $petExpected) { throw '安装目录校验失败。' }
    if (Test-Path -LiteralPath $petResolved) { Remove-Item -LiteralPath $petResolved -Recurse -Force }
    if (Test-Path -LiteralPath $petRegistry) { Remove-Item -LiteralPath $petRegistry }
    Write-Host '已卸载。个人位置设置保留在 %LOCALAPPDATA%\CodexPet。'
    exit
}
if ($Action -eq 'Disable') {
    if (Test-Path -LiteralPath $petStartup) {
        $petLink = (New-Object -ComObject WScript.Shell).CreateShortcut($petStartup)
        if ($petLink.TargetPath -eq $petExe) { Remove-Item -LiteralPath $petStartup }
    }
    if (Test-Path -LiteralPath $petExe) { Start-Process -FilePath $petExe -ArgumentList '--stop-watch' -WindowStyle Hidden -Wait }
    Write-Host '已关闭自动联动，仍可从桌面快捷方式手动打开。'
    exit
}
if ($Action -eq 'Install') {
    if ([IO.Path]::GetFullPath($PSScriptRoot) -eq [IO.Path]::GetFullPath($petDestination)) {
        throw '这里已经是安装目录，请使用桌面快捷方式。'
    }
    if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'CodexPet.exe'))) { throw '请先完整解压发布包。' }
    # Also migrate the previously running portable watcher, if any.
    $petSourceExe = Join-Path $PSScriptRoot 'CodexPet.exe'
    Start-Process -FilePath $petSourceExe -ArgumentList '--stop-watch' -WindowStyle Hidden -Wait
    Start-Process -FilePath $petSourceExe -ArgumentList '--quit' -WindowStyle Hidden -Wait
    Stop-Pet
    Start-Sleep -Milliseconds 500
    New-Item -Path $petDestination -ItemType Directory -Force | Out-Null
    Get-ChildItem -LiteralPath $PSScriptRoot | ForEach-Object {
        # Preserve installed quotes when updating.
        if ($_.Name -eq '语录.txt' -and (Test-Path -LiteralPath (Join-Path $petDestination '语录.txt'))) { return }
        Copy-Item -LiteralPath $_.FullName -Destination $petDestination -Recurse -Force
    }
    Set-PetShortcut $petDesktop '--show'
    Register-PetUninstaller
}
if (!(Test-Path -LiteralPath $petExe)) { throw '请先运行安装.cmd。' }
Set-PetShortcut $petStartup '--watch-gpt'
Start-Process -FilePath $petExe -ArgumentList '--watch-gpt' -WindowStyle Hidden
Write-Host '已开启随GPT自动开关。GPT完全退出后约2–4秒关闭桌宠；最小化或留在托盘不算退出。'
