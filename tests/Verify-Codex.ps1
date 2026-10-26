[CmdletBinding()]
param([switch]$Isolated)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $Isolated) {
    $testDir = Join-Path $root ('work\codex\config-' + [guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($testDir)
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = (Get-Process -Id $PID).Path
    $info.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -Isolated'
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    # An isolated child profile: the user's live config is never touched by tests.
    $info.EnvironmentVariables['CODEX_HOME'] = $testDir
    $process = [Diagnostics.Process]::Start($info)
    $output = $process.StandardOutput.ReadToEndAsync()
    $errors = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(45000)) { $process.Kill(); throw 'Codex config tests timed out' }
    Write-Output $output.Result
    if ($process.ExitCode -ne 0) { throw $errors.Result }
    Write-Output "PASS isolated Codex configuration; evidence: $testDir"
    $process.Dispose()
    return
}
$path = Join-Path $env:CODEX_HOME 'config.toml'
$installer = Join-Path $root 'Connect-Codex.ps1'
$fixture = @'
# Preserve this user comment.
model = "keep-model"
approval_policy = "on-request"

[mcp_servers.existing]
command = "keep.exe"
args = ["unchanged"]
'@
[IO.File]::WriteAllText($path, $fixture)
& $installer
$first = [IO.File]::ReadAllText($path)
& $installer
if ($first -cne [IO.File]::ReadAllText($path)) { throw 'Codex installer is not idempotent' }
$server = (& codex mcp get clawdpet --json | Out-String) | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $server.transport.command -ne (Join-Path $root 'ClawdPet.exe') -or $server.transport.args[0] -ne '--codex-mcp') { throw 'Incorrect Codex command' }
& $installer -Remove
$remaining = [IO.File]::ReadAllText($path)
if ($remaining.Trim() -cne $fixture.Trim()) { throw 'Unrelated Codex configuration changed' }
& $installer -Remove
Write-Output 'PASS install, real TOML parsing, exact unrelated config preservation, idempotency, scoped removal'

$collision = $fixture + "`n`n[mcp_servers.clawdpet]`ncommand = 'unrelated.exe'`nargs = []`n"
[IO.File]::WriteAllText($path, $collision)
foreach ($remove in @($false, $true)) {
    $rejected = $false
    try { & $installer -Remove:$remove } catch { $rejected = $_.Exception.Message -like '*unrelated MCP server*' }
    if (-not $rejected -or [IO.File]::ReadAllText($path) -cne $collision) { throw 'Existing-name conflict was not safely rejected' }
}
Write-Output 'PASS conflicting server retained on install and removal'
