"""Impacts, glass and loose parts: modal synthesis driven by physically-shaped excitations.

Car-vs-car (sheet steel). A hit is one or more *stages*, each the sum of
  * a thump: the body mass, a 40-125 Hz sine whose pitch drops as the contact stiffens, plus low noise;
  * a metal crunch: 1-3 "panels", each a bank of inharmonic plate modes (constant modal density between
    150 Hz and 6 kHz, decay time falling with frequency so the highs die first) excited by the main
    contact pulse (a half-sine of a few ms, which only reaches the low modes) and by a cluster of sharp
    buckling snaps inside the first 30-120 ms;
  * a crackle: band-passed noise gated by hundreds of sub-millisecond bursts (sheet metal buckling and
    tearing);
and for the bigger tiers a tearing layer (dense stick-slip bursts through a sweeping band-pass and a
ringing panel), a rattle tail (small loose pieces, each a click and a few high modes, at thinning random
times), a loose-panel buzz and, for the huge tier, three stages and a glass shower. Layers are mixed by
RMS, then a crest limiter keeps the variants of a tier at a similar loudness after peak normalisation.

Tyre barrier: a soft long contact pulse into a few low stack/air-cavity modes, a rubber slap, a short
stick-slip rubber squeak glide, rebound thumps and the stack rocking, with a bit of the car's own metal.
Concrete: a hard dry crack (impulse + very damped concrete modes), grit, plus a heavy metal crunch.
Glass: a bright crack and a shower of many tiny inharmonic "tinkles" (2-12 kHz modes, 3-40 ms) with
a decaying density and a second wave when the pieces land; laminated windscreens crackle more and
shower less. Parts: a stick-slip tearing excitation through panel modes and a sweeping band-pass, then
the part hitting the floor (free-free beam modes). Debris and loose wheels: bouncing-object timing
(gaps shrink by the coefficient of restitution) with per-object modes.
"""
from __future__ import annotations

from dataclasses import dataclass

import numpy as np

from dsp import (SR, add_at, bounce_times, convolve, env_exp, env_points, half_sine_pulse, modal_ir,
                 limiter, noise, ns, onepole_lp, poisson_times, shape, band_gain, soft_clip, stft_filter, tvec, undb)


def _norm(x):
    p = np.max(np.abs(x))
    return x / p if p > 0 else x


def crest(x, max_db, t0=0.0, t1=0.15):
    """Tame isolated peaks: limit to max_db above the RMS of the body of the sound (keeps variants of a
    tier at a similar loudness once they are peak-normalised)."""
    seg = x[ns(t0):ns(t1)]
    r = np.sqrt(np.mean(seg * seg)) if len(seg) else 0.0
    if r <= 0:
        return x
    return limiter(x, r * undb(max_db), window_ms=3.0)


# ----------------------------------------------------------------------------- building blocks

@dataclass
class Modes:
    freqs: np.ndarray
    taus: np.ndarray
    amps: np.ndarray

    def ir(self, n):
        return modal_ir(self.freqs, self.taus, self.amps, n)


def panel_modes(rng, n_modes=160, f_lo=150.0, f_hi=6000.0, tau_low=0.09, slope=0.85, bright=3500.0,
                boom=4, boom_gain=1.4):
    """A sheet-steel panel: plate-like constant modal density, decay falling with frequency, plus a few
    strong low 'boom' modes. Fast modes start louder (amplitude ~ tau^-0.35) so the crunch stays bright."""
    f = rng.uniform(f_lo, f_hi, n_modes)
    fb = rng.uniform(f_lo * 0.75, 420.0, boom)
    f = np.concatenate([fb, f])
    taus = tau_low * (f / f_lo) ** -slope * rng.lognormal(0.0, 0.35, len(f))
    taus = np.clip(taus, 0.002, tau_low * 1.5)
    amps = rng.rayleigh(1.0, len(f)) * (tau_low / taus) ** 0.35 / np.sqrt(1 + (f / bright) ** 2)
    amps[:boom] *= boom_gain
    return Modes(f, taus, amps / np.sqrt(len(f)))


def small_piece_modes(rng, f_lo=900.0, f_hi=7000.0, count=4, tau=(0.003, 0.016)):
    """A small loose metal piece (bracket, trim, bolt): a few inharmonic modes."""
    f0 = np.exp(rng.uniform(np.log(f_lo), np.log(f_hi)))
    ratios = np.concatenate([[1.0], np.sort(rng.uniform(1.3, 4.2, count - 1))])
    f = f0 * ratios
    taus = rng.uniform(*tau) * ratios ** -0.6
    amps = rng.uniform(0.4, 1.0, count) / ratios ** 0.5
    return Modes(f, taus, amps)


