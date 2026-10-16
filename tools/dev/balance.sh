#!/usr/bin/env bash
# Car balance sims (plan §9.3, M5/M6): N derbies, N Wreckin' races and N Stock Car races with --randomcars, split
# over P parallel headless Godot processes, then the per-car report (tools/dev/balance_report.py).
# Usage: tools/dev/balance.sh [events=200] [parallel=8] [tag=m5] [extra Godot user args...]
# Logs: out/sims/balance_<tag>_{derby,race,stockcar}_<k>.log   Report: out/sims/balance_<tag>.md
set -u
cd "$(dirname "$0")/../.."
EVENTS=${1:-200}
PAR=${2:-8}
TAG=${3:-m5}
shift $(( $# < 3 ? $# : 3 ))
EXTRA=("$@")
GODOT=".engine/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"
mkdir -p out/sims
for mode in derby race stockcar; do
  scene=res://scenes/test/DerbySim.tscn
  kind=()
  # A race takes about 2.5x as long to simulate as a derby (M6: 75 s against 30 s a process): a fifth of the
  # processes for the derbies and the rest split over the two kinds of race finish at about the same time.
  nd=$(( PAR / 5 > 0 ? PAR / 5 : 1 ))
  n=$nd
  [ "$mode" != derby ] && { scene=res://scenes/test/RaceSim.tscn; n=$(( (PAR - nd) / 2 > 0 ? (PAR - nd) / 2 : 1 )); }
  [ "$mode" = stockcar ] && kind=(--kind=stockcar)
  per=$(( (EVENTS + n - 1) / n ))
  for ((k = 0; k < n; k++)); do
    seed=$(( 1 + k * per ))
    "$GODOT" --path . --headless --fixed-fps 120 -- "--scene=$scene" "--sims=$per" "--seed=$seed" --randomcars \
      "${kind[@]}" "${EXTRA[@]}" > "out/sims/balance_${TAG}_${mode}_${k}.log" 2>&1 &
  done
done
wait
python tools/dev/balance_report.py "out/sims/balance_${TAG}_derby_*.log" "out/sims/balance_${TAG}_race_*.log" \
  "out/sims/balance_${TAG}_stockcar_*.log" --md "out/sims/balance_${TAG}.md"
