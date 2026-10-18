using System;
using System.Collections.Generic;
using System.Linq;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Tools;

/// <summary>
/// The car triangle budget (plan §16: LOD0 ≤ 60k, LOD1 ≤ 15k, LOD2 ≤ 4k), <c>--lodreport</c>: every car's
/// triangles at full detail and at each of the LOD levels Godot generated on import (summed over the surfaces; a
/// surface with fewer levels counts its simplest one), then quits.
/// </summary>
public static class LodReport
{
    public static void Run(SceneTree tree)
    {
        foreach (string id in CarCatalog.Ids)
        {
            var root = GD.Load<PackedScene>($"res://assets/cars/{id}/{id}.glb").Instantiate<Node3D>();
            var surfaces = new List<List<long>>();
            foreach (var mi in All<MeshInstance3D>(root))
            {
                if (mi.Mesh is not { } mesh) continue;
                for (int s = 0; s < mesh.GetSurfaceCount(); s++)
                {
                    var surface = RenderingServer.MeshGetSurface(mesh.GetRid(), s);
                    int vertices = surface["vertex_count"].AsInt32();
                    int indexBytes = vertices > 65535 ? 4 : 2;
                    var counts = new List<long> { Triangles(surface, indexBytes, vertices) };
                    if (surface.TryGetValue("lods", out var lods))
                        foreach (var lod in lods.AsGodotArray())
                            counts.Add(Triangles(lod.AsGodotDictionary(), indexBytes, vertices));
                    surfaces.Add(counts);
                }
            }
            if (Core.DevTools.Instance?.Arg("lodreport") == id)
                foreach (var mi in All<MeshInstance3D>(root).Where(m => m.Mesh != null).OrderByDescending(m => Total(m.Mesh)))
                    GD.Print($"[LodReport]   {mi.Name,-28} {Total(mi.Mesh),7:N0}  {mi.Mesh.GetSurfaceCount()} surfaces, materials " +
                             string.Join("/", Enumerable.Range(0, mi.Mesh.GetSurfaceCount()).Select(s => mi.Mesh.SurfaceGetMaterial(s)?.ResourceName)));
            root.Free();
            int n = surfaces.Count == 0 ? 0 : surfaces.Max(c => c.Count);
            var levels = Enumerable.Range(0, n).Select(l => surfaces.Sum(c => c[Math.Min(l, c.Count - 1)])).ToList();
            GD.Print($"[LodReport] {id,-10} {surfaces.Count,3} surfaces  LOD0 {levels.ElementAtOrDefault(0),7:N0} triangles, then " +
                     string.Join(" > ", levels.Skip(1).Select(t => $"{t:N0}")));
        }
        Core.DevTools.QuitClean(tree, 0);
    }

    private static long Total(Mesh mesh)
    {
        long t = 0;
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var surface = RenderingServer.MeshGetSurface(mesh.GetRid(), s);
            int v = surface["vertex_count"].AsInt32();
            t += Triangles(surface, v > 65535 ? 4 : 2, v);
        }
        return t;
    }

    private static long Triangles(Godot.Collections.Dictionary d, int indexBytes, int vertices) =>
        d.TryGetValue("index_data", out var data) && data.AsByteArray() is { Length: > 0 } bytes ? bytes.Length / indexBytes / 3 : vertices / 3;

    private static IEnumerable<T> All<T>(Node n) where T : Node
    {
        foreach (Node c in n.GetChildren())
        {
            if (c is T t) yield return t;
            foreach (var x in All<T>(c)) yield return x;
        }
    }
}
