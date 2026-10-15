"""#7 Volant 105 GTi: mid-80s French hot hatch - short, wide-tracked, steep tailgate, black plastic everywhere.

A morph of the template's 3-door hatch (carsmith/templates.py). 3.72 m long, 1.58 m wide, 1.35 m high,
2.42 m wheelbase; wide rectangular lamps, slim grille, wrap-around black bumpers with a red pinstripe insert,
round fog lamps, a roof spoiler over the tailgate glass, 14" slotted alloys.
"""
from ..templates import Rect, Sedan

CAR = Sedan(
    name="volant", style="hatch", length=3.72, half_width=0.79, wheelbase=2.42, front_overhang=0.72,
    sill=0.24, nose_top=0.68, nose_bottom=0.30, cowl=0.86, belt=0.88, deck=0.93, tail_top=0.88,
    tail_bottom=0.38, roof=1.35, roof_rear_z=1.31,
    cowl_y=0.84, header_y=0.14, roof_rear_y=-1.33, deck_y=-1.70,
    tumblehome_deg=15.0, ledge=0.05, roof_crown=0.07, roof_radius=0.045,
    plan_front=0.07, plan_rear=0.06, shoulder=0.024, bottom_radius=0.06,
    crease_z=0.62, crease_up=0.05, crease_down=0.10, top_crown=0.03, front_lean=0.05, rear_lean=0.03,
    wheel_radius=0.29, wheel_width=0.185, track_front=1.40, track_rear=1.36, arch_radius=0.33,
    rim_inch=14, rim_style="slot",
    a_pillar=0.055, c_pillar=0.07, b_pillar_y=-0.58, b_pillar_w=0.03, door_front_y=0.80, door_rear_y=-0.52,
    c_glass_y=-1.18, c_glass_slant=0.75,
    headlights=[Rect(0.52, 0.58, 0.175, 0.055, 0.012)],
    grille=Rect(0.0, 0.585, 0.30, 0.042, 0.01), grille_bars="horizontal", grille_material="trim_black",
    bumper_indicators=(0.58, 0.74),
    tail_lights=[Rect(0.61, 0.73, 0.135, 0.10, 0.012)],
    tail_amber=[Rect(0.61, 0.795, 0.14, 0.035, 0.004)],
    tail_clear=[Rect(0.61, 0.665, 0.14, 0.03, 0.004)],
    fog_lamps=[Rect(0.47, 0.33, 0.052, 0.052, 0.05)], fog_proud=0.055,
    bumper_material="bumper", bumper_z=0.40, bumper_half=0.085, bumper_proud=0.05, bumper_strip=True,
    bumper_wrap=0.02,
    side_trim="rub", side_trim_z=0.47, mirror_material="trim_black",
    handle_z=0.80, handle_material="trim_black", plate_front_half=(0.24, 0.05), plate_rear_z=0.63,
    spoiler="roof", spoiler_material="paint",
    exhaust=[0.40], exhaust_radius=0.026,
    interior_seat_y=-0.10,
)
SPEC = CAR.spec()


def design(fn):
    return CAR.design(fn)
