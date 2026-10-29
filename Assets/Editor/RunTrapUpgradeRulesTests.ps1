<# Stage only these fixtures, reuse the project's Unity runner, and always remove test sources. #>
[CmdletBinding()]
param(
    [string]$ProjectPath = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$OutputDirectory,
    [switch]$IncludePayments,
    [switch]$IncludeDurability,
    [switch]$IncludeStep5,
    [switch]$IncludeStep6,
    [ValidateRange(30, 1800)][int]$TimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
if (!$OutputDirectory) {
    $step = if ($IncludeStep6) { 'step6' } elseif ($IncludeStep5) { 'step5' } elseif ($IncludeDurability) { 'step4' } elseif ($IncludePayments) { 'step3' } else { 'step2' }
    $OutputDirectory = Join-Path ([Environment]::GetFolderPath('Desktop')) "陷阱素材文件夹\TrapUpgradeSystem\$step"
}
$fixtures = @('TrapUpgradeRulesTests', 'TrapSelectionMenuCanvasTests')
if ($IncludePayments -or $IncludeDurability -or $IncludeStep5 -or $IncludeStep6) { $fixtures += 'TrapUpgradePaymentTests' }
if ($IncludeDurability -or $IncludeStep5 -or $IncludeStep6) { $fixtures += 'TrapUpgradeDurabilityTests' }
if ($IncludeStep5 -or $IncludeStep6) { $fixtures += 'TrapUpgradeCombatIntegrationTests' }
if ($IncludeStep6) { $fixtures += 'TrapUpgradeDelayedDamageTests', 'Betty7JumpMineSlotTests' }
$runFixtures = @($fixtures)
if ($IncludePayments -or $IncludeDurability -or $IncludeStep5 -or $IncludeStep6) { $runFixtures += 'EconomySystemTests' }
if ($IncludeStep6) { $runFixtures += 'MissileLauncherWeaponTests', 'FireStatusEffectTests' }
$staged = @()
try {
    foreach ($fixture in $fixtures) {
        $source = Join-Path $ProjectPath "Tests~\Editor\$fixture.cs"
        $target = Join-Path $ProjectPath "Assets\Editor\$fixture.cs"
        if ((Test-Path -LiteralPath $target) -or (Test-Path -LiteralPath "$target.meta")) {
            throw "Refusing to overwrite an existing staged fixture: $target"
        }
        Copy-Item -LiteralPath $source -Destination $target
        $staged += $target
    }
    & (Join-Path $PSScriptRoot 'RunEnemyDamageResistanceTests.ps1') -ProjectPath $ProjectPath `
        -OutputDirectory $OutputDirectory -TimeoutSeconds $TimeoutSeconds -Fixtures $runFixtures
}
finally {
    foreach ($target in $staged) {
        Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath "$target.meta" -Force -ErrorAction SilentlyContinue
    }
}
