using System;
using System.Collections.Generic;
using Godot;

namespace DestructionRacing.Tracks;

/// <summary>
/// One Crossroads Speedway layout in one direction (plan §7.3: forward and reverse): the racing line with s = 0 at
/// this direction's start line, the grid, three timing sectors, the crossover passes (figure-8), the pit lane in the
/// direction of travel and the terrain under it all.
/// </summary>
public sealed class RaceLayout
{
    public SpeedwayLayout Id { get; }
    public bool Reverse { get; }
    public TrackPath Path { get; }
    public int Laps { get; }
    public float Width => Path.Width;
    public PitLane Pit { get; }
    /// <summary>Lap positions of the passes through the crossover (empty on the oval).</summary>
    public IReadOnlyList<float> CrossoverPasses => _crossPasses;
    public Vector3 CrossoverPoint { get; }
    public float CrossoverRadius { get; }
    public bool HasCrossover => _crossPasses.Count > 0;
    /// <summary>Where sectors 1 and 2 end (sector 3 ends at the line).</summary>
    public float[] SectorEnds { get; }

    private readonly SpeedwayData _data;
    private readonly List<float> _crossPasses = new();

    public string Name => DisplayName(Id, Reverse);

    public static string DisplayName(SpeedwayLayout id, bool reverse) =>
        (id == SpeedwayLayout.Oval ? "Oval" : "Figure-8") + (reverse ? " (reverse)" : "");

    public RaceLayout(SpeedwayData data, SpeedwayLayout id, bool reverse)
    {
        _data = data;
        Id = id;
        Reverse = reverse;
        var src = data.Get(id);
        Laps = src.Laps;
        int n = src.Points.Length;
        float spacing = src.Length / n;
        int start = (int)MathF.Round((reverse ? src.StartReverse : src.StartForward) / spacing) % n;
        var pts = new Vector3[n];
        var bank = new float[n];
        for (int i = 0; i < n; i++)
        {
            int k = reverse ? ((start - i) % n + n) % n : (start + i) % n;
            pts[i] = new Vector3(src.Points[k].X, 0f, src.Points[k].Y);
            bank[i] = src.Bank[k];
        }
        Path = new TrackPath(pts, data.RoadWidth, bank: bank);
        SectorEnds = new[] { Path.Length / 3f, Path.Length * 2f / 3f };

        if (src.Crossover is Vector2 c)
        {
            CrossoverPoint = new Vector3(c.X, 0f, c.Y);
            CrossoverRadius = src.CrossoverRadius;
            // The two passes: the closest point to the crossover, then the closest one at least a quarter lap away.
            float best = float.MaxValue, second = float.MaxValue;
            int bi = -1, si = -1;
            for (int i = 0; i < n; i++)
            {
                float d = new Vector2(pts[i].X - c.X, pts[i].Z - c.Y).Length();
                if (d < best) { best = d; bi = i; }
            }
            for (int i = 0; i < n; i++)
            {
                if (MathF.Abs(Path.Delta(bi * Path.Spacing, i * Path.Spacing)) < Path.Length * 0.25f) continue;
                float d = new Vector2(pts[i].X - c.X, pts[i].Z - c.Y).Length();
                if (d < second) { second = d; si = i; }
            }
            _crossPasses.Add(bi * Path.Spacing);
            if (si >= 0) _crossPasses.Add(si * Path.Spacing);
            _crossPasses.Sort();
        }

        var lane = new Vector3[src.Pit.Length];
        for (int i = 0; i < lane.Length; i++)
        {
            var p = src.Pit[reverse ? lane.Length - 1 - i : i];
            lane[i] = new Vector3(p.X, 0f, p.Y);
        }
        var boxes = new Vector3[src.PitBoxes.Length];
        for (int i = 0; i < boxes.Length; i++)
        {
            var b = src.PitBoxes[reverse ? boxes.Length - 1 - i : i];
            boxes[i] = new Vector3(b.X, 0f, b.Y);
        }
        Pit = new PitLane(lane, boxes, Path, this);
    }

    public bool InCrossover(float s)
    {
        foreach (float cross in _crossPasses)
            if (MathF.Abs(Path.Delta(cross, s)) < CrossoverRadius) return true;
        return false;
    }

    public float HeightAt(float x, float z) => _data.HeightAt(x, z);
    /// <summary>The surface code under a world position (<see cref="SpeedwayData.Pit"/> on the pit lanes).</summary>
    public byte SurfaceAt(Vector3 p) => _data.Surface.Nearest(p.X, p.Z);
    public float HeightAt(Vector3 p) => _data.HeightAt(p.X, p.Z);

