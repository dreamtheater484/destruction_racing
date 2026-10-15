using System.Collections.Generic;
using DestructionRacing.Tracks;
using Godot;

namespace DestructionRacing.Cameras;

/// <summary>
/// DD2-style TV cameras around the Bowl (plan §15.5, §4.1 "Spectate"): towers, grandstand roofs, the scoreboard
/// and low trackside posts. It follows <see cref="Focus"/> with a long lens, zooms to keep the car framed, and cuts
/// to a better-placed camera after a few seconds (or at once when the car leaves the shot).
/// </summary>
public partial class TvDirector : Camera3D
{
    public Node3D? Focus { get; set; }

    private readonly List<Vector3> _posts = new();
    private int _current = -1;
    private double _sinceCut;
    private Vector3 _look;
    private float _fov = 30f;

    public override void _Ready()
    {
        Near = 0.1f;
        Far = 4000f;
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                _posts.Add(new Vector3(sx * 49f, 14f, sz * 44f));                          // floodlight towers
        _posts.Add(new Vector3(0f, 10.5f, Bowl.SemiAxisZ + 9f));                             // grandstand roofs
        _posts.Add(new Vector3(0f, 10.5f, -Bowl.SemiAxisZ - 9f));
        _posts.Add(new Vector3(Bowl.SemiAxisX + 8f, 8f, 0f));                                // the scoreboard
        for (int i = 0; i < 6; i++)
        {
            float t = Mathf.Tau * (i + 0.5f) / 6f;
            _posts.Add(Bowl.Ellipse(t, 0.6f) + Vector3.Up * 2.4f);                            // trackside, low
        }
    }

    /// <summary>Starts on the best camera at once (no blend).</summary>
    public void Snap()
    {
        _current = -1;
        _sinceCut = 100;
    }

    public override void _Process(double delta)
    {
        if (Focus == null || !IsInstanceValid(Focus) || !Current) return;
        float rdt = (float)(delta / System.Math.Max(Engine.TimeScale, 0.01));
        _sinceCut += rdt;
        Vector3 target = Focus.GlobalPosition + Vector3.Up * 0.6f;
        if (Focus is RigidBody3D body) target += body.LinearVelocity * 0.25f;

        // Score the cameras: a good distance, not too close to the last cut, the car not hidden behind the wall
        // (the low posts only see cars near them).
        int best = _current;
        float bestScore = _current >= 0 ? Score(_current, target) * 1.35f : -1f;
        for (int i = 0; i < _posts.Count; i++)
        {
            if (i == _current) continue;
            float s = Score(i, target);
            if (s > bestScore) { bestScore = s; best = i; }
        }
        bool lost = _current < 0 || _posts[_current].DistanceTo(target) > 95f || Score(_current, target) < 0.05f;
        if (best != _current && (lost || _sinceCut > 4.0))
        {
            _current = best;
            _sinceCut = 0;
            _look = target;
            _fov = FovFor(_posts[_current].DistanceTo(target));
        }
        _look = _look.Lerp(target, 1f - Mathf.Exp(-5f * rdt));
        _fov = Mathf.Lerp(_fov, FovFor(_posts[_current].DistanceTo(_look)), 1f - Mathf.Exp(-3f * rdt));
        Fov = _fov;
        GlobalTransform = new Transform3D(Basis.Identity, _posts[_current]).LookingAt(_look, Vector3.Up);
    }

    private static float FovFor(float distance) => Mathf.Clamp(Mathf.RadToDeg(2f * Mathf.Atan(7.5f / Mathf.Max(distance, 1f))), 9f, 60f);

    private float Score(int i, Vector3 target)
    {
        float d = _posts[i].DistanceTo(target);
        bool low = _posts[i].Y < 4f;
        if (low && d > 30f) return 0f;                 // the wall hides far cars from the low posts
        float ideal = low ? 14f : 38f;
        float s = 1f / (1f + Mathf.Abs(d - ideal) / 15f);
        if (low) s *= 1.15f;                           // low shots are the exciting ones
        return s;
    }
}
