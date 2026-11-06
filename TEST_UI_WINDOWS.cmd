@echo off
setlocal
pushd "%~dp0"
if not exist "output\v0.8.4\ui-check" mkdir "output\v0.8.4\ui-check"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /platform:x64 /out:output\v0.8.4\ui-check\UiSmokeTests.exe /reference:System.Drawing.dll /reference:System.Windows.Forms.dll src\Core.cs src\Reporting.cs src\PartsPanel.cs src\CatalogUI.cs src\GeometryPanel.cs src\PreviewCanvas.cs src\SettingsDialog.cs tests\UiSmokeTests.cs
if errorlevel 1 goto failed
output\v0.8.4\ui-check\UiSmokeTests.exe output\v0.8.4\ui-check >output\v0.8.4\ui-check\ui-tests.log 2>&1
if errorlevel 1 goto failed
type output\v0.8.4\ui-check\ui-tests.log
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\UpdateRuntimeTests.ps1
if errorlevel 1 goto failed
popd
exit /b 0
:failed
popd
exit /b 1
