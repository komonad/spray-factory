$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$root = Join-Path $project 'obj/runtime-checks'
[xml]$settings = Get-Content -LiteralPath (Join-Path $project 'Local.props') -Raw
$game = Split-Path (Split-Path ([string]$settings.Project.PropertyGroup.Managed) -Parent) -Parent
$source = Split-Path ([string]$settings.Project.PropertyGroup.BepInExCore) -Parent
$bep = Join-Path $root 'profile/BepInEx'
New-Item -ItemType Directory -Path $bep -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $source 'core') -Destination $bep -Recurse -Force
$plugins = Join-Path $bep 'plugins'
New-Item -ItemType Directory -Path $plugins -Force | Out-Null
foreach ($dependency in @('CommonAPI-CommonAPI','CommonAPI-DSPModSave','xiaoye97-LDBTool','nebula-NebulaMultiplayerModApi')) {
    Copy-Item -LiteralPath (Join-Path $source ('plugins/' + $dependency)) -Destination $plugins -Recurse -Force
}
Copy-Item -LiteralPath (Join-Path $project 'bin/Release/SprayFactory.dll') -Destination $plugins -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/Release/RuntimeChecks.dll') -Destination $plugins -Force
$resultFile = Join-Path $plugins 'results.txt'
if (Test-Path -LiteralPath $resultFile) { Remove-Item -LiteralPath $resultFile }
$arguments = @('-batchmode','-screen-width','1280','-screen-height','720','-logFile',('"' + (Join-Path $root 'Player.log') + '"'),'--doorstop-enable','true','--doorstop-target',('"' + (Join-Path $bep 'core/BepInEx.Preloader.dll') + '"'))
$process = $null
try {
    for ($attempt = 0; $attempt -lt 2; $attempt++) {
        $process = Start-Process -FilePath (Join-Path $game 'DSPGAME.exe') -ArgumentList $arguments -WorkingDirectory $game -WindowStyle Hidden -PassThru -Environment @{SteamAppId='1366540';SteamGameId='1366540'}
        if (!$process.WaitForExit(2000)) { break }
        $process.Dispose(); $process = $null
    }
    if (!$process) { throw 'DSP exited before test startup.' }
    Write-Output "Runtime checks PID=$($process.Id) output=$plugins"
    $process.Id | Set-Content (Join-Path $root 'pid.txt')
    $elapsed = [Diagnostics.Stopwatch]::StartNew()
    while (!$process.WaitForExit(1000)) {
        if ($elapsed.Elapsed.TotalSeconds -gt 180) { throw 'DSP runtime checks timed out.' }
    }
    if (!(Test-Path -LiteralPath $resultFile)) { throw 'DSP exited without fresh test results.' }
    Get-Content -LiteralPath $resultFile -Tail 12
    if ((Get-Content -LiteralPath $resultFile -Tail 1) -ne 'Failures=0') { throw 'DSP runtime checks failed.' }
} finally {
    if ($process) {
        if (!$process.HasExited) { Stop-Process -Id $process.Id }
        $process.Dispose()
    }
}
