<#
    EditMode acceptance for the ceiling trap placement and economy flow.
    UNITY_INCLUDE_TESTS is enabled for this run only; Unity project settings are untouched.
    Success requires the expected fixture and case count in NUnit XML, all passed with none skipped,
    and no C# compiler errors. Unity's process exit code alone is not used as the result.
#>
[CmdletBinding()]
param(
    [string]$ProjectPath = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$OutputDirectory,
    [ValidateRange(1, 200)][int]$ExpectedCases = 17,
    [ValidateRange(30, 1800)][int]$TimeoutSeconds = 600,
    [switch]$WithGraphics
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
    $OutputDirectory = Join-Path ([Environment]::GetFolderPath('Desktop')) '陷阱素材文件夹\CeilingTrapSystem'
}
$session = Join-Path $OutputDirectory (Get-Date -Format 'yyyyMMdd-HHmmss-fff')
New-Item -ItemType Directory -Path $session -Force | Out-Null
$session = (Resolve-Path -LiteralPath $session).Path
$logPath = Join-Path $session 'editmode.log'
$xmlPath = Join-Path $session 'editmode-results.xml'
$graphicsArgument = if ($WithGraphics) { '' } else { '-nographics ' }
$arguments = '-batchmode {0}-projectPath "{1}" -runTests -testPlatform EditMode -testFilter CeilingTrapPlacementTests -extraScriptingDefines=UNITY_INCLUDE_TESTS -testResults "{2}" -logFile "{3}"' -f $graphicsArgument, $ProjectPath, $xmlPath, $logPath
Write-Host "[ceiling-traps] Unity $version; NUnit output: $xmlPath"
$process = Start-Process -FilePath $unity -ArgumentList $arguments -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $session 'stdout.txt') -RedirectStandardError (Join-Path $session 'stderr.txt')
$null = $process.Handle
if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
    Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
    throw "Ceiling trap tests timed out after $TimeoutSeconds seconds. See $logPath"
}
$process.Refresh()
if (!(Test-Path -LiteralPath $xmlPath)) {
    throw "Unity exit code $($process.ExitCode) produced no NUnit XML; this is NOT a passing run. See $logPath"
}
[xml]$results = Get-Content -Raw -Encoding UTF8 -LiteralPath $xmlPath
$run = $results.'test-run'
$cases = @($results.SelectNodes('//test-case'))
$fixture = @($results.SelectNodes('//test-suite[@type="TestFixture"]') | Where-Object { $_.name -eq 'CeilingTrapPlacementTests' })
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
}
$summary | ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $session 'summary.json')
if ($fixture.Count -ne 1 -or [int]$fixture[0].total -ne $ExpectedCases) {
    throw "Expected $ExpectedCases CeilingTrapPlacementTests cases, found $(if ($fixture.Count) { $fixture[0].total } else { 0 }). See $xmlPath"
}
if ($cases.Count -ne [int]$run.total -or $cases.Count -eq 0 -or $run.result -ne 'Passed' -or
    [int]$run.failed -ne 0 -or [int]$run.skipped -ne 0 -or
    @($cases | Where-Object { $_.result -ne 'Passed' }).Count -ne 0 -or
    (Select-String -LiteralPath $logPath -Pattern 'error CS\d+|Scripts have compiler errors')) {
    $cases | Where-Object { $_.result -ne 'Passed' } | ForEach-Object {
        Write-Host ("FAIL: {0}: {1}" -f $_.fullname, $_.failure.message.InnerText)
    }
    throw "Ceiling trap acceptance failed; inspect $xmlPath and $logPath"
}
Write-Host ("[ceiling-traps] CeilingTrapPlacementTests: {0}/{1} passed" -f $run.passed, $run.total)
Write-Host "CEILING_TRAP_NUNIT_PASS: $($run.passed)/$($run.total); $xmlPath"
