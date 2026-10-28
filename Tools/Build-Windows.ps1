param([string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\6000.5.7f1\Editor\Unity.exe')
$ErrorActionPreference = 'Stop'
$afmProjectRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path -LiteralPath $UnityEditor)) { throw "Unity Editor not found: $UnityEditor" }
# Unity Package Manager requires the normal desktop user cache environment.
$env:LOCALAPPDATA = Join-Path $env:USERPROFILE 'AppData\Local'
$env:APPDATA = Join-Path $env:USERPROFILE 'AppData\Roaming'
$env:ALLUSERSPROFILE = 'C:\ProgramData'
$env:TEMP = Join-Path $env:LOCALAPPDATA 'Temp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Path (Join-Path $afmProjectRoot 'Reports') -Force | Out-Null
$afmArgs = '-batchmode -nographics -quit -projectPath "' + $afmProjectRoot + '" -executeMethod AfmLab.Editor.LabBuilder.BuildWindows -logFile "' + $afmProjectRoot + '\Reports\unity-build.log" -upmLogFile "' + $afmProjectRoot + '\Reports\upm.log"'
$afmBuildProcess = Start-Process -FilePath $UnityEditor -ArgumentList $afmArgs -WindowStyle Hidden -PassThru -Wait
if ($afmBuildProcess.ExitCode -ne 0) { throw 'Unity build failed. See Reports/unity-build.log.' }
