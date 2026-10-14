"""Car templates: a few dozen proportions -> BodySpec + panels, glass, lights and details.

The 3-box sedan template covers the US full-size sedan (#1 Aldridge Brougham 400) and the EU "brick"
(#5 Nordvik Fjord DL) - and later the other sedans - as morphs of one parameter set. Blender axes: X right,
Y forward, Z up, origin on the ground at mid-wheelbase.
"""
from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np

from .body import BodySpec, Wheel
from .build import Cut, Design, Detail, rmax, xyz
from .sdf import curve, rounded_rect_2d, round_max, sd_box, smooth_min


@dataclass
class Rect:
    """A rectangle on the front or rear face: centre |x|, centre z, half sizes, corner radius."""
    x: float
    z: float
    hx: float
    hz: float
    r: float = 0.015
    mirrored: bool = True     # one per side (x > 0 means both +x and -x)


@dataclass
class Sedan:
    name: str
    length: float
    half_width: float
    wheelbase: float
    front_overhang: float
    # Heights
    sill: float = 0.22            # rocker bottom at mid-wheelbase
    nose_top: float = 0.82
    nose_bottom: float = 0.36
    cowl: float = 0.92            # hood height at the windscreen base
    belt: float = 0.92            # window sill (door top)
    deck: float = 0.95            # trunk lid height
    tail_top: float = 0.92
    tail_bottom: float = 0.40
    roof: float = 1.40
    # Greenhouse stations (y)
    cowl_y: float = 0.40          # windscreen base
    header_y: float = -0.22       # windscreen top
    roof_rear_y: float = -1.20    # backlight top
    deck_y: float = -1.55         # backlight base
    tumblehome_deg: float = 16.0
    ledge: float = 0.07
    roof_crown: float = 0.10
    roof_radius: float = 0.04
    # Sections
    plan_front: float = 0.08
    plan_rear: float = 0.08
    shoulder: float = 0.02
    bottom_radius: float = 0.06
    crease_z: float = 0.66
    crease_up: float = 0.04
    crease_down: float = 0.08
    top_crown: float = 0.03
    front_lean: float = 0.03      # how far the nose top sits back of its bottom
    rear_lean: float = 0.03
    # Wheels
    wheel_radius: float = 0.31
    wheel_width: float = 0.19
    track_front: float = 1.44
    track_rear: float = 1.42
    arch_radius: float = 0.36
    rim_inch: int = 14
    rim_style: str = "slot"
    # Doors and pillars
    a_pillar: float = 0.06
    c_pillar: float = 0.08        # backlight inset from the cabin side
    b_pillar_y: float = -0.12
    b_pillar_w: float = 0.035
    door_split_y: float = -0.14   # front door rear edge = rear door front edge
    door_front_y: float = 0.50
    c_glass_y: float = -1.05      # rear end of the rear door glass
    c_door_y: float = -1.12       # rear door rear edge above the arch
    # Lights and front end
    headlights: list = field(default_factory=list)
    grille: Rect | None = None
    indicators_front: list = field(default_factory=list)
    tail_lights: list = field(default_factory=list)
    tail_amber: list = field(default_factory=list)
    tail_clear: list = field(default_factory=list)
    grille_bars: str = "vertical"     # vertical | horizontal | eggcrate
    grille_material: str = "chrome"
    # Bumpers and trim
    bumper_material: str = "chrome"
    bumper_z: float = 0.46
    bumper_half: float = 0.07
    bumper_proud: float = 0.06
    bumper_strip: bool = True         # rubber insert strip along the bumper
    side_trim: str = ""               # "" | "rub" (black rubbing strip) | "chrome" (belt moulding)
    side_trim_z: float = 0.55
    vinyl_roof: bool = False
    mirror_material: str = "chrome"
    interior_seat_y: float = -0.25
    handle_z: float = 0.82
    handle_material: str = "chrome"
    bezel_material: str = ""          # "" = no headlamp bezels
    plate_front_half: tuple = (0.26, 0.055)
    plate_rear_z: float = 0.0         # 0 = on the rear bumper

    # ------------------------------------------------------------------------- derived stations
    @property
    def FA(self):
        return self.wheelbase / 2

    @property
    def RA(self):
        return -self.wheelbase / 2

    @property
    def NOSE(self):
        return self.FA + self.front_overhang

    @property
    def TAIL(self):
        return self.NOSE - self.length

    def spec(self) -> BodySpec:
        N, T, FA, RA = self.NOSE, self.TAIL, self.FA, self.RA
        hw = self.half_width
        top = [(T, self.tail_top), (T + 0.18, self.deck), (self.deck_y - 0.12, self.deck),
               (self.deck_y - 0.5, self.belt), (self.cowl_y - 0.30, self.belt), (self.cowl_y, self.cowl),
               (FA, 0.5 * (self.cowl + self.nose_top) + 0.012), (N - 0.10, self.nose_top + 0.004), (N, self.nose_top)]
        top.sort()
        # De-duplicate stations that collapse for short decks.
        top = [p for i, p in enumerate(top) if i == 0 or p[0] - top[i - 1][0] > 0.05]
        bottom = [(T, self.tail_bottom), (RA - self.arch_radius - 0.25, self.sill + 0.05), (RA, self.sill + 0.01),
                  (0.0, self.sill), (FA, self.sill + 0.01), (FA + self.arch_radius + 0.2, self.sill + 0.06),
                  (N, self.nose_bottom)]
        half_width = [(T, hw - 0.025), (T + 0.35, hw), (N - 0.30, hw), (N, hw - 0.025)]
        front = [(self.nose_bottom - 0.05, N - 0.04), (self.nose_bottom + 0.06, N),
                 (self.nose_top - 0.05, N - self.front_lean), (self.nose_top + 0.02, N - self.front_lean - 0.02)]
        rear = [(self.tail_bottom - 0.05, T + 0.04), (self.tail_bottom + 0.06, T),
                (self.tail_top - 0.05, T + self.rear_lean), (self.tail_top + 0.02, T + self.rear_lean + 0.02)]
        roof = [(self.cowl_y + 0.35, self.cowl - 0.16), (self.cowl_y, self.cowl - 0.004),
                (self.header_y, self.roof - 0.02), (self.header_y - 0.16, self.roof),
                (self.roof_rear_y + 0.14, self.roof), (self.roof_rear_y, self.roof - 0.022),
                (self.deck_y, self.deck - 0.004), (self.deck_y - 0.35, self.deck - 0.16)]
        r = self.wheel_radius
        return BodySpec(
            name=self.name, top=top, bottom=bottom, half_width=half_width, front=front, rear=rear,
            plan_radius=[(T, self.plan_rear), (RA, self.plan_rear), (FA, self.plan_front), (N, self.plan_front)],
            shoulder_radius=self.shoulder, bottom_radius=self.bottom_radius,
            crease_z=self.crease_z, crease_in_up=self.crease_up, crease_in_down=self.crease_down,
            crease_radius=0.010, top_crown=self.top_crown,
            roof=roof, cabin_front=self.cowl_y + 0.5, cabin_rear=self.deck_y - 0.5, belt=self.belt,
            ledge=[(T, self.ledge), (N, self.ledge)], tumblehome_deg=self.tumblehome_deg,
            roof_crown=self.roof_crown, roof_radius=self.roof_radius, cabin_end_radius=0.05, belt_blend=0.006,
            wheels=[Wheel(y=FA, radius=r, width=self.wheel_width, track=self.track_front, arch_radius=self.arch_radius),
                    Wheel(y=RA, radius=r, width=self.wheel_width, track=self.track_rear, arch_radius=self.arch_radius)],
            arch_lip=0.018,
        )

    # ------------------------------------------------------------------------- panels, glass, details
    def design(self, fn):
        from . import parts as kit
        P = fn.parts
        lower, cterms = P["lower"], P["cabin_terms"]
        N, T, RA = self.NOSE, self.TAIL, self.RA
        hw = self.half_width

        def low(p):
            return lower(*xyz(p))

        def on_top(p, depth=0.04):
            x, y, z = xyz(p)
            return (P["top"](x, y) - depth) - z

        def on_front(p):
            return (N - 0.10) - p[:, 1]

        def on_rear(p):
            return p[:, 1] - (T + 0.10)

        def face_rect(rect, face):
            def f(p):
                x, y, z = xyz(p)
                ax = np.abs(x) if rect.mirrored else x
                d = rounded_rect_2d(ax, z, rect.x, rect.z, rect.hx, rect.hz, rect.r)
                return round_max(d, face(p), 0.004)
            return f

        def union(fields):
            def f(p):
                return np.min([g(p) for g in fields], axis=0)
            return f

        # Glass
        def windscreen(p):
            ds, dt, dl = cterms(*xyz(p))
            return rmax(0.05, ds + self.a_pillar, (self.header_y + 0.025) - p[:, 1], 0.022 - low(p))

        def backlight(p):
            ds, dt, dl = cterms(*xyz(p))
            return rmax(0.05, ds + self.c_pillar, p[:, 1] - (self.roof_rear_y - 0.025), 0.022 - low(p))

        def side_dlo(p):
            ds, dt, dl = cterms(*xyz(p))
            return rmax(0.03, dt + 0.034, 0.028 - low(p), -ds - 0.025, self.c_glass_y - p[:, 1])

        def b_pillar(p):
            return np.abs(p[:, 1] - self.b_pillar_y) - self.b_pillar_w

        # Panels
        def hood(p):
            x, y, z = xyz(p)
            return rmax(0.025, np.abs(x) - (hw - 0.075), (self.cowl_y + 0.08) - y, y - (N - 0.035), on_top(p))

        def trunk(p):
            x, y, z = xyz(p)
            return rmax(0.025, np.abs(x) - (hw - 0.075), y - (self.deck_y - 0.07), (T + 0.035) - y, on_top(p))

        door_front = curve([(self.belt - 0.06, self.door_front_y), (self.belt + 0.08, self.cowl_y - 0.08)])
        arch_top = self.wheel_radius + self.arch_radius
        door_rear = curve([(arch_top - 0.10, RA + self.arch_radius + 0.07), (arch_top + 0.05, self.c_door_y)])

        def side_panel(p):
            ds, dt, dl = cterms(*xyz(p))
            return np.maximum(dt + 0.012, -ds - 0.03)

        def front_doors(p):
            x, y, z = xyz(p)
            return rmax(0.025, y - door_front(z), self.door_split_y - y, (self.sill + 0.07) - z, side_panel(p))

        def rear_doors(p):
            x, y, z = xyz(p)
            return rmax(0.025, y - (self.door_split_y - 0.0005), door_rear(z) - y, (self.sill + 0.07) - z,
                        side_panel(p))

        def fenders(p):
            x, y, z = xyz(p)
            return rmax(0.02, (door_front(z) - 0.005) - y, y - (N - 0.012), (self.sill + 0.08) - z,
                        (hw - 0.09) - np.abs(x))

        def wheel_wells(p):
            return P["arch_cut"](*xyz(p)) - 0.006

        def underbody(p):
            x, y, z = xyz(p)
            return z - (P["B"](y) + 0.026)

        def vinyl(p):
            ds, dt, dl = cterms(*xyz(p))
            y = p[:, 1]
            return rmax(0.02, ds + 0.012, y - (self.header_y - 0.04), (self.roof_rear_y + 0.02) - y)

        side = lambda s: (lambda p: -s * p[:, 0])                     # noqa: E731
        front_of = lambda yb: (lambda p: yb - p[:, 1])                # noqa: E731

        cuts = [
            Cut("Wheel_Wells", wheel_wells, "trim", "wheel_well", parent="Body", flange=False),
            Cut("Underbody", underbody, "trim", "underbody", parent="Body", flange=False),
            Cut("Glass_Front", windscreen, "glass", "glass"),
            Cut("Glass_Rear", backlight, "glass", "glass"),
            Cut("Glass_Side", side_dlo, "glass", "glass"),
            Cut("Pillar_B", b_pillar, "trim", "trim_black", source="Glass_Side", parent="Body"),
        ]
        if self.vinyl_roof:
            cuts.append(Cut("Vinyl", vinyl, "trim", "vinyl", parent="Body"))
        cuts += [
            Cut("Hood", hood),
            Cut("Trunk", trunk),
            Cut("Doors_F", front_doors),
            Cut("Doors_R", rear_doors),
            Cut("Fenders", fenders),
        ]
        if self.headlights:
            cuts.append(Cut("Light_Head", union([face_rect(r, on_front) for r in self.headlights]), "glass",
                            "lens_clear", recess=0.012))
        if self.grille:
            cuts.append(Cut("Grille_Hole", face_rect(self.grille, on_front), "glass", "trim_black", recess=0.035,
                            parent="Body"))
        if self.indicators_front:
            cuts.append(Cut("Indicator_F", union([face_rect(r, on_front) for r in self.indicators_front]), "glass",
                            "lens_amber", recess=0.006))
        if self.tail_lights:
            cuts.append(Cut("Light_Tail", union([face_rect(r, on_rear) for r in self.tail_lights]), "glass",
                            "lens_red", recess=0.006))
            if self.tail_amber:
                cuts.append(Cut("Tail_Amber", union([face_rect(r, on_rear) for r in self.tail_amber]), "glass",
                                "lens_amber", source="Light_Tail", recess=0.006))
            if self.tail_clear:
                cuts.append(Cut("Tail_Reverse", union([face_rect(r, on_rear) for r in self.tail_clear]), "glass",
                                "lens_clear", source="Light_Tail", recess=0.006))

        splits = {
            "Doors_F": [("Door_FR", side(+1)), ("Door_FL", side(-1))],
            "Doors_R": [("Door_RR", side(+1)), ("Door_RL", side(-1))],
            "Fenders": [("Fender_FR", side(+1)), ("Fender_FL", side(-1))],
            "Glass_Side": [("Glass_FR", lambda p: np.maximum(-p[:, 0], self.b_pillar_y - p[:, 1])),
                           ("Glass_FL", lambda p: np.maximum(p[:, 0], self.b_pillar_y - p[:, 1])),
                           ("Glass_RR", side(+1)), ("Glass_RL", side(-1))],
        }
        if self.headlights:
            splits["Light_Head"] = [("Light_Head_R", side(+1)), ("Light_Head_L", side(-1))]
        if self.indicators_front:
            splits["Indicator_F"] = [("Ind_R", side(+1), "Light_Head_R"), ("Ind_L", side(-1), "Light_Head_L")]
        if self.tail_lights:
            splits["Light_Tail"] = [("Light_Tail_R", side(+1)), ("Light_Tail_L", side(-1))]
            if self.tail_amber:
                splits["Tail_Amber"] = [("Amber_R", side(+1), "Light_Tail_R"), ("Amber_L", side(-1), "Light_Tail_L")]
            if self.tail_clear:
                splits["Tail_Reverse"] = [("Rev_R", side(+1), "Light_Tail_R"), ("Rev_L", side(-1), "Light_Tail_L")]

        body = fn
        bz, bh, bp = self.bumper_z, self.bumper_half, self.bumper_proud
        arch_f = self.FA + self.arch_radius + 0.03
        arch_r = RA - self.arch_radius - 0.03
        details = [
            kit.band("Bumper_F", body, P, z=bz, half_height=bh, proud=bp, y_range=(arch_f, 9.0), radius=0.02,
                     groove=not self.bumper_strip, material=self.bumper_material),
            kit.band("Bumper_R", body, P, z=bz, half_height=bh, proud=bp, y_range=(-9.0, arch_r), radius=0.02,
                     groove=not self.bumper_strip, material=self.bumper_material),
        ]
        if self.bumper_strip:
            details += [
                kit.band("Bumper_F_Strip", body, P, z=bz, half_height=bh * 0.3, proud=bp + 0.008,
                         y_range=(arch_f + 0.02, 9.0), radius=0.008, end_radius=0.01, groove=False,
                         material="rubber", voxel=0.004, target=6000),
                kit.band("Bumper_R_Strip", body, P, z=bz, half_height=bh * 0.3, proud=bp + 0.008,
                         y_range=(-9.0, arch_r - 0.02), radius=0.008, end_radius=0.01, groove=False,
                         material="rubber", voxel=0.004, target=6000),
            ]
        if self.side_trim == "rub":
            details.append(kit.band("Trim_Side", body, P, z=self.side_trim_z, half_height=0.022, proud=0.012,
                                    y_range=(RA + self.arch_radius + 0.05, self.FA - self.arch_radius - 0.05),
                                    radius=0.008, end_radius=0.012, groove=False, material="bumper",
                                    voxel=0.004, target=4000))
        elif self.side_trim == "chrome":
            details.append(kit.band("Trim_Side", body, P, z=self.side_trim_z, half_height=0.009, proud=0.006,
                                    y_range=(T + 0.15, N - 0.15), radius=0.004, end_radius=0.006, groove=False,
                                    material="chrome", voxel=0.0035, target=6000))
        if self.grille:
            details.append(kit.grille("Grille", self.grille, P, style=self.grille_bars, material=self.grille_material))
        if self.bezel_material and self.headlights:
            details.append(kit.bezel("Bezels", self.headlights, P, material=self.bezel_material))
        ph = self.plate_front_half
        details.append(kit.plate("Plate_F", P, z=bz, front=True, half=ph, offset=bp))
        if self.plate_rear_z > 0:
            details.append(kit.plate("Plate_R", P, z=self.plate_rear_z, front=False, half=ph, offset=0.0))
        else:
            details.append(kit.plate("Plate_R", P, z=bz, front=False, half=ph, offset=bp))
        for nm, hy in (("F", self.door_split_y + 0.12), ("R", self.c_door_y + 0.13)):
            for sname, sx in (("R", +1), ("L", -1)):
                details.append(kit.handle(f"Handle_{nm}{sname}", fn, hy, self.handle_z, sx,
                                          material=self.handle_material))
        my = self.cowl_y - 0.18
        wc = hw - self.ledge
        mz = self.belt + 0.07
        details += [
            kit.mirror("Mirror_R", +1, (wc + 0.13, my, mz), stalk_from=wc - 0.005, material=self.mirror_material),
            kit.mirror("Mirror_L", -1, (wc + 0.13, my, mz), stalk_from=wc - 0.005, material=self.mirror_material),
            kit.interior("Interior", body, P, dash_y=(self.cowl_y - 0.30, self.cowl_y + 0.05), seat_y=self.interior_seat_y,
                         belt=self.belt, rhd=False, rear_bench=True),
        ]

        def on_screen(x, y):
            return (x, y, float(P["roof"](y)) - self.roof_crown * x * x)
        wy0 = self.cowl_y - 0.05
        details += [kit.wiper("Wiper_L", on_screen(-0.05, wy0), on_screen(-0.62, wy0 - 0.06)),
                    kit.wiper("Wiper_R", on_screen(0.10, wy0), on_screen(0.66, wy0 - 0.06))]
        details += kit.wheels(fn.spec.wheels, style=self.rim_style, rim_inch=self.rim_inch)
        return Design(cuts, details, splits)
