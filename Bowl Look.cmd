@echo off
rem Builds and starts the M2 look of the Bowl (afternoon, baked lighting) with twelve generated cars.
rem V = next view, W/S move, arrows turn, P photo.
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
echo Starting the Bowl...
start "" ".engine\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path . --log-file "out\last_run.log" -- --scene=res://scenes/bowl/BowlLook.tscn
