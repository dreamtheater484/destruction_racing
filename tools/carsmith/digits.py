"""Race-number digits: our own bold block face (no font licence needed), baked as a signed-distance atlas.

Each digit is a set of thick strokes (polylines with round joins) on a 1 x 1.5 cell; the atlas stores the
signed distance (0.5 = edge) so the paint shader can draw crisp, antialiased numbers at any size and add an
outline. python -m carsmith.digits <out.png>   (run from tools/)
"""
from __future__ import annotations

import sys

import numpy as np

W = 0.30   # stroke width (cell units)

# Strokes per digit: lists of polylines in a 1 x 1.5 box (x right, y up). Straight-edged, slightly rounded.
DIGITS = {
    "0": [[(0.15, 0.15), (0.85, 0.15), (0.85, 1.35), (0.15, 1.35), (0.15, 0.15)]],
    "1": [[(0.28, 1.12), (0.58, 1.35), (0.58, 0.15)]],
    "2": [[(0.15, 1.35), (0.85, 1.35), (0.85, 0.8), (0.15, 0.6), (0.15, 0.15), (0.85, 0.15)]],
    "3": [[(0.15, 1.35), (0.85, 1.35), (0.85, 0.15), (0.15, 0.15)], [(0.4, 0.75), (0.85, 0.75)]],
    "4": [[(0.7, 0.15), (0.7, 1.35), (0.15, 0.55), (0.9, 0.55)]],
    "5": [[(0.85, 1.35), (0.15, 1.35), (0.15, 0.8), (0.85, 0.8), (0.85, 0.15), (0.15, 0.15)]],
    "6": [[(0.85, 1.35), (0.15, 1.35), (0.15, 0.15), (0.85, 0.15), (0.85, 0.75), (0.15, 0.75)]],
    "7": [[(0.15, 1.35), (0.85, 1.35), (0.4, 0.15)]],
    "8": [[(0.15, 0.15), (0.85, 0.15), (0.85, 1.35), (0.15, 1.35), (0.15, 0.15)], [(0.15, 0.78), (0.85, 0.78)]],
    "9": [[(0.85, 0.75), (0.15, 0.75), (0.15, 1.35), (0.85, 1.35), (0.85, 0.15), (0.15, 0.15)]],
}


def seg_dist(px, py, ax, ay, bx, by):
    ex, ey = bx - ax, by - ay
    t = np.clip(((px - ax) * ex + (py - ay) * ey) / (ex * ex + ey * ey), 0, 1)
    dx, dy = px - (ax + t * ex), py - (ay + t * ey)
    return np.sqrt(dx * dx + dy * dy)


def digit_sdf(ch, px, py):
    d = np.full(px.shape, np.inf)
    for line in DIGITS[ch]:
        for (ax, ay), (bx, by) in zip(line[:-1], line[1:]):
            d = np.minimum(d, seg_dist(px, py, ax, ay, bx, by))
    # Squarer ends than a round pen: blend toward a box-ish distance for a blocky look.
    return d - W / 2


def atlas(cell_w=96, cell_h=144, spread=0.25):
    """10 cells side by side; value = 0.5 - sdf / (2 * spread), clamped (0.5 on the edge)."""
    img = np.zeros((cell_h, cell_w * 10), np.float32)
    ys, xs = np.mgrid[0:cell_h, 0:cell_w]
    px = (xs + 0.5) / cell_w * 1.0
    py = (1.0 - (ys + 0.5) / cell_h) * 1.5
    for i in range(10):
        d = digit_sdf(str(i), px, py)
        img[:, i * cell_w:(i + 1) * cell_w] = np.clip(0.5 - d / (2 * spread), 0, 1)
    return img


if __name__ == "__main__":
    from PIL import Image
    out = sys.argv[1]
    a = atlas()
    Image.fromarray((a * 255).astype(np.uint8), "L").save(out)
    print(f"wrote {out} {a.shape}")
