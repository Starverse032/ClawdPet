[CmdletBinding()]
param(
    [switch]$Remove,
    [string]$SettingsPath,
    [string]$PetPath = (Join-Path $PSScriptRoot 'ClawdPet.exe')
)
$ErrorActionPreference = 'Stop'
if (-not $SettingsPath) {
    $configDir = if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { Join-Path $env:USERPROFILE '.claude' }
    $SettingsPath = Join-Path $configDir 'settings.json'
}
$SettingsPath = [IO.Path]::GetFullPath($SettingsPath)
$PetPath = [IO.Path]::GetFullPath($PetPath)
if (-not $Remove -and -not (Test-Path -LiteralPath $PetPath -PathType Leaf)) { throw "Pet executable missing: $PetPath" }
$original = if (Test-Path -LiteralPath $SettingsPath) { [IO.File]::ReadAllText($SettingsPath) } else { '{}' }
$settings = $original | ConvertFrom-Json
if ($null -eq $settings -or $settings -isnot [pscustomobject]) { throw 'Settings must contain a JSON object.' }
if (-not $settings.PSObject.Properties['hooks']) {
    if ($Remove) { Write-Output 'No ClawdPet hooks to remove.'; return }
    $settings | Add-Member -NotePropertyName hooks -NotePropertyValue ([pscustomobject]@{})
}
if ($settings.hooks -isnot [pscustomobject]) { throw 'Existing hooks is not a JSON object. No changes made.' }
$before = $settings | ConvertTo-Json -Depth 100
$events = @('SessionStart','UserPromptSubmit','PreToolUse','PostToolUse','PostToolUseFailure','PermissionRequest','Notification','Stop','StopFailure','SessionEnd')
foreach ($event in $events) {
    $groups = @()
    $property = $settings.hooks.PSObject.Properties[$event]
    if ($property) {
        foreach ($group in @($property.Value)) {
            $remaining = @($group.hooks | Where-Object {
                -not ($_.type -eq 'command' -and $_.command -eq $PetPath -and @($_.args).Count -eq 1 -and $_.args[0] -eq '--claude-hook')
            })
            if ($remaining.Count -gt 0) { $group.hooks = $remaining; $groups += $group }
        }
    }
    if (-not $Remove) {
        $groups += [pscustomobject]@{ hooks = @([pscustomobject]@{
            type = 'command'; command = $PetPath; args = @('--claude-hook'); timeout = 3
        }) }
    }
    if ($groups.Count -gt 0) { $settings.hooks | Add-Member -NotePropertyName $event -NotePropertyValue @($groups) -Force }
    else { $settings.hooks.PSObject.Properties.Remove($event) }
}
if ($Remove -and @($settings.hooks.PSObject.Properties).Count -eq 0) { $settings.PSObject.Properties.Remove('hooks') }
$after = $settings | ConvertTo-Json -Depth 100
if ($before -eq $after) { Write-Output 'ClawdPet hooks already configured; no changes needed.'; return }
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($SettingsPath))
$backup = $null
if (Test-Path -LiteralPath $SettingsPath) {
    $backup = $SettingsPath + '.clawdpet-' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff') + '.bak'
    Copy-Item -LiteralPath $SettingsPath -Destination $backup
    # Detect another writer before replacing the settings.
    if ([IO.File]::ReadAllText($SettingsPath) -cne $original) { throw 'Settings changed during installation. Retry to merge the latest version.' }
}
$temp = $SettingsPath + '.clawdpet-' + [guid]::NewGuid().ToString('N') + '.tmp'
[IO.File]::WriteAllText($temp, $after + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
if (Test-Path -LiteralPath $SettingsPath) { [IO.File]::Replace($temp, $SettingsPath, [NullString]::Value) }
else { [IO.File]::Move($temp, $SettingsPath) }
Write-Output $(if ($Remove) { 'Removed ClawdPet hooks.' } else { 'Connected ClawdPet to Claude Code (10 hooks).' })
Write-Output "Settings: $SettingsPath"
if ($backup) { Write-Output "Backup: $backup" }
if ($settings.disableAllHooks -or $settings.allowManagedHooksOnly) { Write-Warning 'Claude settings restrict hooks; the existing restriction was preserved.' }
