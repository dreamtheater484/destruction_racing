using System;
using System.Collections.Generic;
using Godot;

namespace DestructionRacing.Tracks;

/// <summary>
/// A closed racing line sampled every metre (plan §10.1): positions, tangents, signed curvature, a cornering and
/// braking speed profile, lane offsets and local projection (so the crossover of a figure-8 never confuses the two
/// legs). s = 0 is the start/finish line; lateral offset is positive to the right of the travel direction.
/// </summary>
public sealed class TrackPath
{
    private readonly Vector3[] _p;
    private readonly Vector3[] _t;
    private readonly float[] _k;
    private readonly float[] _v;
    private readonly float[] _bank;

    /// <param name="bank">Banking per point (rad, the outside of the turn higher); null = flat.</param>
    public TrackPath(IReadOnlyList<Vector3> points, float width, float lateralGrip = 7.6f, float braking = 8f,
        IReadOnlyList<float>? bank = null)
    {
        int n = points.Count;
        _p = new Vector3[n];
        _t = new Vector3[n];
        _k = new float[n];
        _v = new float[n];
        _bank = new float[n];
        for (int i = 0; i < n; i++) _p[i] = points[i];
        if (bank != null) for (int i = 0; i < n; i++) _bank[i] = bank[i];

        float length = 0f;
        for (int i = 0; i < n; i++) length += _p[i].DistanceTo(_p[(i + 1) % n]);
        Length = length;
        Spacing = length / n;
        Width = width;

        for (int i = 0; i < n; i++) _t[i] = (_p[(i + 1) % n] - _p[(i - 1 + n) % n]).Normalized();
        for (int i = 0; i < n; i++)
        {
            Vector3 a = _t[(i - 1 + n) % n], b = _t[(i + 1) % n];
            float turn = Mathf.Atan2(a.Cross(b).Y, a.Dot(b));     // + = turning left (CCW)
            _k[i] = turn / (2f * Spacing);
        }

        // Speed profile: cornering limit (banking adds to the grip: a = g(mu + tan b) / (1 - mu tan b)), then a
        // backward pass for braking (two laps to settle the wrap).
        float mu = lateralGrip / 9.81f;
        for (int i = 0; i < n; i++)
        {
            float k = MathF.Abs(SmoothedCurvature(i, 6));
            float tb = MathF.Tan(_bank[i]);
            float lateral = 9.81f * (mu + tb) / MathF.Max(1f - mu * tb, 0.2f);
            _v[i] = k > 1e-4f ? MathF.Min(90f, MathF.Sqrt(lateral / k)) : 90f;
        }
        for (int pass = 0; pass < 2; pass++)
            for (int i = n - 1; i >= 0; i--)
            {
                float next = _v[(i + 1) % n];
                _v[i] = MathF.Min(_v[i], MathF.Sqrt(next * next + 2f * braking * Spacing));
            }
    }

    public float Length { get; }
    public float Spacing { get; }
    public float Width { get; }
    public int Count => _p.Length;

    private int Index(float s) => ((int)MathF.Floor(Wrap(s) / Spacing)) % _p.Length;

    public float Wrap(float s) => ((s % Length) + Length) % Length;

    /// <summary>Signed distance from <paramref name="a"/> forward to <paramref name="b"/> along the lap, in (-L/2, L/2].</summary>
    public float Delta(float a, float b)
    {
        float d = Wrap(b - a);
        return d > Length * 0.5f ? d - Length : d;
    }

    private float SmoothedCurvature(int i, int half)
    {
        float sum = 0f;
        for (int j = -half; j <= half; j++) sum += _k[(i + j + _p.Length) % _p.Length];
        return sum / (2 * half + 1);
    }

    public Vector3 CentreAt(float s)
    {
        float u = Wrap(s) / Spacing;
        int i = (int)MathF.Floor(u) % _p.Length;
        float f = u - MathF.Floor(u);
        return _p[i].Lerp(_p[(i + 1) % _p.Length], f);
    }

    public Vector3 TangentAt(float s) => _t[Index(s)];

    /// <summary>Unit vector to the right of the travel direction at s.</summary>
    public Vector3 RightAt(float s)
    {
        var t = TangentAt(s);
        return new Vector3(-t.Z, 0f, t.X);
    }

    public Vector3 PositionAt(float s, float lateral) => CentreAt(s) + RightAt(s) * lateral;

    /// <summary>Signed curvature (1/m, + = left turn), smoothed over ±4 m.</summary>
    public float CurvatureAt(float s) => SmoothedCurvature(Index(s), 4);

    public float SpeedAt(float s) => _v[Index(s)];

    /// <summary>Banking under the line at s (rad, the outside of the turn higher).</summary>
    public float BankAt(float s) => _bank[Index(s)];

    /// <summary>
    /// Projects a world point onto the line, searching only ±<paramref name="window"/> metres around
    /// <paramref name="hintS"/> (pass a negative hint for a global search).
    /// </summary>
    public (float S, float Lateral) Project(Vector3 p, float hintS, float window = 40f)
    {
        int n = _p.Length;
        int start, count;
        if (hintS < 0f)
        {
            start = 0;
            count = n;
        }
        else
        {
            int w = (int)(window / Spacing);
            start = Index(hintS) - w;
            count = 2 * w + 1;
        }

        float best = float.MaxValue;
        int bi = 0;
        for (int j = 0; j < count; j++)
        {
            int i = ((start + j) % n + n) % n;
            float dx = p.X - _p[i].X, dz = p.Z - _p[i].Z;
            float d = dx * dx + dz * dz;
            if (d < best)
            {
                best = d;
                bi = i;
            }
        }

        // Refine on the segment bi → bi+1 (or bi-1 → bi).
        Vector3 a = _p[bi], b = _p[(bi + 1) % n];
        Vector3 ab = b - a;
        float f = Mathf.Clamp((p - a).Dot(ab) / MathF.Max(ab.LengthSquared(), 1e-6f), -1f, 1f);
        float s = Wrap((bi + f) * Spacing);
        Vector3 c = CentreAt(s);
        float lateral = (p - c).Dot(RightAt(s));
        return (s, lateral);
    }
}