def thump(n, rng, f_end, tau, f_start_mult=1.9, attack=0.0015, noise_amt=0.35, noise_lp=220.0):
    """Body-mass thump: a sine whose pitch drops from f_start to f_end, plus low-passed noise."""
    t = tvec(n)
    f = f_end * (1 + (f_start_mult - 1) * np.exp(-t / 0.02))
    ph = 2 * np.pi * np.cumsum(f) / SR
    env = env_exp(n, tau, attack=attack)
    s = np.sin(ph) * env
    lp = onepole_lp(onepole_lp(rng.standard_normal(n), noise_lp), noise_lp)
    s += noise_amt * _norm(lp) * env_exp(n, tau * 0.5, attack=attack)
    return s


def snaps(n, times, amps, rng, width=(1, 24)):
    """Sharp buckling snaps: tiny noise bursts (sub-millisecond) at the given times."""
    x = np.zeros(n)
    for t, a in zip(times, amps):
        w = int(rng.integers(width[0], width[1] + 1))
        burst = rng.standard_normal(w) * np.exp(-np.arange(w) / max(1.0, w / 3.0))
        burst[0] += rng.choice([-2.0, 2.0])
        add_at(x, a * burst, ns(t))
    return x


def burst_gate(n, times, amps, rng, ms=(0.3, 3.0)):
    """Positive envelope made of many short exponential bursts (for crackles)."""
    g = np.zeros(n)
    for t, a in zip(times, amps):
        tau = rng.uniform(*ms) / 1000.0
        m = ns(tau * 6)
        add_at(g, a * np.exp(-np.arange(m) / (tau * SR)), ns(t))
    return g


def cluster_times(rng, count, t0, window, a=1.2, b=2.6):
    """Event times clustered near the start of a window (beta-distributed)."""
    return t0 + window * rng.beta(a, b, count)


# ----------------------------------------------------------------------------- glass

def tinkle(rng, f_base, tau, n_partials=4):
    """One small glass fragment: a handful of inharmonic modes."""
    ratios = np.array([1.0, rng.uniform(1.45, 1.85), rng.uniform(2.3, 2.95), rng.uniform(3.4, 4.7)])[:n_partials]
    f = f_base * ratios
    taus = tau * ratios ** -0.7
    amps = np.array([1.0, 0.7, 0.45, 0.3])[:n_partials] * rng.uniform(0.5, 1.0, n_partials)
    m = ns(min(0.12, tau * 7))
    return modal_ir(f, taus, amps, m, phases=rng.uniform(0, 0.3, n_partials))


def glass_shower(n, rng, count, t0=0.0, tau_burst=0.06, landing=(0.22, 0.09), landing_share=0.45,
                 f_lo=2000.0, f_hi=12000.0, tail_tau=0.35, level_tau=0.25):
    """Shower of fragments: an initial burst that thins out, a second wave when they land, a tail."""
    x = np.zeros(n)
    dur = n / SR
    k_land = int(count * landing_share)
    k_burst = int(count * 0.4)
    k_tail = count - k_land - k_burst
    times = np.concatenate([
        t0 + rng.exponential(tau_burst, k_burst),
        t0 + np.abs(rng.normal(landing[0], landing[1], k_land)),
        t0 + rng.exponential(tail_tau, k_tail),
    ])
    times = times[times < dur - 0.01]
    for t in times:
        fb = np.exp(rng.uniform(np.log(f_lo), np.log(f_hi * 0.7)))
        tau = rng.uniform(0.004, 0.035) * (4000.0 / fb) ** 0.5
        amp = rng.lognormal(0.0, 0.6) * np.exp(-(t - t0) / level_tau)
        add_at(x, amp * tinkle(rng, fb, tau, n_partials=int(rng.integers(2, 5))), ns(t))
    return x


def glass_crack(n, rng, dull=0.0):
    """The initial fracture: a sharp broadband click, very short high noise and a few bright modes."""
    x = np.zeros(n)
    m = ns(0.004)
    burst = noise(m * 4, rng, lo=2500.0)[:m] * np.exp(-np.arange(m) / (m / 4.0))
    burst[0] += 3.0
    x[:m] += burst
    f = rng.uniform(3000, 9000, 8)
    x += modal_ir(f, rng.uniform(0.002, 0.008, 8), rng.uniform(0.3, 1.0, 8), n)
    if dull > 0:
        # laminated screen: a duller, lower thunk as the interlayer takes the load
        f2 = rng.uniform(500, 2200, 10)
        x += dull * modal_ir(f2, rng.uniform(0.006, 0.02, 10), rng.uniform(0.3, 1.0, 10), n)
    return x


# ----------------------------------------------------------------------------- car-vs-car metal

@dataclass
class MetalTier:
    thump_hz: tuple          # final thump pitch range
    thump_tau: tuple         # thump decay
    contact_ms: tuple        # main contact pulse length (longer = duller)
    buckles: tuple           # number of buckling snaps
    window: tuple            # seconds over which the snaps cluster
    panels: int              # separate panels that get excited
    panel_tau: float         # decay of the lowest panel modes
    boom: float              # gain of the large-panel low modes
    crackle_db: float        # crackle burst level relative to the modal crunch
    crackle_bursts: tuple
    thump_db: float          # thump level relative to the crunch
    tear_db: float | None    # tearing layer (None: none)
    rattle: tuple            # count of debris tinks
    rattle_db: float
    buzz: bool               # loose panel buzz
    stages: int              # crunch stages (huge: several)
    glass_db: float | None
    drive: float


