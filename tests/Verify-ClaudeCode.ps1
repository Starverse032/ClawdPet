[CmdletBinding()]
param([string]$PetPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'ClawdPet.exe'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$testDir = Join-Path $root ('work\verify-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testDir)
$settings = Join-Path $testDir 'settings.json'
$fixture = '{"model":"keep-me","permissions":{"deny":["Bash(dangerous-command)"]},"hooks":{"Stop":[{"matcher":"","hooks":[{"type":"command","command":"echo existing-hook"}]}]}}'
[IO.File]::WriteAllText($settings, $fixture)
& (Join-Path $root 'Connect-ClaudeCode.ps1') -SettingsPath $settings
$first = [IO.File]::ReadAllText($settings)
$cfg = $first | ConvertFrom-Json
if ($cfg.model -ne 'keep-me' -or $cfg.permissions.deny[0] -ne 'Bash(dangerous-command)') { throw 'Unrelated settings changed' }
if ($cfg.hooks.Stop[0].hooks[0].command -ne 'echo existing-hook') { throw 'Existing hook lost' }
if (@($cfg.hooks.PSObject.Properties).Count -ne 10) { throw 'Missing events' }
& (Join-Path $root 'Connect-ClaudeCode.ps1') -SettingsPath $settings
if ($first -cne [IO.File]::ReadAllText($settings)) { throw 'Install is not idempotent' }
& (Join-Path $root 'Connect-ClaudeCode.ps1') -SettingsPath $settings -Remove
$restored = Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json
if (@($restored.hooks.PSObject.Properties).Count -ne 1 -or @($restored.hooks.Stop).Count -ne 1 -or $restored.hooks.Stop[0].hooks[0].command -ne 'echo existing-hook') { throw 'Removal damaged existing hooks' }
Write-Output 'PASS installer preserves settings/hooks, is idempotent, and removes only its hooks'

$eventsDir = Join-Path $testDir 'events'
function Invoke-Hook([string]$Payload) {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $PetPath
    $info.Arguments = '--claude-hook'
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.EnvironmentVariables['CLAWD_BRIDGE_DIR'] = $eventsDir
    $info.EnvironmentVariables['CLAWD_NO_AUTO_START'] = '1'
    $proc = [Diagnostics.Process]::Start($info)
    $proc.StandardInput.Write($Payload)
    $proc.StandardInput.Close()
    if (-not $proc.WaitForExit(5000)) { $proc.Kill(); throw 'Hook timed out' }
    if ($proc.ExitCode -ne 0 -or $proc.StandardOutput.ReadToEnd() -ne '' -or $proc.StandardError.ReadToEnd() -ne '') { throw 'Hook interfered with Claude output' }
    $proc.Dispose()
}
foreach ($event in @('SessionStart','UserPromptSubmit','PreToolUse','PostToolUse','PostToolUseFailure','PermissionRequest','Notification','Stop','StopFailure','SessionEnd')) {
    Invoke-Hook (@{session_id='integration'; hook_event_name=$event; tool_name='Read'; notification_type='permission_prompt'; prompt='PRIVATE_TEST_TEXT'; tool_input=@{command='PRIVATE_TEST_COMMAND'}} | ConvertTo-Json -Compress)
}
if (@(Get-ChildItem -LiteralPath $eventsDir -Filter 'event-*.json').Count -ne 10) { throw 'Actual winexe hook did not consume stdin' }
Invoke-Hook 'invalid json'
Invoke-Hook '{"session_id":"a","hook_event_name":"Stop","agent_id":"child"}'
if (@(Get-ChildItem -LiteralPath $eventsDir -Filter 'event-*.json').Count -ne 10) { throw 'Malformed/subagent event was accepted' }
if (Select-String -Path (Join-Path $eventsDir '*.json') -Pattern 'PRIVATE_TEST') { throw 'Private data persisted' }
Write-Output 'PASS real winexe stdin for all 10 hooks; no stdout/stderr; malformed input is nonblocking; no prompt/command persisted'
Write-Output "Evidence: $testDir"
