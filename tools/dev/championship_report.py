"""Championship report (plan §17.4) from the event sims of tools/dev/championship.sh.

Each log holds whole events ([CarResult] lines: every driver's points, wrecks and place, `who` = the driver's index,
-1 = the PlayerProxy). The script plays thousands of careers from them: every round of a season draws one event of
that division's calendar (same layout and direction, run with the AI at that division's pace), the division tables
are sorted as the game sorts them (points, then wrecks, then best finish), and the season ends as Championship.cs
ends it (the top of each division goes up, the last of the division above comes down, the player is never relegated).

It reports, per championship:
  * the promotion chance per season in each division and the median attempts (target 1-2, plan §17.4);
  * the share of Wreckin' seasons already decided before the Bowl finale (target <= 30%): the leader after the four
    races wins the division in >= 95% of 400 redraws of the finale;
  * every driver's mean season points and place, to check the starting divisions.

Usage: python tools/dev/championship_report.py out/sims/champ_m7 [--careers 4000] [--seasons 12] [--md out.md]
"""
import argparse
import glob
import os
import random
import re
import statistics
import sys
from collections import defaultdict

CAL = {  # the calendar's track events per division (Calendar.cs), as (layout, direction)
    1: [("figure8", "rev"), ("oval", "fwd"), ("oval", "rev"), ("figure8", "fwd")],
    2: [("oval", "fwd"), ("figure8", "rev"), ("figure8", "fwd"), ("oval", "rev")],
    3: [("oval", "rev"), ("figure8", "rev"), ("oval", "rev"), ("figure8", "rev")],
    4: [("figure8", "fwd"), ("oval", "fwd"), ("figure8", "fwd"), ("oval", "fwd")],
}
PLAYER = -1
NAME_RE = re.compile(r"(wreckin|stockcar|derby)_d(\d)_(\w+?)_(fwd|rev)_\d+\.log$")


def parse(folder):
    pools = defaultdict(list)          # (kind, div, layout, dir) -> [event]; event = {who: (points, wrecks, place)}
    field, start = {}, {}
    for path in glob.glob(os.path.join(folder, "*.log")):
        m = NAME_RE.search(os.path.basename(path))
        if not m:
            continue
        kind, div, layout, d = m.group(1), int(m.group(2)), m.group(3), m.group(4)
        events = defaultdict(dict)
        with open(path, encoding="utf-8", errors="replace") as f:
            for line in f:
                if line.startswith("[Field] "):
                    i, short, car, arch = line.split()[1:5]
                    field[int(i)] = (short, car, arch)
                elif line.startswith("[Start] "):
                    parts = line.split()
                    start[parts[1].lower()] = {int(a): int(b) for a, b in (x.split(":") for x in parts[2].split(","))}
                elif line.startswith("[CarResult] "):
                    kv = dict(t.split("=", 1) for t in line.split()[1:] if "=" in t)
                    if "who" not in kv:
                        continue
                    events[kv["seed"]][int(kv["who"])] = (int(kv["points"]), int(kv.get("wrecks", 0)), int(kv["place"]))
        for ev in events.values():
            if len(ev) >= 20:
                pools[(kind, div, layout, d)].append(ev)
    return pools, field, start


def table(members, pts, wrk, best, order):
    return sorted(members, key=lambda w: (-pts[w], -wrk[w], best[w], order[w]))


def play_season(kind, player_div, div_of, pools, rng, order, finale_redraws=0):
    pts, wrk, best = defaultdict(int), defaultdict(int), defaultdict(lambda: 99)

    def add(ev):
        for w, (p, k, place) in ev.items():
            pts[w] += p
            wrk[w] += k
            if place > 0:
                best[w] = min(best[w], place)

    for layout, d in CAL[player_div]:
        add(rng.choice(pools[(kind, player_div, layout, d)]))
    decided = None
    if kind == "wreckin":
        derbies = pools[("derby", player_div, "bowl", "fwd")]
        members = [w for w, dv in div_of.items() if dv == player_div]
        if finale_redraws:
            leader = table(members, pts, wrk, best, order)[0]
            wins = player_wins = 0
            for _ in range(finale_redraws):
                p2, k2, b2 = defaultdict(int, pts), defaultdict(int, wrk), defaultdict(lambda: 99, best)
                for w, (p, k, place) in rng.choice(derbies).items():
                    p2[w] += p
                    k2[w] += k
                    if place > 0:
                        b2[w] = min(b2[w], place)
                top = table(members, p2, k2, b2, order)[0]
                wins += top == leader
                player_wins += top == PLAYER
            decided = (wins / finale_redraws >= 0.95, player_wins / finale_redraws)
        add(rng.choice(derbies))
    return pts, wrk, best, decided


