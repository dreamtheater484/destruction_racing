#!/usr/bin/env bash
# Car balance sims (plan §9.3, M5): N derbies and N races with --randomcars, split over P parallel headless
# Godot processes, then the per-car report (tools/dev/balance_report.py).
# Usage: tools/dev/balance.sh [events=200] [parallel=8] [tag=m5] [extra Godot user args...]
# Logs: out/sims/balance_<tag>_{derby,race}_<k>.log   Report: out/sims/balance_<tag>.md
set -u
cd "$(dirname "$0")/../.."
EVENTS=${1:-200}
PAR=${2:-8}
TAG=${3:-m5}
shift $(( $# < 3 ? $# : 3 ))
EXTRA=("$@")
GODOT=".engine/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"
mkdir -p out/sims
for mode in derby race; do
  scene=res://scenes/test/DerbySim.tscn
  # A race takes about 3x as long to simulate as a derby: a quarter of the processes for the derbies and the
  # rest for the races finish at about the same time.
  n=$(( PAR / 4 > 0 ? PAR / 4 : 1 ))
  [ "$mode" = race ] && { scene=res://scenes/test/WreckinSim.tscn; n=$(( PAR - n > 0 ? PAR - n : 1 )); }
  per=$(( (EVENTS + n - 1) / n ))
  for ((k = 0; k < n; k++)); do
    seed=$(( 1 + k * per ))
    "$GODOT" --path . --headless --fixed-fps 120 -- "--scene=$scene" "--sims=$per" "--seed=$seed" --randomcars \
      "${EXTRA[@]}" > "out/sims/balance_${TAG}_${mode}_${k}.log" 2>&1 &
  done
done
wait
python tools/dev/balance_report.py "out/sims/balance_${TAG}_derby_*.log" "out/sims/balance_${TAG}_race_*.log" \
  --md "out/sims/balance_${TAG}.md"
