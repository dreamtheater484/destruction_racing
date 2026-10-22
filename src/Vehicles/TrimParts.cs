using System;
using System.Collections.Generic;
using Godot;

namespace DestructionRacing.Vehicles;

/// <summary>
/// Small parts the car generator doesn't make (post-M8 look): the maker's chrome emblem on the back and the front
/// with a chrome model script beside the back one, and a whip aerial on the front wing. Their spots are found by
/// rays onto each model's own panels (clear of the lamps, the plate and the glass), once per model. The badges are
/// built in model space with the car's deforming materials and hang on their panel, so they dent and come off with
/// it; the aerial follows the dents on its base (CarModel.Flush) and sways there (CarVisual).
/// </summary>
public static class TrimParts
{
    /// <summary>Badge sizes (m): the emblem on the back, the model script, the emblem on the front.</summary>
    public static readonly Vector2 EmblemSize = new(0.085f, 0.04f), ScriptSize = new(0.13f, 0.018f), FrontSize = new(0.075f, 0.034f);

    /// <summary>Where a part goes: the panel it sits on, and the point and its outward normal (model space).</summary>
    public readonly record struct Spot(string Panel, Vector3 At, Vector3 Normal);

    /// <summary>The spots on one model; a part without a good spot is left off.</summary>
    public sealed class Spots
    {
        public Spot? Emblem, Script, Front, Aerial;
    }

    private static readonly Dictionary<string, Spots> Found = new();

    /// <summary>The spots on a car model (its parts are direct children of <paramref name="root"/>).</summary>
    public static Spots For(string carId, Node3D root)
    {
        if (!Found.TryGetValue(carId, out var spots)) Found[carId] = spots = Find(root);
        return spots;
    }

    // A part's surfaces in model space (the parts have no transform of their own), and which ones are paint; read
    // from the mesh when a search first reaches the part.
    private sealed class Part
    {
        public readonly string Name;
        public readonly Aabb Box;
        private readonly Mesh _mesh;
        private List<(Vector3[] V, int[] I, bool Paint)>? _surfaces;

        public Part(MeshInstance3D mi)
        {
            Name = mi.Name;
            Box = mi.GetAabb();
            _mesh = mi.Mesh;
        }

        public List<(Vector3[] V, int[] I, bool Paint)> Surfaces
        {
            get
            {
                if (_surfaces != null) return _surfaces;
                _surfaces = new();
                for (int s = 0; s < _mesh.GetSurfaceCount(); s++)
                {
                    var arrays = _mesh.SurfaceGetArrays(s);
                    var v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var idx = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                    if (idx.Length == 0)
                    {
                        idx = new int[v.Length];
                        for (int i = 0; i < idx.Length; i++) idx[i] = i;
                    }
                    _surfaces.Add((v, idx, _mesh.SurfaceGetMaterial(s)?.ResourceName == "paint"));
                }
                return _surfaces;
            }
        }
    }

    /// <summary>
    /// The parts' triangles inside a search region, binned on a grid across one axis, so a ray along that axis
    /// tests only its own cell's triangles.
    /// </summary>
    private sealed class Grid
    {
        private const float Cell = 0.04f;
        private readonly List<Vector3> _tris = new();
        private readonly List<(string Part, bool Paint)> _of = new();
        private readonly Dictionary<(int, int), List<int>> _cells = new();
        private readonly int _a, _b;

        /// <param name="along">The rays' axis: 1 (down) or 2 (along the car).</param>
        public Grid(IEnumerable<Part> parts, int along, Aabb region)
        {
            _a = 0;
            _b = along == 2 ? 1 : 2;
            foreach (var part in parts)
            {
                if (!part.Box.Grow(0.01f).Intersects(region)) continue;
                foreach (var (v, idx, paint) in part.Surfaces)
                    for (int i = 0; i + 2 < idx.Length; i += 3)
                    {
                        Vector3 p = v[idx[i]], q = v[idx[i + 1]], r = v[idx[i + 2]];
                        var box = new Aabb(p, Vector3.Zero).Expand(q).Expand(r);
                        if (!box.Grow(0.005f).Intersects(region)) continue;
                        int id = _of.Count;
                        _tris.Add(p);
                        _tris.Add(q);
                        _tris.Add(r);
                        _of.Add((part.Name, paint));
                        int a1 = Bin(box.End[_a]), b1 = Bin(box.End[_b]);
                        for (int ia = Bin(box.Position[_a]); ia <= a1; ia++)
                            for (int ib = Bin(box.Position[_b]); ib <= b1; ib++)
                            {
                                if (!_cells.TryGetValue((ia, ib), out var list)) _cells[(ia, ib)] = list = new List<int>();
                                list.Add(id);
                            }
                    }
            }
        }