TIERS = {
    "light": MetalTier((78, 125), (0.02, 0.036), (0.8, 2.0), (0, 2), (0.01, 0.03), 1, 0.10, 1.6, -9, (8, 24),
                       -6, None, (0, 0), -99, False, 1, None, 1.2),
    "medium": MetalTier((62, 82), (0.04, 0.06), (3.0, 5.0), (6, 12), (0.035, 0.06), 2, 0.09, 1.4, -5, (80, 160),
                        -1, None, (8, 14), -15, False, 1, None, 1.5),
    "heavy": MetalTier((50, 66), (0.06, 0.085), (5.0, 8.0), (16, 28), (0.07, 0.11), 3, 0.085, 1.3, -3, (240, 420),
                       1.5, -4, (16, 26), -14, True, 1, None, 1.9),
    "huge": MetalTier((40, 52), (0.08, 0.11), (8.0, 12.0), (26, 38), (0.09, 0.12), 3, 0.085, 1.3, -3, (380, 620),
                      2.5, -3, (28, 42), -13, True, 3, -9, 2.3),
}


def _rmsnorm(x, t0=0.0, t1=None):
    seg = x[ns(t0):(ns(t1) if t1 is not None else None)]
    r = np.sqrt(np.mean(seg * seg)) if len(seg) else 0.0
    return x / r if r > 0 else x


def metal_crunch(n, rng, tier: MetalTier, t0=0.0):
    """Contact pulse + buckling snaps through 1-3 panels, plus the crackle burst (unit RMS over the crunch)."""
    window = rng.uniform(*tier.window)
    k = int(rng.integers(tier.buckles[0], tier.buckles[1] + 1))
    times = np.sort(cluster_times(rng, k, 0.003, window))
    amps = rng.lognormal(0.0, 0.5, k) * (1 - 0.5 * (times / max(window, 1e-3)))
    if tier.buckles[1] <= 2 and k > 0:
        times = 0.006 + rng.uniform(0.0, 0.025, k)        # a small dent pops in after the knock
        amps = rng.uniform(0.3, 0.6, k)
    pulse = half_sine_pulse(rng.uniform(*tier.contact_ms) / 1000.0)
    ir_len = min(n, ns(0.7))
    modal = np.zeros(n)
    for p in range(tier.panels):
        modes = panel_modes(rng, tau_low=tier.panel_tau * rng.uniform(0.8, 1.25), boom_gain=tier.boom,
                            bright=rng.uniform(2500, 4500))
        exc = np.zeros(n)
        if p == 0:
            add_at(exc, pulse * 6.0 / len(pulse) ** 0.5, 0)
        sel = rng.random(k) < (1.0 / tier.panels + 0.25)
        exc += snaps(n, times[sel], amps[sel], rng)
        modal += convolve(exc, modes.ir(ir_len), full=False)
    # crackle: band-passed noise gated by many micro-bursts in the buckling window
    kb = int(rng.integers(*tier.crackle_bursts))
    bt = cluster_times(rng, kb, 0.0, window * 1.6, a=1.1, b=2.2)
    gate = burst_gate(n, bt, rng.lognormal(0, 0.6, kb) * np.exp(-bt / (window * 1.4)), rng)
    lo = rng.uniform(700, 1400)
    cr = (noise(n, rng, lo=lo, hi=lo * rng.uniform(3, 5), order=2) +
          0.6 * noise(n, rng, lo=250, hi=1200, order=2)) * gate
    span = window + 0.04
    out = _rmsnorm(modal, 0, span) + undb(tier.crackle_db) * _rmsnorm(cr, 0, span)
    shifted = np.zeros(n)
    add_at(shifted, out, ns(t0))
    return shifted


def tear(n, rng, t0, dur):
    """Sheet metal tearing: a dense run of stick-slip bursts, band-passed noise sweeping in frequency
    and the same bursts ringing a panel."""
    rate = rng.uniform(350, 800)
    times = poisson_times(rate, t0, t0 + dur, rng)
    env = np.sin(np.pi * np.clip((times - t0) / dur, 0, 1)) ** 0.6
    amps = rng.lognormal(0, 0.5, len(times)) * env
    gate = burst_gate(n, times, amps, rng, ms=(0.2, 1.5))
    fa, fb = rng.uniform(800, 1600), rng.uniform(2000, 4500)
    if rng.random() < 0.5:
        fa, fb = fb, fa
    q = rng.uniform(1.8, 3.0)

    def sweep(tt, f):
        fc = fa * (fb / fa) ** np.clip((tt - t0) / dur, 0, 1)
        ff = np.maximum(f[None, :], 1.0)
        return 1.0 / np.sqrt(1 + q * q * (ff / fc[:, None] - fc[:, None] / ff) ** 2)

    band = stft_filter(noise(n, rng), sweep) * gate
    ring = convolve(snaps(n, times, amps * 0.5, rng, width=(2, 16)),
                    panel_modes(rng, n_modes=100, tau_low=0.06, bright=4000, boom=0).ir(ns(0.4)), full=False)
    return _rmsnorm(band, t0, t0 + dur) + 0.8 * _rmsnorm(ring, t0, t0 + dur)


