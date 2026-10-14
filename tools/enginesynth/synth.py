"""Synthesized engine sound for the granular engine (no recordings): a source-filter model of firing pulses.

Each firing is a short combustion pulse (decaying noise burst, stronger and sharper on throttle) sent into
the exhaust: per-bank pipes modelled as banks of resonators at the pipe's acoustic modes, a muffler low-pass,
plus intake roar and valve-train tick. Cross-plane V8 banks get their uneven pulse spacing, which is what
makes the burble. The output is cut into one-cycle grains per (rpm, load) for runtime overlap-add.
"""
from __future__ import annotations

import json
import os
import struct
import sys
from dataclasses import dataclass, field

import numpy as np

SR = 44100
TAIL = 0.25          # grain tail beyond its cycle, as a fraction of the cycle (crossfade room)


@dataclass
class Engine:
    name: str
    cycle_deg: float                  # 720 for 4-stroke piston engines, 360 for a twin-rotor Wankel
    firings: list                     # [(crank angle deg, bank index)]
    pipe_lengths: list                # metres, one exhaust per bank (summed at the tailpipe)
    idle_rpm: float
    redline_rpm: float
    pulse_ms: float = 1.6             # combustion pulse decay on throttle
    roughness: float = 0.35           # noise share in each pulse
    mode_q: float = 6.0               # resonance sharpness of the pipe modes
    modes: int = 14
    muffler_hz: float = 2400.0
    intake: float = 0.15              # intake roar level
    tick: float = 0.04                # valve-train tick level
    cyl_spread: float = 0.08          # cylinder-to-cylinder pulse variation
    jitter_deg: float = 0.8           # combustion timing scatter
    lope: float = 0.0                 # idle lope (cam overlap): extra variation at low rpm
    drive: float = 1.3                # soft saturation of the summed exhaust
    seed: int = 1


def v8_crossplane(name="v8", **kw):
    order = [1, 8, 4, 3, 6, 5, 7, 2]
    firings = [(i * 90.0, 0 if c % 2 else 1) for i, c in enumerate(order)]
    return Engine(name, 720.0, firings, [2.6, 2.75], **kw)


def inline4(name="i4", **kw):
    return Engine(name, 720.0, [(i * 180.0, 0) for i in range(4)], [2.2], **kw)


def twin_rotor(name="rotary", **kw):
    # Two rotors, one combustion each per eccentric-shaft revolution, 180 degrees apart.
    return Engine(name, 360.0, [(0.0, 0), (180.0, 0)], [1.9], **kw)


ENGINES = {
    "brougham": v8_crossplane("brougham", idle_rpm=650, redline_rpm=4800, pulse_ms=2.4, roughness=0.3,
                              mode_q=5.0, muffler_hz=1500, intake=0.10, tick=0.025, lope=0.25, drive=1.2),
    "nordvik": inline4("nordvik", idle_rpm=850, redline_rpm=6000, pulse_ms=1.6, roughness=0.4,
                       mode_q=6.5, muffler_hz=2200, intake=0.16, tick=0.05, cyl_spread=0.1, drive=1.4),
    "kitsune": twin_rotor("kitsune", idle_rpm=900, redline_rpm=8500, pulse_ms=1.0, roughness=0.55,
                          mode_q=7.0, muffler_hz=3200, intake=0.2, tick=0.0, cyl_spread=0.05, drive=1.8),
}


# ----------------------------------------------------------------------------- DSP helpers

def fft_convolve(x, h):
    n = len(x) + len(h) - 1
    m = 1 << (n - 1).bit_length()
    y = np.fft.irfft(np.fft.rfft(x, m) * np.fft.rfft(h, m), m)
    return y[:len(x)]


def _iir2_impulse(a1, a2, b0, n):
    """Impulse response of y[n] = b0 x[n] - a1 y[n-1] - a2 y[n-2] (closed form for complex poles)."""
    r = np.sqrt(a2)
    w = np.arccos(np.clip(-a1 / (2 * r), -1.0, 1.0))
    k = np.arange(n)
    return b0 * r ** k * np.sin(w * (k + 1)) / np.sin(w)


