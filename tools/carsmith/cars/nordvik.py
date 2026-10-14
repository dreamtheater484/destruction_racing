"""#5 Nordvik Fjord DL: early-80s Scandinavian "brick" sedan - upright glasshouse, square everything.

A morph of the 3-box sedan template (carsmith/templates.py). 4.80 m long, 1.71 m wide, 1.43 m high,
2.64 m wheelbase; big rectangular lamps, egg-crate grille, park-bench bumpers, tall tail lamps.
"""
from ..templates import Rect, Sedan

CAR = Sedan(
    name="nordvik", length=4.80, half_width=0.855, wheelbase=2.64, front_overhang=0.88,
    sill=0.23, nose_top=0.80, nose_bottom=0.38, cowl=0.93, belt=0.945, deck=0.95, tail_top=0.94,
    tail_bottom=0.42, roof=1.43,
    cowl_y=0.40, header_y=-0.18, roof_rear_y=-1.30, deck_y=-1.62,
    tumblehome_deg=13.0, ledge=0.05, roof_crown=0.06, roof_radius=0.04,
    plan_front=0.05, plan_rear=0.05, shoulder=0.022, bottom_radius=0.06,
    crease_z=0.70, crease_up=0.03, crease_down=0.05, top_crown=0.02, front_lean=0.02, rear_lean=0.02,
    wheel_radius=0.31, wheel_width=0.185, track_front=1.42, track_rear=1.36, arch_radius=0.36,
    rim_inch=14, rim_style="dish",
    a_pillar=0.05, c_pillar=0.07, b_pillar_y=-0.30, b_pillar_w=0.035, door_split_y=-0.32,
    door_front_y=0.46, c_glass_y=-1.17, c_door_y=-1.20,
    headlights=[Rect(0.59, 0.69, 0.155, 0.075, 0.012)],
    grille=Rect(0.0, 0.69, 0.39, 0.10, 0.012),
    grille_bars="eggcrate", grille_material="trim_black",
    indicators_front=[Rect(0.79, 0.69, 0.028, 0.07, 0.008)],
    tail_lights=[Rect(0.70, 0.72, 0.13, 0.16, 0.012)],
    tail_amber=[Rect(0.70, 0.79, 0.14, 0.045, 0.004)],
    tail_clear=[Rect(0.70, 0.635, 0.14, 0.03, 0.004)],
    bumper_material="bumper", bumper_z=0.47, bumper_half=0.08, bumper_proud=0.10, bumper_strip=True,
    side_trim="rub", side_trim_z=0.55, vinyl_roof=False, mirror_material="trim_black",
    handle_z=0.84, handle_material="trim_black", plate_front_half=(0.26, 0.055), plate_rear_z=0.0,
    interior_seat_y=-0.10,
)
SPEC = CAR.spec()


def design(fn):
    return CAR.design(fn)
