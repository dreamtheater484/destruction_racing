"""Crossroads Speedway (plan §7.3): one venue, two layouts on shared banked turn ends.

  oval      two straights tangent to the turn circles (lap ~895 m)
  figure8   the same circles joined by two legs that cross in the infield at grade: the blind crossover (lap ~984 m,
            the M1c grey-box figure-8, so its tuned crossover behaviour carries over)

Writes assets/speedway/crossroads.json (centrelines at 1 m, banking per point, start lines per direction, pit lanes,
walls and per-layout barriers, crossover, dressing anchors), the terrain heightfield (crossroads_height.bin, float32)
and surface map (crossroads_surface.bin, uint8), and a top-down preview (out/speedway/crossroads_preview.png).

Walls come from the pavement itself: pavement = union(oval ring, figure-8 road, both pit lanes). The boundary of
the union is the permanent wall (outer wall and the infield islands). Where one layout's own pavement ends inside the
union (the mouth of a road only the other layout uses), that edge is a barrier of that layout only.

Run: python tools/tracksmith/crossroads.py
"""
from __future__ import annotations

import json
import math
import os
import struct

import numpy as np
from PIL import Image, ImageDraw
from shapely import affinity
from shapely.geometry import LineString, MultiLineString, Point, Polygon
from shapely.geometry.polygon import orient
from shapely.ops import linemerge, unary_union

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_DIR = os.path.join(ROOT, "assets", "speedway")
PREVIEW = os.path.join(ROOT, "out", "speedway", "crossroads_preview.png")

R = 70.0                          # turn centreline radius
PHI = math.radians(38.0)          # half the crossing angle (76°)
D = R / math.sin(PHI)             # turn centres at x = ±D (113.7 m)
W = 13.0                          # road width: 3 cars abreast
HALF = W / 2
TL = D * math.cos(PHI)            # crossover to the tangent point along a leg (89.6 m)
C1 = np.array([-D, 0.0])          # turn 1 (west) and turn 2 (east) centres, (x, z)
C2 = np.array([D, 0.0])
BANK_MAX = math.radians(12.0)     # banking in the middle of each turn
BANK_FULL = math.radians(60.0)    # full banking within ±60° of the apex
BANK_FLAT = math.radians(85.0)    # flat beyond ±85° (the splits, at ±90°, are flat)
WALL_T = 0.8                      # wall thickness (collision and look)
PIT_W = 8.0                       # pit lane: a 5 m lane plus the boxes
PIT_GAP = 2.6                     # wall strip between the track edge and the pit lane
FIG8_START_FROM_CROSS = 85.0      # forward start line, along leg A from the crossover (as M1c)
CROSS_OPEN = 13.0                 # the islands are cut back to this radius round the crossover (no wedge tips)
TIP_ROUND = 1.0                   # every island corner is rounded (pit tapers, junction gores)

# Terrain raster (x, z), 0.5 m.
HF_CELL = 0.5
HF_X0, HF_X1 = -212.0, 212.0
HF_Z0, HF_Z1 = -124.0, 124.0
SURF_CELL = 0.25

SURFACE = {"grass": 0, "asphalt": 1, "pit": 2, "concrete": 3, "dirt": 4}


# ------------------------------------------------------------------------------------------- centrelines

def arc(centre, a0, a1, step=0.5):
    n = max(2, int(math.ceil(abs(a1 - a0) * R / step)))
    return [centre + R * np.array([math.cos(a0 + (a1 - a0) * i / n), math.sin(a0 + (a1 - a0) * i / n)]) for i in range(n)]


def line(p0, p1, step=0.5):
    p0, p1 = np.asarray(p0, float), np.asarray(p1, float)
    n = max(2, int(math.ceil(np.linalg.norm(p1 - p0) / step)))
    return [p0 + (p1 - p0) * i / n for i in range(n)]


