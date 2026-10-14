using System;
using System.Collections.Generic;
using Godot;

namespace DestructionRacing.Tracks;

/// <summary>
/// Grey-box Crossroads Speedway figure-8 (plan §7.3, M1c): two banked-less turns joined by two diagonal straights
/// that cross in the middle at grade — DD1's blind crossover. Built in code: road ribbon, edge walls with tyres
/// (open where the legs cross), a start/finish line, a 3-wide grid and the crossover zone.
/// </summary>
public partial class FigureEightTrack : Node3D
{
    [Export] public float TurnRadius { get; set; } = 70f;
    [Export] public float CrossingAngleDeg { get; set; } = 76f;
    [Export] public float RoadWidth { get; set; } = 13f;
    /// <summary>Distance from the crossover to the start/finish line along the first leg.</summary>
    [Export] public float StartLineFromCrossover { get; set; } = 85f;

    public TrackPath Path { get; private set; } = null!;
    /// <summary>Where the legs cross, and the lap positions (s) of the two passes through it.</summary>
    public Vector3 CrossoverPoint => Vector3.Zero;
    public float CrossoverS1 { get; private set; }
    public float CrossoverS2 { get; private set; }
    public float CrossoverRadius => RoadWidth * 1.1f;

    private readonly List<Vector3> _points = new();

    public override void _Ready()
    {
        BuildCentreline();
        BuildEnvironment();
        BuildGround();
        BuildRoad();
        BuildWalls();
        BuildStartLine();
    }

    /// <summary>Is a lap position inside one of the two crossover passes?</summary>
    public bool InCrossover(float s) =>
        MathF.Abs(Path.Delta(CrossoverS1, s)) < CrossoverRadius || MathF.Abs(Path.Delta(CrossoverS2, s)) < CrossoverRadius;

    /// <summary>Grid slot transform: row 0 is the front row, 3 abreast, rows 8 m apart behind the line.</summary>
    public Transform3D GridSlot(int index)
    {
        int row = index / 3, col = index % 3;
        float s = Path.Wrap(-6f - row * 8.5f - (col == 1 ? 3f : 0f));
        float lateral = (col - 1) * 4.0f;
        Vector3 pos = Path.PositionAt(s, lateral) + Vector3.Up * 0.62f;
        return new Transform3D(Basis.LookingAt(Path.TangentAt(s), Vector3.Up), pos);
    }

    // ------------------------------------------------------------------ geometry

    private void BuildCentreline()
    {
        float r = TurnRadius;
        float phi = Mathf.DegToRad(CrossingAngleDeg * 0.5f);
        float halfL = r / Mathf.Sin(phi);                 // centre of each turn from the crossover
        var c2 = new Vector3(halfL, 0f, 0f);
        var c1 = new Vector3(-halfL, 0f, 0f);
        var dA = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
        var dB = new Vector3(Mathf.Cos(phi), 0f, -Mathf.Sin(phi));
        float tangentLen = halfL * Mathf.Cos(phi);
        float sweep = Mathf.Pi + 2f * phi;

        var raw = new List<Vector3>();
        void Line(Vector3 from, Vector3 to)
        {
            int steps = Mathf.CeilToInt(from.DistanceTo(to));
            for (int i = 0; i < steps; i++) raw.Add(from.Lerp(to, (float)i / steps));
        }
        void Arc(Vector3 centre, Vector3 from, float angle)
        {
            Vector3 radial = from - centre;
            int steps = Mathf.CeilToInt(MathF.Abs(angle) * r);
            for (int i = 0; i < steps; i++) raw.Add(centre + radial.Rotated(Vector3.Up, angle * i / steps));
        }

        // Crossover → leg A out to turn 2 (left-hander) → leg B back through the crossover → turn 1 (right-hander).
        Line(Vector3.Zero, dA * tangentLen);
        Arc(c2, dA * tangentLen, sweep);
        Line(dB * tangentLen, -dB * tangentLen);
        Arc(c1, -dB * tangentLen, -sweep);
        Line(-dA * tangentLen, Vector3.Zero);

        // Re-sample at 1 m and rotate so s = 0 is the start/finish line on leg A.
        var resampled = Resample(raw, 1f);
        int startIndex = (int)MathF.Round(StartLineFromCrossover);
        for (int i = 0; i < resampled.Count; i++) _points.Add(resampled[(i + startIndex) % resampled.Count]);
        Path = new TrackPath(_points, RoadWidth);

        // The two passes through the crossover.
        float firstPass = Path.Wrap(-StartLineFromCrossover);
        float legALen = tangentLen;
        CrossoverS1 = firstPass;
        CrossoverS2 = Path.Wrap(firstPass + legALen + sweep * r + tangentLen);
    }

