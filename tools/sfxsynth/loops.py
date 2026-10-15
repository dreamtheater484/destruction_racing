"""Continuous loops, synthesized as circular signals so they repeat without a seam.

Every loop is exactly N samples and built only from circular parts: noise shaped in the frequency domain
of the whole loop, slow modulators that are periodic in N, oscillators whose total phase over N is a whole
number of cycles, events whose tails wrap round to the start, and resonators applied as circular
convolutions. Sample N-1 therefore runs straight into sample 0.

scrape_metal  rough noise and stick-slip impulses ringing two sheet-steel panels (crossfaded slowly as the
              contact patch moves), grinding noise, a rumble and an intermittent friction squeal.
scrape_wall   rubber dragging past the tyre stack (a judder at the tyre pitch, low groans), concrete grit
              ticks and a little of the car's own metal.
tyre_squeal   two narrow-band tonal squeals (0.8-2.5 kHz) with slow wander, vibrato and jitter FM,
              weak harmonics, a narrow noise band around each and a little road roar.
tyre_dirt     sand spray hiss, dense pebble ticks against the wheel arch, larger stones knocking the
              panels and a gravelly rumble under the tyres, with the wheel's rotation in it.
hub_grind     the lug-studded hub rotating on the ground (periodic chatter), grinding noise through
              ringing hub/disc modes, bursts of spark crackle, bumps, hard saturation.
fire          a low roar with turbulence, a flickering rustle, crackles with power-law sizes, hiss.
steam         jet hiss with slow pressure changes and spurts, a little bubbling and sizzle.
"""
from __future__ import annotations

import numpy as np

from dsp import (SR, add_at, band_gain, circ_convolve, modal_ir, noise, ns, peak_gain, periodic_lfo,
                 shape, smooth_random, soft_clip, tvec)


# ----------------------------------------------------------------------------- circular helpers

def _norm(x):
    p = np.max(np.abs(x))
    return x / p if p > 0 else x


def _unit(x):
    r = np.sqrt(np.mean(x * x))
    return x / r if r > 0 else x


def circ_times(rate, n, rng):
    """Poisson event times (samples) on the circle."""
    k = rng.poisson(rate * n / SR)
    return np.sort(rng.integers(0, n, k))


def circ_times_mod(rate_curve, n, rng):
    """Poisson events with a time-varying rate (array over the circle), by thinning."""
    lam = float(np.max(rate_curve))
    cand = circ_times(lam, n, rng)
    keep = rng.random(len(cand)) * lam < rate_curve[cand]
    return cand[keep]


def scatter(n, idx, events):
    """Sum events at sample positions, wrapping round the end."""
    out = np.zeros(n)
    for i, e in zip(idx, events):
        add_at(out, e, int(i), wrap=True)
    return out


def periodic_freq(f_curve, n):
    """Scale a frequency curve so it completes a whole number of cycles in n samples; returns phase."""
    cycles = np.sum(f_curve) / SR
    k = max(1, round(cycles))
    f = f_curve * (k / cycles)
    ph = 2 * np.pi * np.concatenate([[0.0], np.cumsum(f[:-1])]) / SR
    return ph


def pos(x, floor=0.0):
    return np.maximum(x, floor)


def panel(rng, n_modes=120, f_lo=180.0, f_hi=6500.0, tau_low=0.15, slope=0.75):
    f = rng.uniform(f_lo, f_hi, n_modes)
    taus = np.clip(tau_low * (f / f_lo) ** -slope * rng.lognormal(0, 0.3, n_modes), 0.003, None)
    amps = rng.rayleigh(1.0, n_modes) * (tau_low / taus) ** 0.3 / np.sqrt(1 + (f / 3500.0) ** 2)
    return f, taus, amps


def panel_ir(rng, n, **kw):
    f, taus, amps = panel(rng, **kw)
    return modal_ir(f, taus, amps, n)


def click(rng, w_lo=4, w_hi=30):
    w = int(rng.integers(w_lo, w_hi + 1))
    b = rng.standard_normal(w) * np.exp(-np.arange(w) / (w / 4.0))
    b[0] += rng.choice([-1.5, 1.5])
    return b


# ----------------------------------------------------------------------------- loops

