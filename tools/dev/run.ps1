# Builds and runs the game. Optional: -Scene res://scenes/....tscn
param([string]$Scene = '')
. "$PSScriptRoot\common.ps1"
& "$PSScriptRoot\build.ps1"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$userArgs = @()
if ($Scene) { $userArgs += "--scene=$Scene" }
& $Godot --path $Root -- @userArgs
