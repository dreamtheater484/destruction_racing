"""Build a car in Blender: generate -> decimate (cut lines kept) -> exact normals -> flanges, glass, seals ->
rig-spec nodes -> .glb, plus Workbench preview renders.

blender -b --factory-startup -P blender_build.py -- <car> [--voxel 0.012] [--ratio 0.16] [--glb out.glb]
                                                       [--preview out_prefix] [--views q34f,side,...]
"""
import argparse
import importlib
import math
import os
import sys
import time

import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from carsmith.build import build, reproject  # noqa: E402
from carsmith.regions import Piece, add_flange, boundary_loops, offset, tube_along  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:]
ap = argparse.ArgumentParser()
ap.add_argument("car")
ap.add_argument("--voxel", type=float, default=0.012)
ap.add_argument("--ratio", type=float, default=0.16)
ap.add_argument("--glb", default="")
ap.add_argument("--preview", default="")
ap.add_argument("--views", default="q34f,side,q34r,front,top,close")
args = ap.parse_args(argv)

# Preview colours (Workbench) + glTF defaults. Godot swaps these for its own shaders by material name.
MATERIALS = {
    "paint": ((0.50, 0.055, 0.04), 0.0, 0.3),
    "glass": ((0.035, 0.045, 0.05), 0.0, 0.05),
    "trim_black": ((0.025, 0.025, 0.027), 0.0, 0.55),
    "rubber": ((0.012, 0.012, 0.012), 0.0, 0.8),
    "bumper": ((0.045, 0.045, 0.048), 0.0, 0.6),
    "lens_red": ((0.55, 0.02, 0.02), 0.0, 0.1),
    "lens_amber": ((0.85, 0.40, 0.02), 0.0, 0.1),
    "lens_clear": ((0.75, 0.78, 0.80), 0.0, 0.05),
    "chrome": ((0.85, 0.86, 0.88), 1.0, 0.08),
    "wheel_well": ((0.02, 0.02, 0.02), 0.0, 0.9),
    "underbody": ((0.03, 0.03, 0.03), 0.0, 0.9),
    "interior": ((0.10, 0.095, 0.09), 0.0, 0.85),
    "vinyl": ((0.05, 0.03, 0.025), 0.0, 0.6),
    "plate": ((0.85, 0.85, 0.82), 0.0, 0.4),
    "tyre": ((0.035, 0.035, 0.037), 0.0, 0.85),
    "rim": ((0.68, 0.69, 0.71), 1.0, 0.3),
    "hubcap": ((0.55, 0.56, 0.58), 1.0, 0.25),
}


def material(name):
    m = bpy.data.materials.get(name)
    if m:
        return m
    color, metal, rough = MATERIALS.get(name, ((0.5, 0.5, 0.5), 0.0, 0.5))
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1.0)
    m.metallic = metal
    m.roughness = rough
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*color, 1.0)
        bsdf.inputs["Metallic"].default_value = metal
        bsdf.inputs["Roughness"].default_value = rough
    return m


def mesh_multi(name, chunks, collection=None):
    """chunks: [(Piece, material name)] -> one object, one material slot per chunk, exact custom normals."""
    V = np.concatenate([c[0].V for c in chunks]).astype(np.float32)
    N = np.concatenate([c[0].N for c in chunks]).astype(np.float32)
    C = np.concatenate([c[0].C for c in chunks]).astype(np.float32)
    T, M, off = [], [], 0
    mats = []
    for piece, mat in chunks:
        if mat not in mats:
            mats.append(mat)
        T.append(piece.T + off)
        M.append(np.full(len(piece.T), mats.index(mat), np.int32))
        off += len(piece.V)
    T = np.concatenate(T)
    M = np.concatenate(M)
    me = bpy.data.meshes.new(name)
    me.vertices.add(len(V))
    me.vertices.foreach_set("co", V.ravel())
    me.loops.add(len(T) * 3)
    me.loops.foreach_set("vertex_index", T.ravel().astype(np.int32))
    me.polygons.add(len(T))
    me.polygons.foreach_set("loop_start", (np.arange(len(T)) * 3).astype(np.int32))
    me.polygons.foreach_set("loop_total", np.full(len(T), 3, np.int32))
    me.update(calc_edges=True)
    me.polygons.foreach_set("material_index", M)
    me.polygons.foreach_set("use_smooth", np.ones(len(T), bool))
    me.normals_split_custom_set_from_vertices([tuple(n) for n in N])
    col = me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    rgba = np.repeat(C[:, None], 4, axis=1)
    rgba[:, 3] = 1.0
    col.data.foreach_set("color", rgba.ravel())
    me.color_attributes.active_color = col
    for m in mats:
        me.materials.append(material(m))
    ob = bpy.data.objects.new(name, me)
    (collection or bpy.context.scene.collection).objects.link(ob)
    return ob


def read_mesh(me):
    V = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get("co", V)
    me.calc_loop_triangles()
    T = np.empty(len(me.loop_triangles) * 3, np.int32)
    me.loop_triangles.foreach_get("vertices", T)
    return V.reshape(-1, 3).astype(np.float64), T.reshape(-1, 3).astype(np.int64)


def decimate(piece, target):
    """Collapse decimation to about `target` triangles; Blender keeps open boundaries (our cut lines) intact."""
    if target <= 0 or len(piece.T) <= target * 1.05:
        return piece
    me = bpy.data.meshes.new("dec")
    me.from_pydata(piece.V.tolist(), [], piece.T.tolist())
    ob = bpy.data.objects.new("dec", me)
    bpy.context.scene.collection.objects.link(ob)
    mod = ob.modifiers.new("dec", "DECIMATE")
    mod.ratio = target / len(piece.T)
    mod.use_collapse_triangulate = True
    dg = bpy.context.evaluated_depsgraph_get()
    me2 = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    V, T = read_mesh(me2)
    bpy.data.objects.remove(ob)
    bpy.data.meshes.remove(me)
    bpy.data.meshes.remove(me2)
    return Piece(V, T, np.zeros_like(V))


