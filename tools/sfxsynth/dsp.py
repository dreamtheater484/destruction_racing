"""Plain-numpy DSP building blocks for the procedural sound effects (no scipy, no recordings).

Two kinds of signal are made with these helpers:

* one-shots, which start at sample 0 and are processed with causal tools where a transient matters
  (modal impulse responses, exponential low-passes) and zero-phase frequency-domain masks elsewhere;
* loops, which are built as *circular* signals: noise is shaped in the frequency domain of the whole
  loop, modulators are periodic, events wrap around the end, filters and reverbs are circular
  convolutions. A circular signal's last sample runs straight into its first, so the seam is
  seamless by construction rather than by a crossfade.

Everything is deterministic: callers pass a numpy Generator seeded from the sound id and variant.
"""
from __future__ import annotations

import struct
import wave
import zlib

import numpy as np
from numpy.lib.stride_tricks import sliding_window_view

SR = 44100
NYQ = SR / 2


# ----------------------------------------------------------------------------- basics

def ns(seconds: float) -> int:
    return max(0, int(round(seconds * SR)))


def tvec(n: int) -> np.ndarray:
    return np.arange(n) / SR


def undb(d: float) -> float:
    return 10.0 ** (d / 20.0)


def to_db(x: float) -> float:
    return 20.0 * np.log10(max(float(x), 1e-12))


def make_rng(*keys) -> np.random.Generator:
    """A generator seeded from a stable hash of the keys (Python's hash() is salted per process)."""
    return np.random.default_rng(zlib.crc32(":".join(str(k) for k in keys).encode()))


def fft_size(n: int) -> int:
    return 1 << max(1, (int(n) - 1).bit_length())


def rfreqs(n: int, sr: float = SR) -> np.ndarray:
    return np.fft.rfftfreq(n, 1.0 / sr)


# ----------------------------------------------------------------------------- convolution

def convolve(x, h, full=True):
    """Linear convolution by FFT; `full` keeps the tail, otherwise the result has len(x)."""
    n = len(x) + len(h) - 1
    m = fft_size(n)
    y = np.fft.irfft(np.fft.rfft(x, m) * np.fft.rfft(h, m), m)
    return y[:n] if full else y[:len(x)]


