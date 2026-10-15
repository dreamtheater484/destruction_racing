"""Game and UI sounds: additive and modal synthesis, swept noise, a little reverb.

countdown   band-limited square-ish tones (odd harmonics with extra roll-off) plus a sub-octave body,
            coloured by a stadium-PA horn response (band-pass with a 1.6 kHz presence peak).
air_horn    three reed-horn notes, each two detuned additive sawtooths with a pitch kick at the onset,
            through horn formants, saturated, with a breath of air noise.
stings      a brass-like synth (detuned additive saws with a brightness envelope) playing rising
            arpeggios; each tier higher, brighter and longer; tier 4 lands on a major chord with a glock
            sparkle and a shimmer. sting_wreck is a punchy kick/crack/clang plus a falling saw and noise
            sweep; sting_final a timpani-like boom, an orchestral-hit burst and a swelling D minor chord
            with a tritone, in reverb.
whooshes    noise through a band-pass whose centre sweeps (STFT filter); slowmo_in adds a tape-stop tone.
misfire     a one-sided pressure pulse and combustion noise through exhaust-pipe resonances and a
            muffler low-pass, with an arena slap echo.
UI          short modal clicks and dings (inharmonic or bell partial ratios).
"""
from __future__ import annotations

import numpy as np

from dsp import (SR, add_at, band_gain, convolve, env_exp, half_sine_pulse, modal_ir, noise, ns, onepole_lp,
                 peak_gain, reverb_ir, shape, soft_clip, stft_filter, tvec)


def _norm(x):
    p = np.max(np.abs(x))
    return x / p if p > 0 else x


def additive(f_curve, harmonics, amp_fn, phase0=0.0):
    """Sum of harmonics h*f(t) with amplitude amp_fn(h, f_h) (array or scalar), skipping those above 18 kHz."""
    ph = 2 * np.pi * np.cumsum(f_curve) / SR + phase0
    out = np.zeros(len(f_curve))
    fmax = float(np.max(f_curve))
    for h in harmonics:
        if h * fmax > 18000:
            break
        out += amp_fn(h, h * f_curve) * np.sin(h * ph)
    return out


def ahr(n, attack, hold, release):
    """Attack / hold / release envelope (raised-cosine edges)."""
    e = np.zeros(n)
    a, h, r = ns(attack), ns(hold), ns(release)
    e[:a] = np.sin(0.5 * np.pi * np.arange(a) / max(a, 1)) ** 2
    e[a:a + h] = 1.0
    m = min(r, max(0, n - a - h))
    e[a + h:a + h + m] = np.cos(0.5 * np.pi * np.arange(m) / max(r, 1)) ** 2
    return e


def small_reverb(x, rng, rt60=1.0, wet=0.2):
    ir = reverb_ir(rt60, rng, seconds=rt60, predelay=0.01)
    y = convolve(x, ir, full=False)
    return x + wet * y * (np.std(x) / (np.std(y) + 1e-12))


# ----------------------------------------------------------------------------- countdown and horn

def pa_horn(f):
    return band_gain(f, 280, 7000, 2) * (0.6 + 0.6 * peak_gain(f, 1600, 1.2))


def beep_tone(n, f0, body=0.2):
    f = np.full(n, f0)
    sq = additive(f, range(1, 40, 2), lambda h, fh: 1.0 / h ** 1.25)
    sub = np.sin(2 * np.pi * (f0 / 2) * tvec(n))
    even = 0.15 * np.sin(2 * np.pi * 2 * f0 * tvec(n) + 0.5)
    return sq + body * sub + even


def countdown_beep(rng, v, dur):
    n = ns(dur + 0.05)
    x = beep_tone(n, 880.0, 0.25) * ahr(n, 0.003, 0.2, 0.08)
    return shape(x, pa_horn)