def mirrored(piece):
    V = piece.V * np.array([-1.0, 1, 1])
    N = piece.N * np.array([-1.0, 1, 1])
    return Piece(V, piece.T[:, ::-1].copy(), N, piece.C)


def seals(glass, radius=0.0042, sink=0.0028):
    out = []
    for loop in boundary_loops(glass):
        if len(loop) < 8:
            continue
        pts = glass.V[loop] - glass.N[loop] * sink
        out.append(tube_along(pts, radius, sides=6, closed=True))
    return Piece.merge(out) if out else None


def finish(part, ratio):
    """Decimated, re-projected, flanged pieces of one part: [(Piece, material)]."""
    piece = part.piece
    target = part.target if part.target else int(len(piece.T) * ratio)
    if part.kind == "glass":
        target = max(target, 600)
    piece = decimate(piece, max(target, 200))
    if part.mirror:
        piece = mirrored(piece)
    V, N = reproject(part, piece.V)
    piece = Piece(V, piece.T, N, piece.C)
    chunks = []
    if part.kind == "glass":
        if part.material == "glass":
            s = seals(piece)
            if s is not None:
                chunks.append((s, "rubber"))
        piece = offset(piece, -part.recess)
    elif part.flange:
        piece = add_flange(piece)
    chunks.insert(0, (piece, part.material))
    return chunks


# ----------------------------------------------------------------------------- build

t0 = time.time()
bpy.ops.wm.read_factory_settings(use_empty=True)
car = importlib.import_module(f"carsmith.cars.{args.car}")
fn, parts = build(car, args.voxel)
print(f"generated in {time.time() - t0:.1f}s")

nodes = {}       # node name -> [(Piece, material)]
wheel_children = {}
cache = {}
for part in parts:
    key = (id(part.piece), part.mirror)
    if key not in cache:
        cache[key] = finish(part, args.ratio)
    chunks = cache[key]
    if part.parent.startswith("Wheel_"):
        wheel_children.setdefault(part.parent, []).append((part.name, part.origin, chunks))
    else:
        nodes.setdefault(part.parent or part.name, []).extend(chunks)
print(f"finished in {time.time() - t0:.1f}s")

col = bpy.context.scene.collection
total = 0
for name, chunks in nodes.items():
    ob = mesh_multi(name, chunks, col)
    total += len(ob.data.polygons)
for wname, kids in wheel_children.items():
    empty = bpy.data.objects.new(wname, None)
    col.objects.link(empty)
    empty.location = kids[0][1]
    for kname, _, chunks in kids:
        ob = mesh_multi(f"{wname}_{kname}", chunks, col)
        ob.parent = empty
        total += len(ob.data.polygons)
print(f"{len(nodes)} nodes + {len(wheel_children)} wheels, {total} triangles")

if args.glb:
    os.makedirs(os.path.dirname(os.path.abspath(args.glb)), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=args.glb, export_format="GLB", export_yup=True, export_apply=True,
                              export_normals=True, export_materials="EXPORT", export_extras=False,
                              export_vertex_color="ACTIVE", export_active_vertex_color_when_no_material=True)
    print(f"GLB {args.glb}")

# ----------------------------------------------------------------------------- preview renders

if args.preview:
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
    bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0))
    bpy.context.active_object.data.materials.append(material("ground"))
    bpy.data.materials["ground"].diffuse_color = (0.36, 0.36, 0.37, 1)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x = 1600
    scene.render.resolution_y = 900
    sh = scene.display.shading
    sh.color_type = "MATERIAL"
    sh.light = "STUDIO"
    sh.show_shadows = True
    sh.shadow_intensity = 0.55
    sh.show_cavity = True
    sh.cavity_type = "BOTH"
    sh.curvature_ridge_factor = 0.6
    sh.curvature_valley_factor = 0.9
    sh.show_specular_highlight = True
    scene.display.shadow_focus = 0.25
    scene.view_settings.view_transform = "Standard"
    scene.render.film_transparent = False
    cam_data = bpy.data.cameras.new("cam")
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    views = {
        "q34f": (Vector((1.0, 1.15, 0.42)), None, 1.35),
        "side": (Vector((1.0, 0.0, 0.08)), "ortho", 0),
        "front": (Vector((0.0, 1.0, 0.05)), "ortho", 0),
        "top": (Vector((0.0, 0.0001, 1.0)), "ortho", 0),
        "q34r": (Vector((-1.0, -1.2, 0.45)), None, 1.35),
        "close": (Vector((1.0, 0.55, 0.22)), None, 0.80),
    }
    for name in args.views.split(","):
        d, kind, dist_k = views[name]
        d = d.normalized()
        look_at = centre
        if kind == "ortho":
            cam_data.type = "ORTHO"
            cam_data.ortho_scale = max(size.x, size.y, size.z) * 1.12
            dist = 10
        else:
            cam_data.type = "PERSP"
            cam_data.lens = 50 if name != "close" else 40
            dist = size.length * dist_k
            if name == "close":
                look_at = centre + Vector((0.35, 0.85, -0.12))
        cam.location = look_at + d * dist
        look = (look_at - cam.location).normalized()
        cam.rotation_euler = look.to_track_quat("-Z", "Y").to_euler()
        if name == "top":
            cam.rotation_euler = (0.0, 0.0, math.pi / 2)
        scene.render.filepath = f"{args.preview}_{name}.png"
        bpy.ops.render.render(write_still=True)
print(f"BUILD DONE in {time.time() - t0:.1f}s")
