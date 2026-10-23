<#
    Repeatable stage-six EditMode acceptance for the sentry machine gun art prefab.
    Project tooling (like RunProjectionGunStage7Tests.ps1), not a model-generation intermediate.

    No -quit: the Unity Test Runner must finish its asynchronous run and write NUnit XML
    before the editor exits. UNITY_INCLUDE_TESTS is command-line-only; no persistent
    scripting defines or package settings are changed.

    Fails unless the expected fixture actually ran, every case passed, nothing was skipped,
    and the log has no compiler errors - exit code alone is never treated as success.
#>
[CmdletBinding()]
param(
    [string]$ProjectPath = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$OutputDirectory,
    [string]$Fixture = 'SentryMachineGunTests',
    [ValidateRange(1, 200)][int]$ExpectedCases = 11,
    [ValidateRange(30, 1200)][int]$TimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
$versionFile = Join-Path $ProjectPath 'ProjectSettings\ProjectVersion.txt'
$version = (Select-String -LiteralPath $versionFile -Pattern '^m_EditorVersion:\s*(\S+)').Matches[0].Groups[1].Value
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
    $OutputDirectory = Join-Path ([Environment]::GetFolderPath('Desktop')) '陷阱素材文件夹\SentryMachineGun\stage6-unity'
}
$session = Join-Path $OutputDirectory (Get-Date -Format 'yyyyMMdd-HHmmss-fff')
New-Item -ItemType Directory -Path $session -Force | Out-Null
$session = (Resolve-Path -LiteralPath $session).Path
$logPath = Join-Path $session 'editmode.log'
$xmlPath = Join-Path $session 'editmode-results.xml'
$arguments = '-batchmode -nographics -projectPath "{0}" -runTests -testPlatform EditMode -testFilter "{1}" -extraScriptingDefines=UNITY_INCLUDE_TESTS -testResults "{2}" -logFile "{3}"' -f $ProjectPath, $Fixture, $xmlPath, $logPath
Write-Host "[sentry] Unity $version; NUnit output: $xmlPath"
$process = Start-Process -FilePath $unity -ArgumentList $arguments -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $session 'stdout.txt') -RedirectStandardError (Join-Path $session 'stderr.txt')
# Cache the native handle before waiting: Windows PowerShell can otherwise return a null ExitCode.
$null = $process.Handle
if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
    Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
    throw "Stage-six tests timed out after $TimeoutSeconds seconds. See $logPath"
}
$process.Refresh()
if (!(Test-Path -LiteralPath $xmlPath)) {
    throw "Unity exit code $($process.ExitCode) produced no NUnit XML; this is NOT a passing run. See $logPath"
}
[xml]$results = Get-Content -Raw -Encoding UTF8 -LiteralPath $xmlPath
$run = $results.'test-run'
$cases = @($results.SelectNodes('//test-case'))
$fixtureResults = @($results.SelectNodes('//test-suite[@type="TestFixture"]'))
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
    fixtures = @($fixtureResults | ForEach-Object {
        [ordered]@{ name = $_.name; total = [int]$_.total; passed = [int]$_.passed; failed = [int]$_.failed }
    })
}
$summary | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $session 'summary.json')
$found = @($fixtureResults | Where-Object { $_.name -eq $Fixture })
if ($found.Count -ne 1 -or [int]$found[0].total -le 0) {
    throw "Expected fixture was not executed: $Fixture. See $xmlPath"
}
if ([int]$found[0].total -ne $ExpectedCases) {
    throw "Fixture $Fixture ran $($found[0].total) cases, expected $ExpectedCases. See $xmlPath"
}
if ($cases.Count -ne [int]$run.total -or $cases.Count -eq 0 -or
    $run.result -ne 'Passed' -or [int]$run.failed -ne 0 -or [int]$run.skipped -ne 0 -or
    @($cases | Where-Object { $_.result -ne 'Passed' }).Count -ne 0 -or
    (Select-String -LiteralPath $logPath -Pattern 'error CS\d+|Scripts have compiler errors')) {
    $cases | Where-Object { $_.result -ne 'Passed' } | ForEach-Object {
        Write-Host ("FAIL: {0}: {1}" -f $_.fullname, $_.failure.message.InnerText)
    }
    throw "Stage-six acceptance failed; inspect $xmlPath and $logPath"
}
$summary.fixtures | ForEach-Object { Write-Host ("[sentry] {0}: {1}/{2} passed" -f $_.name, $_.passed, $_.total) }
Write-Host "SENTRY_MG_STAGE6_NUNIT_PASS: $($run.passed)/$($run.total); $xmlPath"
