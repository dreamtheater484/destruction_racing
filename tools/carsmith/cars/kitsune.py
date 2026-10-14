"""#12 Kitsune Rotary GS: early-80s Japanese rotary wedge coupe with pop-up lamps and a glass hatch.

Evokes the archetype (low wedge nose, very raked screen, fastback glass hatch, slim bumpers, 13" wheels),
not any one real car. 4.30 m long, 1.68 m wide, 1.26 m high, 2.42 m wheelbase.
"""
from ..body import BodySpec, Wheel

WB = 2.42
FA, RA = WB / 2, -WB / 2          # front / rear axle (y)
NOSE, TAIL = FA + 0.86, RA - 1.02  # 2.07 / -2.23

SPEC = BodySpec(
    name="kitsune",
    top=[(TAIL, 0.85), (-2.05, 0.865), (-1.6, 0.87), (-0.8, 0.845), (0.0, 0.825), (0.45, 0.795),
         (0.9, 0.745), (1.45, 0.665), (1.85, 0.60), (NOSE, 0.53)],
    bottom=[(TAIL, 0.38), (-1.95, 0.28), (-1.55, 0.20), (RA, 0.185), (0.0, 0.17), (FA, 0.185),
            (1.65, 0.20), (1.95, 0.26), (NOSE, 0.31)],
    half_width=[(TAIL, 0.80), (-2.0, 0.83), (-1.4, 0.84), (0.0, 0.838), (1.25, 0.835), (1.65, 0.81),
                (1.9, 0.75), (NOSE, 0.64)],
    front=[(0.25, NOSE - 0.06), (0.36, NOSE - 0.01), (0.45, NOSE), (0.52, NOSE - 0.025), (0.60, NOSE - 0.09)],
    rear=[(0.34, TAIL + 0.05), (0.48, TAIL + 0.005), (0.68, TAIL), (0.80, TAIL + 0.01), (0.88, TAIL + 0.04)],
    plan_radius=[(TAIL, 0.11), (-1.5, 0.11), (1.5, 0.19), (NOSE, 0.19)],
    shoulder_radius=0.026,
    bottom_radius=0.07,
    crease_z=0.60,
    crease_in_up=0.10,
    crease_in_down=0.15,
    crease_radius=0.010,
    top_crown=0.045,
    roof=[(0.50, 0.74), (0.32, 0.80), (0.10, 0.925), (-0.15, 1.065), (-0.40, 1.21), (-0.60, 1.255),
          (-0.90, 1.245), (-1.20, 1.17), (-1.55, 1.05), (-1.85, 0.94), (-2.05, 0.88), (TAIL, 0.86)],
    cabin_front=0.55,
    cabin_rear=TAIL + 0.13,
    belt=0.82,
    ledge=[(TAIL, 0.035), (-1.7, 0.05), (-1.0, 0.07), (0.0, 0.08), (0.3, 0.09), (0.55, 0.10)],
    tumblehome_deg=26.0,
    roof_crown=0.14,
    roof_radius=0.045,
    cabin_end_radius=0.06,
    belt_blend=0.008,
    wheels=[Wheel(y=FA, radius=0.295, width=0.20, track=1.44, arch_radius=0.335),
            Wheel(y=RA, radius=0.295, width=0.20, track=1.42, arch_radius=0.335)],
    arch_lip=0.02,
)


# ----------------------------------------------------------------------------- panels, glass, details

