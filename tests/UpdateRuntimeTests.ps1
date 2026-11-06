$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$fixture = Join-Path $project ('output/v0.8.4/update-check-' + [Guid]::NewGuid().ToString('N'))
$global:zwlibTestHostRunning = $true
# Simulate process state only in this test process; all writes target an isolated fixture.
function Get-Process { param($Name, $ErrorAction) if ($global:zwlibTestHostRunning) { [pscustomobject]@{ Name = 'ZWCAD' } } }
function Check($condition, $message) { if (-not $condition) { throw "FAIL: $message" }; Write-Output "PASS: $message" }
try {
    $updater = Join-Path $project 'UPDATE_RUNTIME.ps1'
    $source = Join-Path $project 'output/v0.8.4/plugin'
    $rejected = $false
    try { & $updater -SourceDirectory $source -RuntimeDirectory $fixture } catch { $rejected = $_.Exception.Message -like 'Close all ZWCAD windows*' }
    Check ($rejected -and -not (Test-Path -LiteralPath $fixture)) 'running host guard makes no changes'
    $global:zwlibTestHostRunning = $false
    & $updater -SourceDirectory $source -RuntimeDirectory $fixture -WhatIf
    Check (-not (Test-Path -LiteralPath $fixture)) 'WhatIf makes no changes'
    & $updater -SourceDirectory $source -RuntimeDirectory $fixture
    Check ((Get-FileHash (Join-Path $source 'ZWLibrary.dll')).Hash -eq (Get-FileHash (Join-Path $fixture 'ZWLibrary.dll')).Hash) 'runtime DLL matches build'
    $loader = [IO.File]::ReadAllText((Join-Path $fixture 'ZWLibrary-startup.lsp'))
    Check ($loader.Contains((Join-Path $fixture 'ZWLibrary.dll').Replace('\', '/')) -and -not $loader.Contains('@DLL@')) 'loader uses fixed absolute path'
    Check ($loader.Contains('ZWLIBRUNTIMEPATH')) 'loader verifies actual loaded assembly path'
    & $updater -SourceDirectory $source -RuntimeDirectory $fixture
    Check (@(Get-ChildItem (Join-Path $fixture 'backups') -Filter '*.dll').Count -eq 1) 'subsequent update retains previous DLL'
    $before = (Get-FileHash (Join-Path $fixture 'ZWLibrary.dll')).Hash
    $rejected = $false
    try { & $updater -SourceDirectory (Join-Path $fixture 'missing') -RuntimeDirectory $fixture } catch { $rejected = $true }
    Check ($rejected -and (Get-FileHash (Join-Path $fixture 'ZWLibrary.dll')).Hash -eq $before) 'missing build preserves installed DLL'
    Write-Output 'UPDATE TOTAL PASS: 7'
} finally {
    # Resolve and verify the exact test fixture before recursive cleanup.
    $expected = [IO.Path]::GetFullPath((Join-Path $project 'output/v0.8.4')) + [IO.Path]::DirectorySeparatorChar
    $resolved = [IO.Path]::GetFullPath($fixture)
    if (-not $resolved.StartsWith($expected, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup path' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
