[CmdletBinding()]
param(
    [switch]$Remove,
    [string[]]$ConfigPath,
    [string]$PetPath = (Join-Path $PSScriptRoot 'ClawdPet.exe')
)
$ErrorActionPreference = 'Stop'
$PetPath = [IO.Path]::GetFullPath($PetPath)
if (-not $Remove -and -not (Test-Path -LiteralPath $PetPath -PathType Leaf)) { throw "Missing executable: $PetPath" }
if (-not $ConfigPath) {
    $ConfigPath = @((Join-Path $env:APPDATA 'Claude\claude_desktop_config.json'))
    $modeFile = Join-Path $env:LOCALAPPDATA 'Claude\claude_desktop_config.json'
    if (Test-Path -LiteralPath $modeFile) {
        $mode = ([IO.File]::ReadAllText($modeFile) | ConvertFrom-Json).deploymentMode
        if ($mode -eq '3p') { $ConfigPath = @((Join-Path $env:LOCALAPPDATA 'Claude-3p\claude_desktop_config.json')) }
    }
}
foreach ($path in $ConfigPath) {
    $path = [IO.Path]::GetFullPath($path)
    $original = if (Test-Path -LiteralPath $path) { [IO.File]::ReadAllText($path) } else { '{}' }
    $config = $original | ConvertFrom-Json
    if ($config -isnot [pscustomobject]) { throw "Config must be a JSON object: $path" }
    $before = $config | ConvertTo-Json -Depth 100
    if (-not $config.PSObject.Properties['mcpServers']) {
        if ($Remove) { Write-Output "No ClawdPet server: $path"; continue }
        $config | Add-Member -NotePropertyName mcpServers -NotePropertyValue ([pscustomobject]@{})
    }
    if ($config.mcpServers -isnot [pscustomobject]) { throw "mcpServers must be a JSON object: $path" }
    $existing = $config.mcpServers.PSObject.Properties['clawdpet']
    if ($existing -and -not ($existing.Value.command -eq $PetPath -and @($existing.Value.args).Count -eq 1 -and $existing.Value.args[0] -eq '--desktop-mcp')) {
        throw "An unrelated server is already named clawdpet. No changes made to: $path"
    }
    if ($Remove) {
        $config.mcpServers.PSObject.Properties.Remove('clawdpet')
        if (@($config.mcpServers.PSObject.Properties).Count -eq 0) { $config.PSObject.Properties.Remove('mcpServers') }
    } else {
        $config.mcpServers | Add-Member -NotePropertyName clawdpet -NotePropertyValue ([pscustomobject]@{
            command=$PetPath; args=@('--desktop-mcp')
        }) -Force
    }
    $after = $config | ConvertTo-Json -Depth 100
    if ($before -ceq $after) { Write-Output "Already configured: $path"; continue }
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
    if (Test-Path -LiteralPath $path) {
        $backup = $path + '.clawdpet-' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff') + '.bak'
        Copy-Item -LiteralPath $path -Destination $backup
        if ([IO.File]::ReadAllText($path) -cne $original) { throw 'Config changed during installation. Retry to merge the latest version.' }
        Write-Output "Backup: $backup"
    }
    $temp = $path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    [IO.File]::WriteAllText($temp, $after + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
    if (Test-Path -LiteralPath $path) { [IO.File]::Replace($temp, $path, [NullString]::Value) }
    else { [IO.File]::Move($temp, $path) }
    Write-Output $(if($Remove){"Removed ClawdPet MCP: $path"}else{"Connected ClawdPet MCP: $path"})
}