def design(fn):
    import numpy as np

    from ..build import Cut, Design, Detail, rmax, xyz
    from ..sdf import Field2D, curve, fillet_polygon, polygon_sdf_2d, rounded_rect_2d, round_max, round_min, sd_box, sd_capsule
    from .. import parts as kit

    P = fn.parts
    lower, cterms, T, W = P["lower"], P["cabin_terms"], P["T"], P["W"]

    def low(p):
        return lower(*xyz(p))

    def cab(p):
        return cterms(*xyz(p))

    def on_top(p, depth=0.04):            # lower-body top surface only
        x, y, z = xyz(p)
        return (P["top"](x, y) - depth) - z

    # Glass -----------------------------------------------------------------------------------------
    def windscreen(p):
        ds, dt, dl = cab(p)
        y = p[:, 1]
        return rmax(0.05, ds + 0.062, -0.36 - y, 0.022 - low(p))

    def side_dlo(p):
        ds, dt, dl = cab(p)
        y = p[:, 1]
        return rmax(0.03, dt + 0.034, 0.028 - low(p), -ds - 0.025, -1.66 - y)

    def b_pillar(p):
        return np.abs(p[:, 1] + 0.56) - 0.034

    def hatch_glass(p):
        ds, dt, dl = cab(p)
        y = p[:, 1]
        return rmax(0.05, ds + 0.062, y + 0.76, -1.95 - y)

    # Panels ----------------------------------------------------------------------------------------
    hood_outline = fillet_polygon([(0.772, 0.43, 0.03), (0.772, 1.46, 0.03), (0.475, 1.50, 0.02), (0.462, 1.955, 0.06),
                                   (-0.462, 1.955, 0.06), (-0.475, 1.50, 0.02), (-0.772, 1.46, 0.03), (-0.772, 0.43, 0.03)])
    hood2d = Field2D(lambda u, v: polygon_sdf_2d(u, v, hood_outline), -0.95, 0.95, 0.25, 2.15, 0.003)

    def hood(p):
        return round_max(hood2d(p[:, 0], p[:, 1]), on_top(p), 0.005)

    def popup(p):
        return round_max(rounded_rect_2d(np.abs(p[:, 0]), p[:, 1], 0.627, 1.70, 0.123, 0.163, 0.025), on_top(p), 0.005)

    door_front = curve([(0.76, 0.36), (0.90, 0.05)])     # straight up, then raked up into the A-pillar foot

    def door(p):
        ds, dt, dl = cab(p)
        x, y, z = xyz(p)
        return rmax(0.025, y - door_front(z), -0.605 - y, 0.25 - z, dt + 0.012, -ds - 0.03)

    def hatch(p):
        ds, dt, dl = cab(p)
        y = p[:, 1]
        return rmax(0.03, ds + 0.026, y + 0.70, -2.17 - y)

    fender_inner = curve([(1.40, 0.745), (1.52, 0.452)])

    def fender(p):
        x, y, z = xyz(p)
        return rmax(0.02, (door_front(z) - 0.005) - y, y - 1.985, 0.28 - z, fender_inner(y) - np.abs(x))

    # Trim regions ----------------------------------------------------------------------------------
    def tail_band(p):
        x, y, z = xyz(p)
        return rmax(0.015, np.abs(x) - 0.745, np.abs(z - 0.685) - 0.08, y - (TAIL + 0.09))

    def tail_garnish(p):                  # black centre panel between the lamps
        return np.abs(p[:, 0]) - 0.30

    def tail_amber(p):                    # inner indicator sections
        return np.abs(np.abs(p[:, 0]) - 0.385) - 0.07

    def tail_reverse(p):                  # small clear reversing lamp low in each cluster
        x, z = np.abs(p[:, 0]), p[:, 2]
        return rmax(0.006, np.abs(x - 0.60) - 0.075, z - 0.655)

    def nose_slot(p):
        x, y, z = xyz(p)
        return rmax(0.008, np.abs(x) - 0.43, np.abs(z - 0.488) - 0.014, (NOSE - 0.12) - y)

    def wheel_wells(p):
        return P["arch_cut"](*xyz(p)) - 0.006

    def underbody(p):
        x, y, z = xyz(p)
        return z - (P["B"](y) + 0.028)

    cuts = [
        Cut("Wheel_Wells", wheel_wells, "trim", "wheel_well", parent="Body", flange=False),
        Cut("Underbody", underbody, "trim", "underbody", parent="Body", flange=False),
        Cut("Glass_Front", windscreen, "glass", "glass"),
        Cut("Glass_Side", side_dlo, "glass", "glass"),
        Cut("Glass_Rear", hatch_glass, "glass", "glass"),
        Cut("Pillar_B", b_pillar, "trim", "trim_black", source="Glass_Side", parent="Body"),
        Cut("Hood", hood),
        Cut("Light_Head_Covers", popup),
        Cut("Doors", door),
        Cut("Trunk", hatch),
        Cut("Fenders", fender),
        Cut("Light_Tail", tail_band, "glass", "lens_red", recess=0.004),
        Cut("Tail_Garnish", tail_garnish, "trim", "trim_black", source="Light_Tail", parent="Body"),
        Cut("Tail_Amber", tail_amber, "glass", "lens_amber", source="Light_Tail", recess=0.004),
        Cut("Tail_Reverse", tail_reverse, "glass", "lens_clear", source="Light_Tail", recess=0.004),
        Cut("Nose_Slot", nose_slot, "glass", "trim_black", recess=0.012, parent="Body"),
    ]
    side = lambda s: (lambda p: -s * p[:, 0])       # noqa: E731  negative on side s (+1 right, -1 left)
    splits = {
        "Doors": [("Door_FR", side(+1)), ("Door_FL", side(-1))],
        "Fenders": [("Fender_FR", side(+1)), ("Fender_FL", side(-1))],
        "Light_Head_Covers": [("Light_Head_R", side(+1)), ("Light_Head_L", side(-1))],
        "Glass_Side": [("Glass_FR", lambda p: np.maximum(-p[:, 0], -0.56 - p[:, 1])),
                       ("Glass_FL", lambda p: np.maximum(p[:, 0], -0.56 - p[:, 1])),
                       ("Glass_RR", side(+1)), ("Glass_RL", side(-1))],
        "Light_Tail": [("Light_Tail_R", side(+1)), ("Light_Tail_L", side(-1))],
        "Tail_Amber": [("Amber_R", side(+1), "Light_Tail_R"), ("Amber_L", side(-1), "Light_Tail_L")],
        "Tail_Reverse": [("Reverse_R", side(+1), "Light_Tail_R"), ("Reverse_L", side(-1), "Light_Tail_L")],
    }

    body = fn
    details = [
        kit.bumper("Bumper_F", body, P, z=0.405, half_height=0.052, proud=0.032, y_from=1.56, front=True),
        kit.bumper("Bumper_R", body, P, z=0.43, half_height=0.05, proud=0.03, y_from=-1.90, front=False),
        kit.band("Trim_Side", body, P, z=0.42, half_height=0.018, proud=0.010, y_range=(-0.80, 0.80),
                 radius=0.008, end_radius=0.012, groove=False, material="bumper", voxel=0.004, target=4000),
        kit.band("Light_Indicator_F", body, P, z=0.405, half_height=0.021, proud=0.035, y_range=(1.56, 9.0),
                 x_range=(0.46, 0.63), radius=0.006, end_radius=0.006, groove=False, material="lens_amber",
                 voxel=0.003, target=2000),
        kit.plate("Plate_F", P, z=0.405, front=True, half=(0.235, 0.05), offset=0.035),
        kit.plate("Plate_R", P, z=0.675, front=False, half=(0.235, 0.05), offset=0.0),
        kit.handle("Handle_R", fn, -0.47, 0.775, +1, material="trim_black", length=0.10),
        kit.handle("Handle_L", fn, -0.47, 0.775, -1, material="trim_black", length=0.10),
        kit.mirror("Mirror_R", +1, (0.865, 0.07, 0.885), stalk_from=0.735),
        kit.mirror("Mirror_L", -1, (0.865, 0.07, 0.885), stalk_from=0.735),
        kit.interior("Interior", body, P, dash_y=(0.02, 0.36), seat_y=-0.42, belt=SPEC.belt, rhd=False),
    ]
    def on_screen(x, y):                  # a point on the windscreen surface
        return (x, y, float(P["roof"](y)) - SPEC.roof_crown * x * x)
    details += [kit.wiper("Wiper_L", on_screen(-0.03, 0.27), on_screen(-0.60, 0.17)),
                kit.wiper("Wiper_R", on_screen(0.06, 0.27), on_screen(0.62, 0.17))]
    details += kit.wheels(SPEC.wheels, style="turbine")
    return Design(cuts, details, splits)
