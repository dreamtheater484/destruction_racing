using System;
using Godot;

namespace DestructionRacing.Tracks;

/// <summary>
/// The crowd (plan §8.5): seated spectators on the grandstands' benches (the Bowl's two, about 1,500 people, or
/// the stands of another venue), one MultiMesh of a low-poly person animated in <c>crowd.gdshader</c>, plus camera
/// flashes at night. <see cref="Excitement"/> drives both and the crowd audio: it rises with big hits and wrecks,
/// decays back to a base that climbs as the field shrinks.
/// </summary>
public partial class Crowd : Node3D
{
    private static readonly StringName ExcitementParam = "excitement", ActionPointParam = "action_point";   // set every frame: made once
    /// <summary>
    /// A grandstand in its own frame: the stand faces -Z, the first bench row is at the origin and rows step up
    /// (<paramref name="RowRise"/>) and back (+Z, <paramref name="RowDepth"/>); <paramref name="Columns"/> roof columns
    /// stand along the front row, <paramref name="ColumnMargin"/> in from the ends.
    /// </summary>
    public readonly record struct Stand(Transform3D Frame, float Length, int Rows, int Columns, float ColumnMargin,
        float RowDepth = 0.8f, float RowRise = 0.42f);

    /// <summary>The grandstands as BowlLookBuilder.gd builds them: 12 terraces, 46 m long, first bench 3.2 m behind the wall.</summary>
    public static Stand[] BowlStands => new[]
    {
        new Stand(new Transform3D(Basis.Identity, new Vector3(0f, 0f, Bowl.SemiAxisZ + 3.2f)), 46f, 12, 7, 2f),
        new Stand(new Transform3D(new Basis(Vector3.Up, Mathf.Pi), new Vector3(0f, 0f, -(Bowl.SemiAxisZ + 3.2f))), 46f, 12, 7, 2f),
    };

    /// <summary>The stands to fill (default: the Bowl's).</summary>
    public Stand[]? Stands { get; set; }

    public bool Night { get; set; }

    /// <summary>0 = bored, 1 = on their feet.</summary>
    public float Excitement { get; private set; } = 0.2f;
    /// <summary>The level the crowd settles back to (raised by the game as the field shrinks).</summary>
    public float BaseExcitement { get; set; } = 0.2f;
    /// <summary>Where the crowd looks (the action).</summary>
    public Vector3 ActionPoint { get; set; }

    private ShaderMaterial _material = null!;
    private ShaderMaterial? _flashMaterial;
    private Vector3 _look;

    public override void _Ready()
    {
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/crowd.gdshader") };
        var mesh = BuildPerson();
        mesh.SurfaceSetMaterial(0, _material);

        var rng = new Random(1977);
        var multi = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = mesh,
        };
        var spots = new System.Collections.Generic.List<(Transform3D Xf, Color Custom)>();
        foreach (var stand in Stands ?? BowlStands)
        {
            for (int k = 0; k < stand.Rows; k++)
            {
                float z = stand.RowDepth * k + 0.12f;
                float y = stand.RowRise * (k + 1) + 0.06f;
                float half = stand.Length * 0.5f;
                // Fuller in the middle rows and the middle of the stand.
                for (float x = -half + 0.6f; x < half - 0.6f; x += 0.56f + (float)rng.NextDouble() * 0.12f)
                {
                    float middle = 1f - Mathf.Abs(x) / half;
                    float occupancy = 0.62f + 0.25f * middle + (k > 2 && k < stand.Rows - 3 ? 0.08f : 0f);
                    if (rng.NextDouble() > occupancy) continue;
                    // Keep the front row clear of the roof columns.
                    if (k == 0 && ColumnNear(stand, x)) continue;
                    var basis = stand.Frame.Basis * new Basis(Vector3.Up, ((float)rng.NextDouble() - 0.5f) * 0.35f);
                    float scale = 0.9f + 0.18f * (float)rng.NextDouble();
                    var at = stand.Frame * new Vector3(x + ((float)rng.NextDouble() - 0.5f) * 0.08f, y, z);
                    var xf = new Transform3D(basis.Scaled(Vector3.One * scale), at);
                    var custom = new Color((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble());
                    spots.Add((xf, custom));
                }
            }
        }
        multi.InstanceCount = spots.Count;
        for (int i = 0; i < spots.Count; i++)
        {
            multi.SetInstanceTransform(i, spots[i].Xf);
            multi.SetInstanceCustomData(i, spots[i].Custom);
        }
        AddChild(new MultiMeshInstance3D
        {
            Name = "Spectators",
            Multimesh = multi,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Dynamic,
        });

        if (Night) BuildFlashes(spots, rng);
    }

