# Builds, runs a scene windowed, saves a screenshot and quits.
# Example: .\tools\dev\capture.ps1 -Out out\shots\arena.png -Scene res://scenes/test/GreyBoxArena.tscn
param(
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$Scene = '',
    [int]$Frame = 120,
    [string]$Resolution = '1920x1080'
)
. "$PSScriptRoot\common.ps1"
& "$PSScriptRoot\build.ps1"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$outPath = [System.IO.Path]::GetFullPath((Join-Path $Root $Out)) -replace '\\', '/'
$userArgs = @("--capture=$outPath", "--capture-frame=$Frame")
if ($Scene) { $userArgs += "--scene=$Scene" }
& $GodotConsole --path $Root --resolution $Resolution -- @userArgs
exit $LASTEXITCODE
