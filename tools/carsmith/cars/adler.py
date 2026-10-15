"""#6 Adler Falke 2.7i: late-80s German compact sports coupe - quad round lamps, a sharp shoulder, a lip on the boot.

A morph of the template's 2-door coupe (carsmith/templates.py). 4.32 m long, 1.64 m wide, 1.36 m high,
2.57 m wheelbase; four round headlamps in black bezels around a slim grille, wide segmented tail lamps,
body-coloured bumpers with a black insert, 15" cross-spoke mesh alloys, twin tail pipes.
"""
from ..templates import Rect, Sedan

CAR = Sedan(
    name="adler", style="coupe", length=4.32, half_width=0.82, wheelbase=2.57, front_overhang=0.80,
    sill=0.24, nose_top=0.70, nose_bottom=0.30, cowl=0.88, belt=0.90, deck=0.96, tail_top=0.94,
    tail_bottom=0.42, roof=1.36,
    cowl_y=0.88, header_y=0.26, roof_rear_y=-0.84, deck_y=-1.28,
    tumblehome_deg=15.0, ledge=0.06, roof_crown=0.08, roof_radius=0.04,
    plan_front=0.06, plan_rear=0.06, shoulder=0.02, bottom_radius=0.06,
    crease_z=0.72, crease_up=0.04, crease_down=0.07, top_crown=0.03, front_lean=0.02, rear_lean=0.03,
    wheel_radius=0.30, wheel_width=0.205, track_front=1.41, track_rear=1.42, arch_radius=0.335,
    rim_inch=15, rim_style="mesh",
    a_pillar=0.055, c_pillar=0.09, b_pillar_y=-0.20, b_pillar_w=0.03, door_front_y=0.88, door_rear_y=-0.26,
    c_glass_y=-0.80, c_glass_slant=0.55,
    headlights=[Rect(0.40, 0.605, 0.072, 0.072, 0.072), Rect(0.57, 0.605, 0.072, 0.072, 0.072)],
    bezel_material="trim_black",
    grille=Rect(0.0, 0.605, 0.27, 0.06, 0.01), grille_bars="horizontal", grille_material="trim_black",
    bumper_indicators=(0.56, 0.76),
    tail_lights=[Rect(0.57, 0.80, 0.21, 0.065, 0.01)],
    tail_amber=[Rect(0.70, 0.80, 0.08, 0.062, 0.004)],
    tail_clear=[Rect(0.42, 0.80, 0.055, 0.062, 0.004)],
    bumper_material="paint", bumper_z=0.45, bumper_half=0.075, bumper_proud=0.06, bumper_strip=True,
    bumper_wrap=0.03,
    side_trim="rub", side_trim_z=0.50, mirror_material="trim_black",
    handle_z=0.82, handle_material="trim_black", plate_front_half=(0.26, 0.055), plate_rear_z=0.0,
    spoiler="lip", spoiler_material="paint",
    exhaust=[-0.40, -0.48], exhaust_radius=0.026,
    interior_seat_y=0.10,
)
SPEC = CAR.spec()


def design(fn):
    return CAR.design(fn)
