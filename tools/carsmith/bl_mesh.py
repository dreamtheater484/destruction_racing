"""Blender helpers: numpy arrays -> mesh objects with exact custom normals (run inside Blender)."""
import bpy
import numpy as np


def mesh_object(name, V, F, N=None, material=None, collection=None):
    """F: list/array of polygons (quads or tris, all the same size), N: per-vertex normals."""
    F = np.asarray(F, dtype=np.int64)
    me = bpy.data.meshes.new(name)
    nv, nf, k = len(V), len(F), F.shape[1]
    me.vertices.add(nv)
    me.vertices.foreach_set("co", np.asarray(V, dtype=np.float32).ravel())
    me.loops.add(nf * k)
    me.loops.foreach_set("vertex_index", F.ravel().astype(np.int32))
    me.polygons.add(nf)
    me.polygons.foreach_set("loop_start", (np.arange(nf) * k).astype(np.int32))
    me.polygons.foreach_set("loop_total", np.full(nf, k, dtype=np.int32))
    me.update(calc_edges=True)
    me.validate(clean_customdata=False)
    me.polygons.foreach_set("use_smooth", np.ones(len(me.polygons), dtype=bool))
    if N is not None:
        me.normals_split_custom_set_from_vertices([tuple(n) for n in np.asarray(N, dtype=np.float32)])
    if material is not None:
        me.materials.append(material)
    ob = bpy.data.objects.new(name, me)
    (collection or bpy.context.scene.collection).objects.link(ob)
    return ob


def set_vertex_normals(ob, N):
    ob.data.normals_split_custom_set_from_vertices([tuple(n) for n in np.asarray(N, dtype=np.float32)])


def vertices(ob):
    me = ob.data
    co = np.empty(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    return co.reshape(-1, 3).astype(np.float64)


def polygons(ob):
    """Triangles/quads as a list of index arrays (after decimation the mesh is mixed)."""
    me = ob.data
    starts = np.empty(len(me.polygons), dtype=np.int32)
    totals = np.empty(len(me.polygons), dtype=np.int32)
    me.polygons.foreach_get("loop_start", starts)
    me.polygons.foreach_get("loop_total", totals)
    vi = np.empty(len(me.loops), dtype=np.int32)
    me.loops.foreach_get("vertex_index", vi)
    return [vi[s:s + t] for s, t in zip(starts, totals)]


def material(name, color=(0.6, 0.6, 0.6), metallic=0.0, roughness=0.5):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1.0)
    m.metallic = metallic
    m.roughness = roughness
    return m