def rattle(n, rng, t_start, t_end, count, clunks=0):
    """Small loose pieces settling: thinning random times, each a short click and a few high modes."""
    x = np.zeros(n)
    span = max(0.02, t_end - t_start)
    times = t_start + span * rng.beta(1.0, 1.7, count)
    times = np.concatenate([times, t_start + span * rng.uniform(0.75, 0.97, 2)])   # the last bits settling
    for i, t in enumerate(times):
        m = small_piece_modes(rng)
        hit = m.ir(ns(0.08))
        w = int(rng.integers(8, 40))
        hit[:w] += rng.standard_normal(w) * np.exp(-np.arange(w) / (w / 4)) * 0.6 * np.max(np.abs(hit))
        late = i >= count
        amp = rng.lognormal(0, 0.5) * (0.45 if late else (1 - 0.7 * (t - t_start) / span))
        add_at(x, amp * hit, ns(t))
    for _ in range(clunks):
        t = t_start + rng.uniform(0.05, 0.7) * span
        m = small_piece_modes(rng, f_lo=250, f_hi=900, count=5, tau=(0.02, 0.05))
        add_at(x, 1.5 * rng.uniform(0.6, 1.0) * m.ir(ns(0.3)), ns(t))
    return x


def panel_buzz(n, rng, t0, dur):
    """A loose panel flapping against the body: a quick run of clicks through mid modes."""
    rate = rng.uniform(16, 32)
    times = t0 + np.cumsum(rng.gamma(8.0, 1.0 / (8.0 * rate), int(dur * rate)))
    amps = np.exp(-(times - t0) / (dur * 0.45)) * rng.uniform(0.5, 1.0, len(times))
    exc = snaps(n, times, amps, rng, width=(2, 10))
    modes = panel_modes(rng, n_modes=40, f_lo=300, f_hi=3500, tau_low=0.04, boom=0)
    return convolve(exc, modes.ir(ns(0.3)), full=False)


def hit_metal(rng, v, dur, tier_name):
    tier = TIERS[tier_name]
    n = ns(dur + 0.25)
    out = np.zeros(n)
    stage_t = [0.0]
    if tier.stages >= 2:
        stage_t.append(rng.uniform(0.14, 0.3))
    if tier.stages >= 3:
        stage_t.append(stage_t[-1] + rng.uniform(0.18, 0.35))
    stage_gain = [1.0, rng.uniform(0.6, 0.8), rng.uniform(0.4, 0.55)]
    for s, t0 in enumerate(stage_t):
        th = thump(n, rng, rng.uniform(*tier.thump_hz) * (1 + 0.15 * s), rng.uniform(*tier.thump_tau))
        th = _rmsnorm(th, 0, 0.1)
        stage = undb(tier.thump_db) * th + metal_crunch(n, rng, tier)
        if tier.tear_db is not None:
            td = rng.uniform(0.1, 0.22)
            stage += undb(tier.tear_db) * tear(n, rng, rng.uniform(0.015, 0.05), td) * 0.5
        add_at(out, stage_gain[s] * stage, ns(t0))
    end = dur * 0.97
    if tier.rattle[1] > 0:
        cnt = int(rng.integers(tier.rattle[0], tier.rattle[1] + 1))
        clunks = 0 if tier_name == "medium" else int(rng.integers(1, 4))
        r = rattle(n, rng, 0.04, end, cnt, clunks=clunks)
        out += undb(tier.rattle_db) * _rmsnorm(r, 0.04, end)
    if tier.buzz:
        b = panel_buzz(n, rng, rng.uniform(0.03, 0.1), rng.uniform(0.15, 0.35))
        out += undb(-14) * _rmsnorm(b, 0.03, 0.4)
    if tier.glass_db is not None:
        g0 = rng.uniform(0.02, 0.08)
        g = glass_shower(n, rng, int(rng.integers(500, 800)), t0=g0, landing=(0.35, 0.12),
                         level_tau=0.5, tail_tau=0.5)
        out += undb(tier.glass_db) * _rmsnorm(g, g0, g0 + 0.5)
    out = shape(out, lambda f: band_gain(f, None, 9000.0, 1))
    return crest(soft_clip(_norm(out), tier.drive), 13.0)


# ----------------------------------------------------------------------------- car-vs-arena

