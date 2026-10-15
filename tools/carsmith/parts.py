"""Reusable detail parts as SDFs: bumpers that follow the body plan, mirrors, a simple interior, wheels.

Body-following parts are trimmed to the outside of the body, so they sit on it as shells without buried
geometry. Wheels are meshed once in local space (axle = X, outboard = +X) and placed four times.
"""
from __future__ import annotations

import numpy as np

from .build import Detail
from .sdf import (rounded_rect_2d, round_max, round_min, round_sub, sd_box, sd_capsule, smooth_min)


def _rot_x(y, z, cy, cz, ang):
    """Rotate (y, z) about the X axis through (cy, cz) by ang (radians)."""
    c, s = np.cos(ang), np.sin(ang)
    dy, dz = y - cy, z - cz
    return cy + c * dy + s * dz, cz - s * dy + c * dz


def band(name, body, P, z, half_height, proud, y_range, radius=0.022, end_radius=0.035, groove=True,
         x_range=None, material="bumper", voxel=0.004, target=18000):
    """A strip that follows the body plan at its own height: bumpers, side mouldings, lenses set in bumpers."""
    ya, yb = y_range

    def sdf(x, y, zz):
        e = P["plan"](x, y, zz) - proud
        d = round_max(e, np.abs(zz - z) - half_height, radius)
        d = round_max(d, np.maximum(ya - y, y - yb), end_radius)
        if x_range is not None:
            d = round_max(d, np.maximum(x_range[0] - np.abs(x), np.abs(x) - x_range[1]), end_radius)
        if groove:     # a rubbing strip channel along the face
            cut = np.maximum(-e - 0.006, np.abs(zz - z) - 0.013)
            d = round_sub(d, cut, 0.004)
        d = np.maximum(d, -e - (proud + 0.03))      # a ring around the plan outline, never a sheet across the car
        d = np.maximum(d, zz - (P["top"](x, y) - 0.004))   # and never above the body (sloping hoods)
        return np.maximum(d, -(body(x, y, zz) + 0.008))
    spec = body.spec
    wmax = max(p[1] for p in spec.half_width) + proud + 0.05
    ys = [p[1] for p in spec.front] + [p[1] for p in spec.rear]
    y0 = max(ya, min(ys) - proud) - 0.05
    y1 = min(yb, max(ys) + proud) + 0.05
    lo = np.array([-wmax, y0, z - half_height - 0.04])
    hi = np.array([wmax, y1, z + half_height + 0.04])
    return Detail(name, sdf, (lo, hi), material, voxel=voxel, target=target)


def bumper(name, body, P, z, half_height, proud, y_from, front, **kw):
    y_range = (y_from, 9.0) if front else (-9.0, y_from)
    return band(name, body, P, z, half_height, proud, y_range, **kw)


def wiper(name, a, b, lift=0.012, material="trim_black", normal=(0.0, 0.25, 1.0)):
    """A parked wiper lying along a-b (points on the glass): blade plus a thinner arm to the pivot at a.
    `normal` is the glass's outward direction (the default suits a windscreen)."""
    a = np.asarray(a, float)
    b = np.asarray(b, float)
    n = np.asarray(normal, float) / np.linalg.norm(normal)

    def sdf(x, y, z):
        blade = sd_capsule(x, y, z, a + n * lift + (b - a) * 0.08, b + n * lift, 0.0055)
        arm = sd_capsule(x, y, z, a + n * (lift + 0.008), b * 0.85 + a * 0.15 + n * (lift + 0.008), 0.0045)
        pivot = sd_capsule(x, y, z, a, a + n * (lift + 0.01), 0.011)
        return np.minimum(np.minimum(blade, arm), pivot)
    lo = np.minimum(a, b) - 0.04
    hi = np.maximum(a, b) + 0.04
    return Detail(name, sdf, (lo, hi), material, voxel=0.0025, target=1800)