    /// <summary>The terrain normal at a world x, z (from the heightfield).</summary>
    public Vector3 NormalAt(float x, float z)
    {
        const float e = 0.5f;
        float hx = _data.HeightAt(x + e, z) - _data.HeightAt(x - e, z);
        float hz = _data.HeightAt(x, z + e) - _data.HeightAt(x, z - e);
        return new Vector3(-hx, 2f * e, -hz).Normalized();
    }

    /// <summary>A point on the line at s, offset sideways, on the terrain (plus <paramref name="lift"/>).</summary>
    public Vector3 SurfacePoint(float s, float lateral, float lift = 0f)
    {
        var p = Path.PositionAt(s, lateral);
        p.Y = HeightAt(p) + lift;
        return p;
    }

    /// <summary>A car pose on the line at s: facing along the track, standing on the (banked) surface.</summary>
    public Transform3D PoseAt(float s, float lateral, float lift = 0.62f)
    {
        var p = SurfacePoint(s, lateral, lift);
        var up = NormalAt(p.X, p.Z);
        var fwd = Path.TangentAt(s);
        fwd = (fwd - up * fwd.Dot(up)).Normalized();
        return new Transform3D(Basis.LookingAt(fwd, up), p);
    }

    /// <summary>Grid slot: row 0 is the front row, 3 abreast, rows 8.5 m apart behind the line (the middle car 3 m back).</summary>
    public Transform3D GridSlot(int index)
    {
        int row = index / 3, col = index % 3;
        float s = Path.Wrap(-6f - row * 8.5f - (col == 1 ? 3f : 0f));
        return PoseAt(s, (col - 1) * 4.0f);
    }

    /// <summary>Where a flipped or stranded car is put back (plan §6.0): on the line near s, inside the road.</summary>
    public Transform3D ResetPose(float s, float lateral)
    {
        float half = Width * 0.5f - 2.2f;
        return PoseAt(s, Mathf.Clamp(lateral, -half, half), 0.9f);
    }
}

/// <summary>
/// A pit lane in the direction of travel (plan §6.0): from the entry on the track, past the boxes, back onto the track.
/// The pit lane is a polyline; cars on it are guided along it.
/// </summary>
public sealed class PitLane
{
    private readonly Vector3[] _p;
    private readonly float[] _cum;

    public PitLane(Vector3[] points, Vector3[] boxes, TrackPath track, RaceLayout layout)
    {
        _p = points;
        _cum = new float[points.Length];
        for (int i = 1; i < points.Length; i++) _cum[i] = _cum[i - 1] + points[i].DistanceTo(points[i - 1]);
        Length = _cum[^1];
        EntryS = track.Project(points[0], -1f).S;
        ExitS = track.Project(points[^1], -1f).S;
        Boxes = new float[boxes.Length];
        for (int b = 0; b < boxes.Length; b++) Boxes[b] = Project(boxes[b]);
        Layout = layout;
    }

    public RaceLayout Layout { get; }
    public float Length { get; }
    /// <summary>Lap positions where the pit lane leaves and rejoins the track.</summary>
    public float EntryS { get; }
    public float ExitS { get; }
    /// <summary>Distances along the lane of the boxes, in the order cars reach them.</summary>
    public float[] Boxes { get; }

    public Vector3 PositionAt(float u)
    {
        u = Mathf.Clamp(u, 0f, Length);
        int i = Array.BinarySearch(_cum, u);
        if (i >= 0) return _p[i];
        i = ~i;
        if (i <= 0) return _p[0];
        if (i >= _p.Length) return _p[^1];
        float f = (u - _cum[i - 1]) / MathF.Max(_cum[i] - _cum[i - 1], 1e-4f);
        return _p[i - 1].Lerp(_p[i], f);
    }

    public Vector3 TangentAt(float u)
    {
        var a = PositionAt(u - 1f);
        var b = PositionAt(u + 1f);
        var t = b - a;
        t.Y = 0f;
        return t.LengthSquared() > 1e-6f ? t.Normalized() : Vector3.Forward;
    }

    /// <summary>Distance along the lane of the nearest point to <paramref name="p"/>.</summary>
    public float Project(Vector3 p)
    {
        float best = float.MaxValue, bu = 0f;
        for (int i = 0; i < _p.Length - 1; i++)
        {
            Vector3 a = _p[i], b = _p[i + 1];
            Vector3 ab = b - a;
            ab.Y = 0f;
            Vector3 ap = p - a;
            ap.Y = 0f;
            float f = Mathf.Clamp(ap.Dot(ab) / MathF.Max(ab.LengthSquared(), 1e-6f), 0f, 1f);
            float d = (ap - ab * f).LengthSquared();
            if (d < best) { best = d; bu = _cum[i] + f * (_cum[i + 1] - _cum[i]); }
        }
        return bu;
    }
}
