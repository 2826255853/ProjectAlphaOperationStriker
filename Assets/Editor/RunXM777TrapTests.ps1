<# Runs persistent XM777 tests and removes their temporary Assets copy in all exit paths. #>
[CmdletBinding()]
param(
    [string]$ProjectPath = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$OutputDirectory = 'C:\Users\ASUS\Desktop\陷阱素材文件夹\XM777\tests',
    [ValidateRange(30, 1800)][int]$TimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
$source = Join-Path $ProjectPath 'Tests~\Editor\XM777TrapTests.cs'
$target = Join-Path $ProjectPath 'Assets\Editor\XM777TrapTests.cs'
if ((Test-Path -LiteralPath $target) -or (Test-Path -LiteralPath "$target.meta")) {
    throw "Refusing to overwrite a staged test fixture: $target"
}

try {
    Copy-Item -LiteralPath $source -Destination $target
    & (Join-Path $PSScriptRoot 'RunEnemyDamageResistanceTests.ps1') -ProjectPath $ProjectPath `
        -OutputDirectory $OutputDirectory -TimeoutSeconds $TimeoutSeconds -Fixtures @('XM777TrapTests')
}
finally {
    Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$target.meta" -Force -ErrorAction SilentlyContinue
}
