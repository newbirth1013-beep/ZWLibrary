@echo off
setlocal
pushd "%~dp0"
set "ZWLIB_FRAMEWORK=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
set "ZWLIB_REF=%ProgramFiles(x86)%\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8"
set "ZWLIB_CAD=%~1"
if not defined ZWLIB_CAD set "ZWLIB_CAD=C:\Program Files\ZWSOFT\ZWCAD 2024"
set "ZWLIB_OUT=output\v0.8.4-ai.1"
if not exist "%ZWLIB_OUT%\plugin" mkdir "%ZWLIB_OUT%\plugin"
if not exist "%ZWLIB_OUT%\build" mkdir "%ZWLIB_OUT%\build"
set "ZWLIB_LOG=%ZWLIB_OUT%\build.log"
>"%ZWLIB_LOG%" echo ZWLibrary v0.8.4-ai.1 / .NET Framework 4.8 / x64
if not exist "%ZWLIB_FRAMEWORK%\csc.exe" goto missing
if not exist "%ZWLIB_REF%\mscorlib.dll" goto missing
if not exist "%ZWLIB_REF%\PresentationFramework.dll" goto missing
if not exist "%ZWLIB_CAD%\ZwManaged.dll" goto missing
if not exist "%ZWLIB_CAD%\ZwDatabaseMgd.dll" goto missing
set ZWLIB_BASE=/noconfig /nostdlib+ /reference:"%ZWLIB_REF%\mscorlib.dll" /reference:"%ZWLIB_REF%\System.dll" /reference:"%ZWLIB_REF%\System.Core.dll" /reference:"%ZWLIB_REF%\System.Xml.dll" /reference:"%ZWLIB_REF%\System.Net.Http.dll" /reference:"%ZWLIB_REF%\System.Web.Extensions.dll" /reference:"%ZWLIB_REF%\System.Security.dll" /reference:"%ZWLIB_REF%\System.Web.dll"
"%ZWLIB_FRAMEWORK%\csc.exe" /nologo %ZWLIB_BASE% /target:exe /platform:x64 /out:"%ZWLIB_OUT%\build\CoreTests.exe" src\Core.cs src\Reporting.cs tests\CoreTests.cs tests\StructureTests.cs tests\FeatureTests.cs >>"%ZWLIB_LOG%" 2>&1
if errorlevel 1 goto failed
"%ZWLIB_OUT%\build\CoreTests.exe" >>"%ZWLIB_LOG%" 2>&1
if errorlevel 1 goto failed
"%ZWLIB_FRAMEWORK%\csc.exe" /nologo %ZWLIB_BASE% /target:library /platform:x64 /optimize+ /out:"%ZWLIB_OUT%\build\ZWLibrary.dll" /reference:"%ZWLIB_REF%\System.Drawing.dll" /reference:"%ZWLIB_REF%\System.Windows.Forms.dll" /reference:"%ZWLIB_REF%\WindowsBase.dll" /reference:"%ZWLIB_REF%\PresentationCore.dll" /reference:"%ZWLIB_REF%\PresentationFramework.dll" /reference:"%ZWLIB_CAD%\ZwManaged.dll" /reference:"%ZWLIB_CAD%\ZwDatabaseMgd.dll" src\*.cs >>"%ZWLIB_LOG%" 2>&1
if errorlevel 1 goto failed
if exist "%ZWLIB_OUT%\plugin\ZWLibrary.dll" copy /y "%ZWLIB_OUT%\plugin\ZWLibrary.dll" "%ZWLIB_OUT%\build\ZWLibrary.previous.dll" >>"%ZWLIB_LOG%" 2>&1
if errorlevel 1 goto failed
copy /y "%ZWLIB_OUT%\build\ZWLibrary.dll" "%ZWLIB_OUT%\plugin\ZWLibrary.dll" >>"%ZWLIB_LOG%" 2>&1
if errorlevel 1 goto failed
echo BUILD SUCCESS >>"%ZWLIB_LOG%"
type "%ZWLIB_LOG%"
echo DLL: %ZWLIB_OUT%\plugin\ZWLibrary.dll
if /i not "%~2"=="--no-pause" pause
popd
exit /b 0
:missing
echo Missing .NET 4.8 reference assemblies, compiler or ZWCAD DLL. >>"%ZWLIB_LOG%"
echo ZWCAD folder: %ZWLIB_CAD% >>"%ZWLIB_LOG%"
:failed
echo BUILD FAILED >>"%ZWLIB_LOG%"
type "%ZWLIB_LOG%"
if /i not "%~2"=="--no-pause" pause
popd
exit /b 1
