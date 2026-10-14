"""Cutting a body mesh into panels along exact curves, with real gaps and rolled flanges.

A region is a scalar field (metres, negative inside). Triangles are clipped exactly where the field crosses a
level, so cut lines are as smooth as the field, not as coarse as the mesh. A panel = field < -gap/2, the body
keeps field > +gap/2, the sliver in between is the gap. Cut edges get a flange: a rolled lip and a wall that
turns into the gap, which is what makes panel lines read as real metal.
"""
from __future__ import annotations

import numpy as np


def triangulate(F):
    F = np.asarray(F)
    if F.shape[1] == 3:
        return F
    return np.concatenate([F[:, [0, 1, 2]], F[:, [0, 2, 3]]], axis=0)


class Piece:
    """A triangle mesh with per-vertex normals and an optional per-vertex occlusion (1 = open, 0 = in a gap)."""

    def __init__(self, V, T, N, C=None):
        self.V = np.asarray(V, float)
        self.T = np.asarray(T, np.int64).reshape(-1, 3)
        self.N = np.asarray(N, float)
        self.C = np.ones(len(self.V)) if C is None else np.asarray(C, float)

    def compact(self):
        used = np.unique(self.T)
        remap = np.full(len(self.V), -1, np.int64)
        remap[used] = np.arange(len(used))
        return Piece(self.V[used], remap[self.T], self.N[used], self.C[used])

    @staticmethod
    def merge(pieces):
        V, T, N, C, off = [], [], [], [], 0
        for p in pieces:
            V.append(p.V)
            N.append(p.N)
            C.append(p.C)
            T.append(p.T + off)
            off += len(p.V)
        return Piece(np.concatenate(V), np.concatenate(T), np.concatenate(N), np.concatenate(C))


def clip(piece: Piece, field, level, surface=None, iterations=4):
    """Split at field == level. Returns (below, above) pieces sharing nothing (new vertices are duplicated).

    field(P) -> values for an (N, 3) array. surface: optional SDF; new vertices are projected back onto it.
    """
    V, T, N = piece.V, piece.T, piece.N
    fv = field(V) - level
    below = fv < 0
    nb = below[T].sum(axis=1)
    lo = [T[nb == 3]]
    hi = [T[nb == 0]]
    mixed = T[(nb == 1) | (nb == 2)]
    newV, newN = [], []
    if len(mixed):
        b = below[mixed]
        lone_below = b.sum(axis=1) == 1
        lone = np.where(lone_below, np.argmax(b, axis=1), np.argmax(~b, axis=1))
        rot = (np.arange(3)[None, :] + lone[:, None]) % 3
        m = np.take_along_axis(mixed, rot, axis=1)
        a, bb, c = m[:, 0], m[:, 1], m[:, 2]
        e = np.concatenate([np.stack([a, bb], 1), np.stack([a, c], 1)])
        key = np.sort(e, axis=1)
        uniq, inv = np.unique(key, axis=0, return_inverse=True)
        inv = inv.ravel()
        p0, p1 = V[uniq[:, 0]], V[uniq[:, 1]]
        f0, f1 = fv[uniq[:, 0]], fv[uniq[:, 1]]
        lo_t, hi_t = np.zeros(len(uniq)), np.ones(len(uniq))
        flo, fhi = f0.copy(), f1.copy()
        t = f0 / (f0 - f1)
        for _ in range(iterations):          # regula falsi on the exact field along the edge
            ft = field(p0 + (p1 - p0) * t[:, None]) - level
            same = np.sign(ft) == np.sign(flo)
            lo_t = np.where(same, t, lo_t)
            flo = np.where(same, ft, flo)
            hi_t = np.where(same, hi_t, t)
            fhi = np.where(same, fhi, ft)
            t = lo_t + (hi_t - lo_t) * flo / np.where(flo - fhi == 0, 1e-12, flo - fhi)
        P = p0 + (p1 - p0) * t[:, None]
        Nn = N[uniq[:, 0]] * (1 - t[:, None]) + N[uniq[:, 1]] * t[:, None]
        if surface is not None:
            from .mesher import normals, project
            P = project(surface, P, 0.01, 2)
            Nn = normals(surface, P)
        Nn /= np.maximum(np.linalg.norm(Nn, axis=1, keepdims=True), 1e-12)
        base = len(V)
        newV.append(P)
        newN.append(Nn)
        idx = base + inv
        pab, pac = idx[:len(a)], idx[len(a):]
        tri_lone = np.stack([a, pab, pac], 1)
        tri_q1 = np.stack([pab, bb, c], 1)
        tri_q2 = np.stack([pab, c, pac], 1)
        lo += [tri_lone[lone_below], tri_q1[~lone_below], tri_q2[~lone_below]]
        hi += [tri_lone[~lone_below], tri_q1[lone_below], tri_q2[lone_below]]
    V2 = np.concatenate([V] + newV) if newV else V
    N2 = np.concatenate([N] + newN) if newN else N
    return (Piece(V2, np.concatenate(lo), N2).compact(), Piece(V2, np.concatenate(hi), N2).compact())


def cut_panel(piece: Piece, field, gap=0.004, surface=None):
    """Returns (rest, panel): panel where field < -gap/2, rest where field > +gap/2."""
    inner, rest = clip(piece, field, +gap / 2, surface)
    panel, _sliver = clip(inner, field, -gap / 2, surface)
    return rest, panel