def resample(points, spacing=1.0, closed=True):
    pts = np.asarray(points, float)
    if closed:
        pts = np.vstack([pts, pts[:1]])
    seg = np.linalg.norm(np.diff(pts, axis=0), axis=1)
    cum = np.concatenate([[0.0], np.cumsum(seg)])
    total = cum[-1]
    n = int(round(total / spacing))
    s = np.linspace(0.0, total, n, endpoint=not closed)
    x = np.interp(s, cum, pts[:, 0])
    z = np.interp(s, cum, pts[:, 1])
    return np.stack([x, z], axis=1), total


def oval_raw():
    """Forward: along the front straight (z = +R) towards +x, round turn 2, back along the back straight, turn 1."""
    pts = []
    pts += line((0.0, R), (D, R))
    pts += arc(C2, math.pi / 2, -math.pi / 2)
    pts += line((D, -R), (-D, -R))
    pts += arc(C1, 3 * math.pi / 2, math.pi / 2)
    pts += line((-D, R), (0.0, R))
    return pts


def figure8_raw():
    """Forward (as M1c): crossover -> leg A to turn 2 -> turn 2 -> leg B through the crossover -> turn 1 -> leg A."""
    da = np.array([math.cos(PHI), math.sin(PHI)])
    a2 = math.pi / 2 + PHI                   # 128°: where the legs meet turn 2 (around its centre)
    a1 = math.pi / 2 - PHI                   # 52°: where the legs meet turn 1
    pts = []
    pts += line((0.0, 0.0), da * TL)
    pts += arc(C2, a2, -a2)
    pts += line(C2 + R * np.array([math.cos(-a2), math.sin(-a2)]), C1 + R * np.array([math.cos(a1), math.sin(a1)]))
    pts += arc(C1, a1, 2 * math.pi - a1)
    pts += line(C1 + R * np.array([math.cos(-a1), math.sin(-a1)]), (0.0, 0.0))
    return pts


def rotate_to(points, index):
    return np.vstack([points[index:], points[:index]])


def nearest_index(points, p):
    return int(np.argmin(np.linalg.norm(points - np.asarray(p), axis=1)))


# ------------------------------------------------------------------------------------------- banking

def turn_frame(x, z):
    """(rho, alpha) around the nearer turn centre, alpha measured from the turn apex (0 at x = ±(D+R)); None in the
    middle section (between the turn centres)."""
    if abs(x) < D:
        return None
    c = C2 if x > 0 else C1
    dx, dz = x - c[0], z - c[1]
    rho = math.hypot(dx, dz)
    apex = 0.0 if x > 0 else math.pi
    a = math.atan2(dz, dx) - apex
    a = (a + math.pi) % (2 * math.pi) - math.pi
    return rho, a


def smoothstep(e0, e1, x):
    t = min(max((x - e0) / (e1 - e0), 0.0), 1.0)
    return t * t * (3 - 2 * t)


def bank_angle(alpha):
    return BANK_MAX * (1.0 - smoothstep(BANK_FULL, BANK_FLAT, abs(alpha)))


INNER = R - HALF                   # banking pivots on the inner edge of the oval turns
WALL_RHO = R + HALF + WALL_T       # back of the outer wall in the turns


def height(x, z):
    f = turn_frame(x, z)
    if f is None:
        return 0.0
    rho, a = f
    if rho <= INNER or abs(a) > math.pi / 2:
        return 0.0
    t = math.tan(bank_angle(a))
    if rho <= WALL_RHO:
        return (rho - INNER) * t
    # Behind the wall the embankment falls back to grade over 5 m.
    top = (WALL_RHO - INNER) * t
    return top * (1.0 - smoothstep(WALL_RHO, WALL_RHO + 5.0, rho))


def point_bank(x, z):
    """Banking under a centreline point (rad, positive = the outside of the turn is higher)."""
    f = turn_frame(x, z)
    if f is None or abs(f[1]) > math.pi / 2 or f[0] < INNER - 1.0:
        return 0.0
    return bank_angle(f[1])


# ------------------------------------------------------------------------------------------- pit lanes

