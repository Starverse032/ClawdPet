# Local lifecycle replay only: no Codex prompt, model request or trust bypass.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$statusPath = Join-Path $env:APPDATA 'ClawdPet\claude-code\status.json'
$testSession = 'clawdpet-codex-smoke-' + [guid]::NewGuid().ToString('N')
function Send-Hook([string]$Event, [string]$Turn = 'one') {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = Join-Path $root 'ClawdPet.exe'
    $info.Arguments = '--codex-hook'
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.EnvironmentVariables['CLAWD_NO_AUTO_START'] = '1'
    $process = [Diagnostics.Process]::Start($info)
    $process.StandardInput.Write((@{session_id=$testSession;turn_id=$Turn;hook_event_name=$Event;tool_name='Bash'} | ConvertTo-Json -Compress))
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(2000)) { $process.Kill(); throw 'Live hook timed out' }
    if ($process.ExitCode -ne 0) { throw 'Live hook failed' }
    $process.Dispose()
}
function Expect([string]$Kind) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        Start-Sleep -Milliseconds 100
        $status = Get-Content -LiteralPath $statusPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($status.state -eq $Kind -and $status.source -eq 'chatgpt' -and $status.text -like 'ChatGPT*') {
            Write-Output "PASS live $Kind : $($status.text)"
            return
        }
    } while ($timer.ElapsedMilliseconds -lt 4000)
    throw "Did not observe live ChatGPT $Kind"
}
try {
    Send-Hook 'SessionStart'; Expect 'ready'
    Start-Sleep -Milliseconds 1600
    Send-Hook 'UserPromptSubmit'; Expect 'working'
    Start-Sleep -Milliseconds 5200
    Send-Hook 'PermissionRequest'; Expect 'waiting'
    Start-Sleep -Milliseconds 3000
    Send-Hook 'PostToolUse'; Expect 'working'
    Start-Sleep -Milliseconds 800
    Send-Hook 'Stop'; Expect 'done'
    Start-Sleep -Milliseconds 4200
    Send-Hook 'UserPromptSubmit' 'two'; Expect 'working'
    Send-Hook 'Interrupt' 'two'; Expect 'interrupted'
} finally { Send-Hook 'SessionEnd' '' }
Expect 'ended'
