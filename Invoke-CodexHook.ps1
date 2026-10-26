# Windows hook adapter. No transcript access, saved input, stdout or permission decisions.
[CmdletBinding()]
param([string]$PetPath)
$ErrorActionPreference = 'Stop'
$process = $null
try {
    if (-not $PetPath) { $PetPath = Join-Path $PSScriptRoot 'ClawdPet.exe' }
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $PetPath
    $info.Arguments = '--codex-hook'
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $info.RedirectStandardInput = $true
    $process = [Diagnostics.Process]::Start($info)
    $writer = New-Object IO.StreamWriter($process.StandardInput.BaseStream, (New-Object Text.UTF8Encoding($false)))
    $buffer = New-Object char[] 4096
    $total = 0
    while (($count = [Console]::In.Read($buffer, 0, $buffer.Length)) -gt 0) {
        $total += $count
        if ($total -gt 2097152) { $process.Kill(); exit 0 }
        $writer.Write($buffer, 0, $count)
    }
    $writer.Close()
    if (-not $process.WaitForExit(1500)) { $process.Kill() }
} catch { } finally { if ($process) { $process.Dispose() } }
exit 0