def mirror(name, s, centre, stalk_from, material="trim_black"):
    cx, cy, cz = centre

    def sdf(x, y, z):
        xs = s * x
        # Housing: a rounded box whose nose (towards +Y) is pinched into a soft wedge.
        house = sd_box(xs, y, z, cx, cy, cz, 0.058, 0.034, 0.040, r=0.022)
        house = round_max(house, (y - cy) - 0.026 + 0.45 * np.abs(z - cz), 0.012)
        stalk = sd_capsule(xs, y, z, (stalk_from + 0.02, cy + 0.012, cz - 0.032), (cx - 0.03, cy, cz - 0.012), 0.011)
        base = sd_box(xs, y, z, stalk_from + 0.01, cy + 0.015, cz - 0.035, 0.022, 0.055, 0.03, r=0.012)
        return smooth_min(smooth_min(house, stalk, 0.012), base, 0.012)
    xs = sorted([s * (stalk_from - 0.04), s * (cx + 0.09)])
    lo = np.array([xs[0], cy - 0.09, cz - 0.09])
    hi = np.array([xs[1], cy + 0.10, cz + 0.07])
    return Detail(name, sdf, (lo, hi), material, voxel=0.003, target=2500)


def interior(name, body, P, dash_y, seat_y, belt, rhd=False, rear_bench=False, material="interior"):
    """Dash, two buckets, a wheel, console and parcel shelf - enough to read through tinted glass."""
    drv = 0.36 if rhd else -0.36
    y0, y1 = dash_y

    def seat(x, y, z, sx):
        cush = sd_box(x, y, z, sx, seat_y + 0.06, 0.34, 0.25, 0.25, 0.07, r=0.06)
        by, bz = _rot_x(y, z, seat_y - 0.20, 0.62, np.radians(-18))
        back = sd_box(x, by, bz, sx, seat_y - 0.20, 0.62, 0.24, 0.07, 0.27, r=0.06)
        head = sd_box(x, by, bz, sx, seat_y - 0.20, 0.97, 0.13, 0.05, 0.07, r=0.04)
        return smooth_min(smooth_min(cush, back, 0.04), head, 0.03)

    def sdf(x, y, z):
        dash = sd_box(x, y, z, 0.0, 0.5 * (y0 + y1), belt - 0.10, 0.72, 0.5 * (y1 - y0), 0.11, r=0.05)
        hood = sd_box(x, y, z, drv, y0 + 0.08, belt + 0.005, 0.20, 0.08, 0.04, r=0.03)   # instrument binnacle
        cons = sd_box(x, y, z, 0.0, y0 - 0.30, 0.36, 0.10, 0.36, 0.11, r=0.04)
        shelf = sd_box(x, y, z, 0.0, seat_y - 0.95, belt - 0.06, 0.70, 0.45, 0.025, r=0.01)
        # Steering wheel: a torus in a plane tilted back 25 degrees, on a column into the dash.
        wy, wz = _rot_x(y, z, y0 - 0.20, belt - 0.02, np.radians(25))
        q = np.sqrt((x - drv) ** 2 + (wz - (belt - 0.02)) ** 2) - 0.18
        rim = np.sqrt(q * q + (wy - (y0 - 0.20)) ** 2) - 0.014
        col = sd_capsule(x, y, z, (drv, y0 - 0.19, belt - 0.02), (drv, y0 + 0.10, belt - 0.12), 0.03)
        d = np.minimum(np.minimum(dash, hood), np.minimum(cons, shelf))
        d = np.minimum(d, np.minimum(seat(x, y, z, -0.36), seat(x, y, z, 0.36)))
        d = np.minimum(d, np.minimum(rim, col))
        if rear_bench:
            ry = seat_y - 0.85
            bench = sd_box(x, y, z, 0.0, ry + 0.06, 0.36, 0.62, 0.24, 0.07, r=0.06)
            by, bz = _rot_x(y, z, ry - 0.20, 0.62, np.radians(-14))
            back = sd_box(x, by, bz, 0.0, ry - 0.20, 0.62, 0.62, 0.07, 0.26, r=0.06)
            d = np.minimum(d, smooth_min(bench, back, 0.04))
        return np.maximum(d, body(x, y, z) + 0.012)
    hw = max(p[1] for p in body.spec.half_width)
    lo = np.array([-hw, seat_y - 1.45, 0.20])
    hi = np.array([hw, y1 + 0.05, belt + 0.45])
    return Detail(name, sdf, (lo, hi), material, voxel=0.01, target=12000)


