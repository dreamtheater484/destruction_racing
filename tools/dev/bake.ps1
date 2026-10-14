# Bakes the LightmapGI of a scene unattended (opens the editor, presses "Bake Lightmaps", saves, quits).
# Example: .\tools\dev\bake.ps1 -Scene res://scenes/test/LightmapProbe.tscn
param([Parameter(Mandatory = $true)][string]$Scene)
. "$PSScriptRoot\common.ps1"
& $GodotConsole --path $Root --editor -- "--autobake=$Scene"
exit $LASTEXITCODE
