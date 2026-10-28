$ErrorActionPreference='Stop'
$afmRoot=Split-Path -Parent $PSScriptRoot
Push-Location $afmRoot
try {
    $afmFiles=rg --files Assets ProjectSettings Packages Tools -g '*.cs' -g '*.uss' -g '*.tss' -g '*.unity' -g '*.asset' -g '*.mat' -g '*.meta' -g '*.ttf' -g '*.json' -g '*.txt' -g '*.Config' -g '*.csproj' -g '*.ps1' -g '!**/bin/**' -g '!**/obj/**'
    $afmHashes=@($afmFiles | Sort-Object | ForEach-Object {
        $afmHash=Get-FileHash -LiteralPath $_ -Algorithm SHA256
        [ordered]@{path=$_.Replace('\','/');sha256=$afmHash.Hash.ToLowerInvariant()}
    })
    $afmPlayerHash=Get-FileHash -LiteralPath 'Builds/Windows/AFMTeachingLab_Data/Managed/Assembly-CSharp.dll' -Algorithm SHA256
    $afmUnityVersion=((Get-Content ProjectSettings/ProjectVersion.txt -First 1) -split ': ')[1]
    $afmUrpVersion=(Get-Content Packages/manifest.json -Raw | ConvertFrom-Json).dependencies.'com.unity.render-pipelines.universal'
    $afmManifest=[ordered]@{
        capturedUtc=(Get-Date).ToUniversalTime().ToString('o')
        product='AFM Teaching Lab';version='0.1.0';unity=$afmUnityVersion;renderPipeline=('URP '+$afmUrpVersion)
        platform='Windows x64';backend='Mono';scene='Assets/AFM/Scenes/AFMLab.unity'
        executable='Builds/Windows/AFMTeachingLab.exe';distribution='Keep the entire Builds/Windows directory together'
        managedPlayerSha256=$afmPlayerHash.Hash.ToLowerInvariant()
        build=(Get-Content Reports/build.json -Raw | ConvertFrom-Json)
        coreChecks=(Get-Content Reports/core-checks.json -Raw | ConvertFrom-Json | Select-Object passed,total,utc)
        scanMatrix=(Get-Content Reports/scan-matrix.json -Raw | ConvertFrom-Json | Select-Object passed,utc)
        player1600=(Get-Content Reports/player-smoke.json -Raw | ConvertFrom-Json)
        player1280=(Get-Content Reports/Layout1280/player-smoke.json -Raw | ConvertFrom-Json)
        files=$afmHashes
    }
    $afmManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath 'Reports/release-manifest.json' -Encoding utf8
    Write-Output 'Saved Reports/release-manifest.json'
} finally { Pop-Location }
