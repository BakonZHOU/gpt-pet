$ErrorActionPreference = 'Stop'
# Isolate filesystem/shortcuts and stub process commands; never stop the user's GPT/pet.
$petRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'work\package-smoke'
$petPayload = Join-Path $petRoot 'payload'
$petLocal = Join-Path $petRoot 'local'
$petStartup = Join-Path $petRoot 'startup'
$petDesktop = Join-Path $petRoot 'desktop'
foreach ($petFolder in @($petPayload,$petLocal,$petStartup,$petDesktop)) { New-Item -ItemType Directory -Path $petFolder -Force | Out-Null }
$petScript = Get-Content (Join-Path (Split-Path -Parent $PSScriptRoot) 'packaging\setup.ps1') -Raw -Encoding utf8
$petScript = $petScript.Replace("[Environment]::GetFolderPath('LocalApplicationData')", "'$petLocal'").Replace("[Environment]::GetFolderPath('Startup')", "'$petStartup'").Replace("[Environment]::GetFolderPath('DesktopDirectory')", "'$petDesktop'").Replace('$env:LOCALAPPDATA', "'$petLocal'")
$petScript = $petScript.Replace("`$ErrorActionPreference = 'Stop'", "`$ErrorActionPreference = 'Stop'`r`nfunction Start-Process { } ")
$petTestRegistry = 'HKCU:\Software\CodexWhalePetTests\PackageSmoke'
$petScript = $petScript.Replace('HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexWhalePet', $petTestRegistry)
$petFixture = Join-Path $petPayload 'setup.ps1'
[IO.File]::WriteAllText($petFixture,$petScript,[Text.UTF8Encoding]::new($true))
Set-Content (Join-Path $petPayload 'CodexPet.exe') -Value 'fixture'
Set-Content (Join-Path $petPayload '语录.txt') -Value 'original'
$petProject = [xml](Get-Content (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\CodexPet\CodexPet.csproj') -Raw)
$petTestVersion = $petProject.Project.PropertyGroup.Version
Set-Content (Join-Path $petPayload 'version.txt') -Value $petTestVersion
function Invoke-Fixture([string]$Action) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $petFixture -Action $Action
    if ($LASTEXITCODE -ne 0) { throw "Fixture failed: $Action" }
}
function Check([bool]$Condition,[string]$Description) {
    if (!$Condition) { throw "FAILED: $Description" }
    Write-Host "PASS $Description"
}
$petInstall = Join-Path $petLocal 'Programs\CodexWhalePet'
Invoke-Fixture Install
Check ((Test-Path -LiteralPath (Join-Path $petInstall 'CodexPet.exe')) -and @(Get-ChildItem $petDesktop -Filter '*.lnk').Count -eq 1 -and @(Get-ChildItem $petStartup -Filter '*.lnk').Count -eq 1) 'Install creates app and per-user shortcuts in isolated directories'
$petRegistration = Get-ItemProperty -LiteralPath $petTestRegistry
Check ($petRegistration.DisplayName -eq '鲸鱼娘桌宠' -and $petRegistration.DisplayVersion -eq $petTestVersion -and $petRegistration.NoModify -eq 1 -and $petRegistration.EstimatedSize -gt 0) 'Windows Apps registration contains display name, version, size and uninstall-only options'
Check ($petRegistration.UninstallString.Contains((Join-Path $petInstall 'setup.ps1')) -and !$petRegistration.UninstallString.Contains($petPayload) -and $petRegistration.UninstallString.EndsWith('-Action Uninstall')) 'System uninstall points to installed script and survives deleting the download folder'
Set-Content (Join-Path $petInstall '语录.txt') -Value 'custom'
Invoke-Fixture Install
Check ((Get-Content (Join-Path $petInstall '语录.txt') -Raw).Trim() -eq 'custom') 'Upgrade preserves edited quotes'
Invoke-Fixture Disable
Check (@(Get-ChildItem $petStartup -Filter '*.lnk').Count -eq 0) 'Disable removes auto-start shortcut'
Invoke-Fixture Enable
Check (@(Get-ChildItem $petStartup -Filter '*.lnk').Count -eq 1) 'Enable restores auto-start shortcut'
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $petInstall 'setup.ps1') -Action Uninstall
if ($LASTEXITCODE -ne 0) { throw 'Installed uninstaller failed' }
Check (!(Test-Path -LiteralPath $petInstall) -and @(Get-ChildItem $petDesktop -Filter '*.lnk').Count -eq 0 -and @(Get-ChildItem $petStartup -Filter '*.lnk').Count -eq 0) 'Uninstall removes only app directory and owned shortcuts'
Check (!(Test-Path -LiteralPath $petTestRegistry)) 'Uninstall also removes Windows Apps registration'