def bezel(name, rects, P, material="chrome", width=0.008, proud=0.005):
    """Raised surrounds (headlamp bezels, lamp rims) around rectangles on the front face."""
    F = P["F"]

    def sdf(x, y, z):
        face = F(z)
        d = np.full(np.shape(x), np.inf)
        for r in rects:
            ax = np.abs(x) if r.mirrored else x
            d2 = rounded_rect_2d(ax, z, r.x, r.z, r.hx, r.hz, r.r)
            ring = np.abs(d2 - width) - width
            d = np.minimum(d, ring)
        return round_max(d, np.maximum(y - (face + proud), (face - 0.02) - y), 0.003)
    xs = [r.x + r.hx for r in rects]
    zs = [r.z - r.hz for r in rects] + [r.z + r.hz for r in rects]
    ys = F(np.array([min(zs), max(zs)]))
    lo = np.array([-max(xs) - 0.03, float(ys.min()) - 0.04, min(zs) - 0.03])
    hi = np.array([max(xs) + 0.03, float(ys.max()) + 0.03, max(zs) + 0.03])
    return Detail(name, sdf, (lo, hi), material, voxel=0.0025, target=8000)


def side_point(fn, y, z, x_hint=1.2):
    """x of the body side surface at (y, z), found from outside by bisection on the SDF."""
    lo, hi = 0.0, x_hint
    for _ in range(40):
        mid = 0.5 * (lo + hi)
        if fn(np.array([mid]), np.array([y]), np.array([z]))[0] < 0:
            lo = mid
        else:
            hi = mid
    return 0.5 * (lo + hi)


def handle(name, fn, y, z, s, material="chrome", length=0.12, height=0.026):
    """A pull handle sitting on the door skin (s = +1 right side, -1 left)."""
    x0 = side_point(fn, y, z)

    def sdf(x, yy, zz):
        xs = s * x
        grip = sd_box(xs, yy, zz, x0 + 0.006, y, z, 0.007, 0.5 * length, 0.5 * height, r=0.006)
        return np.maximum(grip, -(fn(x, yy, zz) + 0.004))
    xr = sorted([s * (x0 - 0.03), s * (x0 + 0.04)])
    lo = np.array([xr[0], y - length, z - height])
    hi = np.array([xr[1], y + length, z + height])
    return Detail(name, sdf, (lo, hi), material, voxel=0.002, target=1200)


def plate(name, P, z, front, half=(0.26, 0.055), offset=0.0, material="plate"):
    """A number plate on the nose/tail face, `offset` proud of it (on a bumper: the bumper's proud).
    The registration text is added in Godot (Label3D on the plate node)."""
    F = P["F"] if front else P["R"]
    face = float(F(np.array([z]))[0])
    sgn = 1.0 if front else -1.0
    cy = face + sgn * (offset + 0.004)

    def sdf(x, y, zz):
        return sd_box(x, y, zz, 0.0, cy, z, half[0], 0.0035, half[1], r=0.006)
    lo = np.array([-half[0] - 0.02, cy - 0.02, z - half[1] - 0.02])
    hi = np.array([half[0] + 0.02, cy + 0.02, z + half[1] + 0.02])
    return Detail(name, sdf, (lo, hi), material, voxel=0.003, target=400)


def grille(name, rect, P, style="vertical", material="chrome", pitch=0.028):
    """A grille insert for a recessed opening on the front face: a proud surround and bars set back inside."""
    F = P["F"]

    def sdf(x, y, z):
        face = F(z)
        d2 = rounded_rect_2d(x, z, 0.0, rect.z, rect.hx, rect.hz, rect.r)
        ring = np.abs(d2 - 0.003) - 0.008
        surround = round_max(ring, np.maximum(y - (face + 0.006), (face - 0.03) - y), 0.003)
        if style == "horizontal":
            bars = np.abs((z - rect.z + 0.5 * pitch) % pitch - 0.5 * pitch) - 0.004
        elif style == "eggcrate":
            bx = np.abs((x + 0.5 * pitch) % (pitch * 1.6) - 0.8 * pitch) - 0.003
            bz = np.abs((z - rect.z + 0.5 * pitch) % pitch - 0.5 * pitch) - 0.003
            bars = np.minimum(bx, bz)
        else:
            bars = np.abs((x + 0.5 * pitch) % pitch - 0.5 * pitch) - 0.0035
        inner = np.maximum(bars, d2 + 0.002)
        inner = round_max(inner, np.maximum(y - (face - 0.012), (face - 0.042) - y), 0.002)
        return np.minimum(surround, inner)
    ys = F(np.array([rect.z - rect.hz, rect.z + rect.hz]))
    lo = np.array([-rect.hx - 0.03, float(ys.min()) - 0.06, rect.z - rect.hz - 0.03])
    hi = np.array([rect.hx + 0.03, float(ys.max()) + 0.03, rect.z + rect.hz + 0.03])
    return Detail(name, sdf, (lo, hi), material, voxel=0.0025, target=14000)


