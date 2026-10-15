"""#2 Aldridge Homestead: late-70s US full-size station wagon - the Brougham's long roof, wood on the sides.

A morph of the template's wagon (carsmith/templates.py), sharing the Brougham's front end. 5.60 m long,
1.99 m wide, 1.45 m high, 2.95 m wheelbase; quad rectangular lamps, an egg-crate chrome grille, simulated wood
on the doors, quarters and tailgate, a chrome roof rack, vertical tail lamps, 15" full wheel covers.
"""
from ..templates import Rect, Sedan

CAR = Sedan(
    name="homestead", style="wagon", length=5.60, half_width=0.995, wheelbase=2.95, front_overhang=1.05,
    sill=0.24, nose_top=0.86, nose_bottom=0.40, cowl=0.95, belt=0.94, deck=0.98, tail_top=0.95,
    tail_bottom=0.42, roof=1.45,
    cowl_y=0.48, header_y=-0.18, roof_rear_y=-2.85, deck_y=-2.98,
    tumblehome_deg=10.0, ledge=0.075, roof_crown=0.07, roof_radius=0.035,
    plan_front=0.06, plan_rear=0.05, shoulder=0.015, bottom_radius=0.06,
    crease_z=0.62, crease_up=0.02, crease_down=0.05, top_crown=0.02, front_lean=0.0, rear_lean=0.02,
    wheel_radius=0.355, wheel_width=0.215, track_front=1.59, track_rear=1.58, arch_radius=0.40,
    rim_inch=15, rim_style="cover",
    a_pillar=0.06, c_pillar=0.09, b_pillar_y=-0.20, b_pillar_w=0.04, door_split_y=-0.22,
    door_front_y=0.55, c_door_y=-1.12, c_pillar_y=-1.22, c_pillar_w=0.05, c_glass_y=-2.72, c_glass_slant=0.15,
    headlights=[Rect(0.53, 0.70, 0.085, 0.055, 0.008), Rect(0.73, 0.70, 0.085, 0.055, 0.008)],
    grille=Rect(0.0, 0.69, 0.36, 0.105, 0.01),
    grille_bars="eggcrate", grille_material="chrome",
    indicators_front=[Rect(0.885, 0.70, 0.055, 0.05, 0.01)],
    tail_lights=[Rect(0.86, 0.72, 0.075, 0.17, 0.01)],
    tail_clear=[Rect(0.86, 0.60, 0.07, 0.04, 0.004)],
    bumper_material="chrome", bumper_z=0.47, bumper_half=0.085, bumper_proud=0.075, bumper_strip=True,
    side_trim="chrome", side_trim_z=0.905, mirror_material="chrome",
    bezel_material="chrome", handle_z=0.84, plate_front_half=(0.155, 0.078), plate_rear_z=0.70,
    roof_rack=True, wood_panels=True, wood_z=(0.50, 0.0),
    exhaust=[-0.55], exhaust_radius=0.03,
    interior_seat_y=-0.02,
)
SPEC = CAR.spec()


def design(fn):
    return CAR.design(fn)
