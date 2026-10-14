# Shared paths for the dev scripts. Dot-source this file: . "$PSScriptRoot\common.ps1"
# Godot writes normal log lines to stderr; with 'Stop', Windows PowerShell 5.1 would treat them as fatal.
$ErrorActionPreference = 'Continue'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$GodotDir = Join-Path $Root '.engine\godot\Godot_v4.7.2-stable_mono_win64'
$Godot = Join-Path $GodotDir 'Godot_v4.7.2-stable_mono_win64.exe'
$GodotConsole = Join-Path $GodotDir 'Godot_v4.7.2-stable_mono_win64_console.exe'

if (-not (Test-Path $GodotConsole)) {
    throw "Godot not found at $GodotConsole. See IMPLEMENTATION_PLAN.md §2.2."
}
