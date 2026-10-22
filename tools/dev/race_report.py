"""Merges race sim batches (the RaceSim .json files) into one report against the plan §17.4 targets.

Usage: python tools/dev/race_report.py <json files or globs...> [--label X] [--md out.md]
Run several RaceSim processes with different --seed ranges (tools/dev/race_targets.sh does), then merge them here.
"""
from __future__ import annotations

import argparse
import glob
import json
import math
import statistics
import sys


def median(values):
    # .NET writes a race without a winner (all wrecked) as the string "NaN".
    v = [x for x in values if isinstance(x, (int, float)) and not math.isnan(x)]
    return statistics.median(v) if v else float("nan")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("files", nargs="+")
    ap.add_argument("--label", default="")
    ap.add_argument("--stockcar", action="store_true", help="Stock Car targets (else Wreckin')")
    ap.add_argument("--crossover", action="store_true", help="the layout has a crossover (figure-8)")
    ap.add_argument("--md")
    a = ap.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")
    rows = []
    for pattern in a.files:
        for path in sorted(glob.glob(pattern)):
            with open(path, encoding="utf-8") as f:
                rows.extend(json.load(f))
    n = len(rows)
    if n == 0:
        print("no races")
        return
    share = lambda f: sum(1 for r in rows if f(r)) / n
    t = []
    if a.stockcar:
        spread = median(r["Lap2Spread"] for r in rows)
        dnfs = median(r["Dnfs"] for r in rows)
        t.append(("Spread from 1st to 10th on lap 2 (median)", f"{spread:.1f} s", "≤ 6 s", spread <= 6))
        t.append(("Close battle involving the player in the last 2 laps", f"{share(lambda r: r['PlayerLateBattle']) * 100:.0f}% of races", "≥ 50%", share(lambda r: r["PlayerLateBattle"]) >= 0.5))
        t.append(("DNFs per race (median)", f"{dnfs:.1f}", "1–4", 1 <= dnfs <= 4))
    else:
        spl = median(r["SpinsPerLap"] for r in rows)
        t.append(("Spins in the field per lap (median)", f"{spl:.1f}", "≥ 3", spl >= 3))
        if a.crossover:
            ci = median(r["CrossoverIncidents"] for r in rows)
            t.append(("Crossover incidents per race (median)", f"{ci:.1f}", "≥ 2", ci >= 2))
        ls = median(r["LeaderSpins"] for r in rows)
        t.append(("Leader spun per race (median)", f"{ls:.1f}", "≥ 2", ls >= 2))
    pc = median(r["PlayerPositionChanges"] for r in rows)
    close = median(r["PlayerCloseShare"] for r in rows)
    wb = share(lambda r: r["PlayerWreckedBeforeHalf"])
    drive = sum(r["PlayerDrivingTime"] for r in rows)
    stuck = sum(r["PlayerStuckTime"] for r in rows) / drive if drive > 0 else 0
    t += [
        ("Player position changes per race (median)", f"{pc:.0f}", "≥ 6", pc >= 6),
        ("Cars within 3 s of the player (median share of race time)", f"{close * 100:.0f}%", "≥ 60%", close >= 0.6),
        ("Player wrecked before half distance", f"{wb * 100:.0f}%", "≤ 10%" + ("" if not a.stockcar else " (Wreckin' target)"), None if a.stockcar else wb <= 0.10),
        ("Player stuck or recovering", f"{stuck * 100:.1f}% of time", "≤ 5%", stuck <= 0.05),
        ("Races that ended normally (no time-out)", f"{share(lambda r: not r['TimedOut']) * 100:.0f}%", "100%", all(not r["TimedOut"] for r in rows)),
        ("Race length / winner time (median)", f"{median(r['RaceLength'] for r in rows):.0f} s / {median(r['WinnerTime'] for r in rows):.0f} s", "–", None),
        ("Player finished / mean place", f"{share(lambda r: r['PlayerFinished']) * 100:.0f}% / {sum(r['PlayerPlace'] for r in rows) / n:.1f}", "–", None),
        ("Player points (median): total / spin / wreck / finish",
         " / ".join(f"{median(r[k] for r in rows):.0f}" for k in ("PlayerPoints", "PlayerSpinPoints", "PlayerWreckPoints", "PlayerFinishPoints")), "–", None),
        ("Spins / wrecks / finishers per race (mean)", " / ".join(f"{sum(r[k] for r in rows) / n:.1f}" for k in ("Spins", "Wrecks", "Finishers")), "–", None),
        ("Pit stops per race / by the player (mean)", f"{sum(r['PitStops'] for r in rows) / n:.1f} / {sum(r['PlayerPitStops'] for r in rows) / n:.2f}", "–", None),
        ("DD Code per race (mean): T1 brake-checks (tailgater hit) / T4 squeezes",
         f"{sum(r.get('BrakeChecks', 0) for r in rows) / n:.1f} ({sum(r.get('BrakeCheckHits', 0) for r in rows) / n:.1f}) / {sum(r.get('Squeezes', 0) for r in rows) / n:.1f}", "–", None),
        ("DD Code per race (mean): tailgating stints / dodges / blocks",
         " / ".join(f"{sum(r.get(k, 0) for r in rows) / n:.1f}" for k in ("Tailgates", "Evasions", "Blocks")), "–", None),
    ]
    head = f"{a.label} — {n} races"
    print(head)
    for m, v, target, ok in t:
        print(f"  {'  ' if ok is None else 'OK' if ok else '!!'} {m}: {v} (target {target})")
    if a.md:
        with open(a.md, "w", encoding="utf-8") as f:
            f.write(f"# {head}\n\n| Metric | Value | Target | |\n|---|---|---|---|\n")
            for m, v, target, ok in t:
                f.write(f"| {m} | {v} | {target} | {'' if ok is None else '✅' if ok else '❌'} |\n")


if __name__ == "__main__":
    main()
