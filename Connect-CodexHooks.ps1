[CmdletBinding()]
param(
    [switch]$Remove,
    [string]$HooksPath,
    [string]$PetPath
)
$ErrorActionPreference = 'Stop'
if (-not $PetPath) { $PetPath = Join-Path $PSScriptRoot 'ClawdPet.exe' }
if (-not $HooksPath) {
    $configDir = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
    $HooksPath = Join-Path $configDir 'hooks.json'
}
$HooksPath = [IO.Path]::GetFullPath($HooksPath)
$PetPath = [IO.Path]::GetFullPath($PetPath)
$adapter = Join-Path $PSScriptRoot 'Invoke-CodexHook.ps1'
if (-not $Remove -and -not (Test-Path -LiteralPath $PetPath -PathType Leaf)) { throw "Missing executable: $PetPath" }
# An explicit Windows PowerShell adapter preserves UTF-8 stdin regardless of the caller's shell.
$command = 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $adapter + '" -PetPath "' + $PetPath + '"'
$original = if (Test-Path -LiteralPath $HooksPath) { [IO.File]::ReadAllText($HooksPath) } else { '{}' }
$config = $original | ConvertFrom-Json
if ($config -isnot [pscustomobject]) { throw 'Hook configuration must be a JSON object.' }
if (-not $config.PSObject.Properties['hooks']) {
    if ($Remove) { Write-Output 'No ClawdPet lifecycle hooks to remove.'; return }
    $config | Add-Member -NotePropertyName hooks -NotePropertyValue ([pscustomobject]@{})
}
if ($config.hooks -isnot [pscustomobject]) { throw 'Existing hooks must be a JSON object. No changes made.' }
$before = $config | ConvertTo-Json -Depth 100
$events = @('SessionStart','UserPromptSubmit','PreToolUse','PermissionRequest','PostToolUse','PreCompact','PostCompact','Stop','Interrupt','SessionEnd')
foreach ($event in $events) {
    $groups = @()
    $property = $config.hooks.PSObject.Properties[$event]
    if ($property) {
        foreach ($group in @($property.Value)) {
            if ($group -isnot [pscustomobject] -or -not $group.PSObject.Properties['hooks']) { throw "Malformed existing $event hook group. No changes made." }
            $remaining = @($group.hooks | Where-Object { -not ($_.type -eq 'command' -and $_.command -ceq $command) })
            if ($remaining.Count -gt 0) { $group.hooks = $remaining; $groups += $group }
        }
    }
    if (-not $Remove) {
        $groups += [pscustomobject]@{ hooks = @([pscustomobject]@{
            type = 'command'; command = $command; commandWindows = $command; timeout = 3
        }) }
    }
    if ($groups.Count -gt 0) { $config.hooks | Add-Member -NotePropertyName $event -NotePropertyValue @($groups) -Force }
    else { $config.hooks.PSObject.Properties.Remove($event) }
}
if ($Remove -and @($config.hooks.PSObject.Properties).Count -eq 0) { $config.PSObject.Properties.Remove('hooks') }
$after = $config | ConvertTo-Json -Depth 100
if ($before -ceq $after) { Write-Output 'ClawdPet lifecycle hooks already configured; no changes.'; return }
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($HooksPath))
if (Test-Path -LiteralPath $HooksPath) {
    $backup = $HooksPath + '.clawdpet-' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff') + '.bak'
    Copy-Item -LiteralPath $HooksPath -Destination $backup
    if ([IO.File]::ReadAllText($HooksPath) -cne $original) { throw 'Hook config changed during installation. Retry to merge the latest version.' }
    Write-Output "Backup: $backup"
}
$temp = $HooksPath + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
[IO.File]::WriteAllText($temp, $after + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
if (Test-Path -LiteralPath $HooksPath) { [IO.File]::Replace($temp, $HooksPath, [NullString]::Value) }
else { [IO.File]::Move($temp, $HooksPath) }
Write-Output $(if ($Remove) { 'Removed ClawdPet Codex lifecycle hooks.' } else { 'Installed 10 ChatGPT lifecycle hooks. Review and trust these hooks in Codex /hooks before they can run.' })
Write-Output "Hooks: $HooksPath"