    private static bool ColumnNear(Stand stand, float x)
    {
        for (int c = 0; c < stand.Columns; c++)
        {
            float cx = -stand.Length * 0.5f + stand.ColumnMargin + c * (stand.Length - 2f * stand.ColumnMargin) / Mathf.Max(1, stand.Columns - 1);
            if (Mathf.Abs(x - cx) < 0.45f) return true;
        }
        return false;
    }

    private void BuildFlashes(System.Collections.Generic.List<(Transform3D Xf, Color Custom)> spots, Random rng)
    {
        _flashMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/flashbulb.gdshader") };
        var quad = new QuadMesh { Size = new Vector2(0.5f, 0.5f), Material = _flashMaterial };
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = quad };
        int count = spots.Count / 5;
        multi.InstanceCount = count;
        for (int i = 0; i < count; i++)
        {
            var spot = spots[rng.Next(spots.Count)];
            multi.SetInstanceTransform(i, new Transform3D(Basis.Identity, spot.Xf.Origin + new Vector3(0f, 0.75f, 0f)));
            multi.SetInstanceCustomData(i, new Color((float)rng.NextDouble(), (float)rng.NextDouble(), 0f, 0f));
        }
        AddChild(new MultiMeshInstance3D
        {
            Name = "Flashes",
            Multimesh = multi,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            // The quads are billboarded in the shader; keep them from being culled by their unrotated bounds.
            ExtraCullMargin = 1f,
        });
    }

    /// <summary>A reaction: big hits ~0.2-0.4, a wreck ~0.5, the winner 1.</summary>
    public void Pulse(float amount) => Excitement = Mathf.Clamp(Mathf.Max(Excitement, BaseExcitement) + amount, 0f, 1f);

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        // Rise fast, settle slowly.
        float target = BaseExcitement;
        Excitement = Excitement > target ? Mathf.Max(target, Excitement - dt * 0.12f) : Mathf.Min(target, Excitement + dt * 0.5f);
        _look = _look.Lerp(ActionPoint, 1f - Mathf.Exp(-dt * 2.5f));
        _material.SetShaderParameter(ExcitementParam, Excitement);
        _material.SetShaderParameter(ActionPointParam, _look);
        _flashMaterial?.SetShaderParameter(ExcitementParam, Excitement);
    }

    // ------------------------------------------------------------------ the spectator mesh

    /// <summary>
    /// A seated person facing -Z, origin on the bench top: thighs forward, shins down to the terrace in front,
    /// torso, head, and arms hanging from the shoulders (animated in the shader around (±0.205, 0.6, 0)).
    /// </summary>
    private static ArrayMesh BuildPerson()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const float legs = 0f, torso = 0.25f, head = 0.5f, armL = 0.75f, armR = 1f;
        // Legs: thighs on the bench, shins down to the step below (0.48 m lower, in front).
        AddBox(st, new Vector3(-0.17f, 0f, -0.46f), new Vector3(0.17f, 0.15f, 0.05f), legs);
        AddBox(st, new Vector3(-0.16f, -0.45f, -0.5f), new Vector3(0.16f, 0.02f, -0.36f), legs);
        // Torso, slightly narrower at the waist.
        AddBox(st, new Vector3(-0.17f, 0.1f, -0.1f), new Vector3(0.17f, 0.4f, 0.1f), torso);
        AddBox(st, new Vector3(-0.2f, 0.38f, -0.11f), new Vector3(0.2f, 0.64f, 0.11f), torso);
        AddBox(st, new Vector3(-0.05f, 0.62f, -0.05f), new Vector3(0.05f, 0.7f, 0.05f), head);
        AddSphere(st, new Vector3(0f, 0.79f, -0.01f), 0.105f, head);
        // Arms hang from the shoulders; hands (skin) at the ends.
        foreach (var (x, tag) in new[] { (-0.235f, armL), (0.235f, armR) })
        {
            AddBox(st, new Vector3(x - 0.05f, 0.1f, -0.05f), new Vector3(x + 0.05f, 0.62f, 0.05f), tag);
            AddBox(st, new Vector3(x - 0.045f, 0.0f, -0.045f), new Vector3(x + 0.045f, 0.1f, 0.045f), tag, skin: true);
        }
        return st.Commit();
    }

    private static void AddBox(SurfaceTool st, Vector3 lo, Vector3 hi, float part, bool skin = false)
    {
        var c = (lo + hi) * 0.5f;
        var e = (hi - lo) * 0.5f;
        Vector3[] normals = { Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down, Vector3.Back, Vector3.Forward };
        foreach (var n in normals)
        {
            // u x v = n gives clockwise corners as seen from outside (Godot's front faces).
            var u = Mathf.Abs(n.Y) > 0.5f ? Vector3.Right : Vector3.Up.Cross(n);
            var v = n.Cross(u);
            u *= Mathf.Abs(u.X) * e.X + Mathf.Abs(u.Y) * e.Y + Mathf.Abs(u.Z) * e.Z;
            v *= Mathf.Abs(v.X) * e.X + Mathf.Abs(v.Y) * e.Y + Mathf.Abs(v.Z) * e.Z;
            var f = c + n * (Mathf.Abs(n.X) * e.X + Mathf.Abs(n.Y) * e.Y + Mathf.Abs(n.Z) * e.Z);
            Vector3 c0 = f - u - v, c1 = f - u + v, c2 = f + u + v, c3 = f + u - v;
            Tri(st, c0, c1, c2, n, part, skin);
            Tri(st, c0, c2, c3, n, part, skin);
        }
    }

    private static void AddSphere(SurfaceTool st, Vector3 centre, float r, float part)
    {
        const int seg = 10, rings = 7;
        Vector3 P(int i, int j)
        {
            float phi = Mathf.Pi * j / rings, theta = Mathf.Tau * i / seg;
            return centre + new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta)) * r;
        }
        for (int j = 0; j < rings; j++)
            for (int i = 0; i < seg; i++)
            {
                Vector3 a = P(i, j), b = P(i + 1, j), c = P(i, j + 1), d = P(i + 1, j + 1);
                if (j > 0) TriSmooth(st, a, b, c, centre, part);
                if (j < rings - 1) TriSmooth(st, b, d, c, centre, part);
            }
    }

    private static void Tri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 n, float part, bool skin)
    {
        // Clockwise as seen from outside: (b - a) x (c - a) points inwards.
        if ((b - a).Cross(c - a).Dot(n) > 0f) (b, c) = (c, b);
        var color = new Color(part, skin ? 1f : 0f, 0f);
        foreach (var p in new[] { a, b, c })
        {
            st.SetColor(color);
            st.SetNormal(n);
            st.AddVertex(p);
        }
    }

    private static void TriSmooth(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 centre, float part)
    {
        var n = ((a + b + c) / 3f - centre).Normalized();
        if ((b - a).Cross(c - a).Dot(n) > 0f) (b, c) = (c, b);
        var color = new Color(part, 0f, 0f);
        foreach (var p in new[] { a, b, c })
        {
            st.SetColor(color);
            st.SetNormal((p - centre).Normalized());
            st.AddVertex(p);
        }
    }
}
