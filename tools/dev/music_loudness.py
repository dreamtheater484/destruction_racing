"""Measures the loudness of the game's music tracks for MusicPlayer's level matching (plan §12, the M8 final mix).

Each track's integrated level: 400 ms blocks of the mean square, gated as in ITU BS.1770 (blocks under -70 dB,
then those more than 10 dB under the mean, don't count). Plain RMS without the K-weighting, so roughly LUFS.
Writes assets/audio/music/loudness.json ({"file.ogg": dB}); MusicPlayer turns each into a gain to a common level.

Usage (needs numpy and soundfile, e.g. the Kokoro venv):
    .engine\\kokoro-venv\\Scripts\\python.exe -I tools\\dev\\music_loudness.py
"""
import json
from pathlib import Path

import numpy as np
import soundfile as sf

MUSIC = Path(__file__).resolve().parents[2] / "assets" / "audio" / "music"


def integrated_db(path: Path) -> float:
    x, sr = sf.read(str(path), always_2d=True)
    ms = (x ** 2).mean(axis=1)
    b = int(sr * 0.4)
    n = len(ms) // b
    blocks = ms[: n * b].reshape(n, b).mean(axis=1)
    blocks = blocks[10 * np.log10(np.maximum(blocks, 1e-12)) > -70]
    gate = 10 * np.log10(blocks.mean()) - 10
    return float(10 * np.log10(blocks[10 * np.log10(blocks) > gate].mean()))


def main():
    out = {}
    for f in sorted(MUSIC.glob("*.ogg")) + sorted(MUSIC.glob("*.mp3")):
        out[f.name] = round(integrated_db(f), 1)
        print(f"{f.name:34s} {out[f.name]:6.1f} dB")
    (MUSIC / "loudness.json").write_text(json.dumps(out, indent=2) + "\n", encoding="utf-8")
    print(f"-> {MUSIC / 'loudness.json'}")


if __name__ == "__main__":
    main()
