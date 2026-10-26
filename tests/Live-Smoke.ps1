# Replays state metadata through the installed executable; does not send prompts to Claude.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$statusPath = Join-Path $env:APPDATA 'ClawdPet\claude-code\status.json'
$testSession = 'clawdpet-smoke-' + [guid]::NewGuid().ToString('N')
function Send-Event([string]$EventName, [string]$ToolName = '') {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = Join-Path $root 'ClawdPet.exe'
    $info.Arguments = '--claude-hook'
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.EnvironmentVariables['CLAWD_NO_AUTO_START'] = '1'
    $proc = [Diagnostics.Process]::Start($info)
    $proc.StandardInput.Write((@{session_id=$testSession; hook_event_name=$EventName; tool_name=$ToolName} | ConvertTo-Json -Compress))
    $proc.StandardInput.Close()
    if (-not $proc.WaitForExit(3000)) { $proc.Kill(); throw 'Live hook timed out' }
    if ($proc.ExitCode -ne 0) { throw 'Live hook failed' }
    $proc.Dispose()
}
function Expect-State([string]$Expected) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        Start-Sleep -Milliseconds 150
        $status = if(Test-Path -LiteralPath $statusPath){Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json}
        if ($status.state -eq $Expected) {
            Write-Output "PASS live $Expected : $($status.text)"
            Start-Sleep -Milliseconds 850
            return
        }
    } while($timer.ElapsedMilliseconds -lt 5000)
    throw "Live status did not become $Expected"
}
try {
    Send-Event 'SessionStart'; Expect-State 'ready'
    Send-Event 'UserPromptSubmit'; Expect-State 'working'
    Send-Event 'PreToolUse' 'Edit'; Expect-State 'working'
    Send-Event 'PermissionRequest'; Expect-State 'waiting'
    Send-Event 'PostToolUse'; Expect-State 'working'
    Send-Event 'Stop'; Expect-State 'done'
    Send-Event 'StopFailure'; Expect-State 'error'
} finally { Send-Event 'SessionEnd' }
Expect-State 'ended'