def lamp_pods(base, rects, P, proud=0.09, round_lamp=True, lens_material="lens_clear", housing_material="trim_black"):
    """Fog or driving lamps standing in front of the face (in or under the bumper): one node per side,
    Light_<base>_R / _L, with a lens (first material, so the game treats it as a lamp) and a housing."""
    F = P["F"]
    out = []
    for sname, mirror in (("R", False), ("L", True)):
        node = f"{base}_{sname}"

        def lens_sdf(x, y, z):
            d = np.full(np.shape(x), np.inf)
            for r in rects:
                cy = float(F(np.array([r.z]))[0]) + proud
                if round_lamp:
                    q = np.sqrt((x - r.x) ** 2 + (z - r.z) ** 2) - (min(r.hx, r.hz) - 0.006)
                else:
                    q = rounded_rect_2d(x, z, r.x, r.z, r.hx - 0.006, r.hz - 0.006, max(r.r - 0.006, 0.002))
                d = np.minimum(d, round_max(q, np.abs(y - (cy + 0.004)) - 0.006, 0.003))
            return d

        def housing_sdf(x, y, z):
            d = np.full(np.shape(x), np.inf)
            for r in rects:
                face = float(F(np.array([r.z]))[0])
                cy = 0.5 * (face - 0.02 + face + proud)
                hy = 0.5 * (proud + 0.02)
                if round_lamp:
                    q = np.sqrt((x - r.x) ** 2 + (z - r.z) ** 2) - min(r.hx, r.hz)
                    q = round_max(q, np.abs(y - cy) - hy, 0.008)
                else:
                    q = sd_box(x, y, z, r.x, cy, r.z, r.hx, hy, r.hz, r=min(r.r, 0.012))
                # The open front holds the lens.
                cup = np.sqrt((x - r.x) ** 2 + (z - r.z) ** 2) - (min(r.hx, r.hz) - 0.008) if round_lamp else \
                    rounded_rect_2d(x, z, r.x, r.z, r.hx - 0.008, r.hz - 0.008, 0.004)
                q = round_sub(q, np.maximum(cup, (cy + hy - 0.004) - y), 0.002)
                d = np.minimum(d, q)
            return d
        xs = [r.x for r in rects]
        zs = [r.z for r in rects]
        ext = max(max(r.hx, r.hz) for r in rects) + 0.03
        face = F(np.array(zs))
        lo = np.array([min(xs) - ext, float(face.min()) - 0.06, min(zs) - ext])
        hi = np.array([max(xs) + ext, float(face.max()) + proud + 0.03, max(zs) + ext])
        out.append(Detail(node, lens_sdf, (lo, hi), lens_material, voxel=0.0025,
                          placements=[("", (0.0, 0.0, 0.0), mirror)], target=1500))
        out.append(Detail(f"{base}_Housing_{sname}", housing_sdf, (lo, hi), housing_material, voxel=0.0025,
                          placements=[(node, (0.0, 0.0, 0.0), mirror)], target=2500))
    return out


def wing(name, body, y, z, deck_z, half_span, stalk_x, chord=0.17, thickness=0.026, material="paint", parent="Trunk"):
    """A rear wing on two stalks standing on the boot lid; parented to the lid so it comes off with it."""
    def sdf(x, yy, zz):
        # A cambered blade: a rounded slab whose trailing edge kicks up a little.
        lift = 0.10 * np.clip((y - yy) / chord + 0.5, 0, 1)
        blade = sd_box(x, yy, zz - lift * 0.15, 0.0, y, z, half_span, 0.5 * chord, 0.5 * thickness, r=0.011)
        plates = sd_box(np.abs(x), yy, zz, half_span - 0.006, y - 0.005, z + 0.005, 0.007, 0.5 * chord + 0.012,
                        0.5 * thickness + 0.035, r=0.006)
        h = 0.5 * (z - deck_z + 0.04)
        stalks = sd_box(np.abs(x), yy, zz, stalk_x, y + 0.01, deck_z - 0.04 + h, 0.016, 0.05, h, r=0.008)
        d = np.minimum(np.minimum(blade, plates), stalks)
        return np.maximum(d, -(body(x, yy, zz) + 0.002))
    lo = np.array([-half_span - 0.03, y - chord, deck_z - 0.08])
    hi = np.array([half_span + 0.03, y + chord, z + 0.08])
    return Detail(name, sdf, (lo, hi), material, voxel=0.004, placements=[(parent, (0.0, 0.0, 0.0), False)],
                  target=5000)