TYRE_TIERS = {
    # contact ms, stack Hz, rebounds, squeak level, metal tier/level, concrete thock
    "light": dict(contact=(16, 24), stack=(70, 90), rebounds=1, squeak=0.25, metal=("light", 0.25), thock=0.0, drive=1.1),
    "medium": dict(contact=(24, 34), stack=(58, 74), rebounds=2, squeak=0.35, metal=("light", 0.4), thock=0.0, drive=1.3),
    "heavy": dict(contact=(32, 45), stack=(46, 60), rebounds=3, squeak=0.4, metal=("medium", 0.5), thock=0.45, drive=1.6),
}


def rubber_squeak(n, rng, t0, dur):
    """Rubber flexing against paint: a short stick-slip glide with harmonics and jittery amplitude."""
    m = ns(dur)
    t = tvec(m)
    f_a, f_b = rng.uniform(260, 420), rng.uniform(380, 700)
    if rng.random() < 0.5:
        f_a, f_b = f_b, f_a
    f = f_a + (f_b - f_a) * (t / dur) + 12 * np.sin(2 * np.pi * rng.uniform(18, 30) * t)
    ph = 2 * np.pi * np.cumsum(f) / SR
    s = np.tanh(2.5 * np.sin(ph)) + 0.3 * np.sin(2 * ph + 0.4)
    am = 1 + 0.6 * onepole_lp(rng.standard_normal(m), 40.0) * 8
    env = np.sin(np.pi * t / dur) ** 1.5
    s = shape(s * env * np.clip(am, 0.1, None), lambda f_: band_gain(f_, 250, 3000, 2))
    x = np.zeros(n)
    add_at(x, s, ns(t0))
    return x


def tyre_body(n, rng, contact_ms, stack_hz):
    """Soft compression of the stack: a long half-sine pulse into the stack and air-cavity modes."""
    pulse = half_sine_pulse(contact_ms / 1000.0)
    f = [stack_hz, stack_hz * rng.uniform(1.9, 2.3), rng.uniform(150, 210), rng.uniform(260, 340), rng.uniform(420, 560)]
    taus = [0.06, 0.04, 0.035, 0.025, 0.015]
    amps = [1.0, 0.45, 0.5, 0.25, 0.12]
    body = convolve(modal_ir(f, taus, amps, ns(0.5)), pulse, full=False)
    body = np.pad(body, (0, max(0, n - len(body))))[:n]
    # rubber slap: low-passed noise on the contact
    slap = noise(n, rng, lo=120, hi=900, order=2) * env_exp(n, contact_ms / 1000.0 * 0.9, attack=0.003)
    return _norm(body) + 0.35 * _norm(slap)


def hit_tyres(rng, v, dur, tier_name):
    p = TYRE_TIERS[tier_name]
    n = ns(dur + 0.2)
    out = np.zeros(n)
    c = rng.uniform(*p["contact"])
    sh = rng.uniform(*p["stack"])
    out += tyre_body(n, rng, c, sh)
    th = thump(n, rng, sh * 0.8, 0.07 + 0.03 * (tier_name == "heavy"), f_start_mult=1.3, attack=0.006, noise_amt=0.5)
    out += 0.7 * th
    # rebounds: the stack springs back and the car settles
    gaps = bounce_times(rng.uniform(0.12, 0.2), rng.uniform(0.45, 0.6), p["rebounds"] + 1, rng)[1:]
    for k, t in enumerate(gaps):
        if t > dur * 0.8:
            break
        r = tyre_body(n, rng, c * 0.6, sh * 1.1)
        add_at(out, (0.45 ** (k + 1)) * r[:n - ns(t)], ns(t))
    # stack rocking: low rumble modulated at a few Hz, decaying
    t = tvec(n)
    rock = noise(n, rng, lo=40, hi=180, order=2) * (0.7 + 0.3 * np.sin(2 * np.pi * rng.uniform(6, 10) * t))
    out += 0.12 * rock * env_exp(n, min(0.15, dur * 0.15), attack=0.02)
    out += p["squeak"] * _norm(rubber_squeak(n, rng, rng.uniform(0.01, 0.04), rng.uniform(0.07, 0.16)))
    mt, ml = p["metal"]
    out += ml * _norm(metal_crunch(n, rng, TIERS[mt]))
    # the car settles back and its loose bits rattle; on heavy hits a tyre is knocked off the stack
    out += undb(-34.0 if tier_name == "light" else -29.0) * _rmsnorm(
        rattle(n, rng, 0.05, dur * 0.95, int(rng.integers(4, 10)), clunks=0), 0.05, dur)
    if tier_name == "heavy":
        t_fall = rng.uniform(0.45, 0.6) * dur
        e = rng.uniform(0.4, 0.55)
        for k, tb in enumerate(t_fall + bounce_times(rng.uniform(0.12, 0.18), e, 3, rng)):
            if tb > dur * 0.92:
                break
            r = tyre_body(n, rng, rng.uniform(10, 14), rng.uniform(90, 120))
            add_at(out, 0.35 * e ** k * r[:n - ns(tb)], ns(tb))
    if p["thock"] > 0:
        # the car pushes through to the concrete behind: a short damped thock
        f = rng.uniform(140, 900, 8)
        th2 = modal_ir(f, rng.uniform(0.008, 0.03, 8), rng.uniform(0.3, 1.0, 8), n)
        th2 += thump(n, rng, 70, 0.05)
        out += p["thock"] * _norm(np.pad(th2, (ns(0.012), 0))[:n])
    out = shape(out, lambda f_: band_gain(f_, 25, 5000, 1))
    return crest(soft_clip(_norm(out), p["drive"]), 12.0)


