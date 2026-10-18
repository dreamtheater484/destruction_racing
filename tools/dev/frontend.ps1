# Runs the front end windowed with scripted keys and a screenshot series, using the dev user folder
# (user://dev/, so the player's own settings and championships are never touched).
# Example: .\tools\dev\frontend.ps1 -Keys 'Enter,w2,Enter,Down,Enter' -Out out\m7\flow -Every 1.0 -Seconds 30 -Fresh
param(
    [string]$Keys = '',
    [string]$Out = 'out/m7/flow',
    [double]$Every = 1.0,
    [int]$Seconds = 30,
    [switch]$Fresh,
    [string]$Extra = ''
)
. "$PSScriptRoot\common.ps1"
& "$PSScriptRoot\build.ps1" | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'Build failed'; exit $LASTEXITCODE }
$dev = Join-Path $env:APPDATA 'Godot\app_userdata\Destruction Racing\dev'
if ($Fresh -and (Test-Path $dev)) { Remove-Item -Recurse -Force $dev }
$outDir = Join-Path $Root $Out
if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
New-Item -ItemType Directory -Force $outDir | Out-Null
$rel = $Out -replace '\\', '/'
$userArgs = @('--devuser', '--windowed', "--shot-every=$Every", "--shot-dir=$rel", "--quit-after=$($Seconds * 60)")
if ($Keys) { $userArgs += "--keys=$Keys" }
if ($Extra) { $userArgs += ($Extra -split ' ' | Where-Object { $_ }) }
$log = Join-Path $outDir 'run.log'
$p = Start-Process -FilePath $GodotConsole -ArgumentList (@('--path', '.', '--resolution', '1920x1080', '--fixed-fps', '60', '--') + $userArgs) `
    -WorkingDirectory $Root -PassThru -NoNewWindow -RedirectStandardOutput $log -RedirectStandardError (Join-Path $outDir 'err.log')
if (-not $p.WaitForExit(($Seconds + 60) * 1000)) { $p | Stop-Process -Force; Write-Host 'TIMEOUT' }
Get-Content $log | Select-String -Pattern 'ERROR|Exception|FrontEnd\]|\[Boot\]|Session' | Select-Object -First 60
Write-Host "shots: $((Get-ChildItem $outDir -Filter *.png).Count) in $Out"