def lip(name, body, y, z, half_span, height=0.04, depth=0.10, material="paint", parent="Trunk"):
    """A ducktail lip along the boot lid's rear edge."""
    def sdf(x, yy, zz):
        d = sd_box(x, yy, zz, 0.0, y, z + 0.5 * height - 0.01, half_span, 0.5 * depth, 0.5 * height, r=0.014)
        # Sloped front so it rises out of the lid.
        d = round_max(d, (z + 0.01) + (yy - (y - 0.5 * depth)) * 0.6 - zz - 0.02, 0.01)
        return np.maximum(d, -(body(x, yy, zz) + 0.002))
    lo = np.array([-half_span - 0.03, y - depth, z - 0.06])
    hi = np.array([half_span + 0.03, y + depth, z + height + 0.04])
    return Detail(name, sdf, (lo, hi), material, voxel=0.004, placements=[(parent, (0.0, 0.0, 0.0), False)],
                  target=3000)


def roof_flap(name, body, y, z, half_span, chord=0.17, thickness=0.022, tilt_deg=9.0, material="paint",
              parent="Trunk"):
    """A hatch spoiler: a flap from the roof's rear edge out over the top of the rear glass."""
    ang = np.radians(tilt_deg)

    def sdf(x, yy, zz):
        cy = y - 0.5 * chord
        ry, rz = _rot_x(yy, zz, y, z, -ang)
        d = sd_box(x, ry, rz, 0.0, cy, z, half_span, 0.5 * chord, 0.5 * thickness, r=0.010)
        d = round_max(d, np.abs(x) - half_span + 0.02 * np.clip((y - yy) / chord, 0, 1), 0.01)
        # a root that fills down to the roof under the flap's leading edge (the body trims it)
        root = sd_box(x, yy, zz, 0.0, y - 0.02, z - 0.06, half_span - 0.01, 0.05, 0.06, r=0.01)
        d = smooth_min(d, root, 0.015)
        return np.maximum(d, -(body(x, yy, zz) + 0.002))
    lo = np.array([-half_span - 0.03, y - chord - 0.06, z - 0.14])
    hi = np.array([half_span + 0.03, y + 0.06, z + 0.06])
    return Detail(name, sdf, (lo, hi), material, voxel=0.004, placements=[(parent, (0.0, 0.0, 0.0), False)],
                  target=3500)


def roof_rack(name, body, x, y0, y1, z, material="chrome"):
    """Two rails along the roof on three feet each (a wagon's roof rack)."""
    def sdf(xx, yy, zz):
        ax = np.abs(xx)
        rail = sd_capsule(ax, yy, zz, (x, y1, z), (x, y0, z), 0.011)
        d = rail
        for fy in (y0 + 0.03, 0.5 * (y0 + y1), y1 - 0.03):
            foot = sd_box(ax, yy, zz, x, fy, z - 0.04, 0.016, 0.03, 0.045, r=0.008)
            d = smooth_min(d, foot, 0.01)
        return np.maximum(d, -(body(xx, yy, zz) + 0.002))
    lo = np.array([-x - 0.06, min(y0, y1) - 0.06, z - 0.12])
    hi = np.array([x + 0.06, max(y0, y1) + 0.06, z + 0.04])
    return Detail(name, sdf, (lo, hi), material, voxel=0.003, target=3000)


