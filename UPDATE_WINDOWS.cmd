@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0UPDATE_RUNTIME.ps1"
if errorlevel 1 (
  echo UPDATE FAILED
  pause
  exit /b 1
)
pause
