$ErrorActionPreference = 'Stop'

$versionPath = Join-Path $PSScriptRoot 'ProjectSettings/ProjectVersion.txt'
$version = (Select-String -LiteralPath $versionPath -Pattern '^m_EditorVersion: (\S+)$').Matches.Groups[1].Value
$editorPath = Join-Path ${env:ProgramFiles} "Unity $version/Editor/Unity.exe"
if (-not (Test-Path -LiteralPath $editorPath -PathType Leaf)) {
    throw "Unity $version was not found at '$editorPath'."
}

$scenePath = Join-Path $PSScriptRoot 'Assets/Scenes/EditorWorkspace.unity'
Start-Process -FilePath $editorPath -ArgumentList '-openfile', "`"$scenePath`""
