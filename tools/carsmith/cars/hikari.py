"""#9 Hikari Kaze GT: mid-80s light rear-drive liftback - pop-up lamps, a long glass hatch, a lip on the tail.

A morph of the template's 3-door hatch (carsmith/templates.py) with a long, flat rear glass. 4.20 m long, 1.62 m
wide, 1.30 m high, 2.40 m wheelbase; pop-up headlamps closed in a low wedge nose, slim park lamps under them,
black bumpers, a full-width tail-lamp band, 14" turbine alloys.
"""
from ..templates import Rect, Sedan

CAR = Sedan(
    name="hikari", style="hatch", length=4.20, half_width=0.81, wheelbase=2.40, front_overhang=0.82,
    sill=0.22, nose_top=0.62, nose_bottom=0.28, cowl=0.84, belt=0.86, deck=0.96, tail_top=0.92,
    tail_bottom=0.40, roof=1.30, roof_rear_z=1.27,
    cowl_y=0.60, header_y=-0.12, roof_rear_y=-1.00, deck_y=-1.94,
    tumblehome_deg=18.0, ledge=0.05, roof_crown=0.07, roof_radius=0.045,
    plan_front=0.11, plan_rear=0.06, shoulder=0.024, bottom_radius=0.06,
    crease_z=0.64, crease_up=0.04, crease_down=0.09, top_crown=0.03, front_lean=0.12, rear_lean=0.04,
    wheel_radius=0.29, wheel_width=0.195, track_front=1.37, track_rear=1.36, arch_radius=0.33,
    rim_inch=14, rim_style="turbine",
    a_pillar=0.055, c_pillar=0.08, b_pillar_y=-0.50, b_pillar_w=0.03, door_front_y=0.70, door_rear_y=-0.58,
    c_glass_y=-1.34, c_glass_slant=0.9,
    popups=[Rect(0.53, 1.78, 0.16, 0.085, 0.01)],
    headlights=[Rect(0.53, 0.555, 0.15, 0.026, 0.01)],
    grille=Rect(0.0, 0.555, 0.26, 0.022, 0.008), grille_bars="horizontal", grille_material="trim_black",
    bumper_indicators=(0.52, 0.72),
    tail_lights=[Rect(0.56, 0.77, 0.22, 0.072, 0.012)],
    tail_amber=[Rect(0.70, 0.77, 0.08, 0.068, 0.004)],
    tail_clear=[Rect(0.405, 0.77, 0.06, 0.068, 0.004)],
    bumper_material="bumper", bumper_z=0.42, bumper_half=0.08, bumper_proud=0.05, bumper_strip=True,
    bumper_wrap=0.02,
    side_trim="rub", side_trim_z=0.52, mirror_material="trim_black",
    handle_z=0.78, handle_material="trim_black", plate_front_half=(0.24, 0.05), plate_rear_z=0.62,
    spoiler="lip", spoiler_material="paint",
    exhaust=[0.45], exhaust_radius=0.027,
    interior_seat_y=-0.15,
)
SPEC = CAR.spec()


def design(fn):
    return CAR.design(fn)