def pit_oval():
    """Inside the front straight: the lane's centre 2.6 m + 4 m inside the track edge, |x| <= 40, tapering into the
    straight's inner lane at |x| = 62, clear of the figure-8 legs (forward order: entry at the west end)."""
    z_lane = R - HALF - PIT_GAP - PIT_W / 2
    z_track = R - 2.5
    pts = []
    for x in np.arange(-62.0, 62.01, 1.0):
        if abs(x) <= 40.0:
            z = z_lane
        else:
            u = smoothstep(40.0, 62.0, abs(x))
            z = z_lane + (z_track - z_lane) * u
        pts.append((x, z))
    boxes = [(-27.0 + 18.0 * i, z_lane) for i in range(4)]
    return np.asarray(pts), boxes, z_lane


def pit_figure8(fig8_points):
    """Inside the west loop, parallel to the figure-8 from the end of turn 1 to 37 m before the crossover, built
    from the forward centreline (the side towards the turn-1 centre is the loop interior)."""
    # Forward centreline indices: find the turn-1 arc angle 248° (start of the taper) and leg A at 35 m from the
    # crossover (end of the taper).
    angles = np.degrees(np.arctan2(fig8_points[:, 1] - C1[1], fig8_points[:, 0] - C1[0])) % 360
    on_c1 = np.abs(np.linalg.norm(fig8_points - C1, axis=1) - R) < 0.6
    i0 = int(np.where(on_c1 & (np.abs(angles - 236.0) < 0.8))[0][0])
    da = np.array([math.cos(PHI), math.sin(PHI)])
    i1 = nearest_index(fig8_points, -da * 37.0)
    seg = fig8_points[i0:i1 + 1]
    off = HALF + PIT_GAP + PIT_W / 2
    n = len(seg)
    # Which side is the loop interior: the one towards the turn-1 centre (lateral + = right of travel, as TrackPath).
    m = n // 3
    t = seg[m + 1] - seg[m - 1]
    right = np.array([-t[1], t[0]]) / np.linalg.norm(t)
    side = 1.0 if np.linalg.norm(seg[m] + right - C1) < np.linalg.norm(seg[m] - C1) else -1.0
    pts = []
    for k, p in enumerate(seg):
        # Taper in over the first 26 m and out over the last 24 m.
        u_in = smoothstep(0.0, 26.0, float(k))
        u_out = smoothstep(0.0, 24.0, float(n - 1 - k))
        pts.append((p, side * (2.5 + (off - 2.5) * min(u_in, u_out))))
    lane = []
    for k, (p, o) in enumerate(pts):
        j = min(max(k, 1), n - 2)
        t = seg[j + 1] - seg[j - 1]
        t /= np.linalg.norm(t)
        right = np.array([-t[1], t[0]])
        lane.append(p + right * o)
    lane = np.asarray(lane)
    # Four boxes along the middle (full offset) part.
    full = [k for k, (p, o) in enumerate(pts) if abs(abs(o) - off) < 0.05]
    k0, k1 = full[0] + 6, full[-1] - 6
    boxes = [tuple(lane[int(k0 + (k1 - k0) * i / 3)]) for i in range(4)]
    return lane, boxes


# ------------------------------------------------------------------------------------------- geometry

def road_polygon(points, width, closed=True):
    ls = LineString(np.vstack([points, points[:1]]) if closed else points)
    return ls.buffer(width / 2, cap_style="flat" if not closed else "round", join_style="round", quad_segs=16)


def rings_of(poly):
    polys = [poly] if isinstance(poly, Polygon) else list(poly.geoms)
    out = []
    for p in polys:
        # Exterior clockwise and holes anticlockwise in maths terms (x right, z up) puts the pavement on the right
        # there, which is the LEFT of travel in Godot (its top view has +z towards the viewer, a mirror image).
        p = orient(p, sign=-1.0)
        out.append(np.asarray(p.exterior.coords))
        out += [np.asarray(i.coords) for i in p.interiors]
    return out


def lines_of(geom):
    if geom.is_empty:
        return []
    if isinstance(geom, LineString):
        geom = MultiLineString([geom])
    if hasattr(geom, "geoms"):
        merged = linemerge([g for g in geom.geoms if isinstance(g, LineString)])
        geoms = [merged] if isinstance(merged, LineString) else list(merged.geoms)
        return [np.asarray(g.coords) for g in geoms if g.length > 0.5]
    return []


