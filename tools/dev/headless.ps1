# Builds, then runs a scene headless as fast as possible (--fixed-fps 120 = one physics step per frame,
# no real-time sync) and prints the output lines that match -Filter. Exit code is Godot's.
# Example: .\tools\dev\headless.ps1 -Scene res://scenes/test/DdCodeTest.tscn -UserArgs '--tactic=all' -Filter 'DDCode'
param(
    [Parameter(Mandatory = $true)][string]$Scene,
    [string[]]$UserArgs = @(),
    [string]$Filter = '.',
    [switch]$NoBuild
)
. "$PSScriptRoot\common.ps1"
if (-not $NoBuild) {
    $build = & "$PSScriptRoot\build.ps1" 2>&1
    if ($LASTEXITCODE -ne 0) { $build | Select-String -Pattern 'error' | Select-Object -First 20; exit $LASTEXITCODE }
}
$allArgs = @("--scene=$Scene") + $UserArgs
& $GodotConsole --path $Root --headless --fixed-fps 120 -- @allArgs 2>&1 | ForEach-Object { "$_" } | Select-String -Pattern $Filter
exit $LASTEXITCODE
