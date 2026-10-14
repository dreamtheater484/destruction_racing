# Renders the M2 art-gate set into out/gate: the three cars (clean, zebra check, derby damage) and the Bowl.
# Example: .\tools\dev\gate_m2.ps1
param([string]$Out = 'out/gate')
. "$PSScriptRoot\common.ps1"
$build = & "$PSScriptRoot\build.ps1" 2>&1
if ($LASTEXITCODE -ne 0) { $build | Select-String -Pattern 'error' | Select-Object -First 20; exit $LASTEXITCODE }
foreach ($car in 'kitsune', 'brougham', 'nordvik') {
    & "$PSScriptRoot\studio.ps1" -Car $car -Presets 'studio,outdoor' -Modes 'normal,zebra' -Views 'q34f,side,q34r' -Out "$Out/$car/clean" -NoBuild
    & "$PSScriptRoot\studio.ps1" -Car $car -Presets 'outdoor' -Views 'q34f,q34r,close' -Damage derby -Out "$Out/$car/derby" -NoBuild
}
$userArgs = @('--path', "`"$Root`"", '--resolution', '1920x1080', '--', '--scene=res://scenes/bowl/BowlLook.tscn',
              "--shots=$Out/bowl", '--views=pileup,closeup,hero,stands,overview')
$p = Start-Process -FilePath $GodotConsole -ArgumentList $userArgs -RedirectStandardOutput (Join-Path $Root 'out\bowl_run.log') `
    -RedirectStandardError (Join-Path $Root 'out\bowl_err.log') -PassThru
if (-not $p.WaitForExit(300000)) { Stop-Process -Id $p.Id -Force; "bowl: timed out" }
Get-Content (Join-Path $Root 'out\bowl_run.log') | Select-String -Pattern 'BowlLook\]|ERROR' | ForEach-Object { $_.Line }
