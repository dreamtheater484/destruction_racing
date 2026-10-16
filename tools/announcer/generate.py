"""Announcer v1: script.tsv -> Kokoro TTS (or your own recording) -> stadium PA chain -> OGG + manifest.json.

Run with the Kokoro venv (paths are resolved from this file, so any working directory is fine):

    .engine\\kokoro-venv\\Scripts\\python.exe tools\\announcer\\generate.py            incremental (changed lines only)
    ... generate.py --force                                                    regenerate every line
    ... generate.py --only big_hit_03 --only intro_01,intro_02                 just these ids
    ... generate.py --check                                                    verify the output, synthesize nothing

Nothing touches the network: HF_HUB_OFFLINE=1 is forced, the model and voices come from the local cache.
The PA chain is plain numpy (no scipy). See README.md next to this file.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
import time
import warnings
from pathlib import Path

os.environ["HF_HUB_OFFLINE"] = "1"
os.environ["TRANSFORMERS_OFFLINE"] = "1"

import numpy as np
import soundfile as sf

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
SCRIPT = HERE / "script.tsv"
RECORDINGS = HERE / "recordings"             # optional: <id>.wav/.ogg/.flac here replaces the TTS take (gets the PA chain)
OUT = ROOT / "assets" / "audio" / "announcer"
MANIFEST = OUT / "manifest.json"

REPO = "hexgrad/Kokoro-82M"
VOICE = "am_fenrir"    # see README.md "Voice" for the comparison that picked it
SPEED = 0.88           # fenrir talks fast: 0.88 matches the pace of am_michael at 1.12 (~18.7 chars/s incl. spaces)
SR = 24000

# Every value here goes into each line's hash: change one and the next run regenerates everything.
CHAIN = {
    "version": 1,
    # 1. trim: 5 ms frames below gate_db (re the loudest frame) at either end are silence
    "gate_db": -45.0, "head_ms": 30.0, "tail_ms": 80.0,
    # 2. horn loudspeaker: steep windowed-sinc band-pass plus a presence bell
    "hp_hz": 250.0, "lp_hz": 5000.0, "fir_taps": 1023,
    "presence_hz": 2500.0, "presence_db": 4.0, "presence_sigma_oct": 0.55,
    # 3. amplifier grit: tanh soft clip (2x oversampled) at a fixed drive level, then the horn's own roll-off
    "drive_rms_db": -14.0, "drive": 1.5, "horn_rolloff_hz": 6500.0,
    # 4. fast feed-forward compressor (soft knee) with automatic make-up gain
    "comp_threshold_db": -20.0, "comp_ratio": 4.0, "comp_knee_db": 6.0,
    "comp_attack_ms": 3.0, "comp_release_ms": 80.0,
    # 5. slap-back off the far stand
    "slap_ms": 110.0, "slap_db": -12.0, "slap_lp_hz": 2500.0,
    # 6. arena reverb: synthetic IR, multiband exponential decay, highs die faster
    "rev_predelay_ms": 40.0, "rev_rt60_s": 2.0, "rev_rt60_5k_s": 0.9, "rev_hf_knee_hz": 1000.0,
    "rev_wet": 0.22, "rev_seed": 1995,
    # 7. level: same RMS for the dry speech of every line, then a -1 dBFS peak limiter
    "target_rms_db": -16.0, "ceiling_db": -1.0, "limiter_look_ms": 2.0, "limiter_release_ms": 60.0,
    #    reverb tail is cut once it falls under tail_floor_db (dBFS), at most max_tail_s after the speech, faded out
    "tail_floor_db": -54.0, "max_tail_s": 1.1, "fade_ms": 120.0,
}

# Number call-outs for the AI cars (the player is number seven but gets the set too, in case that changes).
NUMBERS = range(2, 62)
NUMBER_TEMPLATES = {          # several phrasings alternate by car number
    "wrecked_num": ["Number {n} is out!", "That's it for number {n}!"],
    "winner_num": ["Number {n} is the last car running!"],
    "leader_num": ["Number {n} takes the lead!"],
    "race_winner_num": ["Number {n} wins the race!", "And number {n} takes the checkered flag!"],
}
GENERATED_MARKER = "# ---- generated number lines: generate.py rewrites everything below from NUMBER_TEMPLATES, edit there ----"

# Sanity limits for --check. Speech rate in characters per second of speech, 8-20: "too fast" (TTS cut the line
# short) counts letters and digits only, since spaces and "!" can take no time; "too slow" (stretched, garbled or
# silent) counts every character, since commas and "!" do carry pauses ("Go, go, go!").
DURATION_RANGE = (0.4, 4.5)
CPS_RANGE = (8.0, 20.0)
CLIP_DBFS = -0.1


# ----------------------------------------------------------------------------------------------- script

_ONES = ("zero one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen "
         "sixteen seventeen eighteen nineteen").split()
_TENS = "_ _ twenty thirty forty fifty sixty seventy eighty ninety".split()


def number_words(n: int) -> str:
    if n < 20:
        return _ONES[n]
    if n < 100:
        return _TENS[n // 10] + ("-" + _ONES[n % 10] if n % 10 else "")
    raise ValueError(n)


def number_lines() -> list[tuple[str, str, str]]:
    rows = []
    for cat, templates in NUMBER_TEMPLATES.items():
        for n in NUMBERS:
            rows.append((f"{cat}_{n:02d}", cat, templates[n % len(templates)].format(n=number_words(n))))
    return rows


def sync_number_lines() -> None:
    """Rewrite the generated block at the end of script.tsv (hand-written lines above the marker are kept)."""
    text = SCRIPT.read_text(encoding="utf-8-sig")
    head = text.split(GENERATED_MARKER)[0].rstrip("\n") + "\n"
    block = [GENERATED_MARKER]
    last_cat = None
    for rid, cat, line in number_lines():
        if cat != last_cat:
            block.append("")
            last_cat = cat
        block.append(f"{rid}\t{cat}\t{line}")
    new = head + "\n" + "\n".join(block) + "\n"
    if new != text:
        SCRIPT.write_text(new, encoding="utf-8", newline="\n")
        print("script.tsv: number lines rewritten")


def read_script() -> list[dict]:
    lines, seen, problems = [], set(), []
    for no, raw in enumerate(SCRIPT.read_text(encoding="utf-8-sig").splitlines(), 1):
        if not raw.strip() or raw.startswith("#"):
            continue
        cols = raw.split("\t")
        if cols[0] == "id":
            continue
        if len(cols) != 3 or not all(c.strip() for c in cols):
            problems.append(f"line {no}: need 3 tab-separated columns (id, category, text): {raw!r}")
            continue
        rid, cat, text = (c.strip() for c in cols)
        text = text.replace("’", "'").replace("‘", "'").replace("“", '"').replace("”", '"')
        if not re.fullmatch(re.escape(cat) + r"_\d{2,}", rid):
            problems.append(f"line {no}: id {rid!r} should be <category>_<nn> for category {cat!r}")
        if rid in seen:
            problems.append(f"line {no}: duplicate id {rid!r}")
        seen.add(rid)
        if re.search(r"\d", text):
            problems.append(f"line {no}: {rid}: write numbers as words so the voice reads them right")
        for word in re.findall(r"\b[A-Z]{2,}\b", text):
            print(f"  note: {rid}: all-caps {word!r} may be spelled out letter by letter")
        lines.append({"id": rid, "category": cat, "text": text})
    if problems:
        sys.exit("script.tsv problems:\n  " + "\n  ".join(problems))
    return lines


# ----------------------------------------------------------------------------------------------- sources

_pipes: dict = {}


def tts(text: str, voice: str = VOICE, speed: float = SPEED) -> np.ndarray:
    try:
        from kokoro import KPipeline
    except ImportError:
        sys.exit("kokoro is not importable: run this with .engine\\kokoro-venv\\Scripts\\python.exe")
    lang = voice[0]                                    # 'a' American, 'b' British voices
    if lang not in _pipes:
        warnings.filterwarnings("ignore", category=UserWarning)
        warnings.filterwarnings("ignore", category=FutureWarning)
        _pipes[lang] = KPipeline(lang_code=lang, repo_id=REPO)
    chunks = [np.asarray(audio, dtype=np.float32) for _, _, audio in _pipes[lang](text, voice=voice, speed=speed)]
    if not chunks:
        raise RuntimeError(f"Kokoro produced no audio for {text!r}")
    return np.concatenate(chunks).astype(np.float64)


def resample(x: np.ndarray, sr_in: int, sr_out: int = SR) -> np.ndarray:
    """FFT resampling (band-limited; fine for short clips that start and end quietly)."""
    if sr_in == sr_out:
        return x
    pad = len(x) // 8 + 256                            # guard against circular wrap
    xp = np.concatenate([np.zeros(pad), x, np.zeros(pad)])
    n_out = int(round(len(xp) * sr_out / sr_in))
    spec = np.fft.rfft(xp)
    keep = n_out // 2 + 1
    spec = spec[:keep] if keep <= len(spec) else np.concatenate([spec, np.zeros(keep - len(spec))])
    y = np.fft.irfft(spec, n_out) * (n_out / len(xp))
    cut = int(round(pad * sr_out / sr_in))
    return y[cut: cut + int(round(len(x) * sr_out / sr_in))]


def recording_for(rid: str) -> Path | None:
    for ext in (".wav", ".ogg", ".flac"):
        p = RECORDINGS / f"{rid}{ext}"
        if p.exists():
            return p
    return None


def load_recording(path: Path) -> np.ndarray:
    data, sr = sf.read(str(path), always_2d=True, dtype="float64")
    return resample(data.mean(axis=1), sr)


# ----------------------------------------------------------------------------------------------- dsp

def db(x):
    return 20.0 * np.log10(np.maximum(x, 1e-12))


def undb(d):
    return 10.0 ** (d / 20.0)


def fftconv(x: np.ndarray, h: np.ndarray) -> np.ndarray:
    n = len(x) + len(h) - 1
    nfft = 1 << (n - 1).bit_length()
    return np.fft.irfft(np.fft.rfft(x, nfft) * np.fft.rfft(h, nfft), nfft)[:n]


def fir_same(x: np.ndarray, h: np.ndarray) -> np.ndarray:
    """Linear-phase FIR (odd length) with its delay removed, so timing is unchanged."""
    d = (len(h) - 1) // 2
    return fftconv(x, h)[d: d + len(x)]


def lowpass_kernel(fc: float, taps: int) -> np.ndarray:
    n = np.arange(taps) - (taps - 1) / 2
    h = 2 * fc / SR * np.sinc(2 * fc / SR * n) * np.blackman(taps)
    return h / h.sum()


def highpass_kernel(fc: float, taps: int) -> np.ndarray:
    h = -lowpass_kernel(fc, taps)
    h[(taps - 1) // 2] += 1.0                          # spectral inversion
    return h


def bell_kernel(fc: float, gain_db: float, sigma_oct: float, taps: int = 511) -> np.ndarray:
    """Zero-phase presence bell (Gaussian in log frequency), frequency-sampled and Hann-windowed."""
    nfft = 8192
    f = np.fft.rfftfreq(nfft, 1 / SR)
    g = 1 + (undb(gain_db) - 1) * np.exp(-0.5 * (np.log2(np.maximum(f, 1.0) / fc) / sigma_oct) ** 2)
    h = np.roll(np.fft.irfft(g, nfft), taps // 2)[:taps]
    return h * np.hanning(taps)


def frame_rms(x: np.ndarray, n: int) -> np.ndarray:
    k = max(1, len(x) // n)
    fr = np.zeros(k * n)
    fr[: min(len(x), k * n)] = x[: k * n]
    return np.sqrt((fr.reshape(k, n) ** 2).mean(axis=1))


def active_rms(x: np.ndarray, rel_db: float = -40.0, frame_ms: float = 20.0) -> float:
    """RMS over the frames that carry speech (ignores the gaps between words)."""
    n = int(SR * frame_ms / 1000)
    r = frame_rms(x, n)
    act = r > r.max() * undb(rel_db)
    return float(np.sqrt((r[act] ** 2).mean()))


def trim(x: np.ndarray, p: dict) -> tuple[np.ndarray, float]:
    """Cut leading/trailing silence, keep head_ms before and tail_ms after the speech. Returns (audio, speech_end_s)."""
    hop = int(SR * 0.005)
    r = frame_rms(x, hop)
    loud = np.nonzero(r > r.max() * undb(p["gate_db"]))[0]
    first, last = loud[0] * hop, min(len(x), (loud[-1] + 1) * hop)
    head, tail = int(SR * p["head_ms"] / 1000), int(SR * p["tail_ms"] / 1000)
    pre = max(0, head - first)                          # audio that starts hot gets zero padding instead
    x = np.concatenate([np.zeros(pre), x, np.zeros(tail)])
    first, last = first + pre, last + pre
    y = x[first - head: last + tail].copy()
    fi, fo = int(SR * 0.004), int(SR * 0.012)
    y[:fi] *= np.linspace(0, 1, fi)
    y[-fo:] *= np.linspace(1, 0, fo)
    return y, head / SR + (last - first) / SR


def saturate(x: np.ndarray, drive: float) -> np.ndarray:
    """tanh soft clip at 2x oversampling (FFT up/down) so the new harmonics don't alias."""
    n = len(x)
    spec = np.fft.rfft(x)
    up = np.fft.irfft(np.concatenate([spec, np.zeros(n + 1 - len(spec))]), 2 * n) * 2
    y = np.tanh(drive * up) / drive                    # unity gain for small signals
    return np.fft.irfft(np.fft.rfft(y)[: n // 2 + 1], n) / 2


def compress(x: np.ndarray, p: dict) -> tuple[np.ndarray, float]:
    """Feed-forward peak compressor, soft knee, gain smoothed in dB (3 ms attack, 80 ms release). Returns (y, mean GR dB)."""
    thr, ratio, knee = p["comp_threshold_db"], p["comp_ratio"], p["comp_knee_db"]
    over = db(np.abs(x)) - thr
    slope = 1 - 1 / ratio
    gr = np.where(over <= -knee / 2, 0.0,
                  np.where(over >= knee / 2, slope * over, slope * (over + knee / 2) ** 2 / (2 * knee)))
    aa = np.exp(-1 / (p["comp_attack_ms"] * 1e-3 * SR))
    ar = np.exp(-1 / (p["comp_release_ms"] * 1e-3 * SR))
    s, out = 0.0, []
    for g in gr.tolist():
        s = aa * s + (1 - aa) * g if g > s else ar * s + (1 - ar) * g
        out.append(s)
    smooth = np.array(out)
    y = x * undb(-smooth)
    makeup = active_rms(x) / max(active_rms(y), 1e-9)  # automatic make-up: speech back to its input level
    loud = np.abs(x) > undb(thr - 12)
    return y * makeup, float(smooth[loud].mean()) if loud.any() else 0.0


def slapback(x: np.ndarray, p: dict) -> np.ndarray:
    d = int(SR * p["slap_ms"] / 1000)
    echo = fir_same(x, lowpass_kernel(p["slap_lp_hz"], 255)) * undb(p["slap_db"])
    y = np.zeros(len(x) + d)
    y[: len(x)] += x
    y[d:] += echo
    return y


_ir_cache: dict = {}


def arena_ir(p: dict) -> np.ndarray:
    key = json.dumps(p, sort_keys=True)
    if key in _ir_cache:
        return _ir_cache[key]
    rng = np.random.default_rng(p["rev_seed"])
    rt_lo, rt_hi, knee = p["rev_rt60_s"], p["rev_rt60_5k_s"], p["rev_hf_knee_hz"]
    n = int(SR * rt_lo * 1.3)                           # ~78 dB of decay at the longest RT60
    t = np.arange(n) / SR
    spec = np.fft.rfft(rng.standard_normal(n))
    f = np.fft.rfftfreq(n, 1 / SR)
    centers = 125.0 * 2.0 ** np.arange(8)               # octave bands 125 Hz .. 16 kHz
    u = np.log2(np.maximum(f, 1.0) / centers[0])
    ir = np.zeros(n)
    for k, fc in enumerate(centers):
        w = np.clip(1 - np.abs(u - k), 0, 1)
        if k == 0:
            w[u < 0] = 1
        if k == len(centers) - 1:
            w[u > k] = 1
        mask = np.sin(np.pi / 2 * w) ** 2               # complementary: the masks sum to one
        frac = np.clip(np.log2(fc / knee) / np.log2(5000.0 / knee), 0, None)
        rt = max(0.35, rt_lo * (rt_hi / rt_lo) ** frac)  # flat below the knee, shorter above it
        ir += np.fft.irfft(spec * mask, n) * np.exp(-6.9078 * t / rt)
    ir *= 1 - np.exp(-t / 0.008)                         # diffuse build-up instead of a hard edge
    ir = np.concatenate([np.zeros(int(SR * p["rev_predelay_ms"] / 1000)), ir])
    ir /= np.sqrt((ir ** 2).sum())                       # unit energy: wet speech as loud as dry
    _ir_cache[key] = ir
    return ir


def limit(x: np.ndarray, p: dict) -> np.ndarray:
    """Look-ahead peak limiter: the gain is never above ceiling/|x| at any sample, and ramps smoothly."""
    ceil = undb(p["ceiling_db"])
    look = max(1, int(SR * p["limiter_look_ms"] / 1000))
    need = np.minimum(1.0, ceil / np.maximum(np.abs(x), 1e-12))
    pad = np.concatenate([need, np.ones(look - 1)])
    m = np.lib.stride_tricks.sliding_window_view(pad, look).min(axis=1)      # min over [n, n+look)
    m = np.concatenate([np.ones(look - 1), m])
    g = np.convolve(m, np.ones(look) / look, mode="valid")                 # still <= need[n]
    if g.min() >= 1.0:
        return x
    ar = np.exp(-1 / (p["limiter_release_ms"] * 1e-3 * SR))
    s, out = 1.0, []
    for v in g.tolist():
        s = v if v < s else ar * s + (1 - ar) * v
        out.append(s)
    return x * np.array(out)


def pa_chain(x: np.ndarray, p: dict = CHAIN) -> tuple[np.ndarray, dict]:
    """Dry speech in, stadium PA out. Returns (audio, info)."""
    # 1. trim
    x, speech_end = trim(x, p)
    # 2. horn loudspeaker band + presence
    taps = p["fir_taps"]
    horn = np.convolve(np.convolve(highpass_kernel(p["hp_hz"], taps), lowpass_kernel(p["lp_hz"], taps)),
                       bell_kernel(p["presence_hz"], p["presence_db"], p["presence_sigma_oct"]))
    x = fir_same(x, horn)
    # 3. amp grit at a fixed drive level, then the horn rolls off the new harmonics
    x *= undb(p["drive_rms_db"]) / active_rms(x)
    x = saturate(x, p["drive"])
    x = fir_same(x, lowpass_kernel(p["horn_rolloff_hz"], 255))
    # 4. compression + make-up
    x, mean_gr = compress(x, p)
    # 5. slap-back, 6. arena reverb (the echo reverberates too)
    dry = slapback(x, p)
    wet = fftconv(dry, arena_ir(p))
    w = p["rev_wet"]
    y = w * wet
    y[: len(dry)] += (1 - w) * dry
    # 7. consistent loudness from the dry speech, then the peak limiter
    y *= undb(p["target_rms_db"]) / active_rms((1 - w) * x)
    pre_peak = float(np.abs(y).max())
    y = limit(y, p)
    # tail: stop once the reverb is under the floor (or max_tail_s after the speech), fade out
    hop = int(SR * 0.01)
    r = frame_rms(y, hop)
    s0 = int(speech_end * SR / hop)
    above = np.nonzero(r[s0:] > undb(p["tail_floor_db"]))[0]
    end = (s0 + (above[-1] + 1 if len(above) else 0)) * hop
    end = min(end, int((speech_end + p["max_tail_s"]) * SR), len(y))
    end = max(end, int((speech_end + p["tail_ms"] / 1000) * SR))
    y = y[:end].copy()
    fo = min(int(SR * p["fade_ms"] / 1000), len(y) // 4)
    y[-fo:] *= 0.5 + 0.5 * np.cos(np.linspace(0, np.pi, fo))
    return y.astype(np.float32), {"speech_end": speech_end, "mean_gr_db": mean_gr, "pre_limit_peak_db": float(db(pre_peak))}


# ----------------------------------------------------------------------------------------------- build

def write_ogg(path: Path, audio: np.ndarray) -> float:
    """Vorbis can overshoot the limiter by ~1 dB on transients: decode, and re-encode slightly lower until the
    file itself peaks at the ceiling (within 0.25 dB). Returns the decoded peak in dBFS."""
    ceil = undb(CHAIN["ceiling_db"])
    for _ in range(4):
        sf.write(str(path), audio, SR, format="OGG", subtype="VORBIS")
        peak = float(np.abs(sf.read(str(path), dtype="float32")[0]).max())
        if peak <= ceil * undb(0.25):
            break
        audio = audio * (ceil / peak)
    return float(db(peak))


def line_hash(line: dict, rec: Path | None) -> str:
    src = ({"recording": hashlib.sha1(rec.read_bytes()).hexdigest()} if rec
           else {"text": line["text"], "voice": VOICE, "speed": SPEED, "model": REPO})
    blob = json.dumps({"src": src, "chain": CHAIN, "sr": SR}, sort_keys=True)
    return hashlib.sha1(blob.encode()).hexdigest()[:16]


def load_manifest() -> dict:
    if MANIFEST.exists():
        try:
            return json.loads(MANIFEST.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            print("manifest.json unreadable, starting over")
    return {}


def write_manifest(entries: list[dict]) -> None:
    data = {"voice": VOICE, "speed": SPEED, "sample_rate": SR, "chain": CHAIN, "lines": entries}
    MANIFEST.write_text(json.dumps(data, indent=1, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")


def build(force: bool, only: set[str]) -> None:
    sync_number_lines()
    lines = read_script()
    ids = {l["id"] for l in lines}
    unknown = only - ids
    if unknown:
        sys.exit("--only: unknown id(s): " + ", ".join(sorted(unknown)))
    OUT.mkdir(parents=True, exist_ok=True)
    old = {e["id"]: e for e in load_manifest().get("lines", [])}

    entries, todo = [], []
    for line in lines:
        rec = recording_for(line["id"])
        h = line_hash(line, rec)
        prev = old.get(line["id"])
        fresh = prev and prev.get("hash") == h and (OUT / prev["file"]).exists()
        selected = line["id"] in only if only else (force or not fresh)
        if selected:
            todo.append((len(entries), line, rec, h))
            entries.append(None)
        elif prev and (OUT / prev["file"]).exists():
            entries.append({**prev, "category": line["category"], "text": line["text"]})
        else:
            entries.append(None)                        # missing and not selected (an --only run): left out
    print(f"{len(lines)} lines in script, {len(todo)} to generate ({VOICE} @ {SPEED})")

    t0 = time.time()
    for k, (slot, line, rec, h) in enumerate(todo, 1):
        src = load_recording(rec) if rec else tts(line["text"])
        audio, info = pa_chain(src)
        name = f"{line['id']}.ogg"
        file_peak = write_ogg(OUT / name, audio)
        entry = {"id": line["id"], "category": line["category"], "text": line["text"], "file": name,
                 "duration": round(len(audio) / SR, 3), "speech_end": round(info["speech_end"], 3), "hash": h}
        if rec:
            entry["source"] = "recording"
        entries[slot] = entry
        eta = (time.time() - t0) / k * (len(todo) - k)
        print(f"[{k:3d}/{len(todo)}] {line['id']:26s} {entry['duration']:4.2f}s  GR {info['mean_gr_db']:4.1f} dB"
              f"  limiter {max(0.0, info['pre_limit_peak_db'] - CHAIN['ceiling_db']):3.1f} dB  file peak {file_peak:+5.2f} dBFS"
              f"  {'(recording) ' if rec else ''}eta {eta:4.0f}s")
        if k % 25 == 0:
            write_manifest([e for e in entries if e])  # keep progress if interrupted

    # files of lines that were removed from the script (only ones this generator wrote)
    for rid, prev in old.items():
        if rid not in ids:
            for f in (OUT / prev["file"], OUT / (prev["file"] + ".import")):
                if f.exists():
                    f.unlink()
                    print(f"removed stale {f.name}")
    write_manifest([e for e in entries if e])
    missing = [l["id"] for l, e in zip(lines, entries) if not e]
    if missing:
        print(f"note: {len(missing)} lines not generated yet (run without --only): {', '.join(missing[:8])}...")


# ----------------------------------------------------------------------------------------------- check

def spoken_chars(text: str) -> int:
    return sum(c.isalnum() for c in text)


def check() -> bool:
    data = load_manifest()
    script = {l["id"]: l for l in read_script()}
    entries = data.get("lines", [])
    problems, total, per_cat, rates, peaks = [], 0, {}, [], []
    for e in entries:
        path = OUT / e["file"]
        if not path.exists():
            problems.append(f"{e['id']}: file missing")
            continue
        total += path.stat().st_size
        per_cat[e["category"]] = per_cat.get(e["category"], 0) + 1
        x, sr = sf.read(str(path), dtype="float64")
        dur = len(x) / sr
        peak = np.abs(x).max()
        if sr != SR or x.ndim != 1:
            problems.append(f"{e['id']}: expected mono {SR} Hz, got {x.ndim}ch {sr} Hz")
        if not DURATION_RANGE[0] <= dur <= DURATION_RANGE[1]:
            problems.append(f"{e['id']}: duration {dur:.2f}s outside {DURATION_RANGE}")
        if abs(dur - e["duration"]) > 0.05:
            problems.append(f"{e['id']}: manifest duration {e['duration']} but file is {dur:.3f}s")
        if peak >= undb(CLIP_DBFS):
            problems.append(f"{e['id']}: clipping (peak {db(peak):+.2f} dBFS)")
        if peak < undb(-12) or active_rms(x) < undb(-35):
            problems.append(f"{e['id']}: too quiet / silent (peak {db(peak):+.1f} dBFS)")
        speech = max(e.get("speech_end", dur) - CHAIN["head_ms"] / 1000, 1e-3)
        fast, slow = spoken_chars(e["text"]) / speech, len(e["text"]) / speech
        if e.get("source") != "recording" and (fast > CPS_RANGE[1] or slow < CPS_RANGE[0]):
            problems.append(f"{e['id']}: implausible speech rate ({fast:.1f} letters/s, {slow:.1f} chars/s) "
                            f"for {e['text']!r}, expected {CPS_RANGE}")
        rates.append((fast, slow))
        peaks.append(peak)
        if e["id"] in script and script[e["id"]]["text"] != e["text"]:
            problems.append(f"{e['id']}: text changed since generation (re-run the generator)")
    for rid in script:
        if rid not in {e["id"] for e in entries}:
            problems.append(f"{rid}: in script.tsv but not in the manifest")
    print(f"{len(entries)} files, {total / 1e6:.2f} MB, {len(per_cat)} categories")
    for cat, n in per_cat.items():
        print(f"  {cat:22s} {n}")
    if rates:
        durs = [e["duration"] for e in entries]
        print(f"durations {min(durs):.2f}-{max(durs):.2f} s (total {sum(durs) / 60:.1f} min); "
              f"fastest {max(r[0] for r in rates):.1f} letters/s, slowest {min(r[1] for r in rates):.1f} chars/s; "
              f"highest peak {db(max(peaks)):+.2f} dBFS")
    if problems:
        print(f"{len(problems)} problem(s):\n  " + "\n  ".join(problems))
    else:
        print("check passed")
    return not problems


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--force", action="store_true", help="regenerate every line")
    ap.add_argument("--only", action="append", default=[], metavar="ID", help="regenerate just this id (repeat or comma list)")
    ap.add_argument("--check", action="store_true", help="only verify the existing output")
    args = ap.parse_args()
    if not args.check:
        build(args.force, {i.strip() for arg in args.only for i in arg.split(",") if i.strip()})
    sys.exit(0 if check() else 1)


if __name__ == "__main__":
    main()
