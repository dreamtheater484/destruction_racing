#!/usr/bin/env bash
# Championship sims (plan §17.4: seasons decided before the Bowl finale <= 30%, promotion attempts 1-2 per division).
# For each player division (4..1) it runs the events of that division's calendar with the real field (the drivers in
# their own cars), the PlayerProxy in a chosen car and the AI at that division's pace: Wreckin' and Stock Car races
# on each layout and direction of the calendar, and derbies. tools/dev/championship_report.py then plays thousands of
# careers by drawing each round from these events.
# Usage: tools/dev/championship.sh [races=10] [derbies=20] [parallel=16] [tag=m7] [root=.]
#   races: per layout and direction of a division's calendar; root: the project folder to run (a snapshot copy lets
#   you keep building while the sims run). WCAR / SCAR (env): the proxy's car in Wreckin' and Stock Car.
# Logs: out/sims/champ_<tag>/<kind>_d<div>_<layout>_<dir>_<k>.log   Report: out/sims/champ_<tag>.md
set -u
cd "$(dirname "$0")/../.."
REPO=$(pwd)
RACES=${1:-10}
DERBIES=${2:-20}
PAR=${3:-16}
TAG=${4:-m7}
ROOT=${5:-.}
WCAR=${WCAR:-nordvik}
SCAR=${SCAR:-ryujin}
GODOT="$REPO/.engine/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"
OUT="$REPO/out/sims/champ_$TAG"
mkdir -p "$OUT"

# The track events of each division's calendar (Calendar.cs), as layout:direction; the light does not matter headless.
declare -A SLOTS=(
  [1]="figure8:rev oval:fwd oval:rev figure8:fwd"
  [2]="oval:fwd figure8:rev figure8:fwd oval:rev"
  [3]="oval:rev figure8:rev"
  [4]="figure8:fwd oval:fwd"
)

JOBS=()
seed=1
for div in 4 3 2 1; do
  for slot in ${SLOTS[$div]}; do
    layout=${slot%%:*}
    dir=${slot##*:}
    rev=()
    [ "$dir" = rev ] && rev=(--reverse)
    # Duplicated slots in a calendar (divisions 3 and 4) share one pool, twice as large.
    n=$RACES
    [ "$div" -ge 3 ] && n=$(( RACES * 2 ))
    half=$(( (n + 1) / 2 ))
    for kind in wreckin stockcar; do
      car=$WCAR
      [ "$kind" = stockcar ] && car=$SCAR
      for k in 0 1; do
        JOBS+=("$OUT/${kind}_d${div}_${layout}_${dir}_${k}.log|--scene=res://scenes/test/RaceSim.tscn --sims=$half --seed=$seed --kind=$kind --layout=$layout ${rev[*]} --division=$div --car=$car")
        seed=$(( seed + half ))
      done
    done
  done
  half=$(( (DERBIES + 1) / 2 ))
  for k in 0 1; do
    JOBS+=("$OUT/derby_d${div}_bowl_fwd_${k}.log|--scene=res://scenes/test/DerbySim.tscn --sims=$half --seed=$seed --division=$div --car=$WCAR")
    seed=$(( seed + half ))
  done
done

echo "championship sims: ${#JOBS[@]} jobs on $PAR processes, root $ROOT"
running=0
for job in "${JOBS[@]}"; do
  log=${job%%|*}
  args=${job#*|}
  # shellcheck disable=SC2086
  "$GODOT" --path "$ROOT" --headless --fixed-fps 120 -- $args > "$log" 2>&1 &
  running=$(( running + 1 ))
  if [ "$running" -ge "$PAR" ]; then
    wait -n
    running=$(( running - 1 ))
  fi
done
wait
python "$REPO/tools/dev/championship_report.py" "$OUT" --md "$REPO/out/sims/champ_$TAG.md"
