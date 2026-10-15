"""Synthesized sound effects for Destruction Racing (no recordings, no downloads): one entry point.

Every sound is generated procedurally with numpy (see the family modules for the models):
  impacts.py  car-vs-car / tyre barrier / concrete hits, glass, lamps, parts, debris, loose wheels
  loops.py    seamless scrape, tyre, hub-grind, fire and steam loops (circular synthesis)
  crowd.py    a stadium crowd as hundreds of formant-filtered voices in a synthetic reverb
  ui.py       countdown, air horn, stings, whooshes, misfires and UI clicks

Mastering (here): one-shots are DC-blocked, cut to their target length with a tail taper, peak-normalised
to -1 dBFS and then given their relative gain, trimmed below -60 dBFS at the end and faded out over
10 ms (5 ms fade-in unless the sound starts on its transient). Loops are built circular and normalised to
-18 dBFS RMS, with a circular look-around limiter keeping peaks under -1 dBFS, and carry a WAV 'smpl'
loop chunk over the whole file.

Each (sound, variant) has its own generator seeded from a CRC of its name, so a full run and an
`--only <id>` run write identical files.

    python tools/sfxsynth/synth.py                 # everything into assets/audio/sfx/
    python tools/sfxsynth/synth.py --only glass_break
    python tools/sfxsynth/synth.py --spectrograms <dir>   # also write a spectrogram PNG per file
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import time
from dataclasses import dataclass
from typing import Callable

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import dsp  # noqa: E402
from dsp import SR, ns, undb  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT_DIR = os.path.join(ROOT, "assets", "audio", "sfx")

PEAK_DB = -1.0
LOOP_RMS_DB = -18.0
TRIM_DB = -60.0
FADE_OUT = 0.010


@dataclass
class Sound:
    id: str
    make: Callable                 # (rng, variant, target_seconds) -> float signal
    variants: int = 1
    gain_db: float = 0.0
    dur: tuple = (0.5, 0.5)        # target length range in seconds (variants are spread across it)
    loop: bool = False
    fade_in: float = 0.005         # 0: starts on its transient
    family: str = ""

    def files(self):
        if self.variants == 1:
            return [f"{self.id}.wav"]
        return [f"{self.id}_{k}.wav" for k in range(self.variants)]

    def target(self, v):
        lo, hi = self.dur
        if self.variants == 1 or hi <= lo:
            return 0.5 * (lo + hi) if not self.loop else lo
        # spread the variants over the range, in a shuffled (but fixed) order
        order = dsp.make_rng(self.id, "order").permutation(self.variants)
        return lo + (hi - lo) * (order[v] + 1.0) / self.variants


# ----------------------------------------------------------------------------- mastering

def finish_oneshot(x, target, gain_db, fade_in):
    x = np.asarray(x, dtype=float)
    x = dsp.dc_block(x, 18.0)
    n = min(len(x), ns(target))
    x = x[:n].copy()
    taper = ns(target * 0.12)
    x[n - taper:] *= dsp.raised_cos(taper)[::-1]
    if fade_in > 0:
        m = ns(fade_in)
        x[:m] *= dsp.raised_cos(m)
    else:
        x[:8] *= dsp.raised_cos(8)            # click guard: 0.2 ms, the transient stays intact
    x *= undb(PEAK_DB) / (dsp.peak(x) + 1e-12)
    x *= undb(gain_db)
    above = np.nonzero(np.abs(x) >= undb(TRIM_DB))[0]
    end = int(above[-1]) + 1 if len(above) else len(x)
    x = x[:end]
    m = min(ns(FADE_OUT), len(x))
    x[len(x) - m:] *= dsp.raised_cos(m)[::-1]
    return x


def finish_loop(x):
    x = np.asarray(x, dtype=float)
    x = dsp.dc_block(x, 18.0, circular=True)
    ceiling = undb(PEAK_DB)
    for _ in range(6):
        x *= undb(LOOP_RMS_DB) / (dsp.rms(x) + 1e-12)
        if dsp.peak(x) <= ceiling:
            break
        x = dsp.limiter(x, ceiling * 0.98, window_ms=6.0, circular=True)
    if dsp.peak(x) > ceiling:
        x = dsp.limiter(x, ceiling * 0.98, window_ms=6.0, circular=True)
    return x - np.mean(x)


# ----------------------------------------------------------------------------- the sound list

def registry():
    import crowd
    import impacts as im
    import loops
    import ui

    S = []

    def add(*a, **k):
        S.append(Sound(*a, **k))

    # car-vs-car
    add("hit_metal_light", lambda r, v, d: im.hit_metal(r, v, d, "light"), 4, -10, (0.25, 0.4), fade_in=0, family="impact")
    add("hit_metal_medium", lambda r, v, d: im.hit_metal(r, v, d, "medium"), 4, -5, (0.5, 0.8), fade_in=0, family="impact")
    add("hit_metal_heavy", lambda r, v, d: im.hit_metal(r, v, d, "heavy"), 4, -2, (0.9, 1.4), fade_in=0, family="impact")
    add("hit_metal_huge", lambda r, v, d: im.hit_metal(r, v, d, "huge"), 3, 0, (1.5, 2.2), fade_in=0, family="impact")
    # car-vs-arena
    add("hit_tyres_light", lambda r, v, d: im.hit_tyres(r, v, d, "light"), 3, -9, (0.35, 0.5), fade_in=0, family="impact")
    add("hit_tyres_medium", lambda r, v, d: im.hit_tyres(r, v, d, "medium"), 3, -5, (0.6, 0.9), fade_in=0, family="impact")
    add("hit_tyres_heavy", lambda r, v, d: im.hit_tyres(r, v, d, "heavy"), 3, -2, (1.0, 1.4), fade_in=0, family="impact")
    add("hit_concrete_heavy", im.hit_concrete, 3, -2, (0.9, 1.3), fade_in=0, family="impact")
    # glass and parts
    add("glass_break", im.glass_break, 4, -3, (1.0, 1.6), fade_in=0, family="glass")
    add("lamp_break", im.lamp_break, 3, -8, (0.3, 0.5), fade_in=0, family="glass")
    add("part_off", im.part_off, 3, -4, (0.6, 1.0), fade_in=0, family="part")
    add("debris_clatter", im.debris_clatter, 6, -12, (0.2, 0.6), fade_in=0, family="part")
    add("wheel_bounce", im.wheel_bounce, 3, -9, (0.3, 0.5), fade_in=0, family="part")
    # continuous loops
    add("scrape_metal_loop", loops.scrape_metal, dur=(4.0, 4.0), loop=True, family="loop")
    add("scrape_wall_loop", loops.scrape_wall, dur=(5.0, 5.0), loop=True, family="loop")
    add("tyre_squeal_loop", loops.tyre_squeal, dur=(4.0, 4.0), loop=True, family="loop")
    add("tyre_dirt_loop", loops.tyre_dirt, dur=(4.0, 4.0), loop=True, family="loop")
    add("hub_grind_loop", loops.hub_grind, dur=(4.0, 4.0), loop=True, family="loop")
    add("fire_loop", loops.fire, dur=(5.0, 5.0), loop=True, family="loop")
    add("steam_loop", loops.steam, dur=(4.0, 4.0), loop=True, family="loop")
    # crowd
    add("crowd_bed_loop", crowd.bed_loop, dur=(16.0, 16.0), loop=True, family="crowd")
    add("crowd_excited_loop", crowd.excited_loop, dur=(16.0, 16.0), loop=True, family="crowd")
    add("crowd_ooh", crowd.ooh, 3, -3, (1.6, 2.4), family="crowd")
    add("crowd_cheer", crowd.cheer, 3, -2, (2.1, 2.9), family="crowd")
    add("crowd_roar", crowd.roar, 2, 0, (3.1, 3.9), family="crowd")
    add("crowd_groan", crowd.groan, 2, -4, (1.55, 1.95), family="crowd")
    add("crowd_applause", crowd.applause, 1, -2, (5.6, 5.6), family="crowd")
    # game / UI
    add("countdown_beep", ui.countdown_beep, 1, -4, (0.3, 0.3), family="ui")
    add("countdown_go", ui.countdown_go, 1, -2, (0.7, 0.7), family="ui")
    add("air_horn", ui.air_horn, 1, 0, (1.2, 1.2), family="ui")
    for k, d in zip(range(1, 5), (0.45, 0.55, 0.65, 0.8)):
        add(f"sting_spin_{k}", (lambda kk: (lambda r, v, dd: ui.sting_spin(r, kk, dd)))(k), 1, -6, (d, d), family="ui")
    add("sting_wreck", ui.sting_wreck, 1, -3, (1.0, 1.0), fade_in=0, family="ui")
    add("sting_final", ui.sting_final, 1, -2, (2.0, 2.0), fade_in=0, family="ui")
    add("banner_whoosh", ui.banner_whoosh, 1, -8, (0.5, 0.5), family="ui")
    add("slowmo_in", ui.slowmo_in, 1, -6, (0.6, 0.6), family="ui")
    add("warning_beep", ui.warning_beep, 1, -8, (0.25, 0.25), family="ui")
    add("misfire", ui.misfire, 4, -6, (0.15, 0.3), fade_in=0, family="ui")
    add("ui_move", ui.ui_move, 1, -12, (0.06, 0.06), fade_in=0, family="ui")
    add("ui_select", ui.ui_select, 1, -8, (0.15, 0.15), fade_in=0, family="ui")
    add("ui_back", ui.ui_back, 1, -10, (0.12, 0.12), fade_in=0, family="ui")
    add("tally_tick", ui.tally_tick, 1, -14, (0.04, 0.04), fade_in=0, family="ui")
    add("tally_done", ui.tally_done, 1, -6, (0.5, 0.5), fade_in=0, family="ui")
    return S


# ----------------------------------------------------------------------------- checks

def file_stats(x, loop):
    st = {
        "dur": len(x) / SR,
        "peak_db": dsp.to_db(dsp.peak(x)),
        "rms_db": dsp.to_db(dsp.rms(x)),
        "dc": float(np.mean(x)),
        "clips": int(np.sum(np.abs(x) >= 32766.5 / 32767.0)),
        "centroid": dsp.spectral_centroid(x),
        "low": dsp.band_energy_fraction(x, 20, 150),
        "low_db": 10 * np.log10(np.sum(dsp.shape(x, lambda f: dsp.band_gain(f, None, 150.0, 4)) ** 2) / SR + 1e-20),
    }
    if loop:
        st["seam_jump"], st["seam_flux"] = dsp.seam_metrics(x)
    return st


def max_xcorr(a, b):
    n = len(a) + len(b) - 1
    m = dsp.fft_size(n)
    c = np.fft.irfft(np.fft.rfft(a, m) * np.conj(np.fft.rfft(b, m)), m)
    return float(np.max(np.abs(c)) / (np.linalg.norm(a) * np.linalg.norm(b) + 1e-12))


def formant_check(x):
    """Peaks of the smoothed long-term spectrum in the F1 (250-950 Hz) and F2 (800-2600 Hz) regions,
    with their prominence over the spectrum's local trend: voice-like babble shows them, noise does not."""
    f, s = dsp.ltas_db(x, frame=8192)
    keep = (f > 80) & (f < 5000)
    f, s = f[keep], s[keep]
    lf = np.log2(f)
    sm = np.array([np.mean(s[np.abs(lf - c) < 1 / 12]) for c in lf])       # 1/6-octave smoothing
    trend = np.array([np.mean(sm[np.abs(lf - c) < 0.75]) for c in lf])    # 1.5-octave trend
    out = []
    for lo, hi in ((250, 950), (800, 2600)):
        r = (f >= lo) & (f <= hi)
        i = np.argmax((sm - trend)[r])
        out.append((float(f[r][i]), float((sm - trend)[r][i])))
    return out


