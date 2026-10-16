@echo off
rem Builds and starts the Crossroads Speedway set-up screen: Wreckin' Racing, Stock Car Racing or Time Trial,
rem on the oval or the figure-8, forward or reverse, in any of the nine cars.
cd /d "%~dp0"
echo Building...
dotnet build DestructionRacing.csproj -nologo -v q
if errorlevel 1 (
  echo.
  echo Build failed - see the messages above.
  pause
  exit /b 1
)
if not exist ".engine\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" (
  echo Godot was not found in .engine\godot - see docs\DEVELOPMENT.md
  pause
  exit /b 1
)
echo Starting Crossroads Speedway...
rem "--path ." (not "%~dp0"): a trailing backslash before a quote breaks the argument.
start "" ".engine\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path . --log-file "out\last_run.log" -- --scene=res://scenes/race/RaceSetup.tscn