def exhaust(name, body, xs, y_tip, z, radius=0.025, length=0.42, material="exhaust"):
    """Tail pipes under the rear: open tubes ending just behind the tail (one node, so they come off as one)."""
    def sdf(x, y, zz):
        d = np.full(np.shape(x), np.inf)
        hole = np.full(np.shape(x), np.inf)
        for x0 in xs:
            d = np.minimum(d, sd_capsule(x, y, zz, (x0, y_tip + length, z + 0.03), (x0, y_tip, z), radius))
            hole = np.minimum(hole, sd_capsule(x, y, zz, (x0, y_tip + 0.06, z), (x0, y_tip - 0.06, z), radius - 0.005))
        d = round_sub(d, hole, 0.002)
        return np.maximum(d, -(body(x, y, zz) + 0.003))
    lo = np.array([min(xs) - 0.06, y_tip - 0.05, z - 0.06])
    hi = np.array([max(xs) + 0.06, y_tip + length + 0.05, z + 0.08])
    return Detail(name, sdf, (lo, hi), material, voxel=0.0025, target=2500)


# ----------------------------------------------------------------------------- wheels

def _tyre_sdf(R, width, rim_r, sidewall_bulge=0.012, shoulder=0.036):
    hh = 0.5 * (R - rim_r)
    rmid = 0.5 * (R + rim_r)

    def sdf(x, y, z):
        r = np.sqrt(y * y + z * z)
        t = np.clip((r - rmid) / hh, -1, 1)
        hw = 0.5 * width + sidewall_bulge * (1 - t * t)
        d = round_max(np.abs(x) - hw, np.abs(r - rmid) - hh, shoulder)
        # Three circumferential grooves.
        for gx in (-0.042, 0.0, 0.042):
            g = np.maximum(np.abs(x - gx) - 0.0045, (R - 0.009) - r)
            d = round_sub(d, g, 0.0012)
        # Lateral shoulder grooves (block pattern seen from the side).
        n = 64
        phi = np.arctan2(z, y)
        sect = 2 * np.pi / n
        a = (phi + 0.5 * sect) % sect - 0.5 * sect
        s = a * r + 0.004 * (np.abs(x) - 0.06) / 0.03 * np.sign(x)
        lat = np.maximum(np.maximum(np.abs(s) - 0.0032, (R - 0.008) - r), 0.064 - np.abs(x))
        d = round_sub(d, lat, 0.001)
        # Sidewall rib near the rim (the bead line).
        rib = np.sqrt((np.abs(x) - (0.5 * width + 0.004)) ** 2 + (r - (rim_r + 0.022)) ** 2) - 0.004
        return smooth_min(d, rib, 0.003)
    return sdf


def _rim_sdf(rim_r, width, style="turbine", spokes=8):
    half = 0.5 * width - 0.012        # rim flange position (inboard of tyre sidewalls)

    def sdf(x, y, z):
        r = np.sqrt(y * y + z * z)
        barrel = round_max(np.abs(r - (rim_r - 0.006)) - 0.005, np.abs(x) - half, 0.003)
        lip = np.sqrt((x - half) ** 2 + (r - (rim_r + 0.004)) ** 2) - 0.007
        lip_in = np.sqrt((x + half) ** 2 + (r - (rim_r + 0.002)) ** 2) - 0.006
        # Face: a dished disc set back from the lip.
        face_x = half - 0.030 - 0.018 * (1 - np.clip(r / rim_r, 0, 1))
        face = round_max(np.abs(x - face_x) - 0.008, r - (rim_r - 0.004), 0.004)
        phi = np.arctan2(z, y)
        sect = 2 * np.pi / spokes
        a = (phi + 0.5 * sect) % sect - 0.5 * sect
        u, v = r * np.cos(a), r * np.sin(a)
        if style == "turbine":
            v = v - 0.45 * (u - 0.10)
            win = rounded_rect_2d(u, v, 0.103, 0.0, 0.036, 0.0125, 0.010)
        elif style == "slot":
            win = rounded_rect_2d(u, v, 0.105, 0.0, 0.034, 0.016, 0.012)
        elif style == "mesh":    # a cross-spoke "mesh" alloy: many small windows on two rings
            win = np.minimum(rounded_rect_2d(u, v, 0.085, 0.0, 0.016, 0.0085, 0.006),
                             rounded_rect_2d(u, v, 0.128, 0.0, 0.020, 0.0105, 0.006))
        else:  # "dish" (and the plain rim under a wheel cover): round holes
            win = np.sqrt((u - 0.105) ** 2 + v * v) - 0.022
        face = round_sub(face, np.maximum(win, -(r - 0.055)), 0.003)
        hub = round_max(r - 0.058, np.abs(x - (face_x + 0.006)) - 0.014, 0.008)
        d = np.minimum(np.minimum(barrel, lip), np.minimum(lip_in, face))
        d = smooth_min(d, hub, 0.006)
        # Four lug nuts.
        ln = 4
        sl = 2 * np.pi / ln
        b = (phi + 0.25 * np.pi + 0.5 * sl) % sl - 0.5 * sl
        nu, nv = r * np.cos(b) - 0.050, r * np.sin(b)
        nut = round_max(np.sqrt(nu * nu + nv * nv) - 0.0085, np.abs(x - (face_x + 0.022)) - 0.007, 0.002)
        return np.minimum(d, nut)
    return sdf