def countdown_go(rng, v, dur):
    n = ns(dur + 0.05)
    x = (beep_tone(n, 1760.0, 0.0) + 0.45 * beep_tone(n, 880.0, 0.2)) * ahr(n, 0.003, 0.52, 0.16)
    return shape(x, pa_horn)


def air_horn(rng, v, dur):
    n = ns(dur + 0.05)
    t = tvec(n)
    out = np.zeros(n)
    kick = 1 - 0.06 * np.exp(-t / 0.03)                    # the reed snaps up to pitch
    droop = 1 - 0.03 * np.clip((t - (dur - 0.12)) / 0.12, 0, 1)
    for f0, lvl in ((233.1, 1.0), (293.7, 0.85), (349.2, 0.75), (466.2, 0.35)):
        for det in (-0.004, 0.0035):
            f = f0 * (1 + det) * kick * droop * (1 + 0.002 * np.sin(2 * np.pi * rng.uniform(4, 7) * t))
            out += lvl * additive(f, range(1, 60), lambda h, fh: 1.0 / h, phase0=rng.uniform(0, 6.3))
    horn = lambda f: band_gain(f, 160, 6000, 2) * (0.3 + peak_gain(f, 700, 2.0) + 0.7 * peak_gain(f, 1400, 3.0)
                                                   + 0.45 * peak_gain(f, 2600, 3.0))
    out = shape(out, horn)
    out = soft_clip(_norm(out), 2.2)
    out += 0.03 * noise(n, rng, lo=900, hi=5000)
    env = ahr(n, 0.025, dur - 0.1, 0.07) * (1 + 0.03 * np.sin(2 * np.pi * 3.1 * t))
    return small_reverb(out * env, rng, rt60=1.2, wet=0.15)


# ----------------------------------------------------------------------------- stings

def brass(n, f0, rng, bright, attack=0.012, decay=0.4, detune=0.004):
    """Brass-like synth note: two detuned additive saws with a brightness (low-pass) envelope."""
    t = tvec(n)
    cutoff = f0 * (1.5 + bright * (0.6 + 0.4 * np.exp(-t / decay)) * (1 - np.exp(-t / 0.02)))
    out = np.zeros(n)
    for d in (-detune, detune):
        f = np.full(n, f0 * (1 + d))
        out += additive(f, range(1, 50), lambda h, fh: (1.0 / h) / np.sqrt(1 + (fh / cutoff) ** 4), phase0=rng.uniform(0, 6.3))
    return out


def glock(n, f0, rng, tau=0.4):
    ratios = np.array([1.0, 2.76, 5.40, 8.93])
    return modal_ir(f0 * ratios, tau * ratios ** -0.8, np.array([1.0, 0.4, 0.2, 0.1]), n)


NOTE = lambda name: 440.0 * 2 ** ({"C": -9, "C#": -8, "D": -7, "D#": -6, "E": -5, "F": -4, "F#": -3, "G": -2,
                                     "G#": -1, "A": 0, "A#": 1, "B": 2}[name[:-1]] / 12 + (int(name[-1]) - 4))

SPIN = {
    1: (["G4", "D5"], 0.10, 3.0, None),
    2: (["A4", "C#5", "E5"], 0.085, 5.0, None),
    3: (["B4", "D#5", "F#5", "B5"], 0.075, 7.5, None),
    4: (["C5", "E5", "G5", "C6"], 0.065, 11.0, ["C5", "E5", "G5", "C6", "E6"]),
}


