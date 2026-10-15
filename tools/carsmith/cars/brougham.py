"""#1 Aldridge Brougham 400: late-70s US full-size sedan - long hood, formal roof, chrome everywhere.

A morph of the 3-box sedan template (carsmith/templates.py). 5.50 m long, 1.99 m wide, 1.40 m high,
2.95 m wheelbase; quad rectangular headlamps, vertical-bar grille, vinyl roof, 15" wheel covers.
"""
from ..templates import Rect, Sedan

CAR = Sedan(
    name="brougham", length=5.50, half_width=0.995, wheelbase=2.95, front_overhang=1.05,
    sill=0.24, nose_top=0.86, nose_bottom=0.40, cowl=0.95, belt=0.93, deck=0.96, tail_top=0.93,
    tail_bottom=0.42, roof=1.40,
    cowl_y=0.48, header_y=-0.18, roof_rear_y=-1.22, deck_y=-1.62,
    tumblehome_deg=12.0, ledge=0.075, roof_crown=0.08, roof_radius=0.035,
    plan_front=0.06, plan_rear=0.06, shoulder=0.015, bottom_radius=0.06,
    crease_z=0.62, crease_up=0.02, crease_down=0.05, top_crown=0.02, front_lean=0.0, rear_lean=0.02,
    wheel_radius=0.355, wheel_width=0.215, track_front=1.59, track_rear=1.58, arch_radius=0.40,
    rim_inch=15, rim_style="cover",
    a_pillar=0.06, c_pillar=0.11, b_pillar_y=-0.20, b_pillar_w=0.04, door_split_y=-0.22,
    door_front_y=0.55, c_glass_y=-1.05, c_door_y=-1.10,
    headlights=[Rect(0.53, 0.70, 0.085, 0.055, 0.008), Rect(0.73, 0.70, 0.085, 0.055, 0.008)],
    grille=Rect(0.0, 0.69, 0.36, 0.105, 0.01),
    grille_bars="vertical", grille_material="chrome",
    indicators_front=[Rect(0.885, 0.70, 0.055, 0.05, 0.01)],
    tail_lights=[Rect(0.62, 0.70, 0.30, 0.085, 0.012)],
    tail_clear=[Rect(0.40, 0.70, 0.075, 0.09, 0.006)],
    bumper_material="chrome", bumper_z=0.47, bumper_half=0.085, bumper_proud=0.075, bumper_strip=True,
    side_trim="chrome", side_trim_z=0.905, vinyl_roof=True, mirror_material="chrome",
    bezel_material="chrome", handle_z=0.84, plate_front_half=(0.155, 0.078), plate_rear_z=0.0, exhaust=[-0.55], exhaust_radius=0.03,
    interior_seat_y=-0.02,
)
SPEC = CAR.spec()


def design(fn):
    return CAR.design(fn)