def simplify_line(pts, spacing=2.0, closed=False):
    if len(pts) < 2:
        return pts
    ls = LineString(pts)
    n = max(2, int(math.ceil(ls.length / spacing)) + 1)
    out = [ls.interpolate(ls.length * i / (n - 1)).coords[0] for i in range(n)]
    if closed:
        out = out[:-1]
    return np.asarray(out)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    os.makedirs(os.path.dirname(PREVIEW), exist_ok=True)

    oval, oval_len = resample(oval_raw())
    fig8, fig8_len = resample(figure8_raw())
    # Figure-8: s = 0 at the M1c start line, 85 m from the crossover on leg A.
    da = np.array([math.cos(PHI), math.sin(PHI)])
    fig8 = rotate_to(fig8, nearest_index(fig8, da * FIG8_START_FROM_CROSS))

    pit_o, boxes_o, z_lane = pit_oval()
    pit_f, boxes_f = pit_figure8(fig8)

    oval_poly = road_polygon(oval, W)
    fig8_poly = road_polygon(fig8, W)
    pit_o_poly = LineString(pit_o).buffer(PIT_W / 2, cap_style="flat", join_style="round")
    pit_f_poly = LineString(pit_f).buffer(PIT_W / 2, cap_style="flat", join_style="round")
    # The taper ends lie inside the track; trim the pit polygons to clean shapes by merging with the road.
    p_oval = unary_union([oval_poly, pit_o_poly])
    p_fig8 = unary_union([fig8_poly, pit_f_poly])
    union = unary_union([p_oval, p_fig8]).buffer(0.01).buffer(-0.01)
    # Cut the islands' wedge tips back round the crossover, and round every island corner: a car drifting into
    # the opening glances off instead of hitting a blunt point (the M1c walls flared out for the same reason).
    union = unary_union([union, Point(0.0, 0.0).buffer(CROSS_OPEN, quad_segs=24)])
    islands = [Polygon(h) for h in union.interiors]
    islands = [i.buffer(-TIP_ROUND, join_style="round").buffer(TIP_ROUND, join_style="round", quad_segs=8) for i in islands]
    islands = [i for i in islands if not i.is_empty]
    union = Polygon(union.exterior, [np.asarray(g.exterior.coords) for i in islands for g in ([i] if isinstance(i, Polygon) else i.geoms)])
    # The layouts' own pavement grows with it: each piece of new pavement (the crossover opening, rounded tips)
    # belongs to every layout whose road it touches.
    extra = union.difference(unary_union([p_oval, p_fig8]))
    pieces = [extra] if isinstance(extra, Polygon) else list(getattr(extra, "geoms", []))
    grown = {"oval": [p_oval], "figure8": [p_fig8]}
    for piece in pieces:
        if piece.area < 1e-3:
            continue
        for name, base in (("oval", p_oval), ("figure8", p_fig8)):
            if piece.buffer(0.05).intersects(base):
                grown[name].append(piece.buffer(0.02))
    p_oval = unary_union(grown["oval"]).intersection(union).buffer(0)
    p_fig8 = unary_union(grown["figure8"]).intersection(union).buffer(0)

    assert isinstance(union, Polygon), "the pavement must be one connected piece"
    permanent = [simplify_line(r, 2.0) for r in rings_of(union)]      # [0] = the outer wall, then the islands
    inside = union.buffer(-0.35)
    barriers = {}
    for name, pl in (("oval", p_oval), ("figure8", p_fig8)):
        segs = []
        for ring in rings_of(pl):
            cut = LineString(ring).intersection(inside)
            segs += [simplify_line(s, 1.0) for s in lines_of(cut)]
        barriers[name] = segs

    # Crossover: the four island corners nearest the centre (billboards make it blind).
    holes = [Polygon(r) for r in rings_of(union)[1:]]
    corners = []
    for h in holes:
        pts = np.asarray(h.exterior.coords)
        d = np.linalg.norm(pts, axis=1)
        if d.min() < 25.0:
            corners.append(tuple(pts[int(np.argmin(d))]))

    # Start lines per direction (s on the forward line; the reverse line runs backwards from the same points).
    # Oval: forward at x = 0 on the front straight (grid behind it, west); reverse at the same line (grid east).
    oval_start = 0.0
    # Figure-8 reverse: the same grid rows, facing the other way: the line at 25 m from the crossover on leg A.
    fig8_rev_start = float(nearest_index(fig8, da * 25.0)) * fig8_len / len(fig8)

    def banking(points):
        return [round(point_bank(x, z), 5) for x, z in points]

    data = {
        "version": 1,
        "name": "Crossroads Speedway",
        "road_width": W,
        "turn_radius": R,
        "turn_centres": [[-D, 0.0], [D, 0.0]],
        "bank_max_deg": math.degrees(BANK_MAX),
        "layouts": {
            "oval": {
                "points": [[round(x, 3), round(z, 3)] for x, z in oval],
                "bank": banking(oval),
                "length": round(oval_len, 2),
                "start_forward": oval_start,
                "start_reverse": oval_start,
                "laps": 8,
                "crossover": None,
                "pit": {"points": [[round(x, 3), round(z, 3)] for x, z in pit_o], "boxes": [list(b) for b in boxes_o]},
            },
            "figure8": {
                "points": [[round(x, 3), round(z, 3)] for x, z in fig8],
                "bank": banking(fig8),
                "length": round(fig8_len, 2),
                "start_forward": 0.0,
                "start_reverse": round(fig8_rev_start, 2),
                "laps": 6,
                "crossover": {"point": [0.0, 0.0], "radius": round(W * 1.1, 2)},
                "pit": {"points": [[round(x, 3), round(z, 3)] for x, z in pit_f], "boxes": [list(map(float, b)) for b in boxes_f]},
            },
        },
        # Every wall polyline runs with the pavement on its left (the wall stands to the right of it).
        "walls": {
            "outer": [[round(x, 3), round(z, 3)] for x, z in permanent[0]],
            "islands": [[[round(x, 3), round(z, 3)] for x, z in w] for w in permanent[1:]],
            "oval": [[[round(x, 3), round(z, 3)] for x, z in w] for w in barriers["oval"]],
            "figure8": [[[round(x, 3), round(z, 3)] for x, z in w] for w in barriers["figure8"]],
        },
        "wall_thickness": WALL_T,
        "crossover_corners": [[round(x, 3), round(z, 3)] for x, z in corners],
    }

    # Terrain heightfield and surface map.
    nx = int(round((HF_X1 - HF_X0) / HF_CELL)) + 1
    nz = int(round((HF_Z1 - HF_Z0) / HF_CELL)) + 1
    xs = HF_X0 + np.arange(nx) * HF_CELL
    zs = HF_Z0 + np.arange(nz) * HF_CELL
    hf = np.zeros((nz, nx), np.float32)
    for j, z in enumerate(zs):
        for i, x in enumerate(xs):
            if abs(x) >= D - 1.0:
                hf[j, i] = height(x, z)
    with open(os.path.join(OUT_DIR, "crossroads_height.bin"), "wb") as f:
        f.write(hf.astype("<f4").tobytes())
    data["heightfield"] = {"file": "res://assets/speedway/crossroads_height.bin", "x0": HF_X0, "z0": HF_Z0,
                           "cell": HF_CELL, "nx": nx, "nz": nz}

    snx = int(round((HF_X1 - HF_X0) / SURF_CELL))
    snz = int(round((HF_Z1 - HF_Z0) / SURF_CELL))
    img = Image.new("L", (snx, snz), SURFACE["grass"])
    draw = ImageDraw.Draw(img)

    def to_px(pts):
        return [((x - HF_X0) / SURF_CELL, (z - HF_Z0) / SURF_CELL) for x, z in pts]

    def fill(poly, value):
        polys = [poly] if isinstance(poly, Polygon) else list(poly.geoms)
        for p in polys:
            draw.polygon(to_px(np.asarray(p.exterior.coords)), fill=value)
            for h in p.interiors:
                draw.polygon(to_px(np.asarray(h.coords)), fill=SURFACE["grass"])
    fill(union, SURFACE["asphalt"])
    fill(unary_union([pit_o_poly, pit_f_poly]).difference(unary_union([oval_poly, fig8_poly])), SURFACE["pit"])
    surf = np.asarray(img, np.uint8)
    with open(os.path.join(OUT_DIR, "crossroads_surface.bin"), "wb") as f:
        f.write(surf.tobytes())
    data["surface"] = {"file": "res://assets/speedway/crossroads_surface.bin", "x0": HF_X0, "z0": HF_Z0,
                       "cell": SURF_CELL, "nx": snx, "nz": snz, "codes": SURFACE}

    with open(os.path.join(OUT_DIR, "crossroads.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, separators=(",", ":"))

    preview(data, union, hf)
    print(f"[tracksmith] oval {oval_len:.1f} m, figure-8 {fig8_len:.1f} m; walls {len(permanent)} permanent, "
          f"{len(barriers['oval'])} oval barriers, {len(barriers['figure8'])} figure-8 barriers; "
          f"crossover corners {len(corners)}; heightfield {nx}x{nz}, max {hf.max():.2f} m")


def preview(data, union, hf):
    scale = 4.0
    w = int((HF_X1 - HF_X0) * scale)
    h = int((HF_Z1 - HF_Z0) * scale)
    img = Image.new("RGB", (w, h), (52, 92, 44))
    d = ImageDraw.Draw(img)

    def px(pts):
        return [((x - HF_X0) * scale, (z - HF_Z0) * scale) for x, z in pts]

    polys = [union] if isinstance(union, Polygon) else list(union.geoms)
    for p in polys:
        d.polygon(px(np.asarray(p.exterior.coords)), fill=(84, 84, 88))
        for hole in p.interiors:
            d.polygon(px(np.asarray(hole.coords)), fill=(64, 110, 52))
    # Banking shade.
    ys, xs = np.nonzero(hf > 0.05)
    for j, i in zip(ys[::7], xs[::7]):
        x = HF_X0 + i * HF_CELL
        z = HF_Z0 + j * HF_CELL
        v = int(min(255, 90 + hf[j, i] * 40))
        d.point(((x - HF_X0) * scale, (z - HF_Z0) * scale), fill=(v, v, 110))
    for wl in [data["walls"]["outer"]] + data["walls"]["islands"]:
        d.line(px(wl), fill=(235, 235, 230), width=3)
    for wl in data["walls"]["oval"]:
        d.line(px(wl), fill=(230, 60, 50), width=5)
    for wl in data["walls"]["figure8"]:
        d.line(px(wl), fill=(60, 140, 240), width=5)
    for name, col in (("oval", (250, 210, 60)), ("figure8", (80, 230, 230))):
        lay = data["layouts"][name]
        pts = lay["points"]
        d.line(px(pts + pts[:1]), fill=col, width=1)
        d.line(px(lay["pit"]["points"]), fill=(250, 140, 40), width=2)
        for b in lay["pit"]["boxes"]:
            x, z = b
            d.ellipse([(x - HF_X0) * scale - 6, (z - HF_Z0) * scale - 6, (x - HF_X0) * scale + 6, (z - HF_Z0) * scale + 6], outline=(250, 140, 40), width=2)
        s0 = pts[0]
        d.ellipse([(s0[0] - HF_X0) * scale - 8, (s0[1] - HF_Z0) * scale - 8, (s0[0] - HF_X0) * scale + 8, (s0[1] - HF_Z0) * scale + 8], fill=col)
    for c in data["crossover_corners"]:
        d.rectangle([(c[0] - HF_X0) * scale - 6, (c[1] - HF_Z0) * scale - 6, (c[0] - HF_X0) * scale + 6, (c[1] - HF_Z0) * scale + 6], fill=(250, 60, 220))
    img.save(PREVIEW)


if __name__ == "__main__":
    main()