def sting_spin(rng, tier, dur):
    notes, step, bright, chord = SPIN[tier]
    n = ns(dur + 0.3)
    out = np.zeros(n)
    for k, name in enumerate(notes):
        f0 = NOTE(name)
        t0 = k * step
        last = k == len(notes) - 1 and chord is None
        length = (dur - t0) if last else step * 1.6
        m = ns(length + 0.05)
        e = ahr(m, 0.006, max(0.0, length - (0.12 if last else 0.04)), 0.12 if last else 0.05)
        note = brass(m, f0, rng, bright) * e * (0.8 + 0.2 * k / len(notes))
        note += 0.25 * (tier / 4) * glock(m, f0 * 2, rng, tau=0.25)
        add_at(out, note, ns(t0))
    if chord:
        t0 = len(notes) * step
        length = dur - t0
        m = ns(length + 0.05)
        e = ahr(m, 0.01, length - 0.2, 0.2)
        for name in chord:
            add_at(out, 0.55 * brass(m, NOTE(name), rng, bright, decay=0.6) * e, ns(t0))
            add_at(out, 0.18 * glock(m, NOTE(name) * 2, rng, tau=0.5), ns(t0))
        shimmer = noise(m, rng, lo=5000, hi=14000) * np.sin(np.pi * np.clip(np.arange(m) / m, 0, 1)) ** 2
        add_at(out, 0.06 * shimmer, ns(t0))
    return small_reverb(out, rng, rt60=0.9, wet=0.18 + 0.04 * tier)


def kick(n, rng, f_end=52.0, f_start=170.0, tau=0.16):
    t = tvec(n)
    f = f_end + (f_start - f_end) * np.exp(-t / 0.03)
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * env_exp(n, tau, attack=0.001)


def sting_wreck(rng, v, dur):
    n = ns(dur + 0.2)
    t = tvec(n)
    out = kick(n, rng) * 1.0
    out += 0.5 * noise(n, rng, lo=900, hi=7000) * env_exp(n, 0.05, attack=0.0005)
    f = rng.uniform(300, 2500, 40)
    out += 0.35 * _norm(modal_ir(f, 0.12 * (300 / f) ** 0.7, rng.uniform(0.3, 1.0, 40), n))
    # downward sweep: a falling saw with a falling low-pass, and a noise band following it
    fs = 1300.0 * (70.0 / 1300.0) ** np.clip(t / (dur * 0.85), 0, 1)
    saw = additive(fs, range(1, 40), lambda h, fh: (1.0 / h) / np.sqrt(1 + (fh / (fs * 3.0)) ** 4))
    sweep_env = np.clip(t / 0.03, 0, 1) * np.exp(-t / (dur * 0.45))
    out += 0.45 * saw * sweep_env

    def band(tt, ff):
        fc = 4000.0 * (150.0 / 4000.0) ** np.clip(tt / (dur * 0.8), 0, 1)
        return peak_gain(ff[None, :], fc[:, None], 2.0)

    out += 0.25 * stft_filter(noise(n, rng), band) * sweep_env
    return small_reverb(soft_clip(_norm(out), 1.6), rng, rt60=1.1, wet=0.25)


def sting_final(rng, v, dur):
    n = ns(dur + 0.3)
    t = tvec(n)
    out = 1.2 * kick(n, rng, f_end=42.0, f_start=120.0, tau=0.5)
    timp = np.array([1.0, 1.5, 1.99, 2.44, 2.96])
    out += 0.5 * modal_ir(98.0 * timp, 0.6 * timp ** -0.7, np.array([1.0, 0.7, 0.5, 0.3, 0.2]), n)
    out += 0.45 * noise(n, rng, lo=200, hi=5000, tilt=-2) * env_exp(n, 0.09, attack=0.001)
    # tension chord: D minor with a G# (tritone) on top, swelling with a tremolo
    chord = np.zeros(n)
    for name, lvl in (("D2", 1.0), ("A2", 0.8), ("D3", 0.7), ("F3", 0.6), ("G#3", 0.5), ("D4", 0.3)):
        chord += lvl * brass(n, NOTE(name), rng, bright=2.0 + 4.0 * 1.0, decay=1.5, detune=0.006)
    swell = np.clip(np.interp(t, [0, 0.05, 0.4, dur * 0.8, dur], [0, 0.55, 0.6, 1.0, 0.0]), 0, 1)
    trem = 1 + 0.25 * np.sin(2 * np.pi * (6 + 4 * t / dur) * t) * np.clip(t / dur, 0, 1)
    chord = shape(chord, lambda f: band_gain(f, 50, 3500, 2))
    out += 0.5 * _norm(chord) * swell * trem
    return small_reverb(soft_clip(_norm(out), 1.4), rng, rt60=2.0, wet=0.35)


