[CmdletBinding()]
param([string]$Executable)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path $root ('work\tests-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testRoot)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework compiler not found.' }
Push-Location $root
try {
    if (-not $Executable) {
        $Executable = Join-Path $testRoot 'ClawdPet.exe'
        & $compiler /target:winexe /win32manifest:app.manifest /win32icon:clawd.ico "/out:$Executable" '@ClawdPet.rsp'
        if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
    }
    $Executable = (Resolve-Path -LiteralPath $Executable).Path
    foreach ($test in Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*Tests.cs' | Sort-Object Name) {
        $testExe = Join-Path $testRoot ($test.BaseName + '.exe')
        & $compiler /target:exe "/main:$($test.BaseName)" "/out:$testExe" '@ClawdPet.rsp' $test.FullName
        if ($LASTEXITCODE -ne 0) { throw "$($test.BaseName) build failed." }
        & $testExe (Join-Path $testRoot ($test.BaseName + '\sessions'))
        if ($LASTEXITCODE -ne 0) { throw "$($test.BaseName) failed." }
    }
    & "$PSScriptRoot\Verify-ClaudeCode.ps1" -PetPath $Executable
    & "$PSScriptRoot\Verify-CodexHooks.ps1" -PetPath $Executable
    & "$PSScriptRoot\Verify-Desktop.ps1" -Executable $Executable
    & "$PSScriptRoot\Verify-Desktop.ps1" -Executable $Executable -Codex
    Write-Output "SUCCESS: all isolated tests passed. Executable: $Executable"
}
finally { Pop-Location }
