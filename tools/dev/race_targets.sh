#!/usr/bin/env bash
# Race sims against the plan §17.4 targets: Wreckin' and Stock Car on the oval and the figure-8, each spread over
# P headless processes of N races (different seed ranges), merged per configuration by race_report.py.
# Usage: tools/dev/race_targets.sh [races per process=8] [processes per config=4] [tag=m6] [extra Godot user args...]
# Each process writes its own report and .json into out/sims/<tag>/; the merged reports are out/sims/<tag>_*.md.
set -u
cd "$(dirname "$0")/../.."
N=${1:-8}
P=${2:-4}
TAG=${3:-m6}
shift $(( $# < 3 ? $# : 3 ))
EXTRA=("$@")
GODOT=".engine/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"
mkdir -p "out/sims/$TAG"
for kind in wreckin stockcar; do
  for layout in oval figure8; do
    for ((k = 0; k < P; k++)); do
      "$GODOT" --path . --headless --fixed-fps 120 -- --scene=res://scenes/test/RaceSim.tscn "--sims=$N" "--seed=$((1 + k * N))" \
        "--kind=$kind" "--layout=$layout" "--outdir=out/sims/$TAG/${kind}_${layout}_$k" "${EXTRA[@]}" \
        > "out/sims/$TAG/${kind}_${layout}_$k.log" 2>&1 &
    done
  done
done
wait
for kind in wreckin stockcar; do
  for layout in oval figure8; do
    flags=(); [ "$kind" = stockcar ] && flags+=(--stockcar); [ "$layout" = figure8 ] && flags+=(--crossover)
    python tools/dev/race_report.py "out/sims/$TAG/${kind}_${layout}_*/*.json" "${flags[@]}" \
      --label "$kind $layout" --md "out/sims/${TAG}_${kind}_${layout}.md"
  done
done
