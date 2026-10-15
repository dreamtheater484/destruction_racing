"""A stadium crowd from hundreds of synthesized voices (source-filter speech model) in a synthetic reverb.

Each voice is a person: male or female pitch range, a vocal-tract length factor (scales the formants),
breathiness, a distance (level and air absorption). A voice produces syllables:
  * source: a Rosenberg glottal pulse (the flow's time derivative, so the closure is the sharp event that
    carries the high harmonics) on a pitch contour with vibrato and jitter, plus aspiration noise that is
    modulated by the glottal flow; vocal effort lowers the open quotient (brighter, pressed voice) and
    raises the pitch and F1, as shouting does;
  * filter: a cascade of five two-pole formant resonators (Klatt form) for the vowel, from Peterson &
    Barney averages, applied in the frequency domain; diphthongs ("yeah", "hey", "go") crossfade two
    vowel filters over the syllable;
  * envelope: syllable-like attack, sustain and release with shimmer.
Talkers chain syllables into phrases with consonant noise (s, sh, f, plosive bursts) and pauses; cheering
voices shout open vowels on arched pitch contours. Claps are short resonant noise bursts per clapper, and
whistles are breathy glides. Voices go to near / mid / far buses (far ones duller and quieter), which are
summed with a large-room reverb (band-split synthetic IR, ~2 s). Loops place every event on a circle
(tails wrap round) and use circular filtering and reverb, so they repeat seamlessly.
"""
from __future__ import annotations

from dataclasses import dataclass

import numpy as np

from dsp import (SR, add_at, band_gain, circ_convolve, convolve, fft_size, noise, ns, peak_gain, rfreqs,
                 resonator_response, reverb_ir, shape, smooth_random, soft_clip, upsample2, wander)

CSR = SR // 2          # voices are rendered at half rate (the crowd is band-limited) and upsampled per bus


def cns(seconds):
    return max(0, int(round(seconds * CSR)))


def ctvec(n):
    return np.arange(n) / CSR



# Peterson & Barney (1952) average formants F1-F3 in Hz: (male, female)
VOWELS = {
    "i": ((270, 2290, 3010), (310, 2790, 3310)),
    "I": ((390, 1990, 2550), (430, 2480, 3070)),
    "e": ((530, 1840, 2480), (610, 2330, 2990)),
    "ae": ((660, 1720, 2410), (860, 2050, 2850)),
    "a": ((730, 1090, 2440), (850, 1220, 2810)),
    "o": ((570, 840, 2410), (590, 920, 2710)),
    "U": ((440, 1020, 2240), (470, 1160, 2680)),
    "u": ((300, 870, 2240), (370, 950, 2670)),
    "uh": ((640, 1190, 2390), (760, 1400, 2780)),
    "er": ((490, 1350, 1690), (500, 1640, 1960)),
}
SPEECH_VOWELS = ["i", "I", "e", "ae", "a", "o", "U", "u", "uh", "er", "uh", "I", "e", "a"]
F45 = ((3300, 3850), (4000, 4600))
BW = (75.0, 100.0, 140.0, 250.0, 320.0)


@dataclass
class Voice:
    female: bool
    f0: float          # speaking pitch, Hz
    tract: float       # formant scale (shorter tract = higher)
    breath: float      # aspiration share
    gain: float
    bus: int           # 0 near, 1 mid, 2 far


def make_voice(rng, bus_weights=(0.12, 0.38, 0.5), female_share=0.4, pitch_shift=1.0):
    female = rng.random() < female_share
    f0 = (rng.uniform(165, 255) if female else rng.uniform(85, 150)) * pitch_shift
    bus = int(rng.choice(3, p=bus_weights))
    gain = (1.0, 0.55, 0.3)[bus] * rng.uniform(0.6, 1.2)
    return Voice(female, f0, rng.uniform(0.92, 1.08), rng.uniform(0.04, 0.25), gain, bus)


def glottal(f0_curve, effort, rng):
    """Rosenberg glottal flow derivative on a pitch curve (Hz per sample), plus the flow itself."""
    ph = np.cumsum(f0_curve) / CSR
    frac = ph % 1.0
    op = 0.62 - 0.22 * effort
    tp, tn = 0.62 * op, 0.38 * op
    rise = frac < tp
    fall = (frac >= tp) & (frac < tp + tn)
    d = np.zeros_like(frac)
    d[rise] = 0.5 * np.pi / tp * np.sin(np.pi * frac[rise] / tp)
    d[fall] = -0.5 * np.pi / tn * np.sin(0.5 * np.pi * (frac[fall] - tp) / tn)
    flow = np.zeros_like(frac)
    flow[rise] = 0.5 * (1 - np.cos(np.pi * frac[rise] / tp))
    flow[fall] = np.cos(0.5 * np.pi * (frac[fall] - tp) / tn)
    return d * (f0_curve / 150.0), flow