def hit_concrete(rng, v, dur):
    """Car body slamming the concrete wall: a hard, dry crack, the body thump and a heavy crunch."""
    n = ns(dur + 0.2)
    out = np.zeros(n)
    # hard dry crack: a broadband slap and very damped concrete modes
    crack = np.zeros(n)
    m = ns(0.012)
    crack[:m] = noise(m * 8, rng, lo=500)[:m] * np.exp(-np.arange(m) / (m / 5.0))
    crack[:8] += rng.choice([-1.0, 1.0]) * 3.0 * np.hanning(8)
    f = rng.uniform(250, 2600, 14)
    crack += 0.6 * modal_ir(f, rng.uniform(0.003, 0.014, 14), rng.uniform(0.3, 1.0, 14), n)
    out += undb(0.0) * _rmsnorm(crack, 0, 0.02)
    out += undb(1.0) * _rmsnorm(thump(n, rng, rng.uniform(55, 68), 0.07, f_start_mult=2.2, attack=0.0008), 0, 0.1)
    out += undb(0.0) * metal_crunch(n, rng, TIERS["heavy"], t0=0.002)
    out += undb(-7.0) * tear(n, rng, rng.uniform(0.01, 0.03), rng.uniform(0.08, 0.15))
    # grit: concrete chips and dust
    k = int(rng.integers(30, 70))
    times = 0.01 + rng.exponential(0.12, k)
    grit = np.zeros(n)
    for t in times[times < dur * 0.8]:
        w = int(rng.integers(20, 120))
        click = onepole_lp(rng.standard_normal(w * 3), rng.uniform(2000, 6000))[:w] * np.exp(-np.arange(w) / (w / 4))
        add_at(grit, rng.lognormal(0, 0.5) * click, ns(t))
    out += undb(-14.0) * _rmsnorm(grit, 0, 0.4)
    # short gritty slide as the car rebounds along the wall
    sl_t, sl_d = rng.uniform(0.04, 0.1), rng.uniform(0.15, 0.3)
    slide = noise(n, rng, lo=900, hi=5000) * np.clip(env_points(n, [(0, 0), (sl_t, 0), (sl_t + 0.03, 1), (sl_t + sl_d, 0), (dur + 1, 0)]), 0, 1)
    gt = poisson_times(900.0, sl_t, sl_t + sl_d, rng)
    slide *= 0.5 + 0.5 * burst_gate(n, gt, np.ones(len(gt)), rng, ms=(0.5, 2.0))
    out += undb(-12.0) * _rmsnorm(slide, sl_t, sl_t + sl_d)
    out += undb(-15.0) * _rmsnorm(rattle(n, rng, 0.05, dur * 0.97, int(rng.integers(10, 18)), clunks=1), 0.05, dur)
    out = shape(out, lambda f_: band_gain(f_, None, 10000.0, 1))
    return crest(soft_clip(_norm(out), 1.9), 12.0)


# ----------------------------------------------------------------------------- glass and parts

def glass_break(rng, v, dur):
    n = ns(dur + 0.2)
    laminated = v % 2 == 0
    out = np.zeros(n)
    out += _rmsnorm(glass_crack(n, rng, dull=0.8 if laminated else 0.0), 0, 0.01)
    if laminated:
        # the crack web spreading: many micro-cracks over the first ~150 ms, then a smaller shower
        k = int(rng.integers(90, 160))
        times = cluster_times(rng, k, 0.002, rng.uniform(0.12, 0.2), a=1.0, b=2.0)
        mc = np.zeros(n)
        for t in times:
            w = int(rng.integers(6, 30))
            b = rng.standard_normal(w) * np.exp(-np.arange(w) / (w / 3))
            add_at(mc, rng.lognormal(0, 0.5) * b, ns(t))
            if rng.random() < 0.5:
                add_at(mc, 0.6 * tinkle(rng, rng.uniform(2500, 8000), rng.uniform(0.003, 0.012)), ns(t))
        mc = shape(mc, lambda f: band_gain(f, 1800, 14000, 2))
        out += undb(-3.0) * _rmsnorm(mc, 0, 0.15)
        sh = glass_shower(n, rng, int(rng.integers(300, 460)), t0=0.02, tau_burst=0.08, landing=(0.3, 0.12),
                          landing_share=0.35, level_tau=0.6, tail_tau=0.6)
        out += undb(-5.0) * _rmsnorm(sh, 0, 0.4)
    else:
        sh = glass_shower(n, rng, int(rng.integers(1000, 1500)), t0=0.003, tau_burst=0.05,
                          landing=(rng.uniform(0.18, 0.3), 0.08), landing_share=0.45, level_tau=0.4, tail_tau=0.4)
        out += undb(-1.0) * _rmsnorm(sh, 0, 0.4)
    # fine glass dust hiss following the shower density
    hiss = noise(n, rng, lo=5000, hi=15000) * onepole_lp(np.abs(_norm(sh)), 30.0)
    out += undb(-14.0) * _rmsnorm(hiss, 0, 0.4)
    out += undb(-8.0) * _rmsnorm(thump(n, rng, rng.uniform(80, 110), 0.03, noise_amt=0.2), 0, 0.06)
    out = shape(out, lambda f: band_gain(f, 60, None, 1))
    return crest(soft_clip(_norm(out), 1.3), 14.0)


