[CmdletBinding()]
param([string]$PetPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $PetPath) { $PetPath = Join-Path $root 'work\codex-hooks\ClawdPet.exe' }
$testDir = Join-Path $root ('work\codex-hooks\check-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testDir)
$path = Join-Path $testDir 'hooks.json'
$fixture = '{"description":"keep","hooks":{"Stop":[{"matcher":"keep","hooks":[{"type":"command","command":"echo existing"}]}]}}'
[IO.File]::WriteAllText($path, $fixture)
$installer = Join-Path $root 'Connect-CodexHooks.ps1'
& $installer -HooksPath $path -PetPath $PetPath
$first = [IO.File]::ReadAllText($path)
& $installer -HooksPath $path -PetPath $PetPath
if ($first -cne [IO.File]::ReadAllText($path)) { throw 'Hook installation is not idempotent' }
$config = $first | ConvertFrom-Json
if ($config.description -ne 'keep' -or $config.hooks.Stop[0].hooks[0].command -ne 'echo existing' -or @($config.hooks.PSObject.Properties).Count -ne 10) { throw 'Original hooks not preserved or events missing' }
$command = $config.hooks.UserPromptSubmit[0].hooks[0].commandWindows
& $installer -HooksPath $path -PetPath $PetPath -Remove
$removed = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
if (@($removed.hooks.PSObject.Properties).Count -ne 1 -or $removed.hooks.Stop[0].matcher -ne 'keep' -or $removed.hooks.Stop[0].hooks[0].command -ne 'echo existing') { throw 'Uninstall changed unrelated hooks' }
Write-Output 'PASS hook merge, idempotency, preservation and scoped removal'
$eventDir = Join-Path $testDir 'events'
function Send-Hook([string]$Payload) {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $env:ComSpec
    $info.Arguments = '/d /s /c "' + $command + '"'
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.EnvironmentVariables['CLAWD_BRIDGE_DIR'] = $eventDir
    $info.EnvironmentVariables['CLAWD_NO_AUTO_START'] = '1'
    $proc = [Diagnostics.Process]::Start($info)
    $writer = New-Object IO.StreamWriter($proc.StandardInput.BaseStream, (New-Object Text.UTF8Encoding($false)))
    $writer.Write($Payload)
    $writer.Close()
    if (-not $proc.WaitForExit(5000)) { $proc.Kill(); throw 'Hook adapter exceeded deadline' }
    if ($proc.ExitCode -ne 0 -or $proc.StandardOutput.ReadToEnd() -ne '' -or $proc.StandardError.ReadToEnd() -ne '') { throw 'Hook generated output or failure that could affect Codex' }
    $proc.Dispose()
}
foreach ($event in @('SessionStart','UserPromptSubmit','PreToolUse','PermissionRequest','PostToolUse','PreCompact','PostCompact','Stop','Interrupt','SessionEnd')) {
    Send-Hook (@{session_id='test';turn_id='turn-one';hook_event_name=$event;tool_name='Bash';prompt='PRIVATE_PROMPT';transcript_path='PRIVATE_PATH';last_assistant_message='PRIVATE_REPLY';tool_input=@{command='PRIVATE_COMMAND'}} | ConvertTo-Json -Depth 5 -Compress)
}
$files = @(Get-ChildItem -LiteralPath $eventDir -Filter 'event-*.json')
if ($files.Count -ne 10) { throw "Expected 10 events, got $($files.Count)" }
if (Select-String -Path (Join-Path $eventDir '*.json') -Pattern 'PRIVATE_') { throw 'Private conversation data was saved' }
Send-Hook 'malformed json'
Send-Hook '{"session_id":"test","hook_event_name":"SubagentStop"}'
Send-Hook '{"session_id":"test","hook_event_name":"Stop","agent_id":"child"}'
if (@(Get-ChildItem -LiteralPath $eventDir -Filter 'event-*.json').Count -ne 10) { throw 'Invalid or child hook accepted' }
Write-Output 'PASS all 10 real Windows shell/adapter/exe hook inputs; no stdout/stderr; no conversation content; malformed and child inputs ignored'
Write-Output "Evidence: $testDir"
