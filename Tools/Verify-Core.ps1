$ErrorActionPreference = 'Stop'
$afmProjectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $afmProjectRoot '.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:MSBuildEnableWorkloadResolver = 'false'
Push-Location $afmProjectRoot
try {
    dotnet run --project Tools/CoreChecks/CoreChecks.csproj -c Release -- verify Reports/core-checks.json
    if ($LASTEXITCODE -ne 0) { throw 'AFM core verification failed.' }
} finally { Pop-Location }
