param([int]$Width=1600,[int]$Height=1000,[string]$ReportFolder='Reports')
$ErrorActionPreference = 'Stop'
$afmProjectRoot = Split-Path -Parent $PSScriptRoot
$afmExecutable = Join-Path $afmProjectRoot 'Builds\Windows\AFMTeachingLab.exe'
$afmReports=Join-Path $afmProjectRoot $ReportFolder
New-Item -ItemType Directory -Path $afmReports -Force | Out-Null
$afmArgs = '-screen-width ' + $Width + ' -screen-height ' + $Height + ' -screen-fullscreen 0 -afmSmoke -afmOutput "' + $afmReports + '" -logFile "' + $afmReports + '\player.log"'
$afmPlayerProcess = Start-Process -FilePath $afmExecutable -ArgumentList $afmArgs -WindowStyle Hidden -PassThru -Wait
if ($afmPlayerProcess.ExitCode -ne 0) { throw "AFM player smoke test failed. See $afmReports\player.log." }
Get-Content -LiteralPath (Join-Path $afmReports 'player-smoke.json')