        private static int Bin(float v) => (int)MathF.Floor(v / Cell);

        /// <summary>The nearest hit (Möller–Trumbore), with the normal turned to face the ray.</summary>
        public bool Cast(Vector3 from, Vector3 dir, out string part, out Vector3 point, out Vector3 normal, out bool paint)
        {
            part = "";
            point = normal = Vector3.Zero;
            paint = false;
            if (!_cells.TryGetValue((Bin(from[_a]), Bin(from[_b])), out var list)) return false;
            float best = float.MaxValue;
            int hit = -1;
            foreach (int t in list)
            {
                Vector3 a = _tris[3 * t], e1 = _tris[3 * t + 1] - a, e2 = _tris[3 * t + 2] - a, h = dir.Cross(e2);
                float det = e1.Dot(h);
                if (MathF.Abs(det) < 1e-12f) continue;
                float inv = 1f / det;
                Vector3 s = from - a;
                float u = s.Dot(h) * inv;
                if (u < 0f || u > 1f) continue;
                Vector3 q = s.Cross(e1);
                float v = dir.Dot(q) * inv;
                if (v < 0f || u + v > 1f) continue;
                float d = e2.Dot(q) * inv;
                if (d <= 0f || d >= best) continue;
                best = d;
                hit = t;
                normal = e1.Cross(e2).Normalized();
            }
            if (hit < 0) return false;
            (part, paint) = _of[hit];
            point = from + dir * best;
            if (normal.Dot(dir) > 0f) normal = -normal;
            return true;
        }
    }

