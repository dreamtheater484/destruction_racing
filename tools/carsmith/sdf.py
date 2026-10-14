"""Signed-distance building blocks for car bodies (numpy, vectorised).

Conventions (Blender): X = right, Y = forward, Z = up, metres. Negative = inside.
The round boolean operators are the exact circular-fillet forms (hg_sdf), so two surfaces that meet at
right angles get a true fillet of radius r - the basis of crisp-but-rounded 80s car edges.
"""
from __future__ import annotations

import numpy as np


# ----------------------------------------------------------------------------- booleans

def round_max(a, b, r):
    """Intersection with a fillet of radius r (r = 0: hard edge)."""
    if np.isscalar(r) and r <= 0:
        return np.maximum(a, b)
    u = np.maximum(r + a, 0.0)
    v = np.maximum(r + b, 0.0)
    return np.minimum(-r, np.maximum(a, b)) + np.sqrt(u * u + v * v)


def round_min(a, b, r):
    """Union with a fillet (concave blend) of radius r."""
    if np.isscalar(r) and r <= 0:
        return np.minimum(a, b)
    u = np.maximum(r - a, 0.0)
    v = np.maximum(r - b, 0.0)
    return np.maximum(r, np.minimum(a, b)) - np.sqrt(u * u + v * v)


def round_sub(a, b, r):
    """a minus b, with a fillet of radius r along the cut edge."""
    return round_max(a, -b, r)


def smooth_min(a, b, k):
    """Polynomial smooth minimum (soft blend over k)."""
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0.0, 1.0)
    return b + (a - b) * h - k * h * (1.0 - h)


# ----------------------------------------------------------------------------- curves

class Pchip:
    """Monotone piecewise-cubic interpolation (no overshoot), clamped outside the knots."""

    def __init__(self, xs, ys):
        x = np.asarray(xs, dtype=np.float64)
        y = np.asarray(ys, dtype=np.float64)
        order = np.argsort(x)
        self.x, self.y = x[order], y[order]
        h = np.diff(self.x)
        delta = np.diff(self.y) / h
        n = len(self.x)
        d = np.zeros(n)
        if n == 2:
            d[:] = delta[0]
        else:
            for k in range(1, n - 1):
                if delta[k - 1] * delta[k] <= 0:
                    d[k] = 0.0
                else:
                    w1 = 2 * h[k] + h[k - 1]
                    w2 = h[k] + 2 * h[k - 1]
                    d[k] = (w1 + w2) / (w1 / delta[k - 1] + w2 / delta[k])
            d[0] = self._end(h[0], h[1], delta[0], delta[1])
            d[-1] = self._end(h[-1], h[-2], delta[-1], delta[-2])
        self.d = d

    @staticmethod
    def _end(h0, h1, d0, d1):
        v = ((2 * h0 + h1) * d0 - h0 * d1) / (h0 + h1)
        if np.sign(v) != np.sign(d0):
            return 0.0
        if np.sign(d0) != np.sign(d1) and abs(v) > abs(3 * d0):
            return 3 * d0
        return v

    def __call__(self, t):
        t = np.asarray(t, dtype=np.float64)
        tc = np.clip(t, self.x[0], self.x[-1])
        i = np.clip(np.searchsorted(self.x, tc) - 1, 0, len(self.x) - 2)
        x0, x1 = self.x[i], self.x[i + 1]
        h = x1 - x0
        s = (tc - x0) / h
        y0, y1 = self.y[i], self.y[i + 1]
        d0, d1 = self.d[i], self.d[i + 1]
        h00 = (2 * s ** 3 - 3 * s ** 2 + 1)
        h10 = (s ** 3 - 2 * s ** 2 + s)
        h01 = (-2 * s ** 3 + 3 * s ** 2)
        h11 = (s ** 3 - s ** 2)
        return h00 * y0 + h10 * h * d0 + h01 * y1 + h11 * h * d1


def curve(points):
    """Pchip from [(x, y), ...]."""
    xs, ys = zip(*points)
    return Pchip(xs, ys)


# ----------------------------------------------------------------------------- 2D shapes

def rounded_rect_2d(u, v, cu, cv, hu, hv, r):
    """2D rounded rectangle centred at (cu, cv) with half sizes (hu, hv) and corner radius r."""
    qu = np.abs(u - cu) - hu + r
    qv = np.abs(v - cv) - hv + r
    return np.sqrt(np.maximum(qu, 0) ** 2 + np.maximum(qv, 0) ** 2) + np.minimum(np.maximum(qu, qv), 0) - r


def polyline_distance_2d(u, v, pts, closed=True):
    """Unsigned distance from points (u, v) to a polyline (N x 2)."""
    p = np.asarray(pts, dtype=np.float64)
    a = p if closed else p[:-1]
    b = np.roll(p, -1, axis=0) if closed else p[1:]
    best = np.full(np.shape(u), np.inf)
    for (ax, ay), (bx, by) in zip(a, b):
        ex, ey = bx - ax, by - ay
        L2 = ex * ex + ey * ey
        t = np.clip(((u - ax) * ex + (v - ay) * ey) / max(L2, 1e-12), 0.0, 1.0)
        dx = u - (ax + t * ex)
        dy = v - (ay + t * ey)
        best = np.minimum(best, dx * dx + dy * dy)
    return np.sqrt(best)