def _cover_sdf(width, rim_r, ribs=12):
    """A full wheel cover (US style): a shallow chrome dish over the whole rim face with radial ribs and a
    centre cap. It is the wheel's Hubcap node, so it can pop off."""
    half = 0.5 * width - 0.012

    def sdf(x, y, z):
        r = np.sqrt(y * y + z * z)
        t = np.clip(r / rim_r, 0, 1)
        face_x = half + 0.004 - 0.026 * t * t
        disc = round_max(x - face_x, r - (rim_r + 0.004), 0.006)
        disc = round_max(disc, (half - 0.036) - x, 0.004)
        phi = np.arctan2(z, y)
        sect = 2 * np.pi / ribs
        a = (phi + 0.5 * sect) % sect - 0.5 * sect
        rib = np.maximum(np.maximum(np.abs(a * r) - 0.006, 0.055 - r), r - (rim_r - 0.022))
        rib = np.maximum(rib, (face_x - 0.004) - x)
        disc = round_sub(disc, rib, 0.002)
        cap = round_max(r - 0.048, x - (face_x + 0.010), 0.010)
        return smooth_min(disc, np.maximum(cap, (half - 0.02) - x), 0.004)
    return sdf


def _cap_sdf(width, rim_r):
    half = 0.5 * width - 0.012
    face_x = half - 0.030 - 0.018

    def sdf(x, y, z):
        r = np.sqrt(y * y + z * z)
        cap = round_max(r - 0.034, np.abs(x - (face_x + 0.026)) - 0.008, 0.006)
        return cap
    return sdf


def wheels(spec_wheels, style="turbine", rim_inch=13, tyre_target=14000, rim_target=12000):
    """Tyre, Rim and Hubcap meshed once and placed at all four corners (left side mirrored)."""
    wh = spec_wheels[0]
    rim_r = rim_inch * 0.0254 / 2
    places = []
    for w, axle in zip(spec_wheels, ("F", "R")):
        for side, sx in (("R", +1), ("L", -1)):
            places.append((f"Wheel_{axle}{side}", (sx * w.track / 2, w.y, w.radius), sx < 0))
    R, W = wh.radius, wh.width
    e = 0.5 * W + 0.03
    tyre = Detail("Tyre", _tyre_sdf(R, W, rim_r), (np.array([-e, -R - 0.01, -R - 0.01]), np.array([e, R + 0.01, R + 0.01])),
                  "tyre", voxel=0.0025, placements=places, target=tyre_target)
    rim = Detail("Rim", _rim_sdf(rim_r, W, "dish" if style == "cover" else style), (np.array([-e, -rim_r - 0.02, -rim_r - 0.02]),
                                                    np.array([e, rim_r + 0.02, rim_r + 0.02])),
                 "rim", voxel=0.002, placements=places, target=rim_target)
    if style == "cover":
        cap = Detail("Hubcap", _cover_sdf(W, rim_r), (np.array([0.0, -rim_r - 0.02, -rim_r - 0.02]),
                                                      np.array([e, rim_r + 0.02, rim_r + 0.02])),
                     "hubcap", voxel=0.002, placements=places, target=9000)
    else:
        cap = Detail("Hubcap", _cap_sdf(W, rim_r), (np.array([-0.02, -0.05, -0.05]), np.array([e, 0.05, 0.05])),
                     "hubcap", voxel=0.002, placements=places, target=1500)
    return [tyre, rim, cap]
