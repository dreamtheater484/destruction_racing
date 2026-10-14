"""Surface nets: signed-distance function -> quad mesh with vertices on the true surface and exact normals.

The mesh is only the carrier: vertices are Newton-projected onto the zero level set and the normals come from
the field gradient, so reflections on paint are as smooth as the maths, whatever the triangle count.
"""
from __future__ import annotations

import numpy as np


def evaluate_grid(fn, bmin, bmax, h, chunk=24):
    bmin = np.asarray(bmin, float)
    bmax = np.asarray(bmax, float)
    n = np.ceil((bmax - bmin) / h).astype(int) + 1
    xs = bmin[0] + np.arange(n[0]) * h
    ys = bmin[1] + np.arange(n[1]) * h
    zs = bmin[2] + np.arange(n[2]) * h
    D = np.empty((n[0], n[1], n[2]), dtype=np.float32)
    X, Y = np.meshgrid(xs, ys, indexing="ij")
    for k0 in range(0, n[2], chunk):
        zz = zs[k0:k0 + chunk]
        Xc = np.repeat(X[:, :, None], len(zz), axis=2)
        Yc = np.repeat(Y[:, :, None], len(zz), axis=2)
        Zc = np.broadcast_to(zz[None, None, :], Xc.shape)
        D[:, :, k0:k0 + chunk] = fn(Xc, Yc, Zc)
    return D, (xs, ys, zs)


def gradient(fn, p, eps=5e-4):
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    gx = fn(x + eps, y, z) - fn(x - eps, y, z)
    gy = fn(x, y + eps, z) - fn(x, y - eps, z)
    gz = fn(x, y, z + eps) - fn(x, y, z - eps)
    return np.stack([gx, gy, gz], axis=1) / (2 * eps)


def project(fn, p, h, iterations=3):
    """Newton steps onto f = 0, each clamped to one cell so sharp features don't fold."""
    p = p.copy()
    for _ in range(iterations):
        f = fn(p[:, 0], p[:, 1], p[:, 2])
        g = gradient(fn, p)
        g2 = np.maximum((g * g).sum(axis=1), 1e-12)
        step = (f / g2)[:, None] * g
        L = np.linalg.norm(step, axis=1)
        scale = np.minimum(1.0, h / np.maximum(L, 1e-12))
        p -= step * scale[:, None]
    return p


def normals(fn, p):
    g = gradient(fn, p)
    return g / np.maximum(np.linalg.norm(g, axis=1, keepdims=True), 1e-12)


def surface_nets(fn, bmin, bmax, h, iterations=3):
    """Returns (vertices N x 3, quads M x 4, normals N x 3); quads are wound counter-clockwise from outside."""
    D, (xs, ys, zs) = evaluate_grid(fn, bmin, bmax, h)
    s = D < 0
    nx, ny, nz = D.shape

    corners = [(dx, dy, dz) for dz in (0, 1) for dy in (0, 1) for dx in (0, 1)]
    cs = [s[dx:nx - 1 + dx, dy:ny - 1 + dy, dz:nz - 1 + dz] for dx, dy, dz in corners]
    any_in = np.zeros_like(cs[0])
    all_in = np.ones_like(cs[0])
    for c in cs:
        any_in |= c
        all_in &= c
    active = any_in & ~all_in
    ai, aj, ak = np.nonzero(active)
    index = np.full(active.shape, -1, dtype=np.int64)
    index[ai, aj, ak] = np.arange(len(ai))

    # Vertex = mean of the edge crossings in the cell.
    acc = np.zeros((len(ai), 3))
    cnt = np.zeros(len(ai))
    edges = []
    for dy in (0, 1):
        for dz in (0, 1):
            edges.append(((0, dy, dz), (1, dy, dz)))
    for dx in (0, 1):
        for dz in (0, 1):
            edges.append(((dx, 0, dz), (dx, 1, dz)))
    for dx in (0, 1):
        for dy in (0, 1):
            edges.append(((dx, dy, 0), (dx, dy, 1)))
    for (a, b) in edges:
        da = D[ai + a[0], aj + a[1], ak + a[2]].astype(np.float64)
        db = D[ai + b[0], aj + b[1], ak + b[2]].astype(np.float64)
        cross = (da < 0) != (db < 0)
        t = np.where(cross, da / np.where(cross, da - db, 1.0), 0.0)
        px = (ai + a[0] + t * (b[0] - a[0]))
        py = (aj + a[1] + t * (b[1] - a[1]))
        pz = (ak + a[2] + t * (b[2] - a[2]))
        acc[cross] += np.stack([px, py, pz], axis=1)[cross]
        cnt[cross] += 1
    rel = acc / np.maximum(cnt, 1)[:, None]
    V = np.stack([xs[0] + rel[:, 0] * h, ys[0] + rel[:, 1] * h, zs[0] + rel[:, 2] * h], axis=1)
    V = project(fn, V, h, iterations)

    quads = []
    # x-edges: cells (j-1,k-1) (j,k-1) (j,k) (j-1,k)
    ex = s[:-1, 1:-1, 1:-1] != s[1:, 1:-1, 1:-1]
    i, j, k = np.nonzero(ex)
    j += 1
    k += 1
    q = np.stack([index[i, j - 1, k - 1], index[i, j, k - 1], index[i, j, k], index[i, j - 1, k]], axis=1)
    flip = ~s[i, j, k]
    q[flip] = q[flip][:, ::-1]
    quads.append(q)
    # y-edges: (i-1,k-1) (i-1,k) (i,k) (i,k-1)
    ey = s[1:-1, :-1, 1:-1] != s[1:-1, 1:, 1:-1]
    i, j, k = np.nonzero(ey)
    i += 1
    k += 1
    q = np.stack([index[i - 1, j, k - 1], index[i - 1, j, k], index[i, j, k], index[i, j, k - 1]], axis=1)
    flip = ~s[i, j, k]
    q[flip] = q[flip][:, ::-1]
    quads.append(q)
    # z-edges: (i-1,j-1) (i,j-1) (i,j) (i-1,j)
    ez = s[1:-1, 1:-1, :-1] != s[1:-1, 1:-1, 1:]
    i, j, k = np.nonzero(ez)
    i += 1
    j += 1
    q = np.stack([index[i - 1, j - 1, k], index[i, j - 1, k], index[i, j, k], index[i - 1, j, k]], axis=1)
    flip = ~s[i, j, k]
    q[flip] = q[flip][:, ::-1]
    quads.append(q)
    Q = np.concatenate(quads, axis=0)
    Q = Q[(Q >= 0).all(axis=1)]
    return V, Q, normals(fn, V)


def write_obj(path, V, Q, N=None):
    with open(path, "w") as f:
        for v in V:
            f.write(f"v {v[0]:.5f} {v[1]:.5f} {v[2]:.5f}\n")
        if N is not None:
            for n in N:
                f.write(f"vn {n[0]:.4f} {n[1]:.4f} {n[2]:.4f}\n")
        for q in Q:
            if N is not None:
                f.write("f " + " ".join(f"{a + 1}//{a + 1}" for a in q) + "\n")
            else:
                f.write("f " + " ".join(str(a + 1) for a in q) + "\n")
