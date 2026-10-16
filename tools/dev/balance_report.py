"""Car balance report (plan §9.3) from the [CarResult] lines of derby and race sims run with --randomcars.

Usage: python tools/dev/balance_report.py out/sims/balance_derby_*.log out/sims/balance_race_*.log
       out/sims/balance_stockcar_*.log [--md out.md]

Targets: every car within ±15% of the mean in its intended mode(s) (mean event points), and no car in the top 3
of every mode (derby, Wreckin' and Stock Car: you chose at the M5 gate to count Stock Car as the third mode). With
--randomcars each driver (and the PlayerProxy) gets a random model, so every car meets every archetype; the numbers
are per car-entry (20 per event). The track cars' intended modes are both races, Wreckin' and Stock Car.

PACE: without Stock Car logs, an estimate (the Stock Car points for the Wreckin' finishing order) is shown instead.

"vs peers" (information, not a target): against the mean of the cars meant for that mode only. Stock Car points are
zero-sum and the three derby cars finish at the back, so the track cars sit above the nine-car mean even when they
are equal; this column shows how they compare with each other. "spun": times a car was spun after a hit, per race.
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
        if r["mode"] == "race" and not any(x["mode"] == "stockcar" for x in rows):
            place = int(r["place"])
            pace = dict(r, mode="pace", points=str(STOCK_CAR[place - 1] if place <= len(STOCK_CAR) else 0))
            by["pace"][r["car"]].append(pace)
            events["pace"].add(r["seed"])
    ranks = {}
    verdict = {}
    ok = True
    for mode in ("derby", "race", "stockcar", "pace"):
        cars = by.get(mode)
        if not cars:
            continue
        title = {"pace": "PACE (estimate: Stock Car points for the Wreckin' finishing order)", "race": "WRECKIN'",
                 "stockcar": "STOCK CAR"}.get(mode, mode.upper())
        p(f"## {title}  ({len(events[mode])} events, {sum(len(v) for v in cars.values())} car entries)")
        p("")
        p("| car | entries | mean pts | ± | vs mean | vs peers | mean place | win % | survival / finish | spun | intended | check |")
        p("|---|---|---|---|---|---|---|---|---|---|---|---|")
        means = {c: statistics.mean(float(r["points"]) for r in v) for c, v in cars.items()}
        overall = statistics.mean(means.values())

        def meant_for(c):
            return mode in INTENDED.get(c, ()) or (mode in ("stockcar", "pace") and "race" in INTENDED.get(c, ()))

        peers = [means[c] for c in means if meant_for(c)]
        peer_mean = statistics.mean(peers) if peers else overall
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
            peer = f"{100 * (means[c] / peer_mean - 1):+.0f}%" if meant_for(c) and peer_mean else ""
            spun = [float(r["spun"]) for r in v if "spun" in r]
            spun_s = f"{statistics.mean(spun):.2f}" if spun else "–"
            # The track cars are meant for both races; the pace estimate is shown, not checked.
            intended = mode in INTENDED.get(c, ()) or (mode == "stockcar" and "race" in INTENDED.get(c, ()))
            within = abs(ratio - 1.0) <= 0.15
            check = ("ok" if within else "OUT") if intended else ""
            if intended and not within:
                ok = False
            verdict.setdefault(c, {})[mode] = stars(ratio)
            p(f"| {c} | {len(v)} | {means[c]:.1f} | {1.96 * se:.1f} | {100 * (ratio - 1):+.0f}% | {peer} | {place:.1f} | {wins:.1f} "
              f"| {extra} | {spun_s} | {'yes' if intended else ''} | {check} |")
        p("")
        p(f"Mean over cars: {overall:.1f} pts. Rank: " + ", ".join(f"{i + 1}. {c}" for i, c in enumerate(order)))
        p("")
    modes = [m for m in ("derby", "race", "stockcar") if m in ranks]
    if len(modes) >= 2:
        everywhere = [c for c in ORDER if all(ranks[m].get(c, 99) <= 3 for m in modes)]
        p(f"Top 3 in every mode ({', '.join(modes)}): {', '.join(everywhere) if everywhere else 'none'}")
        if everywhere and len(modes) == 3:
            ok = False
        if "pace" in ranks:
            all3 = [c for c in everywhere if ranks["pace"].get(c, 99) <= 3]
            p(f"... and in the pace estimate: {', '.join(all3) if all3 else 'none'}")
    p("")
    p("Car-select verdicts (stars: >=+10% 5, >=+4% 4, ±4% 3, >=-10% 2, else 1):")
    for c in ORDER:
        if c in verdict:
            p(f"  {c:12s} derby {verdict[c].get('derby', '-')}  race {verdict[c].get('race', '-')}"
              f"  pace {verdict[c].get('stockcar', verdict[c].get('pace', '-'))}")
    p("")
    p("RESULT: " + ("all balance targets met" if ok else "targets NOT met") + f" ({', '.join(modes)})")
    text = "\n".join(out)
    print(text)
    if args.md:
        with open(args.md, "w", encoding="utf-8") as f:
            f.write(text + "\n")


if __name__ == "__main__":
    main()
