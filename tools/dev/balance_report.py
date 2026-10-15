"""Car balance report (plan §9.3) from the [CarResult] lines of derby and race sims run with --randomcars.

Usage: python tools/dev/balance_report.py out/sims/balance_derby_*.log out/sims/balance_race_*.log [--md out.md]

Targets: every car within ±15% of the mean in its intended mode(s) (mean event points), and no car in the top 3
of every mode. With --randomcars each driver (and the PlayerProxy) gets a random model, so every car meets every
archetype; the numbers are per car-entry (20 per event).

PACE is an estimate, not a checked mode: the Stock Car finishing points (50, 40, 35 ... 0, plan §4.3) applied to
the race finishing order. Wreckin' points are nearly all spin points, so they say little about pace; Stock Car
Racing comes with M6 and gets its own sims then.
"""
from __future__ import annotations

import argparse
import glob
import math
import re
import statistics
import sys
from collections import defaultdict

# Intended modes per car (plan §9.3: heavy/tough US for the Bowl, light/nimble JP and EU hatches for the track).
INTENDED = {
    "brougham": ("derby",), "homestead": ("derby",), "interceptor": ("derby", "race"),
    "nordvik": ("derby",), "adler": ("race",), "volant": ("race",),
    "hikari": ("race",), "ryujin": ("derby", "race"), "kitsune": ("race",),
}
ORDER = ["brougham", "homestead", "interceptor", "nordvik", "adler", "volant", "hikari", "ryujin", "kitsune"]
STOCK_CAR = [50, 40, 35, 30, 25, 20, 18, 16, 14, 12, 10, 8, 7, 6, 5, 4, 3, 2, 1, 0]
LINE = re.compile(r"\[CarResult\] (.*)")


def parse(files):
    rows = []
    for pattern in files:
        for path in glob.glob(pattern):
            with open(path, encoding="utf-8", errors="replace") as f:
                for line in f:
                    m = LINE.search(line)
                    if not m:
                        continue
                    kv = dict(tok.split("=", 1) for tok in m.group(1).split() if "=" in tok)
                    rows.append(kv)
    return rows


def stars(ratio):
    return 5 if ratio >= 1.10 else 4 if ratio >= 1.04 else 3 if ratio >= 0.96 else 2 if ratio >= 0.90 else 1


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("files", nargs="+")
    ap.add_argument("--md", default="")
    args = ap.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")
    rows = parse(args.files)
    out = []
    p = out.append
    by = defaultdict(lambda: defaultdict(list))
    events = defaultdict(set)
    for r in rows:
        by[r["mode"]][r["car"]].append(r)
        events[r["mode"]].add(r["seed"])
        if r["mode"] == "race":
            place = int(r["place"])
            pace = dict(r, mode="pace", points=str(STOCK_CAR[place - 1] if place <= len(STOCK_CAR) else 0))
            by["pace"][r["car"]].append(pace)
            events["pace"].add(r["seed"])
    ranks = {}
    verdict = {}
    ok = True
    for mode in ("derby", "race", "pace"):
        cars = by.get(mode)
        if not cars:
            continue
        title = "PACE (estimate: Stock Car points for the race finishing order)" if mode == "pace" else mode.upper()
        p(f"## {title}  ({len(events[mode])} events, {sum(len(v) for v in cars.values())} car entries)")
        p("")
        p("| car | entries | mean pts | ± | vs mean | mean place | win % | survival / finish | intended | check |")
        p("|---|---|---|---|---|---|---|---|---|---|")
        means = {c: statistics.mean(float(r["points"]) for r in v) for c, v in cars.items()}
        overall = statistics.mean(means.values())
        order = sorted(means, key=lambda c: -means[c])
        ranks[mode] = {c: i + 1 for i, c in enumerate(order)}
        for c in ORDER:
            if c not in cars:
                continue
            v = cars[c]
            pts = [float(r["points"]) for r in v]
            sd = statistics.stdev(pts) if len(pts) > 1 else 0.0
            se = sd / math.sqrt(len(pts))
            place = statistics.mean(float(r["place"]) for r in v)
            wins = 100.0 * sum(1 for r in v if r["place"] == "1") / len(v)
            if mode == "derby":
                extra = f"{statistics.mean(float(r['survival']) for r in v):.0f} s"
            else:
                fin = [float(r["finish"]) for r in v if float(r.get("finish", -1)) > 0]
                extra = f"{statistics.mean(fin):.0f} s ({100 * len(fin) / len(v):.0f}% fin)" if fin else "–"
            ratio = means[c] / overall if overall else 1.0
            intended = mode in INTENDED.get(c, ())        # the pace estimate is shown, not checked
            within = abs(ratio - 1.0) <= 0.15
            check = ("ok" if within else "OUT") if intended else ""
            if intended and not within:
                ok = False
            verdict.setdefault(c, {})[mode] = stars(ratio)
            p(f"| {c} | {len(v)} | {means[c]:.1f} | {1.96 * se:.1f} | {100 * (ratio - 1):+.0f}% | {place:.1f} | {wins:.1f} "
              f"| {extra} | {'yes' if intended else ''} | {check} |")
        p("")
        p(f"Mean over cars: {overall:.1f} pts. Rank: " + ", ".join(f"{i + 1}. {c}" for i, c in enumerate(order)))
        p("")
    modes = [m for m in ("derby", "race") if m in ranks]
    if len(modes) == 2:
        everywhere = [c for c in ORDER if all(ranks[m].get(c, 99) <= 3 for m in modes)]
        p(f"Top 3 in both playable modes (derby, race): {', '.join(everywhere) if everywhere else 'none'}")
        if everywhere:
            ok = False
        if "pace" in ranks:
            all3 = [c for c in everywhere if ranks["pace"].get(c, 99) <= 3]
            p(f"Top 3 in derby, race and the pace estimate: {', '.join(all3) if all3 else 'none'}")
    p("")
    p("Car-select verdicts (stars: >=+10% 5, >=+4% 4, ±4% 3, >=-10% 2, else 1):")
    for c in ORDER:
        if c in verdict:
            p(f"  {c:12s} derby {verdict[c].get('derby', '-')}  race {verdict[c].get('race', '-')}"
              f"  pace {verdict[c].get('pace', '-')}")
    p("")
    p("RESULT: " + ("all balance targets met" if ok else "targets NOT met") + " (derby and race; the pace estimate is not checked)")
    text = "\n".join(out)
    print(text)
    if args.md:
        with open(args.md, "w", encoding="utf-8") as f:
            f.write(text + "\n")


if __name__ == "__main__":
    main()