def scrape_metal(rng, v, dur):
    n = ns(dur)
    pressure = np.clip(0.75 + 0.3 * smooth_random(n, 1.2, rng), 0.2, None)
    rough = 1 + 0.5 * smooth_random(n, 35.0, rng)
    exc = noise(n, rng) * pressure * pos(rough, 0.1)
    rate = 260 * pressure ** 1.5
    idx = circ_times_mod(rate, n, rng)
    imp = scatter(n, idx, [rng.lognormal(0, 0.5) * pressure[i] * 6 * click(rng) for i in idx])
    a = circ_convolve(exc + imp, panel_ir(rng, ns(1.2), tau_low=0.25))
    b = circ_convolve(exc + imp, panel_ir(rng, ns(1.2), tau_low=0.2, f_lo=250))
    w = 0.5 + 0.5 * np.clip(0.8 * periodic_lfo(n, 0.5, rng.uniform(0, 6.3)) + 0.5 * smooth_random(n, 0.8, rng), -1, 1)
    metal = _unit(a) * w + _unit(b) * (1 - w)
    grind = noise(n, rng, lo=1500, hi=6500) * pressure * pos(1 + 0.6 * smooth_random(n, 60.0, rng), 0.05)
    rumble = noise(n, rng, lo=40, hi=220) * pressure
    # friction squeal: one panel mode locks in now and then
    f_sq = rng.uniform(1300, 2600)
    ph = periodic_freq(f_sq * (1 + 0.004 * smooth_random(n, 3.0, rng)), n)
    gate = np.clip(smooth_random(n, 0.9, rng) - 0.6, 0, None) ** 1.5
    squeal = np.sin(ph) * _norm(gate) * pressure
    out = metal + 0.35 * _unit(grind) + 0.35 * _unit(rumble) + 0.5 * squeal
    return soft_clip(out / (4 * np.std(out)), 1.6)


def scrape_wall(rng, v, dur):
    n = ns(dur)
    pressure = np.clip(0.8 + 0.25 * smooth_random(n, 0.8, rng), 0.3, None)
    # tyres of the stack going past: a judder at ~12-20 Hz with jitter, each a soft rubbery bump
    rate = rng.uniform(12, 18)
    k = max(1, int(round(rate * dur)))
    base = (np.arange(k) + 0.25 * rng.standard_normal(k)) * n / k
    idx = np.mod(np.round(base).astype(int), n)
    bumps = []
    for _ in idx:
        m = ns(rng.uniform(0.03, 0.06))
        bumps.append(np.hanning(m) * rng.lognormal(0, 0.3))
    judder = scatter(n, idx, bumps)
    drag = noise(n, rng, lo=60, hi=700, order=2) * (0.35 + judder) * pressure
    drag = circ_convolve(drag, modal_ir([rng.uniform(150, 210), rng.uniform(260, 340)], [0.03, 0.02], [1.0, 0.6], ns(0.3))) + 0.5 * drag
    # rubber groan: a low stick-slip tone that comes and goes
    f0 = rng.uniform(130, 190)
    ph = periodic_freq(f0 * (1 + 0.05 * smooth_random(n, 2.0, rng)), n)
    groan = np.tanh(2.0 * np.sin(ph)) * np.clip(smooth_random(n, 1.2, rng) - 0.2, 0, None)
    groan = shape(groan, lambda f: band_gain(f, 100, 1800, 2), circular=True)
    # grit: concrete behind the tyres, ticks and grinding
    gidx = circ_times(420, n, rng)
    grit = scatter(n, gidx, [rng.lognormal(0, 0.6) * click(rng, 3, 14) for _ in gidx])
    grit = shape(grit, lambda f: band_gain(f, 1500, 9000, 2), circular=True)
    grind = noise(n, rng, lo=900, hi=4500) * pos(1 + 0.7 * smooth_random(n, 25.0, rng), 0.05)
    metal = circ_convolve(noise(n, rng) * pressure, panel_ir(rng, ns(0.5), tau_low=0.08))
    out = (1.0 * _unit(drag) + 0.35 * _unit(groan) + 0.4 * _unit(grit) * pressure +
           0.3 * _unit(grind) * pressure + 0.25 * _unit(metal))
    return soft_clip(out / (4 * np.std(out)), 1.4)


def _squeal_voice(n, rng, f0):
    wander = 0.045 * smooth_random(n, 0.5, rng) + 0.015 * smooth_random(n, 3.0, rng)
    vib = 0.007 * periodic_lfo(n, rng.uniform(6, 11), rng.uniform(0, 6.3))
    jit = 0.003 * smooth_random(n, 40.0, rng)
    f = f0 * (1 + wander + vib + jit)
    ph = periodic_freq(f, n)
    tone = np.sin(ph) + 0.35 * np.sin(2 * ph + 0.7) + 0.12 * np.sin(3 * ph + 1.9) + 0.05 * np.sin(4 * ph + 0.3)
    amp = np.clip(0.8 + 0.25 * smooth_random(n, 0.9, rng), 0.25, None) * pos(1 + 0.2 * smooth_random(n, 18.0, rng), 0.3)
    band = noise(n, rng, gain=lambda ff: peak_gain(ff, f0, 9.0))
    return amp * (tone + 0.5 * band / 1.0), amp


