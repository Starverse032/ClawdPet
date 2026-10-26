[CmdletBinding()]
param(
    [switch]$Remove,
    [string]$PetPath = (Join-Path $PSScriptRoot 'ClawdPet.exe'),
    [string]$CodexPath = 'codex'
)
$ErrorActionPreference = 'Stop'
$PetPath = [IO.Path]::GetFullPath($PetPath)
if (-not $Remove -and -not (Test-Path -LiteralPath $PetPath -PathType Leaf)) { throw "Missing executable: $PetPath" }
$cli = Get-Command $CodexPath -ErrorAction Stop
# Use Codex's own TOML parser/writer. Never rewrite unrelated settings ourselves.
$listing = & $cli.Source mcp list --json
if ($LASTEXITCODE -ne 0) { throw 'Could not read Codex MCP configuration.' }
$servers = @((($listing -join [Environment]::NewLine) | ConvertFrom-Json))
$existing = @($servers | Where-Object { $_.name -eq 'clawdpet' })
if ($existing.Count -gt 0) {
    $transport = $existing[0].transport
    if ($transport.command -ne $PetPath -or @($transport.args).Count -ne 1 -or $transport.args[0] -ne '--codex-mcp') {
        throw 'An unrelated MCP server is already named clawdpet. No changes made.'
    }
    if (-not $Remove) {
        if ($existing[0].enabled -eq $false) { throw 'ClawdPet is configured but disabled. Enable it in Codex Settings > MCP servers.' }
        Write-Output 'Already connected: clawdpet (Codex).'
        return
    }
} elseif ($Remove) { Write-Output 'No ClawdPet server to remove.'; return }
$configDir = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
$configPath = Join-Path $configDir 'config.toml'
if (Test-Path -LiteralPath $configPath) {
    $backup = $configPath + '.clawdpet-' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff') + '.bak'
    Copy-Item -LiteralPath $configPath -Destination $backup
    Write-Output "Backup: $backup"
}
if ($Remove) { & $cli.Source mcp remove clawdpet }
else { & $cli.Source mcp add clawdpet -- $PetPath --codex-mcp }
if ($LASTEXITCODE -ne 0) { throw 'Codex MCP configuration update failed.' }
Write-Output $(if ($Remove) { 'Removed ClawdPet from Codex.' } else { 'Connected ClawdPet to Codex. Reload MCP servers or open a new chat to load its tools.' })