def lamp_break(rng, v, dur):
    n = ns(dur + 0.1)
    out = np.zeros(n)
    # pop: the lens gives way, a short mid resonance and a click
    pop = modal_ir(rng.uniform(700, 2200, 6), rng.uniform(0.003, 0.012, 6), rng.uniform(0.4, 1.0, 6), n)
    out += _rmsnorm(pop, 0, 0.015)
    out += undb(-3.0) * _rmsnorm(glass_crack(n, rng, dull=0.3), 0, 0.01)
    sh = glass_shower(n, rng, int(rng.integers(70, 150)), t0=0.004, tau_burst=0.035, landing=(dur * 0.45, 0.05),
                      landing_share=0.35, level_tau=0.16, tail_tau=0.12, f_lo=2500)
    out += undb(-6.0) * _rmsnorm(sh, 0, 0.15)
    # plastic housing crackle
    k = int(rng.integers(15, 40))
    gate = burst_gate(n, cluster_times(rng, k, 0.0, 0.06), rng.lognormal(0, 0.5, k), rng, ms=(0.2, 1.0))
    out += undb(-9.0) * _rmsnorm(noise(n, rng, lo=900, hi=4000) * gate, 0, 0.06)
    return crest(soft_clip(_norm(out), 1.3), 13.0, 0, 0.08)


BEAM_RATIOS = np.array([1.0, 2.756, 5.404, 8.933, 13.344, 18.64])


def clank(n, rng, base, gain=1.0, tau0=0.25):
    """A bumper or hood hitting the floor: free-free beam modes plus panel modes and a ground thud."""
    f = base * BEAM_RATIOS * rng.uniform(0.98, 1.02, len(BEAM_RATIOS))
    taus = tau0 * BEAM_RATIOS ** -0.55
    amps = np.array([1.0, 0.8, 0.6, 0.45, 0.3, 0.2]) * rng.uniform(0.6, 1.0, 6)
    pulse = half_sine_pulse(rng.uniform(0.0008, 0.002))
    body = convolve(modal_ir(f, taus, amps, ns(0.8)) +
                    0.5 * panel_modes(rng, n_modes=60, tau_low=0.08, boom=1).ir(ns(0.8)), pulse, full=False)
    x = np.zeros(n)
    add_at(x, _norm(body), 0)
    x += 0.45 * thump(n, rng, 80, 0.04, noise_amt=0.6, noise_lp=500)
    return gain * x