def tyre_squeal(rng, v, dur):
    n = ns(dur)
    f1 = rng.uniform(1050, 1400)
    a, amp1 = _squeal_voice(n, rng, f1)
    b, amp2 = _squeal_voice(n, rng, f1 * rng.uniform(1.18, 1.35))
    comes_goes = np.clip(0.5 + 0.7 * smooth_random(n, 0.7, rng), 0, 1)       # the second tyre grips and slips
    roar = noise(n, rng, lo=200, hi=3500, tilt=-3) * 0.5 * (amp1 + amp2)
    out = _unit(a) + 0.6 * _unit(b) * comes_goes + 0.18 * _unit(roar)
    return soft_clip(out / (3.5 * np.std(out)), 1.2)


def tyre_dirt(rng, v, dur):
    n = ns(dur)
    spin = np.clip(0.8 + 0.25 * smooth_random(n, 1.5, rng), 0.3, None)
    wheel = 1 + 0.25 * periodic_lfo(n, rng.uniform(7, 10), rng.uniform(0, 6.3))
    spray = noise(n, rng, lo=500, hi=7000, tilt=-1) * spin * wheel * pos(1 + 0.5 * smooth_random(n, 20.0, rng), 0.1)
    # pebbles against the wheel arch: dense tiny bright ticks
    rate = 650 * spin * wheel
    idx = circ_times_mod(rate, n, rng)
    ticks = []
    for _ in idx:
        f = rng.uniform(2500, 9000, 2)
        m = ns(0.01)
        e = modal_ir(f, rng.uniform(0.0008, 0.003, 2), rng.uniform(0.5, 1.0, 2), m)
        ticks.append(rng.lognormal(0, 0.7) * e)
    peb = scatter(n, idx, ticks)
    # bigger stones knocking the panels
    sidx = circ_times(14, n, rng)
    stones = scatter(n, sidx, [rng.lognormal(0, 0.4) * modal_ir(rng.uniform(700, 3000, 4), rng.uniform(0.006, 0.02, 4),
                                                                rng.uniform(0.4, 1.0, 4), ns(0.12)) for _ in sidx])
    # gravel crunching under the tyre: a low-mid crackle
    cidx = circ_times(900, n, rng)
    crunch = scatter(n, cidx, [rng.lognormal(0, 0.5) * click(rng, 10, 60) for _ in cidx])
    crunch = shape(crunch, lambda f: band_gain(f, 150, 1500, 2), circular=True) * spin
    rumble = noise(n, rng, lo=35, hi=200) * spin * wheel
    out = (0.8 * _unit(spray) + 0.7 * _unit(peb) + 0.35 * _unit(stones) + 0.7 * _unit(crunch) + 0.6 * _unit(rumble))
    return soft_clip(out / (4 * np.std(out)), 1.3)


def hub_grind(rng, v, dur):
    n = ns(dur)
    rot_hz = rng.uniform(6.5, 8.5)
    cycles = max(1, round(rot_hz * dur))
    ph = 2 * np.pi * cycles * np.arange(n) / n
    lugs = 5
    # contact pressure: the hub's lumps (lug studs, brake drum edge) hit the ground each revolution
    lump = np.clip(np.cos(lugs * ph), 0, None) ** 6 + 0.6 * np.clip(np.cos(ph + 0.4), 0, None) ** 3
    bumpy = np.clip(0.7 + 0.5 * smooth_random(n, 2.5, rng), 0.15, None)
    press = (0.35 + lump) * bumpy
    exc = noise(n, rng) * press * pos(1 + 0.8 * smooth_random(n, 80.0, rng), 0.0)
    # stud strikes as impulses
    sidx = (np.arange(cycles * lugs) * n / (cycles * lugs) + rng.normal(0, 30, cycles * lugs)).astype(int) % n
    strikes = scatter(n, sidx, [rng.lognormal(0, 0.5) * bumpy[i] * 8 * click(rng, 3, 20) for i in sidx])
    # big bumps in the ground
    bidx = circ_times(3.0, n, rng)
    strikes += scatter(n, bidx, [rng.uniform(15, 30) * click(rng, 20, 60) for _ in bidx])
    f = np.concatenate([rng.uniform(900, 4500, 10), rng.uniform(300, 900, 4)])     # hub / brake disc modes
    hub = modal_ir(f, rng.uniform(0.02, 0.09, len(f)) * (1000 / f) ** 0.3, rng.uniform(0.4, 1.0, len(f)), ns(0.6))
    ring = circ_convolve(exc + strikes, hub) + 0.4 * circ_convolve(exc, panel_ir(rng, ns(0.4), tau_low=0.05))
    grind = noise(n, rng, lo=1200, hi=8000) * press
    # sparks: bursts of very bright crackle that flare up and die
    flare = np.clip(smooth_random(n, 3.0, rng) + 0.3, 0, None) ** 2 * press
    cidx = circ_times_mod(2500 * flare / (np.max(flare) + 1e-9), n, rng)
    sparks = scatter(n, cidx, [rng.lognormal(0, 0.6) * click(rng, 2, 8) for _ in cidx])
    sparks = shape(sparks, lambda ff: band_gain(ff, 4500, 16000, 2), circular=True)
    rumble = noise(n, rng, lo=40, hi=250) * press
    out = 1.0 * _unit(ring) + 0.5 * _unit(grind) + 0.45 * _unit(sparks) + 0.5 * _unit(rumble)
    return soft_clip(out / (2.5 * np.std(out)), 2.4)


