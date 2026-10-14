# Imports project resources headlessly (creates/refreshes the .godot folder).
. "$PSScriptRoot\common.ps1"
& $GodotConsole --headless --path $Root --import
exit $LASTEXITCODE