def resonator_ir(freqs, q, gains, seconds=0.35):
    """Summed impulse response of parallel two-pole band-pass resonators."""
    n = int(seconds * SR)
    h = np.zeros(n)
    for f, g in zip(freqs, gains):
        if f >= SR * 0.45:
            continue
        w = 2 * np.pi * f / SR
        r = np.exp(-w / (2 * q))
        h += g * _iir2_impulse(-2 * r * np.cos(w), r * r, (1 - r * r) * 0.5, n)
    return h


def resonator_bank(x, freqs, q, gains):
    return fft_convolve(x, resonator_ir(freqs, q, gains))


def onepole_lp(x, hz):
    a = np.exp(-2 * np.pi * hz / SR)
    n = int(min(SR, max(64, 8.0 / (1 - a))))
    h = (1 - a) * a ** np.arange(n)
    return fft_convolve(x, h)


def soft_clip(x, drive):
    return np.tanh(x * drive) / np.tanh(drive)


# ----------------------------------------------------------------------------- synthesis

def render(engine: Engine, rpm: float, load: float, cycles: int, rng: np.random.Generator):
    """Continuous engine sound for `cycles` cycles at constant rpm/load. Returns (signal, cycle_len_samples)."""
    rev_s = 60.0 / rpm
    cycle_s = rev_s * engine.cycle_deg / 360.0
    n_cycle = int(round(cycle_s * SR))
    n = n_cycle * cycles
    banks = len(engine.pipe_lengths)
    src = [np.zeros(n + 4096) for _ in range(banks)]
    on = load > 0.5
    tau = engine.pulse_ms * (1.0 if on else 1.8) / 1000.0
    plen = int(tau * SR * 6)
    t = np.arange(plen) / SR
    env = np.exp(-t / tau) * (1 - np.exp(-t / (tau * 0.12)))
    low = max(0.0, 1.0 - (rpm - engine.idle_rpm) / (1800.0))
    cyl_gain = 1.0 + engine.cyl_spread * rng.standard_normal(len(engine.firings))
    for c in range(cycles):
        lope = 1.0 + engine.lope * low * rng.standard_normal()
        for k, (deg, bank) in enumerate(engine.firings):
            jitter = engine.jitter_deg * rng.standard_normal() * (1 + 2 * low)
            pos = (c + (deg + jitter) / engine.cycle_deg) * n_cycle
            i0 = int(pos)
            amp = cyl_gain[k] * lope * (1.0 if on else 0.22) * (0.9 + 0.2 * rng.random())
            if not on and rng.random() < 0.04 * (rpm / engine.redline_rpm):
                amp *= 4.0     # overrun pop
            noise = rng.standard_normal(plen)
            pulse = env * ((1 - engine.roughness) + engine.roughness * noise)
            i0 = max(i0, 0)
            m = min(plen, len(src[bank]) - i0)
            src[bank][i0:i0 + m] += amp * pulse[:m]
    out = np.zeros(n + 4096)
    for b, L in enumerate(engine.pipe_lengths):
        c_sound = 343.0 + 0.6 * 300.0          # hot exhaust gas is faster
        f0 = c_sound / (2 * L)
        freqs = [f0 * m * (1 + 0.004 * m * m) for m in range(1, engine.modes + 1)]
        gains = [1.0 / (1 + 0.35 * m) for m in range(1, engine.modes + 1)]
        out += resonator_bank(src[b], freqs, engine.mode_q, gains)
        out += 0.25 * src[b]                    # direct (unresonated) blast
    out = onepole_lp(out, engine.muffler_hz * (1.0 if on else 0.7))
    # Intake roar: band-limited noise gated by the firing rhythm, louder on throttle and with rpm.
    roar_src = np.zeros_like(out)
    for s in src:
        roar_src += np.abs(s)
    roar = rng.standard_normal(len(out)) * onepole_lp(roar_src, 60.0)
    roar = resonator_bank(roar, [700.0, 1350.0, 2600.0], 3.0, [1.0, 0.7, 0.4])
    out += engine.intake * (0.4 + 0.6 * load) * (0.5 + rpm / engine.redline_rpm) * roar
    # Valve-train tick: tiny clicks at cam rate.
    if engine.tick > 0:
        clicks = np.zeros_like(out)
        per_rev = max(1, len(engine.firings) // 2)
        for c in range(int(cycles * engine.cycle_deg / 360.0 * per_rev * 2)):
            i0 = int(c * rev_s * SR / (per_rev * 2))
            if i0 + 64 < len(clicks):
                clicks[i0:i0 + 64] += rng.standard_normal(64) * np.exp(-np.arange(64) / 8.0)
        out += engine.tick * resonator_bank(clicks, [3800.0, 5200.0], 8.0, [1.0, 0.6])
    out = out - onepole_lp(out, 28.0)                    # DC blocker (pulses are one-sided)
    out = soft_clip(out / (np.std(out[: n]) * 3.0 + 1e-9), engine.drive)
    out = out - onepole_lp(out, 18.0)                    # the clipper's asymmetry
    return out[:n], n_cycle


def grain_bank(engine: Engine, rpms, loads=(0.0, 1.0), cycles=14, warmup=4):
    """{(rpm_index, load_index): [grain arrays]} - consecutive whole cycles after a warm-up."""
    rng = np.random.default_rng(engine.seed)
    bank = {}
    for i, r in enumerate(rpms):
        for j, ld in enumerate(loads):
            sig, nc = render(engine, r, ld, cycles + warmup + 1, rng)
            tail = int(nc * TAIL)
            # Each grain is one whole cycle plus a tail (the start of the next) for the runtime crossfade.
            grains = [sig[(warmup + k) * nc:(warmup + k + 1) * nc + tail] for k in range(cycles)]
            bank[(i, j)] = grains
    return bank


def rpm_table(engine: Engine, steps=12):
    return [float(v) for v in np.geomspace(engine.idle_rpm, engine.redline_rpm, steps)]


def write_bank(engine: Engine, out_dir: str):
    """<name>.bin (float32 mono, grains back to back) + <name>.json (index)."""
    rpms = rpm_table(engine)
    bank = grain_bank(engine, rpms)
    os.makedirs(out_dir, exist_ok=True)
    data, index, off = [], [], 0
    peak = max(float(np.max(np.abs(g))) for gs in bank.values() for g in gs)
    for (i, j), grains in sorted(bank.items()):
        entry = {"rpm": rpms[i], "load": j, "grains": []}
        for g in grains:
            g32 = (g / peak * 0.9).astype(np.float32)
            data.append(g32)
            entry["grains"].append([off, len(g32)])
            off += len(g32)
        index.append(entry)
    blob = np.concatenate(data)
    blob.tofile(os.path.join(out_dir, f"{engine.name}.bin"))
    meta = {"name": engine.name, "sample_rate": SR, "tail": TAIL, "cycle_deg": engine.cycle_deg,
            "idle_rpm": engine.idle_rpm,
            "redline_rpm": engine.redline_rpm, "rpms": rpms, "tables": index}
    with open(os.path.join(out_dir, f"{engine.name}.json"), "w") as f:
        json.dump(meta, f)
    return blob, meta


def write_wav(path, x):
    x16 = (np.clip(x, -1, 1) * 32767).astype("<i2")
    with open(path, "wb") as f:
        f.write(b"RIFF" + struct.pack("<I", 36 + x16.nbytes) + b"WAVEfmt " +
                struct.pack("<IHHIIHH", 16, 1, 1, SR, SR * 2, 2, 16) + b"data" + struct.pack("<I", x16.nbytes))
        f.write(x16.tobytes())


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "assets/audio/engines"
    for name, eng in ENGINES.items():
        blob, meta = write_bank(eng, out)
        print(f"{name}: {len(meta['rpms'])} rpm x 2 loads, {len(blob) / SR:.1f} s of grains")