def part_off(rng, v, dur):
    n = ns(dur + 0.2)
    out = np.zeros(n)
    rip_d = rng.uniform(0.22, 0.42) * (dur / 0.8)
    rip_d = min(rip_d, dur * 0.5)
    # stick-slip tearing: a jittery impulse train whose rate and strength build up, then let go
    rate = rng.uniform(70, 160)
    times, t = [], 0.0
    while t < rip_d:
        times.append(t)
        t += rng.gamma(3.0, 1.0 / (3.0 * rate * (0.6 + 0.8 * t / rip_d)))
    times = np.array(times)
    build = (times / rip_d) ** 0.6
    amps = rng.lognormal(0, 0.45, len(times)) * (0.3 + 0.7 * build)
    exc = snaps(n, times, amps, rng, width=(4, 40))
    exc[ns(rip_d):ns(rip_d) + 3] += 4.0          # the last weld lets go
    metal = convolve(exc, panel_modes(rng, n_modes=120, tau_low=0.08, bright=3500).ir(ns(0.5)), full=False)
    # torn-edge noise through a sweeping band-pass
    fa, fb = rng.uniform(700, 1200), rng.uniform(2200, 4200)
    if v % 3 == 1:
        fa, fb = fb, fa
    q = rng.uniform(2.5, 4.0)

    def sweep(times_, f):
        fc = fa * (fb / fa) ** np.clip(times_ / rip_d, 0, 1)
        return 1.0 / np.sqrt(1 + q * q * (np.maximum(f[None, :], 1) / fc[:, None] - fc[:, None] / np.maximum(f[None, :], 1)) ** 2)

    tear = stft_filter(noise(n, rng), sweep)
    gate = burst_gate(n, times, amps, rng, ms=(0.5, 4.0))
    rip_env = np.clip(env_points(n, [(0, 0.2), (rip_d * 0.85, 1.0), (rip_d, 1.0), (rip_d + 0.03, 0.0), (dur + 1, 0)]), 0, 1)
    tear = tear * (0.4 + gate) * rip_env
    out += _norm(metal) * 0.9 + 0.7 * _norm(tear)
    # it hits the floor, bounces once
    t_land = rip_d + rng.uniform(0.08, 0.16)
    base = rng.uniform(160, 330)
    cl = clank(n, rng, base)
    add_at(out, 1.3 * cl[:n - ns(t_land)], ns(t_land))
    t2 = t_land + rng.uniform(0.07, 0.13)
    if t2 < dur * 0.8:
        cl2 = clank(n, rng, base * rng.uniform(0.97, 1.03), tau0=0.18)
        add_at(out, 0.45 * cl2[:n - ns(t2)], ns(t2))
    return crest(soft_clip(_norm(out), 1.5), 12.0, 0, dur * 0.6)


def debris_clatter(rng, v, dur):
    n = ns(dur + 0.1)
    out = np.zeros(n)
    pieces = int(rng.integers(2, 5))
    for p in range(pieces):
        m = small_piece_modes(rng, f_lo=700, f_hi=6000, count=int(rng.integers(3, 6)), tau=(0.01, 0.05))
        t0 = 0.0 if p == 0 else rng.uniform(0.0, dur * 0.25)
        e = rng.uniform(0.45, 0.7)
        count = int(rng.integers(4, 8))
        t_last = (rng.uniform(0.7, 0.88) * dur - t0) if p == 0 else rng.uniform(0.3, 0.8) * (dur * 0.85 - t0)
        gap0 = t_last * (1 - e) / (1 - e ** (count - 1))
        times = t0 + bounce_times(gap0, e, count, rng, jitter=0.05)
        amp = rng.uniform(0.5, 1.0) * (1.0 if p == 0 else 0.7)
        for k, t in enumerate(times):
            if t > dur * 0.92:
                break
            a = amp * e ** (0.8 * k)
            # each bounce hits a different spot: re-weight the modes a little
            mm = Modes(m.freqs, m.taus, m.amps * rng.uniform(0.4, 1.2, len(m.amps)))
            hit = mm.ir(ns(0.2))
            w = int(rng.integers(10, 40))
            tick = onepole_lp(rng.standard_normal(w * 3), rng.uniform(1800, 4000))[:w] * np.exp(-np.arange(w) / (w / 4))
            hit[:w] += 0.8 * tick / (np.max(np.abs(tick)) + 1e-9) * np.max(np.abs(hit))
            add_at(out, a * hit, ns(t))
    return crest(soft_clip(_norm(out), 1.2), 14.0, 0.0, dur * 0.5)


def wheel_bounce(rng, v, dur):
    n = ns(dur + 0.1)
    out = np.zeros(n)
    e = rng.uniform(0.4, 0.65)
    times = bounce_times(rng.uniform(0.11, 0.2) * dur / 0.4, e, int(rng.integers(3, 6)), rng)
    rim = Modes(rng.uniform(380, 2400, 10), rng.uniform(0.02, 0.07, 10), rng.uniform(0.2, 1.0, 10))
    f_th = (62, 85, 105)[v % 3] * rng.uniform(0.92, 1.08)       # a different wheel and tyre per variant
    f_cav = (190, 225, 255)[v % 3] * rng.uniform(0.95, 1.05)
    for k, t in enumerate(times):
        if t > dur * 0.8:
            break
        a = e ** (1.1 * k)
        x = np.zeros(n)
        th = thump(n, rng, f_th * rng.uniform(0.95, 1.05), 0.045, f_start_mult=1.5, attack=0.004, noise_amt=0.5, noise_lp=400)
        cav = modal_ir([f_cav], [0.03], [0.6], n)
        cav = convolve(cav, half_sine_pulse(0.004), full=False)
        x += th + 0.6 * _norm(cav)
        x += 0.18 * _norm(np.pad(convolve(rim.ir(ns(0.4)), half_sine_pulse(0.0015), full=False), (0, n))[:n]) * (1 if k == 0 else 0.6)
        x += 0.25 * noise(n, rng, lo=150, hi=1500) * env_exp(n, 0.02, attack=0.002)
        add_at(out, a * x[:n - ns(t)], ns(t))
    return crest(soft_clip(_norm(out), 1.3), 12.0, 0, 0.1)
