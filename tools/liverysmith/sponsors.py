"""Fictional sponsor decals for the liveries (plan §9.4): one RGBA atlas, 2 columns x 4 rows of 4:1 cells.

Every name and logo here is invented for the game (no real brands). The text is set in Barlow Condensed (OFL,
assets/fonts/barlow_condensed). Run: python tools/liverysmith/sponsors.py -> assets/textures/sponsors.png
The cell order is the sponsor index the game uses (Livery.Sponsor, see src/Vehicles/CarModel.cs).
"""
from __future__ import annotations

import os
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
FONTS = os.path.join(ROOT, "assets", "fonts", "barlow_condensed")
CELL_W, CELL_H = 1024, 256
COLS, ROWS = 2, 4

# (top line, bottom line, plate colour, text colour, accent colour, style)
SPONSORS = [
    ("KRAKEN", "MOTOR OIL", (14, 18, 22), (240, 200, 40), (200, 40, 30), "plate"),
    ("VOLTA", "SPARK PLUGS", (245, 245, 240), (20, 60, 160), (220, 40, 30), "plate"),
    ("DUSTBOWL", "TYRES", (20, 20, 20), (245, 245, 240), (245, 160, 20), "stripe"),
    ("HAMMERHEAD", "AUTO PARTS", (200, 30, 25), (250, 250, 245), (20, 20, 20), "plate"),
    ("FIZZ-O", "COLA", (245, 245, 240), (210, 30, 40), (30, 30, 30), "round"),
    ("IRONSIDE", "INSURANCE", (25, 50, 110), (245, 245, 240), (240, 190, 40), "plate"),
    ("RUSTY'S", "SCRAPYARD", (230, 140, 30), (25, 20, 15), (120, 50, 20), "stripe"),
    ("BIG JIM'S", "TOWING", (250, 215, 30), (20, 20, 20), (200, 30, 25), "plate"),
]


def font(weight, size):
    return ImageFont.truetype(os.path.join(FONTS, f"BarlowCondensed-{weight}.ttf"), size)


def fit(draw, text, weight, max_w, max_h):
    size = max_h
    while size > 8:
        f = font(weight, size)
        box = draw.textbbox((0, 0), text, font=f)
        if box[2] - box[0] <= max_w and box[3] - box[1] <= max_h:
            return f, box
        size -= 2
    f = font(weight, 8)
    return f, draw.textbbox((0, 0), text, font=f)


def cell(top, bottom, plate, ink, accent, style):
    img = Image.new("RGBA", (CELL_W, CELL_H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    m = 10
    if style == "round":
        d.rounded_rectangle([m, m, CELL_W - m, CELL_H - m], radius=(CELL_H - 2 * m) // 2, fill=plate + (255,),
                            outline=accent + (255,), width=12)
    else:
        d.rounded_rectangle([m, m, CELL_W - m, CELL_H - m], radius=22, fill=plate + (255,))
        d.rounded_rectangle([m + 10, m + 10, CELL_W - m - 10, CELL_H - m - 10], radius=16, outline=accent + (255,),
                            width=8)
    if style == "stripe":
        for k in range(3):
            x0 = 60 + k * 34
            d.polygon([(x0, CELL_H - 40), (x0 + 22, CELL_H - 40), (x0 + 72, 40), (x0 + 50, 40)], fill=accent + (255,))
    left = 200 if style == "stripe" else 70
    width = CELL_W - left - 70
    f1, b1 = fit(d, top, "ExtraBold", width, 128)
    f2, b2 = fit(d, bottom, "SemiBold", width, 62)
    h1, h2 = b1[3] - b1[1], b2[3] - b2[1]
    gap = 10
    y = (CELL_H - (h1 + gap + h2)) // 2
    x1 = left + (width - (b1[2] - b1[0])) // 2
    x2 = left + (width - (b2[2] - b2[0])) // 2
    d.text((x1 - b1[0], y - b1[1]), top, font=f1, fill=ink + (255,))
    d.text((x2 - b2[0], y + h1 + gap - b2[1]), bottom, font=f2, fill=ink + (255,))
    return img


def main(out):
    atlas = Image.new("RGBA", (CELL_W * COLS, CELL_H * ROWS), (0, 0, 0, 0))
    for i, s in enumerate(SPONSORS):
        atlas.paste(cell(*s), ((i % COLS) * CELL_W, (i // COLS) * CELL_H))
    atlas.save(out)
    print(f"{len(SPONSORS)} sponsors -> {out}")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "assets", "textures", "sponsors.png"))