def end_season(div_of, pts, wrk, best, order):
    tables = {d: table([w for w, dv in div_of.items() if dv == d], pts, wrk, best, order) for d in range(1, 5)}
    player_div = div_of[PLAYER]
    champion = player_div == 1 and tables[1][0] == PLAYER
    moves = []
    for d in range(2, 5):
        up = tables[d][0]
        down = [w for w in tables[d - 1] if w != PLAYER][-1]
        if up != down:
            moves.append((up, d - 1))
            moves.append((down, d))
    for w, d in moves:
        div_of[w] = d
    place = tables[player_div].index(PLAYER) + 1
    return div_of[PLAYER] < player_div, champion, place


def careers(kind, pools, start, n, max_seasons, seed, redraws):
    rng = random.Random(seed)
    order = {PLAYER: 0}
    for i, w in enumerate(start):
        order[w] = i + 1
    seasons_in = defaultdict(int)
    promotions = defaultdict(int)
    attempts = defaultdict(list)       # division -> seasons needed to go up (or to win Division 1); None = never
    decided = defaultdict(list)
    player_alive = defaultdict(list)
    places = defaultdict(list)
    driver_points = defaultdict(list)
    champions = 0
    seasons_to_title = []
    for c in range(n):
        div_of = dict(start)
        div_of[PLAYER] = 4
        tries = 0
        for s in range(max_seasons):
            d = div_of[PLAYER]
            pts, wrk, best, dec = play_season(kind, d, div_of, pools, rng, order, redraws if c < n // 4 else 0)
            if dec is not None:
                decided[d].append(dec[0])
                player_alive[d].append(dec[1] >= 0.05)
            if c < 400 and d == 4 and s == 0:
                for w in div_of:
                    driver_points[w].append(pts[w])
            up, champ, place = end_season(div_of, pts, wrk, best, order)
            seasons_in[d] += 1
            places[d].append(place)
            tries += 1
            if up or champ:
                promotions[d] += 1
                attempts[d].append(tries)
                tries = 0
            if champ:
                champions += 1
                seasons_to_title.append(s + 1)
                break
        else:
            attempts[div_of[PLAYER]].append(None)
    return dict(seasons_in=seasons_in, promotions=promotions, attempts=attempts, decided=decided, alive=player_alive,
                places=places, champions=champions, n=n, to_title=seasons_to_title, driver_points=driver_points)


def standalone(kind, pools, start, n=2000, seed=11):
    """The promotion chance in each division with the player put straight into it (it takes the place of that
    division's first AI, who goes to Division 4): shows the divisions a career never reaches."""
    order = {PLAYER: 0, **{w: i + 1 for i, w in enumerate(start)}}
    res = {}
    for d in (4, 3, 2, 1):
        rng = random.Random(seed + d)
        div_of = dict(start)
        div_of[PLAYER] = d
        if d != 4:
            div_of[next(w for w in start if start[w] == d)] = 4
        members = [w for w, dv in div_of.items() if dv == d]
        tops, places = 0, []
        for _ in range(n):
            pts, wrk, best, _ = play_season(kind, d, div_of, pools, rng, order)
            t = table(members, pts, wrk, best, order)
            tops += t[0] == PLAYER
            places.append(t.index(PLAYER) + 1)
        res[d] = (tops / n, statistics.mean(places))
    return res


def median_attempts(values, cap):
    v = sorted(x if x is not None else cap + 1 for x in values)
    if not v:
        return None
    m = statistics.median(v)
    return f">{cap}" if m > cap else f"{m:g}"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("folder")
    ap.add_argument("--careers", type=int, default=4000)
    ap.add_argument("--seasons", type=int, default=12)
    ap.add_argument("--redraws", type=int, default=400)
    ap.add_argument("--md")
    a = ap.parse_args()
    pools, field, start = parse(a.folder)
    out = [f"# Championship sims ({os.path.basename(a.folder.rstrip('/'))})", ""]
    sizes = ", ".join(f"{k[0]} d{k[1]} {k[2]} {k[3]}: {len(v)}" for k, v in sorted(pools.items()))
    out += [f"Events per pool: {sizes}", ""]
    for kind, label in (("wreckin", "Wreckin' championship"), ("stockcar", "Stock Car championship")):
        need = [(kind, d, lay, dr) for d in CAL for lay, dr in CAL[d]] + ([("derby", d, "bowl", "fwd") for d in CAL] if kind == "wreckin" else [])
        missing = sorted({k for k in need if not pools.get(k)})
        if missing:
            out += [f"## {label}", "", f"Missing event pools: {missing}", ""]
            continue
        st = start.get(kind, {})
        r = careers(kind, pools, st, a.careers, a.seasons, 7, a.redraws)
        out += [f"## {label}", "", f"{r['n']} careers of up to {a.seasons} seasons, the PlayerProxy (average fan) from Division 4.", ""]
        out += ["| Division | Seasons played | Promotion chance per season | Median attempts (target 1–2) | Player's mean place | Decided before the finale (target ≤ 30%) | Player still in it before the finale |",
                "|---|---|---|---|---|---|---|"]
        for d in (4, 3, 2, 1):
            n = r["seasons_in"][d]
            p = r["promotions"][d] / n if n else 0
            dec = r["decided"].get(d, [])
            alive = r["alive"].get(d, [])
            out.append(f"| {d}{' (win it)' if d == 1 else ''} | {n} | {p:.0%} | {median_attempts(r['attempts'][d], a.seasons) or '–'} | "
                       + (f"{statistics.mean(r['places'][d]):.1f}" if r['places'][d] else "–") + " | " + (f"{sum(dec) / len(dec):.0%}" if dec else "–") + " | " +
                       (f"{sum(alive) / len(alive):.0%}" if alive else "–") + " |")
        out += ["", "Each division on its own (the player put straight into it, the AI as they start; 2000 seasons): "
                + ", ".join(f"Division {d} {t:.0%} top, mean place {pl:.1f}" for d, (t, pl) in standalone(kind, pools, st).items()) + "."]
        all_dec = [x for d in r["decided"] for x in r["decided"][d]]
        if all_dec:
            out += ["", f"Decided before the finale, all divisions: **{sum(all_dec) / len(all_dec):.0%}** of {len(all_dec)} seasons."]
        titles = r["to_title"]
        out += [f"Careers that won Division 1 within {a.seasons} seasons: {r['champions'] / r['n']:.0%}" +
                (f" (median {statistics.median(titles):g} seasons)" if titles else ""), ""]
        out += ["Mean season points in the first season (Division 4 pace), by driver:", "",
                "| Driver | Car | Archetype | Starts in | Mean points |", "|---|---|---|---|---|"]
        rows = []
        for w, pl in r["driver_points"].items():
            name, car, arch = ("YOU (proxy)", "", "") if w == PLAYER else field.get(w, (str(w), "", ""))
            rows.append((statistics.mean(pl), name, car, arch, 4 if w == PLAYER else st.get(w, "?")))
        for m, name, car, arch, d in sorted(rows, reverse=True):
            out.append(f"| {name} | {car} | {arch} | {d} | {m:.0f} |")
        out.append("")
    text = "\n".join(out)
    if a.md:
        with open(a.md, "w", encoding="utf-8") as f:
            f.write(text + "\n")
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")   # a redirected stdout on Windows is cp1252
    print(text)


if __name__ == "__main__":
    main()