# ----------------------------------------------------------------------------- whooshes

def _swept_noise(n, rng, fc_fn, q):
    def g(tt, ff):
        fc = fc_fn(tt)
        return peak_gain(ff[None, :], np.maximum(fc, 30.0)[:, None], q)
    return stft_filter(noise(n, rng), g)


def banner_whoosh(rng, v, dur):
    n = ns(dur + 0.1)
    t = tvec(n)
    pk = dur * 0.42
    fc = lambda tt: 500.0 * np.exp(np.log(3200.0 / 500.0) * np.exp(-0.5 * ((tt - pk) / (dur * 0.22)) ** 2))
    x = _swept_noise(n, rng, fc, 1.4) + 0.4 * _swept_noise(n, rng, lambda tt: fc(tt) * 1.9, 3.0)
    env = np.where(t < pk, (t / pk) ** 2.2, np.exp(-(t - pk) / (dur * 0.16)))
    return x * env


def slowmo_in(rng, v, dur):
    n = ns(dur + 0.1)
    t = tvec(n)
    u = np.clip(t / dur, 0, 1)
    fc = lambda tt: 3200.0 * (140.0 / 3200.0) ** np.clip(tt / dur, 0, 1)
    whoosh = _swept_noise(n, rng, fc, 2.0)
    f = 520.0 * (1 - 0.88 * u) ** 1.6 + 30.0
    tone = additive(f, range(1, 30), lambda h, fh: (1.0 / h) / np.sqrt(1 + (fh / (f * 2.5)) ** 4))
    env = np.clip(t / 0.03, 0, 1) * (1 - u) ** 0.7
    sub = np.sin(2 * np.pi * np.cumsum(70.0 * (1 - 0.6 * u)) / SR) * np.clip(t / 0.06, 0, 1) * (1 - u)
    return (0.8 * _norm(whoosh) + 0.45 * _norm(tone) + 0.3 * sub) * env


# ----------------------------------------------------------------------------- engine misfire

def misfire(rng, v, dur):
    """0: a small pop, 1: a big bang, 2: a double pop, 3: a crackle of small pops."""
    n = ns(dur + 0.1)
    L = (2.0, 2.9, 2.4, 3.3)[v % 4] * rng.uniform(0.95, 1.05)    # a different exhaust per variant
    f0 = (343.0 + 0.6 * 300.0) / (2 * L)               # hot exhaust gas
    modes = [f0 * m * (1 + 0.004 * m * m) for m in range(1, 22)]
    gains = [1.0 / (1 + 0.22 * m) for m in range(1, 22)]
    q = rng.uniform(5, 9)
    pipe = modal_ir(modes, [q / (np.pi * f) for f in modes], gains, ns(0.25))
    kind = v % 4
    events = {0: [(0.0, 0.6)], 1: [(0.0, 1.0)], 2: [(0.0, 0.8), (rng.uniform(0.045, 0.075), 0.6)],
              3: [(0.0, 0.55)] + [(t, rng.uniform(0.2, 0.45)) for t in np.sort(rng.uniform(0.02, 0.11, int(rng.integers(3, 6))))]}[kind]
    exc = np.zeros(n)
    for t0, a in events:
        p = half_sine_pulse(rng.uniform(0.0004, 0.0009) * (1.5 if kind == 1 else 1.0))
        m = ns(0.015)
        burst = noise(m, rng, lo=300, hi=8000) * np.exp(-np.arange(m) / (rng.uniform(0.0015, 0.004) * SR))
        add_at(exc, a * 3.0 * p / np.sqrt(len(p) / 20.0), ns(t0))
        add_at(exc, a * (1.6 if kind == 1 else 1.1) * burst, ns(t0))
    out = convolve(exc, pipe, full=False) + 0.9 * exc
    out = onepole_lp(out, rng.uniform(2500, 4000) * (1.5 if kind == 1 else 1.0))
    out = out - onepole_lp(out, 30.0)
    # the arena answers: two slap echoes and a short diffuse tail
    tail = np.zeros(n)
    for d, g in ((rng.uniform(0.045, 0.08), 0.35), (rng.uniform(0.11, 0.16), 0.15)):
        add_at(tail, g * out, ns(d))
    out += onepole_lp(tail, 2200)
    out = small_reverb(out, rng, rt60=0.45, wet=0.25)
    return soft_clip(_norm(out), 1.8)