_GRID = {}


def _grid(M):
    """rfft frequencies and exp(-j w) for an FFT size (cached: every syllable reuses a few sizes)."""
    if M not in _GRID:
        f = rfreqs(M, CSR)
        _GRID[M] = (f, np.exp(-1j * 2 * np.pi * f / CSR))
    return _GRID[M]


def vowel_response(M, voice: Voice, vowel, effort, rng):
    f, z1 = _grid(M)
    fm = VOWELS[vowel][1 if voice.female else 0]
    f45 = F45[1 if voice.female else 0]
    forms = [fm[0] * (1 + 0.2 * effort), fm[1], fm[2], f45[0], f45[1]]
    h = np.ones(len(f), dtype=complex)
    for fc, bw in zip(forms, BW):
        fc = fc * voice.tract * rng.uniform(0.96, 1.04)
        h *= resonator_response(f, fc, bw * rng.uniform(0.85, 1.3), z1, sr=CSR)
    return h


def syllable(voice: Voice, vowels, dur, rng, effort=0.3, pitch=(1.0, 1.0), peak_at=0.4, attack=0.03,
             release=0.06, vibrato=0.0, level=1.0):
    """One voiced syllable; returns the signal including the filter tail."""
    m = max(cns(dur), 64)
    t = np.arange(m) / m
    p0, p1 = pitch[0], pitch[1]
    p_mid = pitch[2] if len(pitch) > 2 else 0.5 * (p0 + p1)
    contour = np.where(t < peak_at, p0 + (p_mid - p0) * np.sin(0.5 * np.pi * t / max(peak_at, 1e-3)),
                       p_mid + (p1 - p_mid) * (1 - np.cos(0.5 * np.pi * (t - peak_at) / max(1 - peak_at, 1e-3))))
    f0 = voice.f0 * (1 + 0.35 * effort) * contour
    f0 = f0 * (1 + vibrato * np.sin(2 * np.pi * rng.uniform(4.5, 6.5) * ctvec(m) + rng.uniform(0, 6.3))
               + 0.008 * wander(m, 25.0, rng, sr=CSR))
    d, flow = glottal(f0, effort, rng)
    asp = rng.standard_normal(m) * (0.25 + flow) * 0.6
    src = (1 - voice.breath) * d + voice.breath * 3.0 * asp
    a = min(cns(attack), m // 2)
    r = min(cns(release), m // 2)
    env = np.ones(m)
    env[:a] = np.sin(0.5 * np.pi * np.arange(a) / max(a, 1)) ** 2
    env[m - r:] = np.cos(0.5 * np.pi * np.arange(r) / max(r, 1)) ** 2
    env *= 1 + 0.08 * wander(m, 12.0, rng, sr=CSR)
    exc = src * env
    tail = cns(0.03)
    M = fft_size(m + tail)
    X = np.fft.rfft(exc, M)
    y = np.fft.irfft(X * vowel_response(M, voice, vowels[0], effort, rng), M)[:m + tail]
    if len(vowels) > 1:
        y2 = np.fft.irfft(X * vowel_response(M, voice, vowels[1], effort, rng), M)[:m + tail]
        w = np.clip((np.arange(m + tail) / m - 0.15) / 0.5, 0, 1)
        w = w * w * (3 - 2 * w)
        y = y * (1 - w) + y2 * w
    return level * y


def consonant(rng, kind):
    """Unvoiced consonant noise: fricatives and plosive bursts."""
    if kind == "s":
        m, lo, hi, q = cns(rng.uniform(0.06, 0.11)), 4500, 10000, None
    elif kind == "sh":
        m, lo, hi, q = cns(rng.uniform(0.06, 0.1)), 1800, 6500, 2800
    elif kind == "f":
        m, lo, hi, q = cns(rng.uniform(0.05, 0.09)), 1200, 9000, None
    else:
        m, lo, hi, q = cns(rng.uniform(0.008, 0.02)), 500, 6000, rng.choice([1800, 3500, 900])
    x = noise(m, rng, lo=lo, hi=hi, gain=(lambda f: 0.4 + peak_gain(f, q, 2.0)) if q else None, sr=CSR)
    env = np.hanning(m) if kind in ("s", "sh", "f") else np.exp(-np.arange(m) / (m / 4))
    lvl = {"s": 0.18, "sh": 0.2, "f": 0.08}.get(kind, 0.3)
    return lvl * x * env


CLAP_GAIN = 60.0       # a nearby hand clap peaks a little above a nearby shout (measured: shout peak ~63)


def clap(rng, fc, q):
    m = cns(0.04)
    e = np.exp(-np.arange(m) / (rng.uniform(0.004, 0.009) * CSR))
    e[:cns(0.0015)] *= np.linspace(0, 1, cns(0.0015))
    x = noise(m, rng, lo=300, hi=9000, gain=lambda f: 0.35 + peak_gain(f, fc * rng.uniform(0.95, 1.05), q), sr=CSR)
    return CLAP_GAIN * x * e


def whistle(rng, dur, kind):
    m = cns(dur)
    t = np.arange(m) / m
    f0 = rng.uniform(1800, 3200)
    if kind == 0:      # wolf whistle: up, then up-and-down
        f = f0 * np.where(t < 0.4, 0.75 + 0.35 * t / 0.4, 1.1 - 0.5 * np.sin(np.pi * (t - 0.4) / 0.6) ** 2 * 0.6)
    else:              # long whistle with a wobble
        f = f0 * (1 + 0.012 * np.sin(2 * np.pi * rng.uniform(5, 8) * ctvec(m)))
    ph = 2 * np.pi * np.cumsum(f) / CSR
    env = np.sin(np.pi * t) ** 0.6
    breath = noise(m, rng, gain=lambda ff: peak_gain(ff, f0, 4.0), sr=CSR) * 0.15
    return (np.sin(ph) + 0.06 * np.sin(2 * ph) + breath) * env * 0.5


# ----------------------------------------------------------------------------- behaviours

def talk(voice: Voice, span, rng, effort=0.25):
    """A talker over `span` seconds: phrases of syllables with consonants, and pauses.
    Returns [(time, signal)]."""
    ev = []
    t = rng.uniform(-2.0, 0.0)
    while t < span:
        if rng.random() < 0.55:
            t += rng.exponential(2.2)                    # listening
            continue
        k = int(rng.integers(2, 9))
        decl = rng.uniform(0.82, 0.95)
        stress = int(rng.integers(0, k))
        for j in range(k):
            dur = float(np.clip(rng.gamma(4.0, 0.045), 0.07, 0.4))
            p = 1.0 + (decl - 1.0) * j / max(k - 1, 1)
            hi = 1.12 if j == stress else 1.0
            pitch = (p * hi, p * hi * rng.uniform(0.94, 1.02), p * hi * rng.uniform(1.0, 1.06))
            if rng.random() < 0.45:
                c = consonant(rng, rng.choice(["s", "sh", "f", "t", "k", "p", "t"]))
                ev.append((t, c * voice.gain))
                t += len(c) / CSR * 0.7
            v = SPEECH_VOWELS[int(rng.integers(len(SPEECH_VOWELS)))]
            lvl = voice.gain * (1.3 if j == stress else 1.0) * rng.uniform(0.7, 1.0)
            ev.append((t, syllable(voice, [v], dur, rng, effort=effort * rng.uniform(0.8, 1.3), pitch=pitch,
                                   attack=0.02, release=0.04, level=lvl)))
            t += dur + rng.uniform(0.0, 0.06)
        t += rng.uniform(0.25, 0.8)
    return ev


SHOUTS = [(["ae", "ae"], "yeah"), (["e", "i"], "hey"), (["u"], "woo"), (["a"], "aah"), (["o", "u"], "go"),
          (["uh", "a"], "come on"), (["i", "ae"], "yeah"), (["a", "o"], "wow")]


def shout(voice: Voice, rng, effort=0.85, dur=None, kind=None, level=1.0):
    """One cheering shout: open vowels on an arched (or rising for 'woo') pitch contour."""
    vowels, name = SHOUTS[int(rng.integers(len(SHOUTS)))] if kind is None else kind
    dur = dur or float(np.clip(rng.gamma(5.0, 0.11), 0.25, 1.3))
    if name == "woo":
        pitch, pk = (0.85, 0.95, rng.uniform(1.25, 1.5)), 0.5
    else:
        pitch, pk = (rng.uniform(0.95, 1.05), rng.uniform(0.85, 0.95), rng.uniform(1.12, 1.3)), rng.uniform(0.2, 0.45)
    lead = []
    if name in ("yeah",) and rng.random() < 0.6:
        lead = [(0.0, syllable(voice, ["i"], 0.06, rng, effort=effort, pitch=(pitch[0], pitch[0] * 1.05), level=0.5 * voice.gain * level))]
    off = 0.045 if lead else 0.0
    body = syllable(voice, vowels, dur, rng, effort=effort, pitch=pitch, peak_at=pk, attack=0.05,
                    release=min(0.25, dur * 0.4), vibrato=rng.uniform(0.0, 0.015), level=voice.gain * level)
    return lead + [(off, body)]


def clapper(rng, t0, t1, rate=None, level=1.0):
    """One person clapping from t0 to t1: [(time, clap)]."""
    rate = rate or rng.uniform(3.5, 6.0)
    fc, q = rng.uniform(800, 2500), rng.uniform(1.5, 4.0)
    ev, t = [], t0
    while t < t1:
        ev.append((t, level * rng.uniform(0.7, 1.0) * clap(rng, fc, q)))
        t += (1.0 / rate) * (1 + 0.05 * rng.standard_normal())
    return ev


# ----------------------------------------------------------------------------- mixing

BUS_LP = (12000.0, 7000.0, 3300.0)
BUS_SEND = (0.25, 0.45, 0.8)


class Mixer:
    """Three distance buses; loops wrap events round a circle of n samples."""

    def __init__(self, n, circular):
        self.n = n + (n % 2)
        self.circular = circular
        self.bus = [np.zeros(self.n // 2) for _ in range(3)]
        self.full = None

    def add(self, bus, t, x):
        i0 = int(round(t * CSR))          # signed: on a loop, events before 0 wrap round to the end
        if not self.circular and i0 < 0:
            x, i0 = x[-i0:], 0
        add_at(self.bus[bus], x, i0, wrap=self.circular)

    def add_events(self, bus, events, t0=0.0, gain=1.0):
        for t, x in events:
            if not self.circular and t0 + t < 0:
                continue
            self.add(bus, t0 + t, gain * x)

    def render(self, rng, rt60=2.0, wet=0.55):
        dry = np.zeros(self.n)
        send = np.zeros(self.n)
        self.full = []
        for b, x in enumerate(self.bus):
            x = upsample2(x, self.n)
            x = shape(x, lambda f, b=b: band_gain(f, 70.0, BUS_LP[b], 2), circular=self.circular)
            self.full.append(x)
            dry += x * (1 - 0.5 * BUS_SEND[b])
            send += x * BUS_SEND[b]
        ir = reverb_ir(rt60, rng, seconds=rt60 * 1.2, predelay=0.03, hf=0.4)
        if self.circular:
            w = circ_convolve(send, ir)
        else:
            w = convolve(send, ir, full=False)
        rs = np.sqrt(np.mean(send ** 2)) + 1e-12
        rw = np.sqrt(np.mean(w ** 2)) + 1e-12
        return dry + wet * w * (rs / rw)


def _finish(x, drive=1.3):
    return soft_clip(x / (4.0 * np.std(x) + 1e-12), drive)


# ----------------------------------------------------------------------------- loops

def bed_loop(rng, v, dur):
    n = ns(dur)
    mix = Mixer(n, circular=True)
    for _ in range(380):
        voice = make_voice(rng, bus_weights=(0.06, 0.34, 0.6))
        mix.add_events(voice.bus, talk(voice, dur, rng, effort=rng.uniform(0.15, 0.4)))
    # occasional distant shouts
    for _ in range(int(rng.integers(10, 16))):
        voice = make_voice(rng, bus_weights=(0.0, 0.4, 0.6), pitch_shift=1.25)
        mix.add_events(voice.bus, shout(voice, rng, effort=rng.uniform(0.6, 0.9), level=2.2), t0=rng.uniform(0, dur))
    for _ in range(3):
        voice = make_voice(rng, bus_weights=(0.0, 0.3, 0.7))
        mix.add(voice.bus, rng.uniform(0, dur), 0.25 * whistle(rng, rng.uniform(0.4, 0.9), int(rng.integers(2))))
    # a few people clapping now and then
    for _ in range(int(rng.integers(5, 9))):
        t0 = rng.uniform(0, dur)
        mix.add_events(int(rng.choice([1, 2])), clapper(rng, 0.0, rng.uniform(1.0, 3.0), level=0.6), t0=t0)
    x = mix.render(rng, rt60=2.0, wet=0.7)
    # far-away murmur of the rest of the stands: the far bus heard again from other directions
    far = shape(mix.full[2], lambda f: band_gain(f, 90.0, 1800.0, 2), circular=True)
    for k in range(3):
        x += 0.35 * np.roll(far, int(rng.integers(n // 5, 4 * n // 5)))
    x *= 1 + 0.12 * smooth_random(n, 0.25, rng)
    return _finish(x, 1.2)


def excited_loop(rng, v, dur):
    n = ns(dur)
    mix = Mixer(n, circular=True)
    for _ in range(460):
        voice = make_voice(rng, bus_weights=(0.08, 0.37, 0.55), pitch_shift=rng.uniform(1.1, 1.45))
        t = rng.uniform(0, dur)
        end = t + dur
        while t < end:
            mix.add_events(voice.bus, shout(voice, rng, effort=rng.uniform(0.7, 1.0)), t0=t)
            t += rng.uniform(0.5, 1.6)
    for _ in range(14):
        voice = make_voice(rng, bus_weights=(0.1, 0.4, 0.5))
        mix.add(voice.bus, rng.uniform(0, dur), 0.4 * whistle(rng, rng.uniform(0.4, 1.0), int(rng.integers(2))))
    for _ in range(70):
        t0 = rng.uniform(0, dur)
        mix.add_events(int(rng.choice([1, 2], p=[0.4, 0.6])), clapper(rng, 0.0, rng.uniform(1.5, 5.0), level=0.5), t0=t0)
    x = mix.render(rng, rt60=2.0, wet=0.6)
    far = shape(mix.full[2], lambda f: band_gain(f, 90.0, 2200.0, 2), circular=True)
    for k in range(3):
        x += 0.35 * np.roll(far, int(rng.integers(n // 5, 4 * n // 5)))
    stomp = noise(n, rng, lo=30, hi=140) * (1 + 0.3 * smooth_random(n, 2.0, rng))
    x += 0.12 * stomp * np.std(x)
    x *= 1 + 0.1 * smooth_random(n, 0.3, rng)
    return _finish(x, 1.3)


# ----------------------------------------------------------------------------- reactions

def _reaction(rng, dur, voices, make_events, claps=0, clap_window=(0.3, 2.0), whistles=0, rt60=1.9, wet=0.55,
              stomp=0.0, envelope=None, pitch_shift=(1.0, 1.25)):
    n = ns(dur + 0.3)
    n += n % 2
    mix = Mixer(n, circular=False)
    for _ in range(voices):
        voice = make_voice(rng, bus_weights=(0.08, 0.37, 0.55), pitch_shift=rng.uniform(*pitch_shift))
        for t0, evs in make_events(voice):
            mix.add_events(voice.bus, evs, t0=t0)
    for _ in range(claps):
        a, b = clap_window
        t0 = a + rng.gamma(2.0, 0.12)
        mix.add_events(int(rng.choice([1, 2])), clapper(rng, 0.0, rng.uniform(0.6, 1.0) * (b - t0), level=0.6), t0=t0)
    for _ in range(whistles):
        voice = make_voice(rng)
        mix.add(voice.bus, rng.uniform(0.1, dur * 0.5), 0.5 * whistle(rng, rng.uniform(0.4, 1.0), int(rng.integers(2))))
    x = mix.render(rng, rt60=rt60, wet=wet)
    if stomp > 0:
        st = noise(n, rng, lo=30, hi=160) * (envelope(n) if envelope else 1.0)
        x += stomp * st * np.std(x) * 2
    return _finish(x, 1.3)


def ooh(rng, v, dur):
    peak = rng.uniform(0.3, 0.45)

    def ev(voice):
        t0 = max(0.0, rng.normal(0.14, 0.07))
        d = rng.uniform(0.75, 1.0) * (dur - t0 - 0.35)
        vw = ["u"] if rng.random() < 0.7 else ["o", "u"]
        rise = rng.uniform(1.18, 1.4)
        return [(t0, [(0.0, syllable(voice, vw, d, rng, effort=rng.uniform(0.45, 0.7), pitch=(0.95, 0.8, rise),
                                     peak_at=peak, attack=rng.uniform(0.08, 0.16), release=d * 0.45,
                                     vibrato=0.01, level=voice.gain))])]

    return _reaction(rng, dur, 300, ev, pitch_shift=(1.0, 1.2))


def cheer(rng, v, dur):
    def ev(voice):
        out = []
        t = 0.03 + rng.gamma(2.0, 0.07)
        for _ in range(int(rng.integers(1, 3))):
            if t > dur * 0.7:
                break
            s = shout(voice, rng, effort=rng.uniform(0.75, 1.0), dur=float(np.clip(rng.gamma(5, 0.13), 0.35, 1.3)))
            out.append((t, s))
            t += sum(len(x) for _, x in s) / CSR * 0.8 + rng.uniform(0.1, 0.5)
        return out

    return _reaction(rng, dur, 420, ev, claps=60, clap_window=(0.3, dur * 0.85), whistles=8, pitch_shift=(1.15, 1.45))


def roar(rng, v, dur):
    def env(n):
        return np.clip(np.interp(np.arange(n) / SR, [0, 0.25, dur * 0.6, dur], [0, 1, 0.9, 0]), 0, 1)

    def ev(voice):
        out = []
        t = 0.02 + rng.gamma(1.6, 0.06)
        while t < dur * 0.65:
            d = float(np.clip(rng.gamma(5, 0.25), 0.6, 2.2))
            d = min(d, dur - t - 0.4)
            if d < 0.3:
                break
            kind = SHOUTS[int(rng.choice([0, 3, 3, 7, 4, 2]))]
            out.append((t, shout(voice, rng, effort=rng.uniform(0.85, 1.0), dur=d, kind=kind)))
            t += d * rng.uniform(0.8, 1.0) + rng.uniform(0.05, 0.3)
        return out

    return _reaction(rng, dur, 600, ev, claps=90, clap_window=(0.4, dur * 0.85), whistles=14, stomp=0.25,
                     envelope=env, wet=0.6, pitch_shift=(1.25, 1.6))


def groan(rng, v, dur):
    def ev(voice):
        t0 = max(0.0, rng.normal(0.08, 0.05))
        d = rng.uniform(0.7, 1.0) * (dur - t0 - 0.35)
        vw = ["a", "o"] if rng.random() < 0.6 else ["o", "uh"]
        return [(t0, [(0.0, syllable(voice, vw, d, rng, effort=rng.uniform(0.35, 0.6),
                                     pitch=(rng.uniform(1.1, 1.25), rng.uniform(0.7, 0.8), rng.uniform(1.15, 1.3)),
                                     peak_at=0.12, attack=0.07, release=d * 0.5, vibrato=0.008, level=voice.gain))])]

    return _reaction(rng, dur, 320, ev, pitch_shift=(1.0, 1.15))


def applause(rng, v, dur):
    n = ns(dur + 0.3)
    n += n % 2
    mix = Mixer(n, circular=False)
    for _ in range(240):
        t0 = rng.gamma(2.0, 0.08)
        t1 = float(np.clip(rng.normal(dur * 0.78, 0.5), 1.5, dur - 0.35))
        b = int(rng.choice(3, p=[0.1, 0.35, 0.55]))
        lvl = (1.0, 0.6, 0.35)[b] * rng.uniform(0.5, 1.0)
        evs = clapper(rng, 0.0, t1 - t0, level=lvl)
        # people slow down and get quieter as they stop
        evs = [(t, x * (1.0 if t < (t1 - t0) - 0.8 else max(0.2, ((t1 - t0) - t) / 0.8))) for t, x in evs]
        mix.add_events(b, evs, t0=t0)
    for _ in range(110):
        voice = make_voice(rng, pitch_shift=rng.uniform(1.15, 1.45))
        t = rng.uniform(0.05, dur * 0.6)
        mix.add_events(voice.bus, shout(voice, rng, effort=rng.uniform(0.7, 1.0), level=0.25), t0=t)
    for _ in range(9):
        voice = make_voice(rng)
        mix.add(voice.bus, rng.uniform(0.2, dur * 0.6), 0.5 * whistle(rng, rng.uniform(0.4, 1.0), int(rng.integers(2))))
    x = mix.render(rng, rt60=1.9, wet=0.5)
    return _finish(x, 1.2)
