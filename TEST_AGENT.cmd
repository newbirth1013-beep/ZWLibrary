@echo off
setlocal
pushd "%~dp0"
if not exist "output\agent-tests" mkdir "output\agent-tests"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /noconfig /target:exe /out:output\agent-tests\AgentTests.exe /reference:System.dll /reference:System.Core.dll /reference:System.Security.dll /reference:System.Web.dll /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll src\ChatGptLogin.cs src\AgentApi.cs tests\AgentTests.cs
if errorlevel 1 exit /b 1
output\agent-tests\AgentTests.exe
set "TEST_RESULT=%ERRORLEVEL%"
popd
exit /b %TEST_RESULT%
