"""Offline twin of the runtime granular engine player (src/Audio/GranularEngine.cs), for previews and checks.

Per output cycle: blend the 4 grains around (rpm, load) - two rpm tables x off/on throttle - each resampled
to the current cycle length, windowed with a short crossfade into the next grain, and overlap-added.
python granular.py <bank_dir> <name> <out.wav> [out_spectrogram.png]
"""
from __future__ import annotations

import json
import sys

import numpy as np

from synth import write_wav


class Bank:
    def __init__(self, directory, name):
        self.meta = json.load(open(f"{directory}/{name}.json"))
        self.data = np.fromfile(f"{directory}/{name}.bin", dtype=np.float32)
        self.sr = self.meta["sample_rate"]
        self.rpms = np.array(self.meta["rpms"])
        self.tables = {(i, t["load"]): t["grains"] for i, t in enumerate(self.meta["tables"]) for _ in [0]}
        # tables are listed rpm-major with load 0 then 1
        self.tables = {}
        for t in self.meta["tables"]:
            i = int(np.argmin(np.abs(self.rpms - t["rpm"])))
            self.tables[(i, t["load"])] = t["grains"]

    def grain(self, i, load, k):
        off, n = self.tables[(i, load)][k % len(self.tables[(i, load)])]
        return self.data[off:off + n]


def resample(x, n):
    """Linear resample x to n samples."""
    pos = np.linspace(0, len(x) - 1, n)
    i = np.floor(pos).astype(int)
    f = pos - i
    j = np.minimum(i + 1, len(x) - 1)
    return x[i] * (1 - f) + x[j] * f


def play(bank: Bank, rpm_curve, load_curve, seconds, rng=None):
    rng = rng or np.random.default_rng(3)
    sr = bank.sr
    out = np.zeros(int(seconds * sr) + sr)
    tail = bank.meta["tail"]
    t = 0
    last = {}
    while t < int(seconds * sr):
        rpm = float(rpm_curve(t / sr))
        load = float(np.clip(load_curve(t / sr), 0, 1))
        period = int(round(sr * 60.0 / rpm * bank.meta["cycle_deg"] / 360.0))
        x = np.log(rpm / bank.rpms[0]) / np.log(bank.rpms[-1] / bank.rpms[0]) * (len(bank.rpms) - 1)
        i0 = int(np.clip(np.floor(x), 0, len(bank.rpms) - 2))
        w = float(np.clip(x - i0, 0, 1))
        n_out = period + int(period * tail)
        g = np.zeros(n_out)
        for i, wi in ((i0, 1 - w), (i0 + 1, w)):
            for ld, wl in ((0, 1 - load), (1, load)):
                wt = wi * wl
                if wt < 1e-3:
                    continue
                count = len(bank.tables[(i, ld)])
                k = int(rng.integers(count))
                if last.get((i, ld)) == k:
                    k = (k + 1) % count
                last[(i, ld)] = k
                src = bank.grain(i, ld, k)
                g += wt * resample(src, n_out)
        fade = n_out - period
        win = np.ones(n_out)
        ramp = 0.5 - 0.5 * np.cos(np.linspace(0, np.pi, fade))
        win[:fade] = ramp
        win[period:] = ramp[::-1]
        out[t:t + n_out] += g * win
        t += period
    return out[:int(seconds * sr)]


def spectrogram_png(x, sr, path, fmax=4000, width=1200, height=400):
    from PIL import Image
    n = 2048
    hop = max(1, (len(x) - n) // width)
    win = np.hanning(n)
    cols = []
    for c in range(width):
        seg = x[c * hop:c * hop + n]
        if len(seg) < n:
            seg = np.pad(seg, (0, n - len(seg)))
        cols.append(np.abs(np.fft.rfft(seg * win)))
    S = np.array(cols).T
    kmax = int(fmax / (sr / n))
    S = 20 * np.log10(S[:kmax] + 1e-6)
    S = np.clip((S - (S.max() - 70)) / 70, 0, 1)
    img = (np.flipud(S) * 255).astype(np.uint8)
    Image.fromarray(img).resize((width, height)).save(path)


if __name__ == "__main__":
    d, name, out = sys.argv[1], sys.argv[2], sys.argv[3]
    bank = Bank(d, name)
    idle, red = bank.meta["idle_rpm"], bank.meta["redline_rpm"]

    def rpm(t):     # idle, rev up on throttle, lift and fall back, two blips
        if t < 2: return idle
        if t < 6: return idle + (red - idle) * ((t - 2) / 4) ** 1.4
        if t < 10: return red - (red - idle) * (1 - np.exp(-(t - 6) / 1.2))
        if t < 10.6: return idle + (red * 0.6 - idle) * np.sin((t - 10) / 0.6 * np.pi)
        return idle

    def load(t):
        return 1.0 if 2 <= t < 6 or 10 <= t < 10.3 else 0.0

    x = play(bank, rpm, load, 12.0)
    x = x / (np.max(np.abs(x)) + 1e-9) * 0.9
    write_wav(out, x)
    if len(sys.argv) > 4:
        spectrogram_png(x, bank.sr, sys.argv[4])
    print(f"wrote {out}")
