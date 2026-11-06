[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$SourceDirectory,
    [string]$RuntimeDirectory
)
$ErrorActionPreference = 'Stop'
$scriptBase = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $SourceDirectory) { $SourceDirectory = Join-Path $scriptBase 'output/v0.8.4/plugin' }
if (-not $RuntimeDirectory) { $RuntimeDirectory = Join-Path $scriptBase 'runtime' }
$source = [IO.Path]::GetFullPath((Join-Path $SourceDirectory 'ZWLibrary.dll'))
$targetRoot = [IO.Path]::GetFullPath($RuntimeDirectory)
$target = Join-Path $targetRoot 'ZWLibrary.dll'
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Build DLL not found: $source" }
if ($source -eq $target) { throw 'Source and runtime DLL must be different files.' }
$assembly = [Reflection.AssemblyName]::GetAssemblyName($source)
if ($assembly.Name -ne 'ZWLibrary' -or $assembly.ProcessorArchitecture -ne 'Amd64') { throw 'Expected ZWLibrary x64 managed DLL.' }
if (Get-Process -Name 'ZWCAD' -ErrorAction SilentlyContinue) { throw 'Close all ZWCAD windows before updating. No runtime files were changed.' }
if (-not $PSCmdlet.ShouldProcess($targetRoot, "Install ZWLibrary $($assembly.Version) and startup loader")) { return }
New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null
$temporary = Join-Path $targetRoot ([Guid]::NewGuid().ToString('N') + '.tmp')
try {
    Copy-Item -LiteralPath $source -Destination $temporary
    if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $temporary).Hash) { throw 'DLL copy verification failed.' }
    if (Test-Path -LiteralPath $target) {
        $backupRoot = Join-Path $targetRoot 'backups'
        New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
        $backup = Join-Path $backupRoot ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N') + '.dll')
        [IO.File]::Replace($temporary, $target, $backup)
    } else { [IO.File]::Move($temporary, $target) }
} finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
$lispPath = $target.Replace('\', '/').Replace('"', '\"')
$loader = @'
; ZWLibrary startup loader. Register this LSP in APPLOAD Startup Suite once.
(vl-load-com)
(defun zwlib-startup-load (/ dll result loaded)
  (setq dll "@DLL@")
  (cond
    ((not (findfile dll)) (princ "\nZWLibrary DLL missing. Run UPDATE_WINDOWS.cmd."))
    ((equal (vl-bb-ref 'ZWLIB_RUNTIME_LOADED) dll) (princ))
    (T
      (setq result (vl-catch-all-apply 'command (list "_NETLOAD" dll)))
      (if (vl-catch-all-error-p result)
        (princ (strcat "\nZWLibrary load failed: " (vl-catch-all-error-message result)))
        (progn
          (setq loaded (vl-catch-all-apply 'ZWLIBRUNTIMEPATH nil))
          (if (and (not (vl-catch-all-error-p loaded)) (= (type loaded) 'STR) (equal (strcase loaded) (strcase dll)))
            (progn (vl-bb-set 'ZWLIB_RUNTIME_LOADED dll) (princ "\nZWLibrary startup loaded. Command: ZLIB"))
            (princ "\nZWLibrary runtime not verified. Remove old startup entries, restart ZWCAD and run ZLIBHELLO.")
          )
        )
      )
    )
  )
  (princ)
)
(zwlib-startup-load)
(princ)
'@
[IO.File]::WriteAllText((Join-Path $targetRoot 'ZWLibrary-startup.lsp'), $loader.Replace('@DLL@', $lispPath), [Text.Encoding]::ASCII)
Write-Output "Installed ZWLibrary $($assembly.Version): $target"
Write-Output "Register once in APPLOAD Startup Suite: $(Join-Path $targetRoot 'ZWLibrary-startup.lsp')"
Write-Output 'Restart ZWCAD, then run ZLIBHELLO to verify the version.'