def fire(rng, v, dur):
    n = ns(dur)
    turb = np.clip(0.75 + 0.3 * smooth_random(n, 1.4, rng), 0.2, None)
    flick = pos(1 + 0.35 * smooth_random(n, 9.0, rng), 0.1)
    roar = noise(n, rng, lo=30, hi=320, order=2, tilt=-2) * turb * flick
    rustle = noise(n, rng, lo=300, hi=2500, tilt=-3) * turb * pos(1 + 0.6 * smooth_random(n, 16.0, rng), 0.05)
    hiss = noise(n, rng, lo=3000, hi=11000) * flick
    # crackles: power-law sized pops, a few bigger snaps that ring a little
    idx = circ_times(26, n, rng)
    pops = []
    for _ in idx:
        a = min(8.0, rng.pareto(2.2) + 0.3)
        e = shape(click(rng, 6, 80), lambda ff: band_gain(ff, 800, 7000, 1))
        if a > 2.0:
            e = np.concatenate([e, np.zeros(ns(0.03))])
            e += 0.4 * modal_ir(rng.uniform(900, 3500, 3), rng.uniform(0.003, 0.01, 3), rng.uniform(0.4, 1, 3), len(e))
        pops.append(a * e)
    crackle = scatter(n, idx, pops)
    fidx = circ_times(220, n, rng)
    fizz = scatter(n, fidx, [rng.lognormal(0, 0.5) * click(rng, 2, 10) for _ in fidx])
    fizz = shape(fizz, lambda ff: band_gain(ff, 2000, 9000, 2), circular=True)
    out = (1.0 * _unit(roar) + 0.35 * _unit(rustle) + 0.12 * _unit(hiss) + 0.55 * _unit(crackle) + 0.2 * _unit(fizz))
    return soft_clip(out / (4 * np.std(out)), 1.3)


def steam(rng, v, dur):
    n = ns(dur)
    pres = np.clip(0.8 + 0.2 * smooth_random(n, 0.6, rng), 0.3, None)
    # spurts: the pressure cap lifting now and then
    k = int(rng.integers(2, 4))
    for c in rng.uniform(0, n, k):
        w = ns(rng.uniform(0.25, 0.6))
        d = np.minimum(np.abs(np.arange(n) - c), n - np.abs(np.arange(n) - c))
        pres += 0.35 * np.exp(-0.5 * (d / (w / 2.5)) ** 2)
    flutter = pos(1 + 0.12 * smooth_random(n, 14.0, rng), 0.3)
    jet = noise(n, rng, lo=1200, hi=12000, order=2, gain=lambda f: 0.5 + peak_gain(f, rng.uniform(4000, 6500), 1.2)) * pres * flutter
    body = noise(n, rng, lo=300, hi=1800, tilt=-2) * pres
    # bubbling: small rising blips
    idx = circ_times(9, n, rng)
    bubs = []
    for _ in idx:
        m = ns(rng.uniform(0.02, 0.05))
        tt = tvec(m)
        f = rng.uniform(300, 900) * (1 + 2.5 * tt / tt[-1])
        bubs.append(rng.lognormal(0, 0.4) * np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-tt / (m / SR / 3)) * np.hanning(m) ** 0.3)
    bubbles = scatter(n, idx, bubs)
    sidx = circ_times(60, n, rng)
    sizzle = shape(scatter(n, sidx, [rng.lognormal(0, 0.5) * click(rng, 2, 8) for _ in sidx]),
                   lambda f: band_gain(f, 3500, 12000, 2), circular=True)
    out = 1.0 * _unit(jet) + 0.25 * _unit(body) + 0.2 * _unit(bubbles) + 0.12 * _unit(sizzle)
    return soft_clip(out / (4 * np.std(out)), 1.2)
