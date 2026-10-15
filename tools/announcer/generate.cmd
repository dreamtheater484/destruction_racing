@echo off
rem Regenerates the announcer lines that changed in script.tsv (offline Kokoro TTS + stadium PA chain).
rem Extra arguments pass through: generate.cmd --force   /   generate.cmd --only big_hit_03   /   generate.cmd --check
cd /d "%~dp0..\.."
set HF_HUB_OFFLINE=1
if not exist ".engine\kokoro-venv\Scripts\python.exe" (
  echo The Kokoro venv is missing: .engine\kokoro-venv
  pause
  exit /b 1
)
".engine\kokoro-venv\Scripts\python.exe" "tools\announcer\generate.py" %*
pause
