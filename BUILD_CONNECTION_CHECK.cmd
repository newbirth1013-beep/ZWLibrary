@echo off
setlocal
pushd "%~dp0"
if not exist output\connection-check mkdir output\connection-check
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /noconfig /target:winexe /out:output\connection-check\ConnectionCheck-TLS.exe /reference:System.dll /reference:System.Core.dll /reference:System.Security.dll /reference:System.Web.dll /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll src\ChatGptLogin.cs src\AgentApi.cs tools\ConnectionCheck.cs
set "BUILD_RESULT=%ERRORLEVEL%"
popd
exit /b %BUILD_RESULT%
