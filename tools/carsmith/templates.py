"""Car templates: a few dozen proportions -> BodySpec + panels, glass, lights and details.

One parametric template covers the four body styles of the roster (plan §8.6) as morphs of one parameter set:
- sedan: 3-box, four doors, boot lid (#1 Aldridge Brougham 400, #5 Nordvik Fjord DL, #11 Ryujin Raiden GTS);
- coupe: 3-box, two doors and a quarter window (#6 Adler Falke 2.7i);
- hatch: the roof runs to the tail, two doors, a tailgate with the rear glass; with a long, flat rear glass it
  is a liftback (#7 Volant 105 GTi, #9 Hikari Kaze GT, #3 Columbia Interceptor GT);
- wagon: the long roof with four doors, cargo windows and a tailgate (#2 Aldridge Homestead).
Blender axes: X right, Y forward, Z up, origin on the ground at mid-wheelbase.
"""
from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np

from .body import BodySpec, Wheel
from .build import Cut, Design, Detail, rmax, xyz
from .sdf import curve, rounded_rect_2d, round_max, sd_box, smooth_min


@dataclass
class Rect:
    """A rectangle on the front or rear face: centre |x|, centre z, half sizes, corner radius.
    (Pop-up lamp covers use it on the hood: centre |x|, centre y, half sizes.)"""
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
    # Body style
    style: str = "sedan"          # sedan | coupe | hatch | wagon (see the module docstring)
    door_rear_y: float = -0.62    # coupe / hatch: rear edge of the (only) door
    c_pillar_y: float = -1.55     # wagon: the pillar between the rear-door glass and the cargo glass
    c_pillar_w: float = 0.05
    tailgate_bottom: float = 0.0  # hatch / wagon: lower edge of the tailgate (0 = just above the rear bumper)
    hood_bulge: tuple = ()        # (height, half width, y from, y to): a power bulge on the hood
    # Heights
    sill: float = 0.22            # rocker bottom at mid-wheelbase
    nose_top: float = 0.82
    nose_bottom: float = 0.36
    cowl: float = 0.92            # hood height at the windscreen base
    belt: float = 0.92            # window sill (door top)
    deck: float = 0.95            # boot lid height; hatch / wagon: the rear glass's lower edge
    tail_top: float = 0.92
    tail_bottom: float = 0.40
    roof: float = 1.40
    roof_rear_z: float = 0.0      # roof height where it starts down to the rear glass (0 = flat at `roof`)
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
    rim_style: str = "slot"       # turbine | slot | dish | mesh | cover (a full wheel cover over a plain rim)
    # Doors and pillars
    a_pillar: float = 0.06
    c_pillar: float = 0.08        # backlight inset from the cabin side
    b_pillar_y: float = -0.12
    b_pillar_w: float = 0.035
    door_split_y: float = -0.14   # front door rear edge = rear door front edge
    door_front_y: float = 0.50
    c_glass_y: float = -1.05      # rear end of the side glass (at the belt)
    c_glass_slant: float = 0.0    # the side glass's rear edge moves forward this much per metre up (a raked C pillar)
    c_door_y: float = -1.12       # rear door rear edge above the arch
    # Lights and front end
    headlights: list = field(default_factory=list)
    popups: list = field(default_factory=list)       # pop-up headlamp covers on the hood (Rect: |x|, y, hx, hy)
    grille: Rect | None = None
    indicators_front: list = field(default_factory=list)
    tail_lights: list = field(default_factory=list)
    tail_amber: list = field(default_factory=list)
    tail_clear: list = field(default_factory=list)
    grille_bars: str = "vertical"     # vertical | horizontal | eggcrate
    grille_material: str = "chrome"
    bumper_indicators: tuple = ()     # (|x| from, |x| to): amber lenses set in the front bumper corners
    fog_lamps: list = field(default_factory=list)    # lamp pods in front of the face (Rect: |x|, z, hx, hz)
    fog_proud: float = 0.09
    fog_round: bool = True
    # Bumpers and trim
    bumper_material: str = "chrome"
    bumper_z: float = 0.46
    bumper_half: float = 0.07
    bumper_proud: float = 0.06
    bumper_strip: bool = True         # rubber insert strip along the bumper
    bumper_wrap: float = 0.03         # how far the bumper ends reach past the wheel arches
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
    spoiler: str = ""                 # "" | "lip" (on the boot lid) | "wing" (on stalks) | "roof" (over the hatch glass)
    spoiler_material: str = "paint"
    roof_rack: bool = False
    wood_panels: bool = False         # wagon: simulated wood on the sides and the tailgate (material "wood")
    wood_z: tuple = (0.42, 0.0)       # lower, upper edge of the wood (0 = 0.07 below the belt)
    exhaust: list = field(default_factory=lambda: [0.42])   # tail-pipe x positions (negative = left side)
    exhaust_radius: float = 0.025

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

    @property
    def long_roof(self):
        return self.style in ("hatch", "wagon")

    @property
    def four_doors(self):
        return self.style in ("sedan", "wagon")

    def spec(self) -> BodySpec:
        N, T, FA, RA = self.NOSE, self.TAIL, self.FA, self.RA
        hw = self.half_width
        front_part = [(self.cowl_y - 0.30, self.belt), (self.cowl_y, self.cowl),
                      (FA, 0.5 * (self.cowl + self.nose_top) + 0.012), (N - 0.10, self.nose_top + 0.004), (N, self.nose_top)]
        if self.long_roof:
            # The roof covers the tail: the lower body stays at belt height back to the tail face.
            top = [(T, self.tail_top), (T + 0.22, self.belt)] + front_part
        else:
            top = [(T, self.tail_top), (T + 0.18, self.deck), (self.deck_y - 0.12, self.deck),
                   (self.deck_y + 0.5, self.belt)] + front_part     # the belt kicks up to the deck under the C pillar
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
                (self.roof_rear_y + 0.14, self.roof_rear_z or self.roof),
                (self.roof_rear_y, (self.roof_rear_z or self.roof) - 0.022),
                (self.deck_y, self.deck - 0.004)]
        if not self.long_roof:
            roof.append((self.deck_y - 0.35, self.deck - 0.16))
        r = self.wheel_radius
        return BodySpec(
            name=self.name, top=top, bottom=bottom, half_width=half_width, front=front, rear=rear,
            plan_radius=[(T, self.plan_rear), (RA, self.plan_rear), (FA, self.plan_front), (N, self.plan_front)],
            shoulder_radius=self.shoulder, bottom_radius=self.bottom_radius,
            crease_z=self.crease_z, crease_in_up=self.crease_up, crease_in_down=self.crease_down,
            crease_radius=0.010, top_crown=self.top_crown, hood_bulge=self.hood_bulge,
            roof=roof, cabin_front=self.cowl_y + 0.5,
            cabin_rear=T + self.rear_lean + 0.004 if self.long_roof else self.deck_y - 0.5, belt=self.belt,
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

        def on_front(p):          # the face leans back by front_lean at its top
            return (N - 0.10 - self.front_lean) - p[:, 1]

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
            d = rmax(0.05, ds + self.c_pillar, p[:, 1] - (self.roof_rear_y - 0.025), 0.022 - low(p))
            if self.long_roof:       # the glass stops above the tailgate's lower panel
                d = round_max(d, (self.deck + 0.03) - p[:, 2], 0.04)
            return d

        def side_dlo(p):
            ds, dt, dl = cterms(*xyz(p))
            rear_edge = self.c_glass_y + self.c_glass_slant * (p[:, 2] - self.belt)
            return rmax(0.03, dt + 0.034, 0.028 - low(p), -ds - 0.025, rear_edge - p[:, 1])

        def b_pillar(p):
            return np.abs(p[:, 1] - self.b_pillar_y) - self.b_pillar_w

        def c_pillar(p):
            return np.abs(p[:, 1] - self.c_pillar_y) - self.c_pillar_w

        # Panels
        def hood(p):
            x, y, z = xyz(p)
            return rmax(0.025, np.abs(x) - (hw - 0.075), (self.cowl_y + 0.08) - y, y - (N - 0.035), on_top(p))

        def trunk(p):
            x, y, z = xyz(p)
            return rmax(0.025, np.abs(x) - (hw - 0.075), y - (self.deck_y - 0.07), (T + 0.035) - y, on_top(p))

        lamps_x = [r.x - r.hx for r in self.tail_lights if r.mirrored and r.x - r.hx > 0.2]
        tg_hw = min(lamps_x + [hw - 0.10]) - 0.02
        tg_bottom = self.tailgate_bottom or (self.bumper_z + self.bumper_half + 0.03)

        def tailgate(p):
            x, y, z = xyz(p)
            ds, dt, dl = cterms(x, y, z)
            # full glass width up top, narrowing between the tail lamps below the glass
            between_lamps = np.minimum(np.abs(x) - tg_hw, (self.deck + 0.01) - z)
            return rmax(0.025, ds + (self.c_pillar - 0.035), between_lamps, y - (self.roof_rear_y + 0.035),
                        tg_bottom - z)

        door_front = curve([(self.belt - 0.06, self.door_front_y), (self.belt + 0.08, self.cowl_y - 0.08)])
        arch_top = self.wheel_radius + self.arch_radius
        door_rear = curve([(arch_top - 0.10, RA + self.arch_radius + 0.07), (arch_top + 0.05, self.c_door_y)])

        def side_panel(p):
            ds, dt, dl = cterms(*xyz(p))
            return np.maximum(dt + 0.012, -ds - 0.03)

        front_door_rear = self.door_split_y if self.four_doors else self.door_rear_y

        def front_doors(p):
            x, y, z = xyz(p)
            return rmax(0.025, y - door_front(z), front_door_rear - y, (self.sill + 0.07) - z, side_panel(p))

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

        def popup(p):
            x, y, z = xyz(p)
            d = np.min([rounded_rect_2d(np.abs(x), y, r.x, r.z, r.hx, r.hz, r.r) for r in self.popups], axis=0)
            return round_max(d, on_top(p), 0.005)

        side = lambda s: (lambda p: -s * p[:, 0])                     # noqa: E731

        cuts = [
            Cut("Wheel_Wells", wheel_wells, "trim", "wheel_well", parent="Body", flange=False),
            Cut("Underbody", underbody, "trim", "underbody", parent="Body", flange=False),
            Cut("Glass_Front", windscreen, "glass", "glass"),
            Cut("Glass_Rear", backlight, "glass", "glass"),
            Cut("Glass_Side", side_dlo, "glass", "glass"),
            Cut("Pillar_B", b_pillar, "trim", "trim_black", source="Glass_Side", parent="Body"),
        ]
        if self.style == "wagon":
            cuts.append(Cut("Pillar_C", c_pillar, "trim", "trim_black", source="Glass_Side", parent="Body"))
        if self.vinyl_roof:
            cuts.append(Cut("Vinyl", vinyl, "trim", "vinyl", parent="Body"))
        cuts += [
            Cut("Hood", hood),
            Cut("Trunk", tailgate if self.long_roof else trunk),
            Cut("Doors_F", front_doors),
        ]
        if self.four_doors:
            cuts.append(Cut("Doors_R", rear_doors))
        cuts.append(Cut("Fenders", fenders))
        if self.popups:
            cuts.append(Cut("Popup_Covers", popup, source="Hood", parent="Hood"))   # they stay with the hood
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

        wood_splits = {}
        if self.wood_panels:
            wz0 = self.wood_z[0]
            wz1 = self.wood_z[1] or (self.belt - 0.07)

            def wood(p):
                x, y, z = xyz(p)
                return rmax(0.02, wz0 - z, z - wz1, (hw - 0.07) - np.abs(x), 0.05 - P["arch_cut"](x, y, z),
                            y - (N - 0.32), (T + 0.16) - y)

            def wood_gate(p):
                x, y, z = xyz(p)
                return rmax(0.02, wz0 - z, z - wz1, np.abs(x) - (tg_hw - 0.05), p[:, 1] - (T + 0.12))
            cuts += [Cut("Wood_Body", wood, "trim", "wood", parent="Body"),
                     Cut("Wood_Fenders", wood, "trim", "wood", source="Fenders"),
                     Cut("Wood_DoorsF", wood, "trim", "wood", source="Doors_F"),
                     Cut("Wood_Trunk", wood_gate, "trim", "wood", source="Trunk", parent="Trunk")]
            wood_splits = {"Wood_Fenders": [("Wood_FR", side(+1), "Fender_FR"), ("Wood_FL", side(-1), "Fender_FL")],
                           "Wood_DoorsF": [("Wood_DFR", side(+1), "Door_FR"), ("Wood_DFL", side(-1), "Door_FL")]}
            if self.four_doors:
                cuts.append(Cut("Wood_DoorsR", wood, "trim", "wood", source="Doors_R"))
                wood_splits["Wood_DoorsR"] = [("Wood_DRR", side(+1), "Door_RR"), ("Wood_DRL", side(-1), "Door_RL")]

        bp, cp = self.b_pillar_y, self.c_pillar_y
        if self.style == "wagon":
            glass = [("Glass_FR", lambda p: np.maximum(-p[:, 0], bp - p[:, 1])),
                     ("Glass_FL", lambda p: np.maximum(p[:, 0], bp - p[:, 1])),
                     ("Glass_RR", lambda p: np.maximum(-p[:, 0], cp - p[:, 1])),
                     ("Glass_RL", lambda p: np.maximum(p[:, 0], cp - p[:, 1])),
                     ("Glass_QR", side(+1)), ("Glass_QL", side(-1))]
        elif self.four_doors:
            glass = [("Glass_FR", lambda p: np.maximum(-p[:, 0], bp - p[:, 1])),
                     ("Glass_FL", lambda p: np.maximum(p[:, 0], bp - p[:, 1])),
                     ("Glass_RR", side(+1)), ("Glass_RL", side(-1))]
        else:      # two doors: door glass, then a fixed quarter window (not part of the door)
            glass = [("Glass_FR", lambda p: np.maximum(-p[:, 0], bp - p[:, 1])),
                     ("Glass_FL", lambda p: np.maximum(p[:, 0], bp - p[:, 1])),
                     ("Glass_QR", side(+1)), ("Glass_QL", side(-1))]
        splits = {
            "Doors_F": [("Door_FR", side(+1)), ("Door_FL", side(-1))],
            "Fenders": [("Fender_FR", side(+1)), ("Fender_FL", side(-1))],
            "Glass_Side": glass,
        }
        if self.four_doors:
            splits["Doors_R"] = [("Door_RR", side(+1)), ("Door_RL", side(-1))]
        splits.update(wood_splits)
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
        bz, bh, bp_ = self.bumper_z, self.bumper_half, self.bumper_proud
        arch_f = self.FA + self.arch_radius + self.bumper_wrap
        arch_r = RA - self.arch_radius - self.bumper_wrap
        details = [
            kit.band("Bumper_F", body, P, z=bz, half_height=bh, proud=bp_, y_range=(arch_f, 9.0), radius=0.02,
                     groove=not self.bumper_strip, material=self.bumper_material),
            kit.band("Bumper_R", body, P, z=bz, half_height=bh, proud=bp_, y_range=(-9.0, arch_r), radius=0.02,
                     groove=not self.bumper_strip, material=self.bumper_material),
        ]
        if self.bumper_strip:
            details += [
                kit.band("Bumper_F_Strip", body, P, z=bz, half_height=bh * 0.3, proud=bp_ + 0.008,
                         y_range=(arch_f + 0.02, 9.0), radius=0.008, end_radius=0.01, groove=False,
                         material="rubber", voxel=0.004, target=6000),
                kit.band("Bumper_R_Strip", body, P, z=bz, half_height=bh * 0.3, proud=bp_ + 0.008,
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
        if self.bumper_indicators:
            details.append(kit.band("Light_Indicator_F", body, P, z=bz, half_height=bh * 0.45, proud=bp_ + 0.004,
                                    y_range=(arch_f, 9.0), x_range=self.bumper_indicators, radius=0.006,
                                    end_radius=0.006, groove=False, material="lens_amber", voxel=0.003, target=2000))
        if self.fog_lamps:
            details += kit.lamp_pods("Light_Fog", self.fog_lamps, P, proud=self.fog_proud, round_lamp=self.fog_round)
        ph = self.plate_front_half
        details.append(kit.plate("Plate_F", P, z=bz, front=True, half=ph, offset=bp_))
        if self.plate_rear_z > 0:
            details.append(kit.plate("Plate_R", P, z=self.plate_rear_z, front=False, half=ph, offset=0.0))
        else:
            details.append(kit.plate("Plate_R", P, z=bz, front=False, half=ph, offset=bp_))
        handles = [("F", front_door_rear + 0.12)]
        if self.four_doors:
            handles.append(("R", self.c_door_y + 0.13))
        for nm, hy in handles:
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
        if self.long_roof:         # a rear wiper parked across the bottom of the tailgate glass
            y0 = self.deck_y + 0.07
            y1 = y0 + 0.30 * (self.roof_rear_y - self.deck_y)
            slope = np.arctan2(self.roof - self.deck, self.roof_rear_y - self.deck_y)
            details.append(kit.wiper("Wiper_Rear", on_screen(0.0, y0), on_screen(-0.40, y1),
                                     normal=(0.0, -np.sin(slope), np.cos(slope))))

        # Spoilers ride on the boot lid / tailgate, so they come off with it.
        roof_w = wc - (self.roof - self.belt) * np.tan(np.radians(self.tumblehome_deg))
        if self.spoiler == "wing":
            yw = T + 0.16
            deck_z = float(P["top"](np.array([0.0]), np.array([yw]))[0])
            details.append(kit.wing("Spoiler", body, y=yw, z=deck_z + 0.11, deck_z=deck_z, half_span=hw - 0.10,
                                    stalk_x=hw - 0.32, material=self.spoiler_material))
        elif self.spoiler == "lip":
            yl = T + 0.07
            deck_z = float(P["top"](np.array([0.0]), np.array([yl]))[0])
            details.append(kit.lip("Spoiler", body, y=yl, z=deck_z, half_span=hw - 0.10, material=self.spoiler_material))
        elif self.spoiler == "roof":
            rz = (self.roof_rear_z or self.roof) - 0.022
            details.append(kit.roof_flap("Spoiler", body, y=self.roof_rear_y + 0.03, z=rz - 0.004,
                                         half_span=roof_w - 0.05, material=self.spoiler_material))
        if self.roof_rack:
            details.append(kit.roof_rack("Roof_Rack", body, x=roof_w - 0.10, y0=self.b_pillar_y - 0.05,
                                         y1=self.roof_rear_y + 0.12,
                                         z=self.roof + 0.05))
        if self.exhaust:
            ez = min(self.tail_bottom, bz - bh) - 0.07
            details.append(kit.exhaust("Exhaust", body, xs=self.exhaust, y_tip=T - 0.01, z=ez,
                                       radius=self.exhaust_radius))
        details += kit.wheels(fn.spec.wheels, style=self.rim_style, rim_inch=self.rim_inch)
        return Design(cuts, details, splits)


# The template's old name, used by the first sedans.
Template = Sedan
