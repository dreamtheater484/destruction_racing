"""Tiles screenshots into one contact sheet, each labelled with its file name.

Example: python tools/dev/contact_sheet.py out/lab/sheet_kitsune.png out/lab/kitsune_*.png --cols 3
"""
import argparse
import glob
import os
import re

from PIL import Image, ImageDraw, ImageFont


def natural(path):
    return [int(t) if t.isdigit() else t for t in re.split(r"(\d+)", os.path.basename(path))]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("inputs", nargs="+")
    ap.add_argument("--cols", type=int, default=3)
    ap.add_argument("--width", type=int, default=640, help="tile width in pixels")
    a = ap.parse_args()
    files = sorted({f for p in a.inputs for f in glob.glob(p) if os.path.abspath(f) != os.path.abspath(a.out)}, key=natural)
    if not files:
        raise SystemExit("contact_sheet: no input images")
    first = Image.open(files[0])
    tw, th = a.width, round(a.width * first.height / first.width)
    rows = (len(files) + a.cols - 1) // a.cols
    sheet = Image.new("RGB", (tw * a.cols, th * rows), (16, 16, 16))
    try:
        font = ImageFont.truetype("arial.ttf", 18)
    except OSError:
        font = ImageFont.load_default()
    draw = ImageDraw.Draw(sheet)
    for n, f in enumerate(files):
        x, y = (n % a.cols) * tw, (n // a.cols) * th
        sheet.paste(Image.open(f).convert("RGB").resize((tw, th), Image.LANCZOS), (x, y))
        label = os.path.splitext(os.path.basename(f))[0]
        draw.rectangle((x, y, x + 12 + draw.textlength(label, font=font), y + 26), fill=(0, 0, 0))
        draw.text((x + 6, y + 3), label, fill=(240, 240, 240), font=font)
    sheet.save(a.out)
    print(f"contact_sheet: {len(files)} images -> {a.out}")


if __name__ == "__main__":
    main()
