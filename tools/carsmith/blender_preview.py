"""Clay preview renders in Blender (Workbench). Run:
blender -b --factory-startup -P blender_preview.py -- <car:NAME[:voxel] | model.glb> <out_prefix> [matcap|studio]
Writes <out_prefix>_{q34f,side,front,top,q34r}.png.
"""
import math
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, out = argv[0], argv[1]
mode = argv[2] if len(argv) > 2 else "studio"

bpy.ops.wm.read_factory_settings(use_empty=True)
if src.startswith("car:"):
    # Generate inside Blender: car:<name>[:voxel]
    import importlib
    import os
    sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    from carsmith.body import make_body_sdf
    from carsmith.mesher import surface_nets
    from carsmith.bl_mesh import mesh_object
    parts = src.split(":")
    spec = importlib.import_module(f"carsmith.cars.{parts[1]}").SPEC
    h = float(parts[2]) if len(parts) > 2 else 0.012
    fn = make_body_sdf(spec)
    V, Q, N = surface_nets(fn, *spec.bounds(), h)
    mesh_object(spec.name, V, Q, N)
else:
    bpy.ops.import_scene.gltf(filepath=src)
objs = [o for o in bpy.context.scene.objects if o.type == "MESH"]

lo = Vector((1e9, 1e9, 1e9))
hi = Vector((-1e9, -1e9, -1e9))
for o in objs:
    for c in o.bound_box:
        w = o.matrix_world @ Vector(c)
        lo = Vector(map(min, lo, w))
        hi = Vector(map(max, hi, w))
centre = (lo + hi) / 2
size = hi - lo

bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, lo.z))

scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.render.resolution_x = 1280
scene.render.resolution_y = 720
scene.render.film_transparent = False
sh = scene.display.shading
sh.color_type = "SINGLE"
sh.single_color = (0.62, 0.62, 0.64)
if mode == "matcap":
    sh.light = "MATCAP"
    sh.studio_light = "metal_carpaint.exr" if "metal_carpaint.exr" in [s.name for s in bpy.context.preferences.studio_lights] else sh.studio_light
else:
    sh.light = "STUDIO"
sh.show_shadows = True
sh.shadow_intensity = 0.6
sh.show_cavity = True
sh.cavity_type = "WORLD"
sh.show_specular_highlight = True
scene.display.shadow_focus = 0.2
scene.view_settings.view_transform = "Standard"

cam_data = bpy.data.cameras.new("cam")
cam = bpy.data.objects.new("cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam

views = {
    "q34f": (Vector((1.0, 1.15, 0.42)), 32),
    "side": (Vector((1.0, 0.0, 0.08)), 0),
    "front": (Vector((0.0, 1.0, 0.12)), 0),
    "top": (Vector((0.0, 0.0001, 1.0)), 0),
    "q34r": (Vector((-1.0, -1.2, 0.45)), 32),
}
for name, (d, _) in views.items():
    d = d.normalized()
    if name in ("side", "front", "top"):
        cam_data.type = "ORTHO"
        cam_data.ortho_scale = max(size.x, size.y, size.z) * 1.12
        dist = 10
    else:
        cam_data.type = "PERSP"
        cam_data.lens = 50
        dist = size.length * 1.35
    cam.location = centre + d * dist
    look = (centre - cam.location).normalized()
    cam.rotation_euler = look.to_track_quat("-Z", "Y").to_euler()
    if name == "top":
        cam.rotation_euler = (0.0, 0.0, math.pi / 2)   # nose to the right, like the side view
    scene.render.filepath = f"{out}_{name}.png"
    bpy.ops.render.render(write_still=True)
print("PREVIEW DONE")