# ----------------------------------------------------------------------------- UI

def _click(n, rng, freqs, taus, amps, body=0.4):
    """A crisp click: a few modes plus a one-cycle (DC-free) body pulse."""
    x = modal_ir(freqs, taus, amps, n)
    m = ns(0.0008)
    x[:m] += body * np.sin(2 * np.pi * np.arange(m) / m)
    return x


def ui_move(rng, v, dur):
    n = ns(dur + 0.02)
    return _click(n, rng, [2650, 4120, 5930, 7480], [0.011, 0.008, 0.006, 0.004], [1.0, 0.6, 0.4, 0.25])


def ui_select(rng, v, dur):
    n = ns(dur + 0.05)
    x = _click(n, rng, [2200, 3500, 5200], [0.006, 0.005, 0.004], [1.0, 0.6, 0.3])
    ting = modal_ir([1567.98, 1567.98 * 2.0, 1567.98 * 2.76], [0.06, 0.035, 0.02], [0.8, 0.3, 0.15], n)
    add_at(x, ting, ns(0.012))
    hi = modal_ir([2349.3, 2349.3 * 2.76], [0.05, 0.02], [0.6, 0.15], n)
    add_at(x, hi, ns(0.045))
    return x


def ui_back(rng, v, dur):
    n = ns(dur + 0.05)
    x = _click(n, rng, [1900, 3000, 4600], [0.006, 0.004, 0.003], [0.8, 0.5, 0.25], body=0.5)
    add_at(x, modal_ir([1396.9, 1396.9 * 2.76], [0.04, 0.015], [0.6, 0.15], n), ns(0.008))
    add_at(x, modal_ir([1046.5, 1046.5 * 2.76], [0.045, 0.015], [0.55, 0.12], n), ns(0.04))
    return x


def tally_tick(rng, v, dur):
    n = ns(dur + 0.01)
    return _click(n, rng, [3100, 4700, 6150], [0.0075, 0.005, 0.004], [1.0, 0.5, 0.3], body=0.25)


def tally_done(rng, v, dur):
    n = ns(dur + 0.1)
    bell = np.array([1.0, 2.0, 2.76, 4.07, 5.4])
    amps = np.array([1.0, 0.5, 0.35, 0.2, 0.12])
    x = modal_ir(1046.5 * bell, 0.12 * bell ** -0.6, amps, n) * 0.7
    y = modal_ir(1568.0 * bell, 0.3 * bell ** -0.6, amps, n)
    add_at(x, y, ns(0.07))
    return small_reverb(x, rng, rt60=0.8, wet=0.12)


def warning_beep(rng, v, dur):
    n = ns(dur + 0.05)
    f0 = 1650.0
    tone = additive(np.full(n, f0), range(1, 12, 2), lambda h, fh: 1.0 / h ** 1.6)
    tone = shape(tone, lambda f: band_gain(f, 600, 8000, 2) * (0.7 + 0.6 * peak_gain(f, 3300, 3.0)))
    env = np.zeros(n)
    for t0 in (0.0, 0.135):
        e = ahr(ns(0.1), 0.002, 0.083, 0.015)
        add_at(env, e, ns(t0))
    return tone * env
