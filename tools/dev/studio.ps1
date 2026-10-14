# Renders photo-studio shots of a generated car (re-imports changed assets first).
# Example: .\tools\dev\studio.ps1 -Car kitsune -Presets studio,outdoor -Modes normal,zebra -Views q34f,side
param(
    [string]$Car = 'kitsune',
    [string]$Presets = 'studio,outdoor',
    [string]$Modes = 'normal',
    [string]$Views = 'q34f,side,q34r,close',
    [string]$Out = 'out/studio',
    [int]$Livery = 0,
    [string]$Damage = 'none',
    [switch]$NoBuild
)
. "$PSScriptRoot\common.ps1"
if (-not $NoBuild) {
    $build = & "$PSScriptRoot\build.ps1" 2>&1
    if ($LASTEXITCODE -ne 0) { $build | Select-String -Pattern 'error' | Select-Object -First 20; exit $LASTEXITCODE }
}
& $GodotConsole --headless --path $Root --import 2>&1 | Out-Null
$userArgs = @('--path', "`"$Root`"", '--resolution', '1920x1080', '--', '--scene=res://scenes/test/PhotoStudio.tscn',
              "--car=$Car", "--shots=$Out", "--presets=$Presets", "--modes=$Modes", "--views=$Views", "--livery=$Livery",
              "--damage=$Damage")
$log = Join-Path $Root 'out\studio_run.log'
$p = Start-Process -FilePath $GodotConsole -ArgumentList $userArgs -RedirectStandardOutput $log `
    -RedirectStandardError (Join-Path $Root 'out\studio_err.log') -PassThru -WindowStyle Normal
if (-not $p.WaitForExit(300000)) { Stop-Process -Id $p.Id -Force; "studio: timed out" }
Get-Content $log | Select-String -Pattern 'PhotoStudio\]|ERROR|SCRIPT ERROR' | ForEach-Object { $_.Line }
