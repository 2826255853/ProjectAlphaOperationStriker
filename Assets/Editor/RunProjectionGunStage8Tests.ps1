<#
Repeatable stage-eight runtime acceptance. The fixture is an EditMode test because
it enters and exits Play Mode explicitly with UnityTest's EnterPlayMode yield.
No -quit: the Test Runner must finish writing NUnit XML before Unity exits.
#>
[CmdletBinding()]
param(
    [string]$ProjectPath = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$OutputDirectory,
    [ValidateRange(60, 1800)][int]$TimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
$versionFile = Join-Path $ProjectPath 'ProjectSettings\ProjectVersion.txt'
$versionMatch = Select-String -LiteralPath $versionFile -Pattern '^m_EditorVersion:\s*(\S+)'
if (!$versionMatch) { throw "Could not read m_EditorVersion from $versionFile" }
$version = $versionMatch.Matches[0].Groups[1].Value
$candidates = @(
    "C:\Program Files\Unity $version\Editor\Unity.exe",
    "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe",
    "C:\Program Files\Unity\Hub\Editor\Unity $version\Editor\Unity.exe"
)
$unity = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$unity) { throw "The installed Unity editor must match ProjectVersion.txt ($version)." }
if (Get-Process Unity -ErrorAction SilentlyContinue) {
    throw 'Close the running Unity editor first; this runner never terminates an existing editor.'
}

if (!$OutputDirectory) {
    $materialFolder = -join [char[]]@(0x9677, 0x9631, 0x7D20, 0x6750, 0x6587, 0x4EF6, 0x5939)
    $OutputDirectory = Join-Path ([Environment]::GetFolderPath('Desktop')) "$materialFolder\TrapPlacementProjectionGun\stage8-unity"
}
$session = Join-Path $OutputDirectory (Get-Date -Format 'yyyyMMdd-HHmmss-fff')
New-Item -ItemType Directory -Path $session -Force | Out-Null
$session = (Resolve-Path -LiteralPath $session).Path
$logPath = Join-Path $session 'editmode.log'
$xmlPath = Join-Path $session 'editmode-results.xml'
$stdoutPath = Join-Path $session 'stdout.txt'
$stderrPath = Join-Path $session 'stderr.txt'
$fixture = 'TrapPlacementProjectionGunStage8Tests'
$arguments = '-batchmode -nographics -projectPath "{0}" -runTests -testPlatform EditMode -testFilter "{1}" -extraScriptingDefines=UNITY_INCLUDE_TESTS -testResults "{2}" -logFile "{3}"' -f $ProjectPath, $fixture, $xmlPath, $logPath

Write-Host "[stage8] Unity $version; NUnit output: $xmlPath"
$process = Start-Process -FilePath $unity -ArgumentList $arguments -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
$null = $process.Handle
if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
    Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
    throw "Stage-eight tests timed out after $TimeoutSeconds seconds. See $logPath"
}
$process.Refresh()

if (!(Test-Path -LiteralPath $xmlPath)) {
    throw "Unity exit code $($process.ExitCode) produced no NUnit XML; this is NOT a passing run. See $logPath"
}

[xml]$results = Get-Content -Raw -Encoding UTF8 -LiteralPath $xmlPath
$run = $results.'test-run'
$cases = @($results.SelectNodes('//test-case'))
$fixtureResults = @($results.SelectNodes('//test-suite[@type="TestFixture"]'))
$fixtureResult = @($fixtureResults | Where-Object { $_.name -eq $fixture })
$summary = [ordered]@{
    unityVersion = $version
    exitCode = $process.ExitCode
    result = $run.result
    total = [int]$run.total
    passed = [int]$run.passed
    failed = [int]$run.failed
    skipped = [int]$run.skipped
    xml = $xmlPath
    log = $logPath
    fixture = $fixture
}
$summary | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $session 'summary.json')

if ($fixtureResult.Count -ne 1 -or [int]$fixtureResult[0].total -le 0) {
    throw "Expected fixture was not executed: $fixture. See $xmlPath"
}
if ($cases.Count -ne [int]$run.total -or $cases.Count -eq 0 -or
    $run.result -ne 'Passed' -or [int]$run.failed -ne 0 -or [int]$run.skipped -ne 0 -or
    @($cases | Where-Object { $_.result -ne 'Passed' }).Count -ne 0 -or
    (Select-String -LiteralPath $logPath -Pattern 'error CS\d+|Scripts have compiler errors')) {
    $cases | Where-Object { $_.result -ne 'Passed' } | ForEach-Object {
        Write-Host ("FAIL: {0}: {1}" -f $_.fullname, $_.failure.message.InnerText)
    }
    throw "Stage-eight acceptance failed; inspect $xmlPath and $logPath"
}

Write-Host "PROJECTION_GUN_STAGE8_NUNIT_PASS: $($run.passed)/$($run.total); $xmlPath"