# ----------------------------------------------------------------------------- boundaries and flanges

def boundary_edges(piece: Piece):
    """Directed boundary edges (a -> b) with the piece on the left when seen from outside."""
    T = piece.T
    e = np.concatenate([T[:, [0, 1]], T[:, [1, 2]], T[:, [2, 0]]])
    key = np.sort(e, axis=1)
    _, inv, cnt = np.unique(key, axis=0, return_inverse=True, return_counts=True)
    return e[cnt[inv.ravel()] == 1]


def boundary_loops(piece: Piece):
    """Boundary edges chained into ordered vertex loops."""
    E = boundary_edges(piece)
    nxt = {int(a): int(b) for a, b in E}
    loops, seen = [], set()
    for start in list(nxt):
        if start in seen:
            continue
        loop, v = [], start
        while v not in seen and v in nxt:
            seen.add(v)
            loop.append(v)
            v = nxt[v]
        if len(loop) > 2:
            loops.append(loop)
    return loops


def add_flange(piece: Piece, depth=0.012, roll=0.0025):
    """Adds a rolled lip and a wall into the gap along every boundary. Returns a new piece."""
    E = boundary_edges(piece)
    if len(E) == 0:
        return piece
    V, N = piece.V, piece.N
    verts = np.unique(E)
    # Outward in-plane direction per boundary vertex: (b - a) x n averaged over its two edges.
    out = np.zeros_like(V)
    d = V[E[:, 1]] - V[E[:, 0]]
    n_e = N[E[:, 0]] + N[E[:, 1]]
    o_e = np.cross(d, n_e)
    o_e /= np.maximum(np.linalg.norm(o_e, axis=1, keepdims=True), 1e-12)
    np.add.at(out, E[:, 0], o_e)
    np.add.at(out, E[:, 1], o_e)
    o = out[verts]
    o /= np.maximum(np.linalg.norm(o, axis=1, keepdims=True), 1e-12)
    n = N[verts]
    p = V[verts]
    r1 = p - n * roll + o * (roll * 0.6)
    r2 = p - n * depth + o * (roll * 0.6)
    n1 = n + o
    n1 /= np.linalg.norm(n1, axis=1, keepdims=True)
    k = len(verts)
    base = len(V)
    local = np.full(len(V), -1, np.int64)
    local[verts] = np.arange(k)
    # Ring 0 is a copy of the boundary vertex with a softened normal so the lip highlights.
    n0 = n * 0.75 + o * 0.25
    n0 /= np.linalg.norm(n0, axis=1, keepdims=True)
    ring0, ring1, ring2 = base + np.arange(k), base + k + np.arange(k), base + 2 * k + np.arange(k)
    Vn = np.concatenate([V, p, r1, r2])
    Nn = np.concatenate([N, n0, n1, o])
    Cn = np.concatenate([piece.C, np.full(k, 0.85), np.full(k, 0.25), np.full(k, 0.02)])
    a, b = local[E[:, 0]], local[E[:, 1]]
    tris = []
    for ra, rb in ((ring0, ring1), (ring1, ring2)):
        tris.append(np.stack([ra[a], rb[a], rb[b]], 1))
        tris.append(np.stack([ra[a], rb[b], ra[b]], 1))
    # The surface triangles keep their own boundary vertices; the lip starts from a duplicate (hard edge).
    return Piece(Vn, np.concatenate([piece.T] + tris), Nn, Cn)


def offset(piece: Piece, distance):
    """Moves a piece along its normals (glass sits a few millimetres under its frame)."""
    return Piece(piece.V + piece.N * distance, piece.T, piece.N, piece.C)


def tube_along(loop_points, radius=0.004, sides=6, closed=True):
    """A small tube along a polyline (rubber seals, chrome trim). Returns a Piece."""
    P = np.asarray(loop_points, float)
    n = len(P)
    tang = np.roll(P, -1, 0) - np.roll(P, 1, 0)
    if not closed:
        tang[0] = P[1] - P[0]
        tang[-1] = P[-1] - P[-2]
    tang /= np.maximum(np.linalg.norm(tang, axis=1, keepdims=True), 1e-12)
    ref = np.where(np.abs(tang[:, 2:3]) < 0.9, np.array([[0, 0, 1.0]]), np.array([[1.0, 0, 0]]))
    u = np.cross(tang, ref)
    u /= np.linalg.norm(u, axis=1, keepdims=True)
    v = np.cross(tang, u)
    ang = np.linspace(0, 2 * np.pi, sides, endpoint=False)
    ring = np.cos(ang)[None, :, None] * u[:, None, :] + np.sin(ang)[None, :, None] * v[:, None, :]
    V = (P[:, None, :] + radius * ring).reshape(-1, 3)
    Nn = ring.reshape(-1, 3)
    T = []
    segs = n if closed else n - 1
    for i in range(segs):
        j = (i + 1) % n
        for s in range(sides):
            s2 = (s + 1) % sides
            a, b, c, d = i * sides + s, i * sides + s2, j * sides + s2, j * sides + s
            T += [(a, d, c), (a, c, b)]
    return Piece(V, np.array(T), Nn)
