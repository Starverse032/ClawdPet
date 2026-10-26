[CmdletBinding()]
param([switch]$Live, [switch]$Codex, [string]$Executable)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$petPath = if ($Executable) { $Executable } elseif ($Live) { Join-Path $root 'ClawdPet.exe' } else { Join-Path $root 'work\desktop\ClawdPet.exe' }
$testRoot = Join-Path $root ('work\desktop\check-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testRoot)
$configPath = Join-Path $testRoot 'claude_desktop_config.json'
[IO.File]::WriteAllText($configPath, '{"preferences":{"keep":true},"mcpServers":{"existing":{"command":"keep.exe","args":["unchanged"]}}}')
& (Join-Path $root 'Connect-ClaudeDesktop.ps1') -ConfigPath $configPath -PetPath $petPath
$first = [IO.File]::ReadAllText($configPath)
& (Join-Path $root 'Connect-ClaudeDesktop.ps1') -ConfigPath $configPath -PetPath $petPath
if ($first -cne [IO.File]::ReadAllText($configPath)) { throw 'Installer is not idempotent' }
$installed = $first | ConvertFrom-Json
if (-not $installed.preferences.keep -or $installed.mcpServers.existing.command -ne 'keep.exe' -or $installed.mcpServers.clawdpet.args[0] -ne '--desktop-mcp') { throw 'Incorrect config merge' }
& (Join-Path $root 'Connect-ClaudeDesktop.ps1') -ConfigPath $configPath -PetPath $petPath -Remove
$removed = [IO.File]::ReadAllText($configPath) | ConvertFrom-Json
if (@($removed.mcpServers.PSObject.Properties).Count -ne 1 -or $removed.mcpServers.existing.args[0] -ne 'unchanged') { throw 'Removal changed unrelated server' }
Write-Output 'PASS Desktop config merge, idempotency and scoped removal'

$info = New-Object Diagnostics.ProcessStartInfo
$info.FileName = $petPath
$info.Arguments = if ($Codex) { '--codex-mcp' } else { '--desktop-mcp' }
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
$info.RedirectStandardInput = $true
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
$info.StandardOutputEncoding = New-Object Text.UTF8Encoding($false)
if (-not $Live) {
    $info.EnvironmentVariables['CLAWD_NO_AUTO_START'] = '1'
    $info.EnvironmentVariables['CLAWD_BRIDGE_DIR'] = Join-Path $testRoot 'events'
}
$proc = [Diagnostics.Process]::Start($info)
$pipeWriter = New-Object IO.StreamWriter($proc.StandardInput.BaseStream, (New-Object Text.UTF8Encoding($false)))
$pipeWriter.AutoFlush = $true
$script:rpcId = 0
function Read-Rpc {
    $read = $proc.StandardOutput.ReadLineAsync()
    if (-not $read.Wait(6000)) { throw 'MCP response timed out' }
    if ($null -eq $read.Result) { throw 'MCP stdout ended unexpectedly' }
    return ($read.Result | ConvertFrom-Json)
}
function Rpc([string]$Method, $Params = @{}) {
    $script:rpcId++
    $pipeWriter.WriteLine((@{jsonrpc='2.0';id=$script:rpcId;method=$Method;params=$Params} | ConvertTo-Json -Depth 20 -Compress))
    $response = Read-Rpc
    if ($response.id -ne $script:rpcId) { throw ('Incorrect RPC response id: ' + ($response | ConvertTo-Json -Depth 8 -Compress)) }
    return $response
}
function Assert($Value, [string]$Label) {
    if (-not $Value) { throw $Label }
    Write-Output "PASS $Label"
}
try {
    $pre = Rpc 'tools/list'
    Assert ($pre.error.code -eq -32002) 'requires initialization'
    $init = Rpc 'initialize' @{protocolVersion='2025-11-25';capabilities=@{};clientInfo=@{name='ClawdPet integration test';version='1.0'}}
    Assert ($init.result.protocolVersion -eq '2025-11-25' -and $init.result.serverInfo.name -eq 'clawdpet') 'MCP initialization'
    $pipeWriter.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
    $tools = Rpc 'tools/list'
    Assert ($tools.result.tools.Count -eq 2 -and $tools.result.tools[0].inputSchema.properties.state.enum -contains 'dance') 'tool discovery after notification'
    $status = Rpc 'tools/call' @{name='clawdpet_status';arguments=@{}}
    Assert (-not $status.result.isError) 'read-only status tool'
    $bad = Rpc 'tools/call' @{name='clawdpet_act';arguments=@{state='run-shell'}}
    Assert ($bad.result.isError) 'unknown actions rejected'
    $bad = Rpc 'tools/call' @{name='clawdpet_act';arguments=@{state='dance';duration_seconds=121}}
    Assert ($bad.result.isError) 'duration bounded'
    $bad = Rpc 'tools/call' @{name='clawdpet_act';arguments=@{state='dance';text=('x'*81)}}
    Assert ($bad.result.isError) 'bubble length bounded'
    $bad = Rpc 'tools/call' @{name='clawdpet_status';arguments='wrong-type'}
    Assert ($bad.error.code -eq -32602) 'non-object arguments rejected'
    $bad = Rpc 'unknown/method'
    Assert ($bad.error.code -eq -32601) 'unknown methods rejected'
    $pipeWriter.WriteLine('{invalid-json')
    $bad = Read-Rpc
    Assert ($bad.error.code -eq -32700) 'malformed input has structured error'
    $pipeWriter.WriteLine(('x'*65540))
    $bad = Read-Rpc
    Assert ($bad.error.code -eq -32700) 'oversized message bounded'
    $ping = Rpc 'ping'
    Assert ($null -ne $ping.result) 'server recovers after invalid input'
    foreach ($state in @('wave','thinking','waiting','dance','happy','sleep','error','idle')) {
        $response = Rpc 'tools/call' @{name='clawdpet_act';arguments=@{state=$state;duration_seconds=2;text='你好，桌宠！'}}
        $value = $response.result.content[0].text | ConvertFrom-Json
        if ($Live) {
            Assert (-not $response.result.isError -and $value.accepted) "live action $state acknowledged by desktop pet"
            Start-Sleep -Milliseconds 900
        } else {
            Assert ($value.queued -and -not $value.accepted) "action $state queued without false delivery claim"
        }
    }
    if(-not $Live){
        $queued = @(Get-ChildItem -LiteralPath (Join-Path $testRoot 'events') -Filter 'event-*.json' | ForEach-Object {Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json})
        Assert ($queued.Count -eq 8 -and @($queued | Where-Object {$_.Text -eq '你好，桌宠！'}).Count -eq 8) 'UTF-8 Chinese preserved through actual stdin/stdout transport'
        $source = if ($Codex) { 'codex' } else { 'desktop' }
        Assert (@($queued | Where-Object { $_.Source -eq $source -and $_.Session.StartsWith($source + ':') }).Count -eq 8) 'correct source and isolated session namespace'
    }
} finally {
    $pipeWriter.Close()
    if (-not $proc.WaitForExit(4000)) { $proc.Kill(); throw 'MCP server did not exit on EOF' }
    $errText = $proc.StandardError.ReadToEnd()
    if ($proc.ExitCode -ne 0 -or $errText) { throw "MCP server error: $errText" }
    $proc.Dispose()
}
Write-Output "PASS clean shutdown; evidence: $testRoot"