    private static Spots Find(Node3D root)
    {
        var spots = new Spots();
        var all = new List<Part>();
        foreach (var child in root.GetChildren())
            if (child is MeshInstance3D { Mesh: not null } mi && mi.Name != "Interior") all.Add(new Part(mi));
        Aabb? Box(string name) => all.Find(p => p.Name == name)?.Box;
        if (Box("Body") is not { } car) return spots;
        float back = car.End.Z, top = car.End.Y;
        foreach (var p in all) back = MathF.Max(back, p.Box.End.Z);

        // The back: high on the boot lid or the tail panel, on flat paint clear of the lamps, the plate and the
        // glass; the model script to the right of the emblem, at its height if it fits there.
        var lid = Box("Trunk") ?? car;
        float yLo = (Box("Bumper_R")?.End.Y ?? car.Position.Y + 0.3f) + 0.02f, yHi = lid.End.Y;
        var rear = new Grid(all, 2, new Aabb(new Vector3(car.Position.X - 0.1f, yLo - 0.05f, back - 0.8f), new Vector3(car.Size.X + 0.2f, yHi - yLo + 0.1f, 1.4f)));
        bool OnBack(float x, float y, out Spot spot)
        {
            spot = default;
            if (!rear.Cast(new Vector3(x, y, back + 0.5f), Vector3.Forward, out var part, out var at, out var n, out bool paint)
                || !paint || part is not ("Trunk" or "Body") || n.Z < 0.85f) return false;
            spot = new Spot(part, at, n);
            return true;
        }
        Spot? Back(float x, Vector2 size, float? preferY)
        {
            float hw = size.X * 0.5f, hh = size.Y * 0.5f + 0.012f;
            bool Fits(float y, out Spot spot)
            {
                if (!OnBack(x, y, out spot)) return false;
                foreach (var (dx, dy) in new[] { (-hw, -hh), (hw, -hh), (-hw, hh), (hw, hh), (0f, -hh), (0f, hh) })
                    if (!OnBack(x + dx, y + dy, out var c) || c.Panel != spot.Panel || MathF.Abs(c.At.Z - spot.At.Z) > 0.02f) return false;
                return true;
            }
            if (preferY is { } py && Fits(py, out var level)) return level;
            for (float y = yHi - hh; y >= yLo + hh; y -= 0.005f)
                if (Fits(y, out var spot)) return spot;
            return null;
        }
        float cx = lid.GetCenter().X, half = lid.Size.X * 0.5f;
        spots.Emblem = Back(cx, EmblemSize, null);
        foreach (float k in new[] { 0.55f, 0.45f, 0.65f, 0.35f })
            if ((spots.Script = Back(cx + k * half, ScriptSize, spots.Emblem?.At.Y)) != null) break;

        // The front: on the grille's centre bar, or on the hood's nose when there is no grille.
        if (Box("Grille") is { } g)
            spots.Front = new Spot("Grille", new Vector3(g.GetCenter().X, g.GetCenter().Y, g.Position.Z - 0.004f), Vector3.Forward);
        else if (Box("Hood") is { } h)
        {
            var nose = new Vector3(h.GetCenter().X, top + 1f, h.Position.Z + 0.08f);
            var grid = new Grid(all, 1, new Aabb(new Vector3(nose.X - 0.05f, car.Position.Y - 0.1f, nose.Z - 0.05f), new Vector3(0.1f, car.Size.Y + 0.2f, 0.1f)));
            if (grid.Cast(nose, Vector3.Down, out var part, out var at, out var n, out bool paint) && paint && part == "Hood" && n.Y > 0.5f)
                spots.Front = new Spot("Hood", at, n);
        }

        // The aerial: on the flat top of the left front wing, a hand's width ahead of the windscreen.
        if (Box("Fender_FL") is { } wing)
        {
            float z = wing.End.Z - 0.16f;
            var down = new Grid(all, 1, new Aabb(new Vector3(car.Position.X - 0.05f, car.Position.Y - 0.1f, z - 0.03f), new Vector3(0.55f, car.Size.Y + 0.2f, 0.06f)));
            bool Flat(float x, out Spot spot)
            {
                spot = default;
                if (!down.Cast(new Vector3(x, top + 1f, z), Vector3.Down, out var part, out var at, out var n, out bool paint)
                    || !paint || !part.StartsWith("Fender") || n.Y < 0.85f) return false;
                spot = new Spot(part, at, n);
                return true;
            }
            float x0 = float.NaN, x1 = float.NaN;
            for (float x = car.Position.X + 0.01f; x < car.Position.X + 0.45f; x += 0.005f)
            {
                if (Flat(x, out _)) { if (float.IsNaN(x0)) x0 = x; x1 = x; }
                else if (!float.IsNaN(x0)) break;
            }
            if (!float.IsNaN(x0) && x1 - x0 >= 0.015f && Flat(0.5f * (x0 + x1), out var mount)) spots.Aerial = mount;
        }
        return spots;
    }

    /// <summary>An oval emblem (chrome ring, dark centre) or a model script (a chrome bar) lying on a panel.</summary>
    public static ArrayMesh Badge(Vector3 at, Vector3 normal, Vector2 size, bool script, Material chrome, Material dark)
    {
        Vector3 n = normal.Normalized();
        Vector3 side = Vector3.Up.Cross(n);
        if (side.LengthSquared() < 1e-4f) side = Vector3.Right;
        side = side.Normalized();
        Vector3 up = n.Cross(side).Normalized();
        float width = size.X, height = size.Y;
        var mesh = new ArrayMesh();
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        if (script) RoundedBar(st, at + n * 0.002f, side, up, n, width * 0.5f, height * 0.5f, 0.0025f);
        else Oval(st, at + n * 0.002f, side, up, n, width * 0.5f, height * 0.5f, 0.0035f);
        st.Commit(mesh);
        mesh.SurfaceSetMaterial(0, chrome);
        if (!script)
        {
            st.Begin(Mesh.PrimitiveType.Triangles);
            Oval(st, at + n * 0.0045f, side, up, n, width * 0.36f, height * 0.3f, 0.0012f);
            st.Commit(mesh);
            mesh.SurfaceSetMaterial(1, dark);
        }
        return mesh;
    }

