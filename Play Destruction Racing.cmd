@echo off
rem Builds and starts the game: the title, the main menu (Championship, Single Event, Time Trial, Practice,
rem Options, Credits) and every event from there.
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
if not exist out mkdir out
echo Importing new assets (quick when nothing changed)...
".engine\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" --headless --path . --import > "out\last_import.log" 2>&1
echo Starting Destruction Racing...
rem "--path ." (not "%~dp0"): a trailing backslash before a quote breaks the argument.
start "" ".engine\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path . --log-file "out\last_run.log"
