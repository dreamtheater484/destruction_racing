@echo off
rem Builds and starts the M2 fog and smoke test: a burning car laps the Bowl through dust haze.
rem C = chase / fixed camera.
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
echo Starting the fog test...
start "" ".engine\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path . --log-file "out\last_run.log" -- --scene=res://scenes/test/FogTest.tscn
