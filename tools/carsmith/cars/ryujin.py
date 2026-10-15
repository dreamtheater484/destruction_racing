"""#11 Ryujin Raiden GTS: mid-80s Japanese turbo sedan - a hard-edged box, flat hood, a wing on the boot.

A morph of the 3-box sedan template (carsmith/templates.py). 4.60 m long, 1.68 m wide, 1.38 m high,
2.62 m wheelbase; wide rectangular lamps either side of a black slat grille, black bumpers, segmented tail
lamps, 15" slotted alloys, a boot wing on two stalks.
"""
from ..templates import Rect, Sedan

CAR = Sedan(
    name="ryujin", length=4.60, half_width=0.84, wheelbase=2.62, front_overhang=0.88,
    sill=0.23, nose_top=0.74, nose_bottom=0.32, cowl=0.90, belt=0.92, deck=0.98, tail_top=0.96,
    tail_bottom=0.44, roof=1.38,
    cowl_y=0.90, header_y=0.24, roof_rear_y=-0.98, deck_y=-1.42,
    tumblehome_deg=12.0, ledge=0.05, roof_crown=0.06, roof_radius=0.035,
    plan_front=0.05, plan_rear=0.05, shoulder=0.016, bottom_radius=0.06,
    crease_z=0.68, crease_up=0.03, crease_down=0.06, top_crown=0.02, front_lean=0.02, rear_lean=0.02,
    wheel_radius=0.30, wheel_width=0.205, track_front=1.43, track_rear=1.43, arch_radius=0.34,
    rim_inch=15, rim_style="slot",
    a_pillar=0.055, c_pillar=0.08, b_pillar_y=-0.10, b_pillar_w=0.035, door_split_y=-0.12,
    door_front_y=0.92, c_glass_y=-0.98, c_door_y=-1.02,
    headlights=[Rect(0.56, 0.645, 0.19, 0.055, 0.01)],
    grille=Rect(0.0, 0.645, 0.34, 0.055, 0.01), grille_bars="horizontal", grille_material="trim_black",
    bumper_indicators=(0.60, 0.80),
    tail_lights=[Rect(0.60, 0.82, 0.22, 0.075, 0.01)],
    tail_amber=[Rect(0.75, 0.82, 0.07, 0.072, 0.004)],
    tail_clear=[Rect(0.45, 0.82, 0.06, 0.072, 0.004)],
    bumper_material="bumper", bumper_z=0.46, bumper_half=0.08, bumper_proud=0.06, bumper_strip=True,
    side_trim="rub", side_trim_z=0.55, mirror_material="trim_black",
    handle_z=0.84, handle_material="trim_black", plate_front_half=(0.26, 0.055), plate_rear_z=0.0,
    spoiler="wing", spoiler_material="paint",
    exhaust=[0.48], exhaust_radius=0.03,
    interior_seat_y=0.20,
)
SPEC = CAR.spec()


def design(fn):
    return CAR.design(fn)