def polygon_sdf_2d(u, v, pts):
    """Signed distance to a closed polygon (negative inside), even-odd rule."""
    p = np.asarray(pts, dtype=np.float64)
    d = polyline_distance_2d(u, v, p, closed=True)
    inside = np.zeros(np.shape(u), dtype=bool)
    q = np.roll(p, -1, axis=0)
    for (ax, ay), (bx, by) in zip(p, q):
        cond = ((ay > v) != (by > v))
        xint = (bx - ax) * (v - ay) / np.where(by - ay == 0, 1e-12, by - ay) + ax
        inside ^= cond & (u < xint)
    return np.where(inside, -d, d)


def fillet_polygon(corners, samples=8):
    """Polygon with per-corner fillets: corners = [(u, v, radius), ...] -> dense point list."""
    pts = []
    n = len(corners)
    for i in range(n):
        p0 = np.array(corners[i - 1][:2], float)
        p1 = np.array(corners[i][:2], float)
        p2 = np.array(corners[(i + 1) % n][:2], float)
        r = corners[i][2]
        if r <= 0:
            pts.append(p1)
            continue
        a = p0 - p1
        b = p2 - p1
        la, lb = np.linalg.norm(a), np.linalg.norm(b)
        a, b = a / la, b / lb
        cosang = np.clip(np.dot(a, b), -1, 1)
        ang = np.arccos(cosang)
        if ang < 1e-3 or ang > np.pi - 1e-3:
            pts.append(p1)
            continue
        t = r / np.tan(ang / 2)
        t = min(t, 0.45 * la, 0.45 * lb)
        rr = t * np.tan(ang / 2)
        s0 = p1 + a * t
        s1 = p1 + b * t
        bis = (a + b) / np.linalg.norm(a + b)
        c = p1 + bis * (rr / np.sin(ang / 2))
        a0 = np.arctan2(*(s0 - c)[::-1])
        a1 = np.arctan2(*(s1 - c)[::-1])
        da = (a1 - a0 + np.pi) % (2 * np.pi) - np.pi
        for k in range(samples + 1):
            th = a0 + da * k / samples
            pts.append(c + rr * np.array([np.cos(th), np.sin(th)]))
    return np.array(pts)


class Field2D:
    """A 2D signed-distance field baked on a grid, sampled bilinearly (fast reuse in 3D evaluation)."""

    def __init__(self, sdf_fn, umin, umax, vmin, vmax, h=0.004):
        self.u0, self.v0, self.h = umin, vmin, h
        nu = int(np.ceil((umax - umin) / h)) + 1
        nv = int(np.ceil((vmax - vmin) / h)) + 1
        uu, vv = np.meshgrid(umin + np.arange(nu) * h, vmin + np.arange(nv) * h, indexing="ij")
        self.grid = sdf_fn(uu, vv).astype(np.float32)
        self.nu, self.nv = nu, nv

    def __call__(self, u, v):
        fu = (np.asarray(u) - self.u0) / self.h
        fv = (np.asarray(v) - self.v0) / self.h
        # Outside the baked area: extrapolate with the distance to the box (keeps the sign right).
        cu = np.clip(fu, 0, self.nu - 1.001)
        cv = np.clip(fv, 0, self.nv - 1.001)
        i = cu.astype(np.int64)
        j = cv.astype(np.int64)
        tu = cu - i
        tv = cv - j
        g = self.grid
        val = (g[i, j] * (1 - tu) * (1 - tv) + g[i + 1, j] * tu * (1 - tv)
               + g[i, j + 1] * (1 - tu) * tv + g[i + 1, j + 1] * tu * tv)
        out = np.sqrt((fu - cu) ** 2 + (fv - cv) ** 2) * self.h
        return val + out


# ----------------------------------------------------------------------------- 3D primitives

def sd_box(x, y, z, cx, cy, cz, hx, hy, hz, r=0.0):
    qx = np.abs(x - cx) - hx + r
    qy = np.abs(y - cy) - hy + r
    qz = np.abs(z - cz) - hz + r
    out = np.sqrt(np.maximum(qx, 0) ** 2 + np.maximum(qy, 0) ** 2 + np.maximum(qz, 0) ** 2)
    return out + np.minimum(np.maximum(qx, np.maximum(qy, qz)), 0) - r


def sd_cyl_x(x, y, z, cy, cz, radius, x0, x1, r=0.0):
    """Cylinder along X between x0 and x1 (rounded rim r)."""
    d_rad = np.sqrt((y - cy) ** 2 + (z - cz) ** 2) - radius
    xm = 0.5 * (x0 + x1)
    d_ax = np.abs(x - xm) - 0.5 * (x1 - x0)
    return round_max(d_rad, d_ax, r)


def sd_capsule(x, y, z, a, b, radius):
    a = np.asarray(a, float)
    b = np.asarray(b, float)
    ba = b - a
    px, py, pz = x - a[0], y - a[1], z - a[2]
    t = np.clip((px * ba[0] + py * ba[1] + pz * ba[2]) / np.dot(ba, ba), 0, 1)
    dx = px - t * ba[0]
    dy = py - t * ba[1]
    dz = pz - t * ba[2]
    return np.sqrt(dx * dx + dy * dy + dz * dz) - radius


def sd_torus_x(x, y, z, cy, cz, cx, R, rt):
    """Torus whose axis is X (a tyre), centred at (cx, cy, cz)."""
    q = np.sqrt((y - cy) ** 2 + (z - cz) ** 2) - R
    return np.sqrt(q * q + (x - cx) ** 2) - rt
