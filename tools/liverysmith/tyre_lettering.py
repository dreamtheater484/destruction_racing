"""Tyre sidewall lettering (post-M8 car look): the game's fictional Dustbowl Tyres (see sponsors.py) round the
sidewall, as on a real tyre: the maker in big raised letters, the line and the size in small ones.

One strip covers half a turn of the sidewall (the shader repeats it twice); x runs round the tyre, the top row is
the outer edge of the lettering band (the letters' tops face the tread). R = the letters (antialiased),
G = a softened copy for the raised-letter bump. Set in Barlow Condensed (OFL, assets/fonts/barlow_condensed).
Run: python tools/liverysmith/tyre_lettering.py -> assets/textures/tyre_lettering.png
"""
from __future__ import annotations

import os
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
FONTS = os.path.join(ROOT, "assets", "fonts", "barlow_condensed")
W, H = 2048, 192
SS = 4                       # supersampling for the antialiased letters

# (text, weight, height as a share of the band, centre x as a share of the strip, centre y as a share of the band)
ITEMS = [
    ("DUSTBOWL", "ExtraBold", 0.66, 0.24, 0.52),
    ("STEEL BELTED RADIAL", "SemiBold", 0.28, 0.66, 0.34),
    ("185/70 R13  86S", "SemiBold", 0.28, 0.66, 0.72),
    ("TUBELESS", "SemiBold", 0.22, 0.92, 0.52),
]


def font(weight, size):
    return ImageFont.truetype(os.path.join(FONTS, f"BarlowCondensed-{weight}.ttf"), size)


def main(out):
    big = Image.new("L", (W * SS, H * SS), 0)
    draw = ImageDraw.Draw(big)
    for text, weight, height, cx, cy in ITEMS:
        f = font(weight, int(H * SS * height / 0.72))     # cap height is ~72% of the font size
        x0, y0, x1, y1 = draw.textbbox((0, 0), text, font=f)
        # Tracked out a little, as moulded lettering is.
        spacing = int(f.size * 0.08)
        widths = [draw.textbbox((0, 0), ch, font=f)[2] for ch in text]
        total = sum(widths) + spacing * (len(text) - 1)
        x = cx * W * SS - total / 2
        y = cy * H * SS - (y0 + y1) / 2
        for ch, w in zip(text, widths):
            draw.text((x, y), ch, font=f, fill=255)
            x += w + spacing
    letters = big.resize((W, H), Image.LANCZOS)
    soft = letters.filter(ImageFilter.GaussianBlur(2.2))
    img = Image.merge("RGB", (letters, soft, Image.new("L", (W, H), 0)))
    os.makedirs(os.path.dirname(out), exist_ok=True)
    img.save(out)
    print(f"tyre_lettering: {out} ({W}x{H})")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "assets", "textures", "tyre_lettering.png"))
