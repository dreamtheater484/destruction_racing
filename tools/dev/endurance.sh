#!/usr/bin/env bash
# The endurance check (plan §17.1): the AI completes 100 laps of every layout in both directions without getting
# stuck. One 100-lap race (8 cars, no damage) per layout and direction in parallel headless processes, then a
# per-car table of laps, stuck resets and the longest stop.
# Usage: tools/dev/endurance.sh [laps=100] [extra Godot user args...]
# Logs: out/sims/endurance_<layout>_<dir>.log
set -u
cd "$(dirname "$0")/../.."
LAPS=${1:-100}
shift $(( $# < 1 ? $# : 1 ))
GODOT=".engine/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"
mkdir -p out/sims
for layout in oval figure8; do
  for dir in forward reverse; do
    rev=(); [ "$dir" = reverse ] && rev=(--reverse)
    "$GODOT" --path . --headless --fixed-fps 120 -- --scene=res://scenes/test/RaceSim.tscn --sims=1 --endurance \
      "--laps=$LAPS" "--layout=$layout" "${rev[@]}" "$@" > "out/sims/endurance_${layout}_${dir}.log" 2>&1 &
  done
done
wait
python - "$LAPS" <<'PY'
import glob, re, sys
laps = int(sys.argv[1])
ok = True
for path in sorted(glob.glob("out/sims/endurance_*.log")):
    rows = [dict(t.split("=", 1) for t in m.group(1).split() if "=" in t)
            for m in (re.search(r"\[CarResult\] (.*)", l) for l in open(path, encoding="utf-8", errors="replace")) if m]
    print(path)
    for r in rows:
        done = laps if float(r.get("finish", -1)) >= 0 else int(r.get("laps", 1)) - 1
        bad = r.get("retired") == "1" or int(r.get("stuck", 0)) > 0 or float(r.get("longeststop", 0)) >= 10.0
        ok &= not bad
        print(f"  {r['driver']:<8} {r['car']:<12} laps {done:>3}/{laps}  stuck resets {r.get('stuck')}  longest stop {r.get('longeststop')} s"
              f"{'  retired' if r.get('retired') == '1' else ''}{'   <-- STUCK' if bad else ''}")
print("ENDURANCE", "OK" if ok else "FAILED")
PY