    /// <summary>A whip aerial along +y from its base: a chrome cap and a rod tapering to the tip.</summary>
    public static ArrayMesh Aerial(float length, Material chrome)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Tube(st, 0f, 0.022f, 0.011f, 0.009f, 10);
        Tube(st, 0.02f, length, 0.0032f, 0.0018f, 6);
        var mesh = st.Commit();
        mesh.SurfaceSetMaterial(0, chrome);
        return mesh;
    }

    // Godot's front faces wind clockwise: (b - a) × (c - a) points into the face.
    private static void Tri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 n)
    {
        if ((b - a).Cross(c - a).Dot(n) > 0f) (b, c) = (c, b);
        foreach (var p in new[] { a, b, c })
        {
            st.SetNormal(n);
            st.AddVertex(p);
        }
    }

    private static void Oval(SurfaceTool st, Vector3 c, Vector3 u, Vector3 v, Vector3 n, float a, float b, float thick, int sides = 24)
    {
        Vector3 top = c + n * thick;
        for (int i = 0; i < sides; i++)
        {
            float t0 = Mathf.Tau * i / sides, t1 = Mathf.Tau * (i + 1) / sides;
            Vector3 d0 = u * (a * Mathf.Cos(t0)) + v * (b * Mathf.Sin(t0)), d1 = u * (a * Mathf.Cos(t1)) + v * (b * Mathf.Sin(t1));
            Tri(st, top, top + d0, top + d1, n);
            Vector3 out_ = (d0 + d1).Normalized();
            Tri(st, c + d0, top + d0, top + d1, out_);
            Tri(st, c + d0, top + d1, c + d1, out_);
        }
    }

    private static void RoundedBar(SurfaceTool st, Vector3 c, Vector3 u, Vector3 v, Vector3 n, float a, float b, float thick)
    {
        // Two half-round ends and a straight middle, as a stretched oval.
        Oval(st, c - u * (a - b), u, v, n, b, b, thick, 12);
        Oval(st, c + u * (a - b), u, v, n, b, b, thick, 12);
        Vector3 top = c + n * thick;
        Vector3 p0 = top - u * (a - b) - v * b, p1 = top + u * (a - b) - v * b, p2 = top + u * (a - b) + v * b, p3 = top - u * (a - b) + v * b;
        Tri(st, p0, p1, p2, n);
        Tri(st, p0, p2, p3, n);
        foreach (float s in new[] { -1f, 1f })
        {
            Vector3 e0 = c - u * (a - b) + v * (b * s), e1 = c + u * (a - b) + v * (b * s);
            Tri(st, e0, e1, e1 + n * thick, v * s);
            Tri(st, e0, e1 + n * thick, e0 + n * thick, v * s);
        }
    }

    private static void Tube(SurfaceTool st, float y0, float y1, float r0, float r1, int sides)
    {
        for (int i = 0; i < sides; i++)
        {
            float t0 = Mathf.Tau * i / sides, t1 = Mathf.Tau * (i + 1) / sides, tm = 0.5f * (t0 + t1);
            var n = new Vector3(Mathf.Cos(tm), 0f, Mathf.Sin(tm));
            Vector3 a0 = new(r0 * Mathf.Cos(t0), y0, r0 * Mathf.Sin(t0)), a1 = new(r0 * Mathf.Cos(t1), y0, r0 * Mathf.Sin(t1));
            Vector3 b0 = new(r1 * Mathf.Cos(t0), y1, r1 * Mathf.Sin(t0)), b1 = new(r1 * Mathf.Cos(t1), y1, r1 * Mathf.Sin(t1));
            Tri(st, a0, b0, b1, n);
            Tri(st, a0, b1, a1, n);
            Tri(st, new Vector3(0f, y1, 0f), b0, b1, Vector3.Up);
        }
    }
}
