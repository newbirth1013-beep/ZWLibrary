$ErrorActionPreference='Stop'
$root = $PSScriptRoot
$checkRoot=Join-Path $root 'output\host-check'
New-Item -ItemType Directory -Force $checkRoot | Out-Null
$ref='C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$cad='C:\Program Files\ZWSOFT\ZWCAD 2024'
$sources = Get-ChildItem -LiteralPath "$root/src" -Filter '*.cs' | ForEach-Object FullName
$testDll=Join-Path $checkRoot 'ZWLibrary.HostChecks.dll'
$argsList=@('/nologo','/noconfig','/nostdlib+','/target:library','/platform:x64',"/out:$testDll")
foreach($name in @('mscorlib','System','System.Core','System.Xml','System.Net.Http','System.Web.Extensions','System.Security','System.Web','System.Drawing','System.Windows.Forms','WindowsBase','PresentationCore','PresentationFramework')){$argsList+="/reference:$ref\$name.dll"}
$argsList+="/reference:$cad\ZwManaged.dll"; $argsList+="/reference:$cad\ZwDatabaseMgd.dll";$argsList+=$sources;$argsList+=(Join-Path $root 'tests\AgentHostTests.cs')
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' @argsList
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /noconfig /target:library /platform:x64 "/out:$checkRoot\HostCheckLoader.dll" "/reference:$cad\ZwManaged.dll" "/reference:$cad\ZwDatabaseMgd.dll" /reference:System.dll /reference:System.Core.dll "$root\tools\HostCheckLoader.cs"
$testDll,(Join-Path $checkRoot 'cad-memory-test.txt') | Set-Content -Encoding UTF8 (Join-Path $checkRoot 'HostCheckLoader.txt')
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}

