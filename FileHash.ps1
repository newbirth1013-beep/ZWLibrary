# Compatible with Windows PowerShell installations without Get-FileHash.
function Get-ZwFileHash {
    param([Parameter(Mandatory=$true)][string]$LiteralPath)
    $stream = [IO.File]::OpenRead([IO.Path]::GetFullPath($LiteralPath))
    $sha = [Security.Cryptography.SHA256]::Create()
    try { [pscustomobject]@{ Hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') } }
    finally { $sha.Dispose(); $stream.Dispose() }
}