def print_report(rows, groups):
    hdr = f"{'file':34s} {'dur s':>6s} {'peak':>6s} {'rms':>6s} {'dc':>8s} {'clip':>4s} {'cent Hz':>7s} {'<150':>5s} {'seam':>11s}"
    print(hdr)
    print("-" * len(hdr))
    for name, st in rows:
        seam = f"{st['seam_jump']:.2f} z{st['seam_flux']:+5.1f}" if "seam_jump" in st else ""
        print(f"{name:34s} {st['dur']:6.3f} {st['peak_db']:6.1f} {st['rms_db']:6.1f} {st['dc']:8.1e} {st['clips']:4d} "
              f"{st['centroid']:7.0f} {st['low'] * 100:4.0f}% {seam:>11s}")
    print("\npeak/rms in dBFS; <150 = share of energy below 150 Hz; seam (loops) = end->start sample jump / the loop's\n"
          "own 99th-percentile sample step (< 1: no click), and z = spectral flux across the boundary as a z-score\n"
          "against every other frame-to-frame flux of the loop (< 3: the seam looks like any other point)")
    print("\nvariants (max normalised cross-correlation over all lags between any two variants; 1.0 = identical):")
    for sid, xs in groups.items():
        if len(xs) > 1:
            c = max(max_xcorr(xs[i], xs[j]) for i in range(len(xs)) for j in range(i + 1, len(xs)))
            print(f"  {sid:24s} {len(xs)} variants, max xcorr {c:.2f}")