    private static List<Vector3> Resample(List<Vector3> pts, float spacing)
    {
        var result = new List<Vector3> { pts[0] };
        float carry = 0f;
        int n = pts.Count;
        for (int i = 0; i < n; i++)
        {
            Vector3 a = pts[i], b = pts[(i + 1) % n];
            float seg = a.DistanceTo(b);
            float d = spacing - carry;
            while (d <= seg)
            {
                result.Add(a.Lerp(b, d / seg));
                d += spacing;
            }
            carry = seg - (d - spacing);
        }
        if (result.Count > 1 && result[^1].DistanceTo(result[0]) < spacing * 0.5f) result.RemoveAt(result.Count - 1);
        return result;
    }

    // ------------------------------------------------------------------ visuals and collision

    private void BuildEnvironment()
    {
        var sky = new Sky { SkyMaterial = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.22f, 0.42f, 0.72f),
            SkyHorizonColor = new Color(0.72f, 0.70f, 0.66f),
            GroundBottomColor = new Color(0.20f, 0.17f, 0.14f),
            GroundHorizonColor = new Color(0.62f, 0.58f, 0.52f),
        } };
        AddChild(new WorldEnvironment { Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            SsaoEnabled = true,
            GlowEnabled = true,
            GlowIntensity = 0.4f,
            FogEnabled = true,
            FogLightColor = new Color(0.75f, 0.72f, 0.66f),
            FogDensity = 0.0015f,
        } });
        var sun = new DirectionalLight3D
        {
            LightEnergy = 1.6f,
            LightColor = new Color(1.0f, 0.93f, 0.82f),
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 250f,
        };
        sun.RotationDegrees = new Vector3(-40f, -35f, 0f);
        AddChild(sun);
    }

    private void BuildGround()
    {
        var ground = new StaticBody3D { Name = "Ground" };
        ground.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(800f, 600f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.45f, 0.24f), Roughness = 0.95f },
        });
        ground.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D() });
        AddChild(ground);
    }

    /// <summary>Road ribbon with white edge lines. The second half sits 4 mm higher so the crossover never z-fights.</summary>
    private void BuildRoad()
    {
        var road = new SurfaceTool();
        road.Begin(Mesh.PrimitiveType.Triangles);
        var lines = new SurfaceTool();
        lines.Begin(Mesh.PrimitiveType.Triangles);
        float half = RoadWidth * 0.5f;
        float sHalf = Path.Length * 0.5f;

        for (int i = 0; i < Path.Count; i++)
        {
            float s0 = i * Path.Spacing, s1 = (i + 1) * Path.Spacing;
            float y = (Path.Delta(CrossoverS1, s0) is > -40f and < 40f) ? 0.016f : 0.012f;
            if (s0 > sHalf) y += 0.004f;
            Quad(road, Path.PositionAt(s0, -half), Path.PositionAt(s0, half), Path.PositionAt(s1, -half), Path.PositionAt(s1, half), y);
            foreach (float edge in new[] { -half + 0.35f, half - 0.35f })
                Quad(lines, Path.PositionAt(s0, edge - 0.12f), Path.PositionAt(s0, edge + 0.12f),
                     Path.PositionAt(s1, edge - 0.12f), Path.PositionAt(s1, edge + 0.12f), y + 0.002f);
        }
        road.GenerateNormals();
        lines.GenerateNormals();
        AddChild(new MeshInstance3D
        {
            Name = "Road",
            Mesh = road.Commit(),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.30f, 0.30f, 0.31f), Roughness = 0.88f },
        });
        AddChild(new MeshInstance3D
        {
            Name = "EdgeLines",
            Mesh = lines.Commit(),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.92f, 0.92f, 0.88f), Roughness = 0.7f },
        });
    }

    private static void Quad(SurfaceTool st, Vector3 a0, Vector3 b0, Vector3 a1, Vector3 b1, float y)
    {
        a0.Y = b0.Y = a1.Y = b1.Y = y;
        st.AddVertex(a0); st.AddVertex(a1); st.AddVertex(b0);
        st.AddVertex(b0); st.AddVertex(a1); st.AddVertex(b1);
    }

    /// <summary>Concrete walls with a tyre row along both edges, left open where the other leg crosses.</summary>
    private void BuildWalls()
    {
        var wallMat = new StandardMaterial3D { AlbedoColor = new Color(0.72f, 0.71f, 0.68f), Roughness = 0.85f };
        var tyreMat = new StandardMaterial3D { AlbedoColor = new Color(0.06f, 0.06f, 0.06f), Roughness = 0.9f };
        var walls = new Node3D { Name = "Walls" };
        AddChild(walls);
        float half = RoadWidth * 0.5f + 0.5f;
        const float segment = 4f;
        int index = 0;
        // Wall ends at the crossover opening, by wall run (side and leg), to close the four corners afterwards.
        var ends = new List<(Vector3 P, int Run)>();

        StaticBody3D AddWall(Vector3 p0, Vector3 p1)
        {
            Vector3 mid = (p0 + p1) * 0.5f;
            var body = new StaticBody3D { Name = $"Wall{index++}" };
            walls.AddChild(body);
            body.Position = mid;
            body.LookAt(mid + (p1 - p0), Vector3.Up);
            var size = new Vector3(0.6f, 1.2f, p0.DistanceTo(p1) + 0.1f);
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = new Vector3(0f, 0.6f, 0f) });
            body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = wallMat, Position = new Vector3(0f, 0.6f, 0f) });
            return body;
        }

        foreach (float side in new[] { -1f, 1f })
        {
            bool afterOpening = false;
            for (float s = 0f; s < Path.Length; s += segment)
            {
                Vector3 p0 = Path.PositionAt(s, side * half);
                Vector3 p1 = Path.PositionAt(s + segment, side * half);
                Vector3 mid = (p0 + p1) * 0.5f;
                int run = (side < 0f ? 0 : 2) + (MathF.Abs(Path.Delta(CrossoverS1, s)) < 60f ? 0 : 1);
                // Check both ends too: the legs cross at an angle, so a segment's end can poke into the other road.
                if (OnOtherRoad(p0, s) || OnOtherRoad(mid, s) || OnOtherRoad(p1, s))
                {
                    if (!afterOpening) ends.Add((p0, run));
                    afterOpening = true;
                    continue;
                }

                // Where the wall resumes after the crossover opening, flare it outwards so a car drifting into
                // the opening is deflected back onto the road instead of hitting a blunt wall end head-on.
                if (afterOpening)
                {
                    afterOpening = false;
                    foreach (float flare in new[] { 1.8f, 1.2f, 0.6f })
                    {
                        Vector3 tip = Path.PositionAt(s, side * (half + flare));
                        if (OnOtherRoad(tip, s)) continue;
                        p0 = tip;
                        p1 = Path.PositionAt(s + 3f * segment, side * half);
                        s += 2f * segment; // the flare (about 8°) covers the next two segments too
                        break;
                    }
                    ends.Add((p0, run));
                }

                var body = AddWall(p0, p1);
                if (index % 2 == 0)
                    body.AddChild(new MeshInstance3D
                    {
                        Mesh = new CylinderMesh { TopRadius = 0.36f, BottomRadius = 0.36f, Height = 0.7f },
                        MaterialOverride = tyreMat,
                        Position = new Vector3(side * -0.75f, 0.35f, 0f),
                    });
            }
        }

        // Close each corner of the crossing: join every wall end to the nearest end of a wall of the other leg.
        var joined = new HashSet<int>();
        for (int i = 0; i < ends.Count; i++)
        {
            if (joined.Contains(i)) continue;
            int best = -1;
            float bestDist = 12f;
            for (int j = 0; j < ends.Count; j++)
            {
                if (j == i || joined.Contains(j) || ends[j].Run % 2 == ends[i].Run % 2) continue;
                float d = ends[i].P.DistanceTo(ends[j].P);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = j;
                }
            }
            if (best < 0) continue;
            joined.Add(i);
            joined.Add(best);
            if (bestDist > 0.3f) AddWall(ends[i].P, ends[best].P);
        }
    }

    /// <summary>True if a point lies on the road of the other leg (the crossover opening).</summary>
    private bool OnOtherRoad(Vector3 p, float ownS)
    {
        if (p.Length() > RoadWidth * 2.5f) return false;
        // Check the other pass explicitly (a global projection could land on our own leg).
        foreach (float cross in new[] { CrossoverS1, CrossoverS2 })
        {
            if (MathF.Abs(Path.Delta(cross, ownS)) < 40f) continue; // this is our own pass
            var (_, lateral) = Path.Project(p, cross, 40f);
            if (MathF.Abs(lateral) < RoadWidth * 0.5f + 1.0f) return true;
        }
        return false;
    }

    private void BuildStartLine()
    {
        var white = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.95f, 0.95f) };
        var black = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.05f, 0.05f) };
        Vector3 t = Path.TangentAt(0f);
        int cells = 13;
        float cell = RoadWidth / cells;
        for (int row = 0; row < 2; row++)
            for (int c = 0; c < cells; c++)
            {
                float lateral = -RoadWidth * 0.5f + (c + 0.5f) * cell;
                Vector3 pos = Path.PositionAt(row * cell, lateral) + Vector3.Up * 0.03f;
                AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(cell, 0.01f, cell) },
                    MaterialOverride = (c + row) % 2 == 0 ? white : black,
                    Transform = new Transform3D(Basis.LookingAt(t, Vector3.Up), pos),
                });
            }
    }
}
