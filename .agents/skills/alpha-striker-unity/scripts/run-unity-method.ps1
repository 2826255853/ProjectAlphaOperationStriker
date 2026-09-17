<#
    在 ProjectAlphaOperationStriker 里批处理执行一个 Unity 编辑器静态方法，并按日志判定真实成败。

    为什么需要它：Unity 的进程退出码不可信。本机实测：成功的运行返回 0，但被中止的运行
    也常返回 1（成功但日志被截断时也可能返回 0）。唯一可靠的判据是日志内容
    "Exiting batchmode successfully now!"。所以本脚本只看日志，退出码仅作参考打印。

    用法：
        .\run-unity-method.ps1 MapForgeWorldImporter.ImportDefault
        .\run-unity-method.ps1 GenerateLadderPrefab.Generate
        .\run-unity-method.ps1 PlaceGameplayLadders.Place -WithGraphics

    退出码：0 = 日志确认成功；1 = 日志确认失败；2 = 环境问题（找不到 Unity 等）。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Method,

    [string]$ProjectPath,
    [string]$UnityPath,
    [string]$LogFile,
    [int]$TimeoutSeconds = 1200,
    [string[]]$ExtraArgs = @(),
    [switch]$WithGraphics
)

$ErrorActionPreference = 'Stop'

# --- 项目根目录：<root>/.agents/skills/<skill>/scripts/run-unity-method.ps1 ---
if (-not $ProjectPath) {
    $here = Split-Path -Parent $MyInvocation.MyCommand.Path
    $ProjectPath = (Resolve-Path -LiteralPath (Join-Path $here '..\..\..\..')).Path
}
if (-not (Test-Path -LiteralPath $ProjectPath)) {
    Write-Host ("[unity] 项目路径不存在：{0}" -f $ProjectPath) -ForegroundColor Red
    exit 2
}

# --- Unity 可执行文件：优先用 ProjectSettings/ProjectVersion.txt 指定的版本 ---
if (-not $UnityPath) {
    $version = $null
    $versionFile = Join-Path $ProjectPath 'ProjectSettings\ProjectVersion.txt'
    if (Test-Path -LiteralPath $versionFile) {
        $match = Select-String -LiteralPath $versionFile -Pattern '^m_EditorVersion:\s*(\S+)' | Select-Object -First 1
        if ($match) { $version = $match.Matches[0].Groups[1].Value }
    }
    $candidates = @()
    if ($version) { $candidates += "C:\Program Files\Unity\Hub\Editor\Unity $version\Editor\Unity.exe" }
    $hub = 'C:\Program Files\Unity\Hub\Editor'
    if (Test-Path -LiteralPath $hub) {
        $candidates += @(Get-ChildItem -LiteralPath $hub -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'Editor\Unity.exe' })
    }
    $UnityPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $UnityPath) {
    Write-Host '[unity] 找不到 Unity 编辑器，用 -UnityPath 指定 Unity.exe。' -ForegroundColor Red
    exit 2
}

# --- Library/ 单实例锁：同时开着编辑器时批处理会失败 ---
$running = @(Get-Process -Name 'Unity' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    $pids = ($running | ForEach-Object { $_.Id }) -join ', '
    Write-Host ("[unity] 警告：检测到正在运行的 Unity 进程（PID {0}）。同一时刻只有一个实例能持有 Library/，批处理很可能失败。" -f $pids) -ForegroundColor Yellow
}

