@echo off
rem Builds and starts the M2 car photo studio: the three generated cars, damage, zebra and clay checks.
rem Tab = next car, B = derby damage, X = repair, arrows orbit, W/S zoom, 1/2/3 lighting. Keys are on screen.
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
echo Starting the car studio...
start "" ".engine\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path . --log-file "out\last_run.log" -- --scene=res://scenes/test/PhotoStudio.tscn --preset=outdoor
