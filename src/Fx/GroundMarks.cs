using System;
using System.Collections.Generic;
using DestructionRacing.Rules;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Fx;

/// <summary>
/// What the cars leave on the Bowl's dirt floor (plan §7.4, §8.3): rolling ruts that build up where the field
/// drives, darker marks from sliding, locked or spinning wheels, gouges from a hub without its wheel, an oil
/// trail from a car with a critical front and an oil pool under each wreck. Everything is painted additively
/// into one texture over the floor (red = ruts and skids, green = oil) that the floor shader reads, and nothing
/// is ever cleared, so by the end the floor shows the whole fight. Painting costs a few dozen lines a frame.
/// Replays show the final marks (like the dents).
/// </summary>
public partial class GroundMarks : Node
{
    public IReadOnlyList<Car> Cars { get; set; } = Array.Empty<Car>();
    /// <summary>The floor whose material gets the marks (a ShaderMaterial with <c>marks</c> and <c>marks_on</c>).</summary>
    public MeshInstance3D? Floor { get; set; }
    /// <summary>Semi-axes of the area the texture covers (centred on the origin), as in the floor shader.</summary>
    public Vector2 Axes { get; set; } = new(55f, 38f);

    private const int TextureWidth = 2048;
    private const float TyreWidth = 0.24f;
    private const float RutPerPass = 0.035f, SkidPerPass = 0.45f;

    private sealed class Track
    {
        public readonly Vector3[] Last = new Vector3[4];
        public readonly bool[] Has = new bool[4];
        public Vector3 LastDrip;
        public double WreckedAt = -1, NextPool;
    }

    private SubViewport _viewport = null!;
    private GroundMarkCanvas _canvas = null!;
    private readonly Dictionary<Car, Track> _tracks = new();
    private readonly Random _rng = new(5);
    private Vector2 _pixelsPerMetre;
    private double _time;

    public override void _Ready()
    {
        int height = Mathf.RoundToInt(TextureWidth * Axes.Y / Axes.X);
        _pixelsPerMetre = new Vector2(TextureWidth / (2f * Axes.X), height / (2f * Axes.Y));
        _viewport = new SubViewport
        {
            Size = new Vector2I(TextureWidth, height),
            TransparentBg = true,
            Disable3D = true,
            RenderTargetClearMode = SubViewport.ClearMode.Once,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
        };
        AddChild(_viewport);
        _canvas = new GroundMarkCanvas { Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add } };
        _viewport.AddChild(_canvas);