# --- 日志文件：默认写到临时目录，避免污染仓库（每次先删掉旧日志，防止读到上一次的成功标记） ---
$slug = ($Method -replace '[^A-Za-z0-9]+', '-').Trim('-').ToLowerInvariant()
if (-not $LogFile) {
    $logDir = Join-Path $env:TEMP 'alpha-striker-unity'
    if (-not (Test-Path -LiteralPath $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
    $LogFile = Join-Path $logDir ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $slug + '.log')
}
$LogFile = [System.IO.Path]::GetFullPath($LogFile)
if (Test-Path -LiteralPath $LogFile) { [System.IO.File]::Delete($LogFile) }

$unityArgs = @('-batchmode')
if (-not $WithGraphics) { $unityArgs += '-nographics' }
$unityArgs += @('-quit', '-projectPath', ('"' + $ProjectPath + '"'), '-executeMethod', $Method, '-logFile', ('"' + $LogFile + '"'))
$unityArgs += $ExtraArgs

Write-Host ("[unity] {0}" -f $UnityPath)
Write-Host ("[unity] -executeMethod {0}" -f $Method)
Write-Host ("[unity] 日志 {0}" -f $LogFile)

# Unity 把 "Aborting batchmode due to failure: ..." 写到 stdout 而不是日志文件，
# 所以控制台输出也要抓下来一起判定。
$stdoutFile = $LogFile + '.stdout.txt'
$stderrFile = $LogFile + '.stderr.txt'
foreach ($f in @($stdoutFile, $stderrFile)) { if (Test-Path -LiteralPath $f) { [System.IO.File]::Delete($f) } }

$process = Start-Process -FilePath $UnityPath -ArgumentList $unityArgs -NoNewWindow -PassThru `
    -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    try { $process.Kill() } catch { }
    Write-Host ("[unity] 超过 {0} 秒仍未结束，已终止。" -f $TimeoutSeconds) -ForegroundColor Red
    exit 1
}
$exitCode = $process.ExitCode

# --- 只按日志判定成败 ---
# Unity 刚退出时日志句柄可能还没释放（实测在 "Aborting batchmode due to failure" 时会锁住），
# 所以要重试，不能直接 ReadAllText，否则脚本自己抛异常、退出码也拿不到。
function Read-LogText([string]$path) {
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try {
            return [System.IO.File]::ReadAllText($path)
        } catch [System.IO.IOException] {
            Start-Sleep -Milliseconds 250
        } catch [System.UnauthorizedAccessException] {
            Start-Sleep -Milliseconds 250
        }
    }
    # 兜底：只读到能读的部分，避免整个脚本失败
    try {
        $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try {
            $reader = New-Object System.IO.StreamReader($stream)
            return $reader.ReadToEnd()
        } finally { $stream.Dispose() }
    } catch {
        Write-Host '[unity] 无法读取日志文件（被占用）。' -ForegroundColor Red
        return ''
    }
}

$text = ''
if (Test-Path -LiteralPath $LogFile) { $text = Read-LogText $LogFile }
$sizeKb = [math]::Round(([System.Text.Encoding]::UTF8.GetByteCount($text)) / 1KB, 1)

# 控制台那一路（含 "Aborting batchmode due to failure:"）拼在日志后面一起看。
$consoleText = ''
foreach ($f in @($stdoutFile, $stderrFile)) {
    if (Test-Path -LiteralPath $f) { $consoleText += (Read-LogText $f) + "`n" }
}
$allText = $text + "`n" + $consoleText
if ($consoleText.Trim()) {
    Write-Host '[unity] 控制台输出：'
    foreach ($line in ($consoleText -split "`n" | Where-Object { $_.Trim() })) { Write-Host ("    " + $line.Trim()) }
}

$finished = $text -match 'Exiting batchmode successfully now!'
$compileErrors = @()
$exceptions = @()
if ($text) {
    $compileErrors = @(Select-String -LiteralPath $LogFile -Pattern 'error CS' -SimpleMatch -ErrorAction SilentlyContinue)
    $exceptions = @(Select-String -LiteralPath $LogFile -Pattern 'Exception:' -SimpleMatch -ErrorAction SilentlyContinue)
}

Write-Host ("[unity] 进程退出码 {0}（仅供参考，不能用来判定成败）；日志 {1} KB" -f $exitCode, $sizeKb)

$reasons = @()
if (-not $finished) { $reasons += '日志里没有 "Exiting batchmode successfully now!"，方法没有跑完' }
$abortMatch = [regex]::Match($allText, 'Aborting batchmode due to failure[^\r\n]*[\r\n]+([^\r\n]*)')
if ($abortMatch.Success) {
    $detail = $abortMatch.Groups[1].Value.Trim()
    $reasons += ('Unity 主动中止批处理：' + $abortMatch.Value.Split("`n")[0].Trim().TrimEnd(':') + ($(if ($detail) { ' / ' + $detail } else { '' })))
}
if ($allText -match 'executeMethod class .+ could not be found') {
    $reasons += '-executeMethod 的类型/方法名写错了（只能用 类型名.方法名 的静态方法）'
}
if ($compileErrors.Count -gt 0) { $reasons += ("出现 {0} 处编译错误（error CS）" -f $compileErrors.Count) }
if ($exceptions.Count -gt 0) { $reasons += ("出现 {0} 处异常（Exception:）" -f $exceptions.Count) }
if ($sizeKb -lt 3) { $reasons += ("日志只有 {0} KB，通常是启动或脚本编译阶段就中止，-executeMethod 根本没执行" -f $sizeKb) }

if ($sizeKb -gt 0) {
    Write-Host '[unity] 日志末尾 10 行：'
    $tail = @(Get-Content -LiteralPath $LogFile -Tail 10 -ErrorAction SilentlyContinue)
    foreach ($line in $tail) { Write-Host ("    " + $line) }
}

if ($reasons.Count -eq 0) {
    Write-Host '[unity] 结果：成功（日志确认方法已跑完，无编译错误、无异常）。' -ForegroundColor Green
    exit 0
}

Write-Host '[unity] 结果：失败。' -ForegroundColor Red
foreach ($reason in $reasons) { Write-Host ("    - " + $reason) -ForegroundColor Red }
foreach ($hit in @($compileErrors | Select-Object -First 5)) {
    Write-Host ("    编译错误 L{0}: {1}" -f $hit.LineNumber, $hit.Line.Trim()) -ForegroundColor Red
}
foreach ($hit in @($exceptions | Select-Object -First 5)) {
    Write-Host ("    异常 L{0}: {1}" -f $hit.LineNumber, $hit.Line.Trim()) -ForegroundColor Red
}
exit 1