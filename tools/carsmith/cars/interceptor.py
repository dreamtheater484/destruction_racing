"""#3 Columbia Interceptor GT: mid-80s American pony car - long hood with a power bulge, a big glass hatch, a wing.

A morph of the template's 3-door hatch (carsmith/templates.py) with a long, flat rear glass. 4.70 m long,
1.78 m wide, 1.28 m high, 2.56 m wheelbase; slim lamps in a sloped nose, a glass-only hatch over a full-width
tail-lamp panel, black bumpers, rectangular fog lamps, 16" turbine alloys, twin pipes.
"""
from ..templates import Rect, Sedan

CAR = Sedan(
    name="interceptor", style="hatch", length=4.70, half_width=0.89, wheelbase=2.56, front_overhang=1.00,
    sill=0.22, nose_top=0.66, nose_bottom=0.28, cowl=0.86, belt=0.88, deck=0.95, tail_top=0.93,
    tail_bottom=0.40, roof=1.28, roof_rear_z=1.25,
    cowl_y=0.36, header_y=-0.36, roof_rear_y=-1.04, deck_y=-1.98,
    tailgate_bottom=0.93, hood_bulge=(0.035, 0.30, 0.55, 1.95),
    tumblehome_deg=18.0, ledge=0.06, roof_crown=0.08, roof_radius=0.045,
    plan_front=0.12, plan_rear=0.07, shoulder=0.024, bottom_radius=0.06,
    crease_z=0.64, crease_up=0.04, crease_down=0.09, top_crown=0.03, front_lean=0.10, rear_lean=0.04,
    wheel_radius=0.32, wheel_width=0.225, track_front=1.52, track_rear=1.52, arch_radius=0.355,
    rim_inch=16, rim_style="turbine",
    a_pillar=0.055, c_pillar=0.09, b_pillar_y=-0.68, b_pillar_w=0.03, door_front_y=0.72, door_rear_y=-0.74,
    c_glass_y=-1.34, c_glass_slant=1.0,
    headlights=[Rect(0.58, 0.575, 0.20, 0.038, 0.01)],
    grille=Rect(0.0, 0.575, 0.30, 0.03, 0.008), grille_bars="horizontal", grille_material="trim_black",
    bumper_indicators=(0.64, 0.84),
    tail_lights=[Rect(0.53, 0.77, 0.31, 0.07, 0.012)],
    tail_amber=[Rect(0.78, 0.77, 0.07, 0.066, 0.004)],
    tail_clear=[Rect(0.27, 0.77, 0.05, 0.066, 0.004)],
    fog_lamps=[Rect(0.44, 0.33, 0.085, 0.03, 0.01)], fog_proud=0.06, fog_round=False,
    bumper_material="bumper", bumper_z=0.42, bumper_half=0.085, bumper_proud=0.055, bumper_strip=True,
    bumper_wrap=0.02,
    side_trim="rub", side_trim_z=0.50, mirror_material="trim_black",
    handle_z=0.79, handle_material="trim_black", plate_front_half=(0.26, 0.055), plate_rear_z=0.0,
    spoiler="wing", spoiler_material="paint",
    exhaust=[-0.50, 0.50], exhaust_radius=0.03,
    interior_seat_y=-0.25,
)
SPEC = CAR.spec()


def design(fn):
    return CAR.design(fn)
