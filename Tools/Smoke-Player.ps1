$ErrorActionPreference = 'Stop'
$afmProjectRoot = Split-Path -Parent $PSScriptRoot
$afmExecutable = Join-Path $afmProjectRoot 'Builds\Windows\AFMTeachingLab.exe'
$afmArgs = '-force-d3d11 -screen-width 1600 -screen-height 1000 -screen-fullscreen 0 -afmSmoke -afmOutput "' + $afmProjectRoot + '\Reports" -logFile "' + $afmProjectRoot + '\Reports\player.log"'
$afmPlayerProcess = Start-Process -FilePath $afmExecutable -ArgumentList $afmArgs -WindowStyle Hidden -PassThru -Wait
if ($afmPlayerProcess.ExitCode -ne 0) { throw 'AFM player smoke test failed. See Reports/player.log.' }
Get-Content -LiteralPath (Join-Path $afmProjectRoot 'Reports\player-smoke.json')
