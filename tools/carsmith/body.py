"""Parametric car body as one signed-distance field.

The lower body and the greenhouse are each a "variable rounded box": side, plan and section profiles
(monotone splines along the car) bound the solid, and exact round intersections give each family of edges
its own fillet radius (crisp shoulder, rounder plan corners, soft underside). The greenhouse is joined
with a small concave blend, the wheel arches are cut with a rolled lip.

Blender axes: X right, Y forward, Z up. Origin on the ground, Y = 0 at mid-wheelbase.
"""
from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np

from .sdf import curve, round_max, round_min, round_sub


@dataclass
class Wheel:
    y: float          # axle position
    radius: float     # tyre radius (wheel centre height = radius)
    width: float
    track: float      # centre-to-centre width
    arch_radius: float
    arch_depth: float = 0.30   # how far the wheelhouse cuts in from the body side


@dataclass
class BodySpec:
    name: str
    # Lower body ---------------------------------------------------------------
    top: list            # [(y, z)] side-view top line (nose, hood, belt, deck)
    bottom: list         # [(y, z)] underside / rocker line
    half_width: list     # [(y, w)] plan-view half width
    front: list          # [(z, y)] front face position by height (rake)
    rear: list           # [(z, y)] rear face position by height
    plan_radius: list    # [(y, r)] plan-view corner radius (nose/tail roundness)
    shoulder_radius: float = 0.03
    bottom_radius: float = 0.08
    side_bulge: float = 0.02         # how much the side curves in towards top and bottom
    bulge_centre: float = 0.55
    bulge_half: float = 0.35
    crease_z: float = 0.0            # > 0: a crisp body-side character line at this height instead of the bulge
    crease_in_up: float = 0.10       # side lean-in above the crease (tan)
    crease_in_down: float = 0.14     # tuck-under below the crease (tan)
    crease_radius: float = 0.012
    top_crown: float = 0.0           # hood / deck drop per x^2
    hood_bulge: tuple = ()           # (height, half width, y from, y to): a raised power bulge on the hood
    # Greenhouse ---------------------------------------------------------------
    roof: list = field(default_factory=list)      # [(y, z)] roofline incl. windscreen and backlight
    cabin_front: float = 0.6                      # cabin box ends (hidden inside the body if set wide)
    cabin_rear: float = -2.2
    belt: float = 0.85                            # belt height (cabin base level for tumblehome)
    ledge: list = field(default_factory=lambda: [(-9, 0.07), (9, 0.07)])   # [(y, inset)] window ledge
    tumblehome_deg: float = 25.0
    roof_crown: float = 0.12                      # roof/windscreen drop per x^2
    roof_radius: float = 0.04
    cabin_end_radius: float = 0.05
    belt_blend: float = 0.008
    # Wheels -------------------------------------------------------------------
    wheels: list = field(default_factory=list)
    arch_lip: float = 0.012

    def bounds(self, margin=0.06):
        ys = [p[0] for p in self.top] + [p[1] for p in self.front] + [p[1] for p in self.rear]
        w = max(p[1] for p in self.half_width)
        zmax = max([p[1] for p in self.top] + [p[1] for p in self.roof] if self.roof else [p[1] for p in self.top])
        return (np.array([-w - margin, min(ys) - margin, -0.02]),
                np.array([w + margin, max(ys) + margin, zmax + margin]))


def make_body_sdf(spec: BodySpec):
    T = curve(spec.top)
    B = curve(spec.bottom)
    W = curve(spec.half_width)
    F = curve(spec.front)
    R = curve(spec.rear)
    RP = curve(spec.plan_radius)
    roof = curve(spec.roof) if spec.roof else None
    ledge = curve(spec.ledge)
    tan_th = np.tan(np.radians(spec.tumblehome_deg))

    def side(x, y, z):
        """Body-side distance: a bowed side, or two planes meeting in a filleted crease."""
        if spec.crease_z > 0:
            up = np.abs(x) - W(y) + spec.crease_in_up * (z - spec.crease_z)
            dn = np.abs(x) - W(y) + spec.crease_in_down * (spec.crease_z - z)
            return round_max(up, dn, spec.crease_radius)
        bulge = spec.side_bulge * ((z - spec.bulge_centre) / spec.bulge_half) ** 2
        return np.abs(x) - (W(y) - bulge)

    def top(x, y):
        t = T(y) - spec.top_crown * x * x
        if spec.hood_bulge:
            h, bw, y0, y1 = spec.hood_bulge
            u = np.clip(1.0 - (x / bw) ** 2, 0.0, 1.0)
            v = np.clip(np.minimum(y - y0, y1 - y) / 0.18, 0.0, 1.0)
            t = t + h * (u * u * (3 - 2 * u)) * (v * v * (3 - 2 * v))
        return t

    def plan(x, y, z):
        """Plan-view distance at the point's own height (side, nose and tail faces with corner radius)."""
        dl = np.maximum(y - F(z), R(z) - y)
        return round_max(side(x, y, z), dl, RP(y))

    def lower(x, y, z):
        d = plan(x, y, z)
        d = round_max(d, z - top(x, y), spec.shoulder_radius)
        d = round_max(d, B(y) - z, spec.bottom_radius)
        return d

    def cabin_terms(x, y, z):
        """(side, top, ends) distances of the greenhouse: the raw faces that panel and glass fields use."""
        wc = W(y) - ledge(y) - np.maximum(z - spec.belt, 0.0) * tan_th
        ds = np.abs(x) - wc
        dt = z - (roof(y) - spec.roof_crown * x * x)
        dl = np.maximum(y - spec.cabin_front, spec.cabin_rear - y)
        return ds, dt, dl

    def cabin(x, y, z):
        ds, dt, dl = cabin_terms(x, y, z)
        d = round_max(ds, dt, spec.roof_radius)
        d = round_max(d, dl, spec.cabin_end_radius)
        d = np.maximum(d, (spec.belt - 0.2) - z)
        return d

    def arch_cut(x, y, z):
        """Distance to the wheelhouse cut (about 0 on the arch walls, > lip radius elsewhere on the body)."""
        best = np.full(np.shape(x), np.inf)
        for wh in spec.wheels:
            rad = np.sqrt((y - wh.y) ** 2 + (z - wh.radius) ** 2) - wh.arch_radius
            inner = (W(wh.y) - wh.arch_depth) - np.abs(x)
            best = np.minimum(best, np.maximum(rad, inner))
        return best

    def arches(x, y, z, d):
        return round_sub(d, arch_cut(x, y, z), spec.arch_lip)

    def fn(x, y, z):
        d = lower(x, y, z)
        if roof is not None:
            d = round_min(d, cabin(x, y, z), spec.belt_blend)
        return arches(x, y, z, d)

    fn.spec = spec
    fn.parts = {"lower": lower, "cabin": cabin, "cabin_terms": cabin_terms, "arch_cut": arch_cut,
                "T": T, "B": B, "W": W, "F": F, "R": R, "RP": RP, "roof": roof, "ledge": ledge,
                "side": side, "top": top, "plan": plan}
    return fn
