"""Aggregates derby sim batches (out/sims/derby_*.json) into one report: the plan §17.4 targets over all derbies.

Usage: python tools/dev/derby_report.py out/sims/derby_average_2026*.json [--label "M4 average fan"]
Each JSON file is one DerbySim batch (a list of DerbyMetricsResult).
"""
from __future__ import annotations

import argparse
import glob
import json
import statistics
import sys


def num(x):
    """JSON NaN is written as the string "NaN"."""
    return float(x) if isinstance(x, (int, float, str)) else float("nan")


def median(values):
    v = [num(x) for x in values if x is not None]
    v = [x for x in v if x == x]
    return statistics.median(v) if v else float("nan")


def p90(values):
    v = sorted(x for x in (num(y) for y in values if y is not None) if x == x)
    if not v:
        return float("nan")
    return v[min(len(v) - 1, max(0, int(-(-0.9 * len(v) // 1)) - 1))]


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("files", nargs="+")
    ap.add_argument("--label", default="")
    args = ap.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")
    rows = []
    for pattern in args.files:
        for f in sorted(glob.glob(pattern)):
            rows.extend(json.load(open(f, encoding="utf-8")))
    n = len(rows)
    if n == 0:
        raise SystemExit("no results")
    wrecked = [r for r in rows if r["PlayerWrecked"]]
    share = lambda f: sum(1 for r in rows if f(r)) / n
    survival = sum(r["PlayerSurvival"] for r in rows)
    scored = sum(r["PlayerSpinPoints"] + r["PlayerWreckPoints"] for r in rows) or 1
    pct = lambda k: 100.0 * sum(r.get(k, 0) for r in rows) / scored

    print(f"# Derby report {args.label} — {n} derbies")
    print()
    print("| Metric | Value | Target | |")
    print("|---|---|---|---|")

    def line(metric, value, target, ok):
        mark = "" if ok is None else ("✅" if ok else "❌")
        print(f"| {metric} | {value} | {target} | {mark} |")

    fc = median(r["FirstContactTime"] for r in rows)
    line("Time to first contact (median)", f"{fc:.1f} s", "< 3 s", fc < 3)
    idle = median(r["LongestIdle"] for r in rows)
    line("Longest idle stretch, ≥ 5 cars (median / p90)", f"{idle:.1f} / {p90(r['LongestIdle'] for r in rows):.1f} s", "≤ 8 s", idle <= 8)
    hpm = median(r["PlayerHitsDealt"] + r["PlayerHitsTaken"] and (r["PlayerHitsDealt"] + r["PlayerHitsTaken"]) / (r["PlayerSurvival"] / 60) if r["PlayerSurvival"] > 1 else 0 for r in rows)
    line("Player hits per minute, dealt + taken (median)", f"{hpm:.1f}", "≥ 8", hpm >= 8)
    length = median(r["EventLength"] for r in rows)
    line("Event length (median)", f"{length:.0f} s", "120–240 s", 120 <= length <= 240)
    surv = median(r["PlayerSurvival"] for r in rows)
    line("Player survival (median)", f"{surv:.0f} s", "60–150 s", 60 <= surv <= 150)
    ds = median(r["DeliberatePointsShare"] for r in rows)
    line("Share of player points from deliberate play (aimed, T5, T4) (median)", f"{ds * 100:.0f}%", "≥ 70%", ds >= 0.7)
    aimed = median(r.get("AimedPointsShare") for r in rows)
    line("  aimed attacking hits only (median)", f"{aimed * 100:.0f}%", "–", None)
    line("  points: aimed / T5 / squeeze / passive / not aimed",
         f"{pct('PlayerPointsDeliberate'):.0f}% / {pct('PlayerPointsT5'):.0f}% / {pct('PlayerPointsSqueeze'):.0f}% / {pct('PlayerPointsPassive'):.0f}% / {pct('PlayerPointsNotAimed'):.0f}%", "–", None)
    w30 = share(lambda r: r["PlayerWrecked"] and r["PlayerSurvival"] <= 30)
    w60 = share(lambda r: r["PlayerWrecked"] and r["PlayerSurvival"] <= 60)
    line("Player wrecked within 30 s / 60 s", f"{w30 * 100:.1f}% / {w60 * 100:.1f}%", "≤ 5% / ≤ 15%", w30 <= 0.05 and w60 <= 0.15)
    gang = sum(1 for r in wrecked if r["PlayerWreckAttackers"] >= 3) / len(wrecked) if wrecked else 0
    line("Player wrecks with ≥ 3 attackers in the last 3 s", f"{gang * 100:.0f}%", "≤ 10%", gang <= 0.10)
    stuck = sum(r["PlayerStuckTime"] for r in rows) / survival if survival > 0 else 0
    line("Player stuck or recovering", f"{stuck * 100:.1f}% of time", "≤ 5%", stuck <= 0.05)
    line("Derbies that ended normally (no time-out)", f"{share(lambda r: not r['TimedOut']) * 100:.0f}%", "100%", all(not r["TimedOut"] for r in rows))
    wins = share(lambda r: r["PlayerPlace"] == 1)
    top = share(lambda r: r.get("PlayerTopPoints", False))
    line("Player wins (last car running) / tops the points", f"{wins * 100:.0f}% / {top * 100:.0f}%", "≤ 40% for strategy bots", None)
    line("Player mean place / median points", f"{statistics.mean(r['PlayerPlace'] for r in rows):.1f} / {median(r['PlayerPoints'] for r in rows):.0f}", "–", None)
    line("Spins / wrecks per derby (mean)", f"{statistics.mean(r['Spins'] for r in rows):.1f} / {statistics.mean(r['Wrecks'] for r in rows):.1f}", "–", None)


if __name__ == "__main__":
    main()