        if (Floor?.GetActiveMaterial(0) is ShaderMaterial floorMaterial)
        {
            var material = (ShaderMaterial)floorMaterial.Duplicate();
            material.SetShaderParameter("marks", _viewport.GetTexture());
            material.SetShaderParameter("marks_on", 1f);
            if (Core.DevTools.Instance?.Arg("marks-debug") != null) material.SetShaderParameter("marks_debug", 1f);
            Floor.MaterialOverride = material;
        }
        else GD.PushWarning("[GroundMarks] no floor ShaderMaterial: marks are off");
    }

    public override void _ExitTree()
    {
        // Dev: --marks-dump=out/shots/marks.png saves the marks texture (red = ruts and skids, green = oil).
        if (Core.DevTools.Instance?.Arg("marks-dump") is { } path)
            _viewport.GetTexture().GetImage()?.SavePng(ProjectSettings.GlobalizePath(path.StartsWith("res://") ? path : "res://" + path));
    }

    public override void _Process(double delta)
    {
        _time += delta;
        foreach (var car in Cars)
        {
            if (!IsInstanceValid(car) || car.IsReplaying) continue;
            if (!_tracks.TryGetValue(car, out var track)) _tracks[car] = track = new Track();
            if (car.IsWrecked) Pool(car, track);
            else Tyres(car, track);
        }
        if (_canvas.Pending)
        {
            _canvas.QueueRedraw();
            _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        }
    }

    private void Tyres(Car car, Track track)
    {
        float speed = car.LinearVelocity.Length();
        var xf = car.GlobalTransform;
        for (int i = 0; i < 4; i++)
        {
            bool grounded = car.WheelMark(i, out Vector3 p, out float skid);
            float width = TyreWidth * (1f + 0.4f * skid);
            if (!grounded && car.Damage?.IsWheelLost((Wheel)i) == true)
            {
                // The bare hub digs a narrow, dark groove where the corner drags.
                Vector3 c = car.WheelCentre(i);
                p = car.ToGlobal(new Vector3(c.X, car.GroundY + 0.08f, c.Z));
                grounded = speed > 1.5f && p.Y < 0.35f;      // as the hub sparks (CarVisual)
                skid = 0.5f;
                width = 0.1f;
            }
            if (!grounded)
            {
                track.Has[i] = false;
                continue;
            }
            if (!track.Has[i])
            {
                track.Last[i] = p;
                track.Has[i] = true;
                continue;
            }
            float d = Flat(p - track.Last[i]).Length();
            if (d < (skid > 0.05f ? 0.3f : 0.7f)) continue;
            float amount = RutPerPass * Mathf.Clamp(speed / 3f, 0f, 1f) + SkidPerPass * skid;
            if (d < 3f && amount > 0.004f) _canvas.Line(ToPixels(track.Last[i]), ToPixels(p), width * _pixelsPerMetre.X, new Color(amount, 0f, 0f));
            track.Last[i] = p;
        }

        // Oil drips from a car whose radiator and engine are nearly gone.
        if (car.Health(DamageZone.Front) < 0.12f)
        {
            Vector3 engine = xf * new Vector3(0f, 0f, -car.HalfExtents.Z * 0.55f);
            if (Flat(engine - track.LastDrip).Length() > 0.8f)
            {
                track.LastDrip = engine;
                float r = (0.1f + (float)_rng.NextDouble() * 0.12f) * _pixelsPerMetre.X;
                _canvas.Dot(ToPixels(engine), r, new Color(0f, 0.3f, 0f));
            }
        }
    }

    /// <summary>A wreck bleeds oil for ten seconds: a pool that spreads under the engine.</summary>
    private void Pool(Car car, Track track)
    {
        if (track.WreckedAt < 0) track.WreckedAt = _time;
        double age = _time - track.WreckedAt;
        if (age > 10.0 || _time < track.NextPool) return;
        track.NextPool = _time + 0.5;
        Vector3 engine = car.GlobalTransform * new Vector3(0f, 0f, -car.HalfExtents.Z * 0.45f);
        float r = (0.3f + 0.9f * (float)(age / 10.0)) * _pixelsPerMetre.X;
        _canvas.Dot(ToPixels(engine), r, new Color(0f, 0.07f, 0f));
    }

    private Vector2 ToPixels(Vector3 p) => new((p.X + Axes.X) * _pixelsPerMetre.X, (p.Z + Axes.Y) * _pixelsPerMetre.Y);

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);
}

/// <summary>Draws the strokes queued since the last redraw into the marks texture (which is never cleared).</summary>
public partial class GroundMarkCanvas : Node2D
{
    private readonly List<(Vector2 A, Vector2 B, float Width, Color Color)> _lines = new();
    private readonly List<(Vector2 Centre, float Radius, Color Color)> _dots = new();

    public bool Pending => _lines.Count > 0 || _dots.Count > 0;

    public void Line(Vector2 a, Vector2 b, float width, Color color) => _lines.Add((a, b, width, color));

    public void Dot(Vector2 centre, float radius, Color color) => _dots.Add((centre, radius, color));

    public override void _Draw()
    {
        foreach (var (a, b, width, color) in _lines) DrawLine(a, b, color, width, true);
        foreach (var (centre, radius, color) in _dots) DrawCircle(centre, radius, color, true, -1f, true);
        _lines.Clear();
        _dots.Clear();
    }
}