def upsample2(x, n_out):
    """Band-limited (FFT, circular) interpolation of x to n_out samples (n_out ~ 2 len(x))."""
    X = np.fft.rfft(x)
    if len(x) % 2 == 0:
        X[-1] *= 0.5
    Y = np.zeros(n_out // 2 + 1, dtype=complex)
    k = min(len(X), len(Y))
    Y[:k] = X[:k]
    return np.fft.irfft(Y, n_out) * (n_out / len(x))


def fold(h, n):
    """Wrap an impulse response onto a circle of n samples (for circular convolution)."""
    if len(h) <= n:
        return np.pad(h, (0, n - len(h)))
    k = -(-len(h) // n)
    return np.pad(h, (0, k * n - len(h))).reshape(k, n).sum(axis=0)


def circ_convolve(x, h):
    n = len(x)
    return np.fft.irfft(np.fft.rfft(x) * np.fft.rfft(fold(h, n)), n)


# ----------------------------------------------------------------------------- spectral (zero-phase) shaping

def _smoothstep(x):
    x = np.clip(x, 0.0, 1.0)
    return x * x * (3 - 2 * x)


def band_gain(f, lo=None, hi=None, order=2):
    """Butterworth-like magnitude: high-pass at lo, low-pass at hi (either may be None)."""
    g = np.ones_like(f, dtype=float)
    ff = np.maximum(f, 1e-3)
    if lo:
        g = g / np.sqrt(1.0 + (lo / ff) ** (2 * order))
    if hi:
        g = g / np.sqrt(1.0 + (ff / hi) ** (2 * order))
    return g


def peak_gain(f, fc, q):
    """Two-pole band-pass magnitude, 1 at fc."""
    ff = np.maximum(f, 1e-3)
    return 1.0 / np.sqrt(1.0 + q * q * (ff / fc - fc / ff) ** 2)


def tilt_gain(f, db_per_oct, ref=1000.0):
    return (np.maximum(f, 10.0) / ref) ** (db_per_oct / 6.0206)


def shape(x, gain, circular=False, pad=0.06):
    """Filter by a zero-phase magnitude `gain` (array over rfft bins, or callable f -> gain).

    Circular: the transform of the whole signal (for loops). Otherwise the signal is zero-padded so
    the (short, symmetric) filter response does not wrap onto the start.
    """
    m = len(x) if circular else fft_size(len(x) + ns(pad))
    f = rfreqs(m)
    g = gain(f) if callable(gain) else gain
    return np.fft.irfft(np.fft.rfft(x, m) * g, m)[:len(x)]


def noise(n, rng, lo=None, hi=None, order=2, tilt=0.0, gain=None, sr=SR):
    """Unit-RMS periodic noise, band-limited and tilted (dB/octave) in the frequency domain."""
    w = rng.standard_normal(n)
    f = rfreqs(n, sr)
    g = band_gain(f, lo, hi, order)
    if tilt:
        g = g * tilt_gain(f, tilt)
    if gain is not None:
        g = g * (gain(f) if callable(gain) else gain)
    g[0] = 0.0
    y = np.fft.irfft(np.fft.rfft(w) * g, n)
    return y / (np.std(y) + 1e-12)


def smooth_random(n, rate_hz, rng, sr=SR):
    """Periodic slow random modulator (Gaussian low-pass of white noise), zero mean, unit std."""
    w = rng.standard_normal(n)
    f = rfreqs(n, sr)
    g = np.exp(-0.5 * (f / max(rate_hz, 1e-3)) ** 2)
    g[0] = 0.0
    y = np.fft.irfft(np.fft.rfft(w) * g, n)
    return y / (np.std(y) + 1e-12)


def wander(n, rate_hz, rng, sr=SR):
    """Cheap slow random modulator: random points every 1/rate seconds, smoothly interpolated (not periodic)."""
    k = int(n / sr * rate_hz) + 3
    pts = rng.standard_normal(k)
    x = np.arange(n) / sr * rate_hz
    i = np.floor(x).astype(int)
    u = x - i
    u = u * u * (3 - 2 * u)
    return pts[i] * (1 - u) + pts[i + 1] * u


def periodic_lfo(n, approx_hz, phase=0.0):
    """Sine LFO with a whole number of cycles in n samples (so it loops)."""
    cycles = max(1, int(round(approx_hz * n / SR)))
    return np.sin(2 * np.pi * cycles * np.arange(n) / n + phase)


# ----------------------------------------------------------------------------- causal filters

def onepole_lp(x, hz, circular=False):
    """Causal one-pole low-pass (exponential impulse response)."""
    a = np.exp(-2 * np.pi * hz / SR)
    m = int(min(4 * SR, max(64, 9.0 / (1 - a))))
    h = (1 - a) * a ** np.arange(m)
    return circ_convolve(x, h) if circular else convolve(x, h, full=False)


def dc_block(x, hz=20.0, circular=False):
    if circular:
        x = x - np.mean(x)
        return shape(x, lambda f: band_gain(f, lo=hz, order=2), circular=True)
    return x - onepole_lp(x, hz)


def modal_ir(freqs, taus, amps, n, phases=None, attack=None):
    """Sum of exponentially decaying sinusoids (modal synthesis); each mode stops at -80 dB."""
    out = np.zeros(n)
    if phases is None:
        phases = np.zeros(len(freqs))
    for k, (f, tau, a) in enumerate(zip(freqs, taus, amps)):
        if f <= 20 or f >= NYQ * 0.92 or a == 0:
            continue
        m = min(n, int(tau * 9.2 * SR) + 1)
        t = np.arange(m) / SR
        out[:m] += a * np.exp(-t / tau) * np.sin(2 * np.pi * f * t + phases[k])
    if attack:
        m = min(n, ns(attack))
        out[:m] *= np.sin(0.5 * np.pi * np.arange(m) / max(m, 1)) ** 2
    return out


def resonator_response(f, fc, bw, z1=None, sr=SR):
    """Complex response of a two-pole resonator (Klatt form, unity gain at DC) at frequencies f.
    Pass z1 = exp(-j 2 pi f / SR) when evaluating many resonators on the same grid."""
    r = np.exp(-np.pi * bw / sr)
    th = 2 * np.pi * fc / sr
    b = 2 * r * np.cos(th)
    c = -r * r
    a = 1 - b - c
    if z1 is None:
        z1 = np.exp(-1j * 2 * np.pi * f / sr)
    return a / (1 - z1 * (b + c * z1))


def fir_lowpass(fc, taps=255):
    m = np.arange(taps) - (taps - 1) / 2
    h = 2 * fc / SR * np.sinc(2 * fc / SR * m) * np.blackman(taps)
    return h / np.sum(h)


# ----------------------------------------------------------------------------- time-varying filtering

def stft_filter(x, gain_fn, frame=1024, hop=256, circular=False):
    """Apply a time-varying zero-phase gain: gain_fn(times[frames], freqs[bins]) -> (frames, bins)."""
    n = len(x)
    win = np.hanning(frame + 1)[:frame]
    if circular:
        starts = np.arange(0, n, hop) - frame // 2
        idx = (starts[:, None] + np.arange(frame)[None, :]) % n
        src = x
        centers = (starts + frame // 2) / SR
        length = n
    else:
        src = np.concatenate([np.zeros(frame), x, np.zeros(frame)])
        starts = np.arange(0, n + frame, hop)
        idx = starts[:, None] + np.arange(frame)[None, :]
        centers = (starts + frame // 2 - frame) / SR
        length = len(src)
    spec = np.fft.rfft(src[idx] * win, axis=1)
    g = gain_fn(centers, rfreqs(frame))
    frames = np.fft.irfft(spec * g, frame, axis=1) * win
    y = np.bincount(idx.ravel(), weights=frames.ravel(), minlength=length)
    wsum = np.bincount(idx.ravel(), weights=np.tile(win * win, len(starts)), minlength=length)
    y = y / np.maximum(wsum, 1e-3)
    return y if circular else y[frame:frame + n]


# ----------------------------------------------------------------------------- envelopes

def env_exp(n, tau, attack=0.0):
    t = tvec(n)
    e = np.exp(-t / max(tau, 1e-5))
    if attack > 0:
        e *= 1 - np.exp(-t / (attack / 3.0))
    return e


def env_points(n, points, db_interp=False):
    """Piecewise-linear envelope through (seconds, level) points."""
    ts = np.array([p[0] for p in points]) * SR
    vs = np.array([p[1] for p in points], dtype=float)
    if db_interp:
        return 10 ** (np.interp(np.arange(n), ts, 20 * np.log10(np.maximum(vs, 1e-6))) / 20)
    return np.interp(np.arange(n), ts, vs)


def raised_cos(n):
    """0 -> 1 over n samples."""
    if n <= 0:
        return np.ones(0)
    return 0.5 - 0.5 * np.cos(np.pi * np.arange(n) / n)


def half_sine_pulse(seconds):
    m = max(2, ns(seconds))
    return np.sin(np.pi * np.arange(m) / m)


# ----------------------------------------------------------------------------- events

def add_at(out, x, i0, wrap=False, gain=1.0):
    """out[i0:] += gain * x, wrapping round the end for loops."""
    i0 = int(i0)
    n = len(out)
    if wrap:
        i0 %= n
        m = len(x)
        pos = 0
        while pos < m:
            j = (i0 + pos) % n
            k = min(m - pos, n - j)
            out[j:j + k] += gain * x[pos:pos + k]
            pos += k
        return out
    if i0 >= n:
        return out
    if i0 < 0:
        x = x[-i0:]
        i0 = 0
    k = min(len(x), n - i0)
    out[i0:i0 + k] += gain * x[:k]
    return out


def poisson_times(rate, t0, t1, rng, rate_max=None):
    """Event times of a Poisson process on [t0, t1); `rate` is a number or a callable (thinning)."""
    if not callable(rate):
        k = rng.poisson(max(rate, 0) * (t1 - t0))
        return np.sort(rng.uniform(t0, t1, k))
    lam = rate_max
    k = rng.poisson(lam * (t1 - t0))
    t = np.sort(rng.uniform(t0, t1, k))
    keep = rng.random(k) * lam < rate(t)
    return t[keep]


def bounce_times(first_gap, restitution, count, rng, jitter=0.08):
    """Impact times of a bouncing object: gaps shrink by the coefficient of restitution."""
    t, gap, out = 0.0, first_gap, [0.0]
    for _ in range(count - 1):
        t += gap * (1 + jitter * rng.standard_normal())
        out.append(t)
        gap *= restitution
    return np.array(out)


# ----------------------------------------------------------------------------- dynamics and nonlinearity

def soft_clip(x, drive):
    return np.tanh(x * drive) / np.tanh(drive)


def limiter(x, ceiling, window_ms=4.0, circular=False):
    """Look-around peak limiter: |y| <= ceiling guaranteed, gain changes smoothed over ~window_ms."""
    a = np.abs(x)
    g = np.minimum(1.0, ceiling / np.maximum(a, 1e-12))
    if np.all(g >= 1.0):
        return x.copy()
    half = max(1, ns(window_ms / 2000.0))
    w = 2 * half + 1
    gp = np.concatenate([g[-half:], g, g[:half]]) if circular else np.pad(g, half, mode="edge")
    gm = sliding_window_view(gp, w).min(axis=1)
    k = np.hanning(w + 2)[1:-1]
    k /= k.sum()
    gp2 = np.concatenate([gm[-half:], gm, gm[:half]]) if circular else np.pad(gm, half, mode="edge")
    gs = np.convolve(gp2, k, mode="valid")
    return x * np.minimum(gs, g)


# ----------------------------------------------------------------------------- reverb

def reverb_ir(rt60, rng, seconds=None, predelay=0.012, hf=0.45, lf=1.15, early=6, early_spread=0.05):
    """Synthetic room response: band-split noise with per-band exponential decays, a diffuse build-up
    and a few discrete early reflections. Energy-normalised."""
    seconds = seconds or rt60 * 1.1
    n = ns(seconds)
    t = tvec(n)
    W = np.fft.rfft(rng.standard_normal(n))
    f = rfreqs(n)
    lf2 = np.log2(np.maximum(f, 1.0))
    xs = [np.log2(300.0), np.log2(1500.0), np.log2(5000.0)]
    s = [_smoothstep((lf2 - c) + 0.5) for c in xs]
    masks = [1 - s[0], s[0] - s[1], s[1] - s[2], s[2]]
    rts = [rt60 * lf, rt60, rt60 * (0.5 + 0.5 * hf), rt60 * hf * 0.7]
    ir = np.zeros(n)
    for m, rt in zip(masks, rts):
        ir += np.fft.irfft(W * m, n) * np.exp(-6.9078 * t / rt)
    i0 = ns(predelay)
    build = np.zeros(n)
    build[i0:] = 1 - np.exp(-(t[i0:] - predelay) / 0.012)
    ir *= build
    ref = np.max(np.abs(ir))
    for _ in range(early):
        j = ns(predelay * 0.5 + rng.uniform(0, early_spread))
        if j < n:
            ir[j] += rng.choice([-1.0, 1.0]) * rng.uniform(0.4, 1.0) * ref
    return ir / np.sqrt(np.sum(ir * ir))


def reverb(x, ir, wet, dry=1.0, circular=False):
    y = circ_convolve(x, ir) if circular else convolve(x, ir, full=True)
    out = wet * y
    out[:len(x)] += dry * x
    return out


# ----------------------------------------------------------------------------- analysis

def rms(x):
    return float(np.sqrt(np.mean(np.square(x)))) if len(x) else 0.0


def peak(x):
    return float(np.max(np.abs(x))) if len(x) else 0.0


def spectral_centroid(x):
    m = np.abs(np.fft.rfft(x))
    f = rfreqs(len(x))
    return float(np.sum(m * f) / (np.sum(m) + 1e-12))


def band_energy_fraction(x, lo, hi):
    p = np.abs(np.fft.rfft(x)) ** 2
    f = rfreqs(len(x))
    return float(np.sum(p[(f >= lo) & (f < hi)]) / (np.sum(p) + 1e-20))


def ltas_db(x, frame=4096):
    """Long-term average spectrum (dB) with Hann frames, plus its frequency axis."""
    hop = frame // 2
    if len(x) < frame:
        x = np.pad(x, (0, frame - len(x)))
    idx = np.arange(0, len(x) - frame + 1, hop)[:, None] + np.arange(frame)[None, :]
    p = np.mean(np.abs(np.fft.rfft(x[idx] * np.hanning(frame), axis=1)) ** 2, axis=0)
    return rfreqs(frame), 10 * np.log10(p + 1e-20)


def seam_metrics(x, frames=(512, 1024, 2048)):
    """Seam check for a loop, played as x, x, x, ...:
    * jump: the end->start sample step relative to the loop's own 99th-percentile sample step
      (a click would be >> 1);
    * flux z: the spectral flux between the frames just before and just across the seam, as a z-score
      against every other frame-to-frame flux of the loop (worst of three frame sizes; a splice where the
      spectrum changes stands out at z >> 3, a seamless loop looks like any other position)."""
    d = np.abs(np.diff(x))
    jump = abs(float(x[0] - x[-1])) / (float(np.percentile(d, 99.0)) + 1e-12)
    zs = []
    for frame in frames:
        hop = frame // 2
        n = len(x)
        # frames aligned so that one frame ends exactly at the seam and the next starts at it
        starts = (np.arange(-(n // hop), 1) * hop + n - frame) % n
        xx = np.concatenate([x, x[:frame]])
        mags = np.abs(np.fft.rfft(xx[starts[:, None] + np.arange(frame)] * np.hanning(frame), axis=1))
        lm = np.log(mags + 1e-9)
        flux = np.sqrt(np.mean(np.diff(lm, axis=0) ** 2, axis=1))
        # crossing frames: last frame (ends at the seam) vs. the frame centred on the seam
        seam = np.concatenate([x[n - frame // 2:], x[:frame // 2]])
        before = x[n - frame:]                      # the frame one hop earlier
        ls = np.log(np.abs(np.fft.rfft(seam * np.hanning(frame))) + 1e-9)
        lb = np.log(np.abs(np.fft.rfft(before * np.hanning(frame))) + 1e-9)
        sf = np.sqrt(np.mean((ls - lb) ** 2))
        zs.append((sf - float(np.mean(flux))) / (float(np.std(flux)) + 1e-12))
    return jump, float(max(zs))


# ----------------------------------------------------------------------------- files

def write_wav(path, x, loop=False):
    """16-bit PCM mono at SR (stdlib wave). Loops also get a standard 'smpl' chunk with one forward loop
    over the whole file, so importers that read WAV loop points (Godot's "Detect From WAV") loop them."""
    x16 = np.clip(np.round(np.asarray(x) * 32767.0), -32768, 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(x16.tobytes())
    if loop:
        smpl = struct.pack("<9I", 0, 0, int(round(1e9 / SR)), 60, 0, 0, 0, 1, 0)
        smpl += struct.pack("<6I", 0, 0, 0, len(x16) - 1, 0, 0)       # id, forward, start, end (inclusive)
        with open(path, "r+b") as f:
            f.seek(0, 2)
            f.write(b"smpl" + struct.pack("<I", len(smpl)) + smpl)
            size = f.tell()
            f.seek(4)
            f.write(struct.pack("<I", size - 8))
    return x16


def read_wav(path):
    with wave.open(path, "rb") as w:
        data = w.readframes(w.getnframes())
    return np.frombuffer(data, dtype="<i2").astype(float) / 32767.0


def _png_chunk(tag, data):
    return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)


def write_png(path, rgb):
    """Minimal RGB PNG writer: rgb is uint8 (h, w, 3)."""
    h, w, _ = rgb.shape
    raw = b"".join(b"\x00" + rgb[y].tobytes() for y in range(h))
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(_png_chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)))
        f.write(_png_chunk(b"IDAT", zlib.compress(raw, 9)))
        f.write(_png_chunk(b"IEND", b""))


def spectrogram_png(path, x, frame=1024, hop=None, height=320, fmax=NYQ, db_range=90.0, log_f=True, max_w=1400):
    """Spectrogram image (time left->right, frequency bottom->top, log axis from 30 Hz)."""
    hop = hop or max(64, int(np.ceil(len(x) / max_w)))
    xp = np.pad(x, (frame // 2, frame))
    starts = np.arange(0, len(x), hop)
    s = np.abs(np.fft.rfft(xp[starts[:, None] + np.arange(frame)] * np.hanning(frame), axis=1))
    sdb = 20 * np.log10(s + 1e-9)
    sdb -= sdb.max()
    f = rfreqs(frame)
    rows = np.geomspace(30.0, fmax, height) if log_f else np.linspace(0, fmax, height)
    img = np.stack([np.interp(rows, f, col) for col in sdb], axis=1)[::-1]
    v = np.clip(1 + img / db_range, 0, 1)
    r = np.clip(3 * v - 1.2, 0, 1)
    g = np.clip(3 * v - 2.0, 0, 1) * 0.9 + np.clip(v - 0.2, 0, 1) * 0.1
    b = np.clip(np.where(v < 0.5, 2 * v, 2 - 2 * v), 0, 1) * 0.8
    rgb = (np.stack([r, g, b], axis=2) * 255).astype(np.uint8)
    write_png(path, rgb)
