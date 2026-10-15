@echo off
rem Builds and starts the M3 damage lab: a parked car in the Bowl takes real hits from ram cars.
rem 1 = front ram, 2 = rear ram, 3/4 = left/right T-bone, 5 = corner (Shift = harder), 6 = wall scrape,
rem 7 = roll it, 8 = wreck it, 9 = knock a wheel off, R = fresh car, Tab = next model,
rem Space = slow motion, arrows and W/S = camera, Esc = quit.
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
echo Starting the damage lab...
start "" ".engine\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path . --log-file "out\last_run.log" -- --scene=res://scenes/test/DamageLab.tscn
