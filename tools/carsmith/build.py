"""A car = the body SDF cut into rig-spec parts, plus separately meshed details. Pure numpy (no Blender).

A car module (carsmith/cars/<name>.py) provides SPEC (BodySpec) and design(fn) -> Design. Cuts run in order on
the named source piece: "panel" cuts leave a real gap, "glass" cuts open a daylight opening and put a recessed
pane in it, "trim" cuts re-skin a region without a gap (black pillars, lenses, wheel wells).
"""
from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np

from .mesher import normals, project, surface_nets
from .regions import Piece, clip, cut_panel, triangulate
from .sdf import round_max


def rmax(r, *terms):
    """Round intersection of several field terms (corner radius r) - the usual way to draw a panel outline."""
    d = terms[0]
    for t in terms[1:]:
        d = round_max(d, t, r)
    return d


def xyz(P):
    P = np.asarray(P)
    return P[:, 0], P[:, 1], P[:, 2]


@dataclass
class Cut:
    name: str                 # resulting part name (rig spec), e.g. "Hood", "Glass_Front"
    field: object             # P (N, 3) -> metres, negative inside
    kind: str = "panel"       # panel | glass | trim
    material: str = "paint"
    source: str = "Body"
    parent: str = ""          # merge into this node as another material (e.g. wheel wells into Body)
    gap: float = 0.005
    recess: float = 0.006     # glass / lens depth under the opening
    flange: bool = True


@dataclass
class Detail:
    name: str
    sdf: object               # (x, y, z) -> distance
    bounds: tuple             # (bmin, bmax)
    material: str
    voxel: float = 0.006
    placements: list = field(default_factory=lambda: [("", (0.0, 0.0, 0.0), False)])  # (parent, origin, mirror x)
    target: int = 0           # triangle budget after decimation (0 = keep)


@dataclass
class Design:
    cuts: list = field(default_factory=list)
    details: list = field(default_factory=list)
    splits: dict = field(default_factory=dict)   # part -> [(new name, field)] e.g. doors by side


@dataclass
class Part:
    name: str
    material: str
    piece: Piece
    surface: object = None    # SDF the piece lies on (for re-projection after decimation)
    flange: bool = False
    kind: str = "surface"
    recess: float = 0.0
    parent: str = ""
    origin: tuple = (0.0, 0.0, 0.0)
    mirror: bool = False
    target: int = 0


def build(car, voxel=0.012, log=print):
    """Returns (fn, parts). Parts are full-resolution surfaces; decimation, flanges and seals come later."""
    from .body import make_body_sdf
    spec = car.SPEC
    fn = make_body_sdf(spec)
    design = car.design(fn)

    V, Q, N = surface_nets(fn, *spec.bounds(), voxel)
    log(f"body: {len(V)} vertices")
    pieces = {"Body": Part("Body", "paint", Piece(V, triangulate(Q), N), fn, True)}
    for c in design.cuts:
        src = pieces[c.source]
        if c.kind == "panel":
            rest, region = cut_panel(src.piece, c.field, c.gap, fn)
        else:
            region, rest = clip(src.piece, c.field, 0.0, fn)
        if len(region.T) == 0:
            log(f"  cut {c.name}: empty!")
            continue
        src.piece = rest
        kind = "glass" if c.kind == "glass" else "surface"
        recess = c.recess if c.kind == "glass" else 0.0
        pieces[c.name] = Part(c.name, c.material, region, fn, c.flange and c.kind != "glass", kind, recess,
                              c.parent)
        log(f"  cut {c.name}: {len(region.T)} triangles")
    for name, splits in design.splits.items():
        if name not in pieces:
            log(f"  split {name}: nothing to split (empty cut)")
            continue
        src = pieces.pop(name)
        rest = src.piece
        for i, entry in enumerate(splits):
            new, fld = entry[0], entry[1]
            parent = entry[2] if len(entry) > 2 else src.parent
            if i == len(splits) - 1:
                part = rest          # the last split takes whatever is left
            else:
                part, rest = clip(rest, fld, 0.0)
            pieces[new] = Part(new, src.material, part, src.surface, src.flange, src.kind, src.recess, parent)
    parts = list(pieces.values())
    for d in design.details:
        Vd, Qd, Nd = surface_nets(d.sdf, *d.bounds, d.voxel)
        if len(Vd) == 0:
            log(f"  detail {d.name}: empty!")
            continue
        piece = Piece(Vd, triangulate(Qd), Nd)
        for parent, origin, mirror in d.placements:
            parts.append(Part(d.name, d.material, piece, d.sdf, False, "detail", 0.0, parent, tuple(origin), mirror,
                              d.target))
        log(f"  detail {d.name}: {len(Qd) * 2} triangles x {len(d.placements)}")
    return fn, parts


def reproject(part: Part, V):
    """Vertices (local, already mirrored if the part is) back onto the part's surface + exact normals."""
    if part.surface is None:
        return V, None
    sgn = -1.0 if part.mirror else 1.0

    def f(x, y, z):
        return part.surface(sgn * x, y, z)
    P = project(f, V, 0.004, 2)
    return P, normals(f, P)