def tier_report(rows):
    by = {}
    for name, st in rows:
        by.setdefault(name.rsplit("_", 1)[0], []).append(st)
    print("\ntiers (mean duration, mean RMS, low end = energy below 150 Hz in dB re 1 s of full scale, and its share):")
    for fam in ("hit_metal", "hit_tyres"):
        for tier in ("light", "medium", "heavy", "huge"):
            sts = by.get(f"{fam}_{tier}")
            if sts:
                print(f"  {fam}_{tier:7s} dur {np.mean([s['dur'] for s in sts]):.2f} s  rms {np.mean([s['rms_db'] for s in sts]):6.1f} dB"
                      f"  low end {np.mean([s['low_db'] for s in sts]):6.1f} dB ({100 * np.mean([s['low'] for s in sts]):2.0f}%)")


# ----------------------------------------------------------------------------- main

def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--only", action="append", help="sound id to regenerate (repeatable)")
    ap.add_argument("--out", default=OUT_DIR)
    ap.add_argument("--spectrograms", help="also write a spectrogram PNG per file into this folder")
    args = ap.parse_args(argv)

    sounds = registry()
    ids = {s.id for s in sounds}
    if args.only:
        bad = [o for o in args.only if o not in ids]
        if bad:
            sys.exit(f"unknown id(s): {', '.join(bad)}")
        sounds = [s for s in sounds if s.id in args.only]
    os.makedirs(args.out, exist_ok=True)
    if args.spectrograms:
        os.makedirs(args.spectrograms, exist_ok=True)
    man_path = os.path.join(args.out, "manifest.json")
    manifest = {"sample_rate": SR, "generator": "tools/sfxsynth/synth.py", "sounds": {}}
    if args.only and os.path.exists(man_path):
        with open(man_path) as f:
            manifest["sounds"] = json.load(f).get("sounds", {})

    rows, groups, crowd_rows = [], {}, []
    t_start = time.time()
    for s in sounds:
        t0 = time.time()
        durs = []
        for v, fname in enumerate(s.files()):
            rng = dsp.make_rng(s.id, v)
            target = s.target(v)
            raw = s.make(rng, v, target)
            if s.loop:
                if len(raw) != ns(target):
                    raise RuntimeError(f"{s.id}: loop length {len(raw)} != {ns(target)}")
                x = finish_loop(raw)
            else:
                x = finish_oneshot(raw, target, s.gain_db, s.fade_in)
            x16 = dsp.write_wav(os.path.join(args.out, fname), x, loop=s.loop)
            xq = x16.astype(float) / 32767.0
            st = file_stats(xq, s.loop)
            rows.append((fname, st))
            groups.setdefault(s.id, []).append(xq)
            durs.append(round(len(x16) / SR, 3))
            if s.family == "crowd":
                crowd_rows.append((fname, formant_check(xq)))
            if args.spectrograms:
                dsp.spectrogram_png(os.path.join(args.spectrograms, fname.replace(".wav", ".png")), xq)
        manifest["sounds"][s.id] = {"files": s.files(), "loop": s.loop, "duration": durs}
        print(f"  {s.id:24s} {s.variants} file(s)  {time.time() - t0:5.1f} s", flush=True)

    order = [s.id for s in registry()]
    manifest["sounds"] = {k: manifest["sounds"][k] for k in order if k in manifest["sounds"]}
    if not args.only:
        # a full run owns the folder: drop WAVs that no longer belong to any sound
        keep = {f for s in sounds for f in s.files()}
        for f in os.listdir(args.out):
            if f.endswith(".wav") and f not in keep:
                os.remove(os.path.join(args.out, f))
                print(f"  removed stale {f}")
    with open(man_path, "w") as f:
        json.dump(manifest, f, indent=2)
        f.write("\n")

    print()
    print_report(rows, groups)
    tier_report(rows)
    if crowd_rows:
        print("\ncrowd formant check (F1-region peak, F2-region peak: Hz / dB above the 1.5-octave trend):")
        for name, ((f1, p1), (f2, p2)) in crowd_rows:
            print(f"  {name:28s} F1 {f1:5.0f} Hz {p1:+5.1f} dB   F2 {f2:5.0f} Hz {p2:+5.1f} dB")
    total = sum(os.path.getsize(os.path.join(args.out, f)) for f in os.listdir(args.out))
    print(f"\n{len(rows)} files, folder total {total / 1e6:.2f} MB, {time.time() - t_start:.0f} s")


if __name__ == "__main__":
    main()
