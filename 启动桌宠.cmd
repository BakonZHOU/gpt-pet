@echo off
cd /d "%~dp0"
if exist "dist\CodexPet.exe" (
    start "" "dist\CodexPet.exe"
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1"
)
