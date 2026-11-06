@echo off
setlocal
call "%~dp0BUILD_WINDOWS.cmd" "%~1" --no-pause
if errorlevel 1 exit /b 1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0UPDATE_RUNTIME.ps1"
if errorlevel 1 exit /b 1
echo BUILD AND UPDATE SUCCESS
