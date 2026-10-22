using System;
using Godot;

namespace DestructionRacing.Vehicles;

/// <summary>
/// What shows through the spokes (post-M8 car look): a disc brake (the disc, its hat, the caliper and the dust
/// shield behind it) or a drum, in a wheel's own space (axle along x, centre at the origin). One mesh per wheel,
/// coloured by vertex (alpha = metalness, see car_brake.gdshader), so a wheel costs one draw call and no shadow.
/// It hangs on the wheel node with the wheel's spin taken out again (CarVisual), so the caliper stays put.
/// </summary>
public static class BrakeMesh
{
    private static readonly Color Disc = new(0.46f, 0.45f, 0.44f, 0.85f);
    private static readonly Color DiscFace = new(0.36f, 0.34f, 0.32f, 0.8f);
    private static readonly Color Hat = new(0.17f, 0.15f, 0.13f, 0.55f);
    private static readonly Color Caliper = new(0.10f, 0.095f, 0.09f, 0.45f);
    private static readonly Color Shield = new(0.035f, 0.034f, 0.033f, 0.3f);
    private static readonly Color Drum = new(0.15f, 0.135f, 0.12f, 0.5f);

    /// <param name="rimRadius">The rim's outer radius (m).</param>
    /// <param name="outboard">+1 when the wheel's outside faces +x (the right-hand wheels), -1 for the left.</param>
    /// <param name="drum">A drum brake (most 1980s cars' rear axle) instead of a disc.</param>
    /// <param name="front">Front calipers sit behind the axle, rear ones ahead of it.</param>
    public static ArrayMesh Build(float rimRadius, float outboard, bool drum, bool front)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float s = outboard >= 0f ? 1f : -1f;
        if (drum)
        {
            float rd = rimRadius - 0.055f;
            Tube(st, s, -0.055f, 0.02f, rd, Drum);
            Annulus(st, s, 0.02f, 0.035f, rd, Drum, +1f);
            Tube(st, s, 0.02f, 0.035f, 0.035f, Hat);
            Annulus(st, s, 0.035f, 0f, 0.035f, Hat, +1f);
            Annulus(st, s, -0.055f, 0f, rd + 0.012f, Shield, -1f);
            Annulus(st, s, -0.056f, 0f, rd + 0.012f, Shield, +1f);
        }
        else
        {
            float rd = rimRadius - 0.05f, ri = rd * 0.56f;
            const float x0 = -0.02f, x1 = 0.002f;           // the disc, just inside the spokes
            Tube(st, s, x0, x1, rd, Disc);
            Annulus(st, s, x1, ri, rd, DiscFace, +1f);
            Annulus(st, s, x0, ri, rd, DiscFace, -1f);
            Tube(st, s, x1, 0.032f, ri, Hat);
            Annulus(st, s, 0.032f, 0f, ri, Hat, +1f);
            Annulus(st, s, x0 - 0.022f, 0f, rd + 0.01f, Shield, +1f);
            // The caliper straddles the disc's rim, at 40° above the axle line.
            float angle = Mathf.DegToRad(40f);
            float rc = rd - 0.014f;
            var dir = new Vector3(0f, Mathf.Sin(angle), (front ? 1f : -1f) * Mathf.Cos(angle));
            var tangent = new Vector3(0f, dir.Z, -dir.Y);
            Box(st, s, new Vector3(0.5f * (x0 + x1), 0f, 0f) + dir * rc, new Vector3(0.03f, 0f, 0f), dir * 0.026f, tangent * 0.05f, Caliper);
        }
        return st.Commit();
    }

    // A triangle facing `outward` (Godot's front faces wind clockwise: (b - a) × (c - a) points into the face).
    private static void Tri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 n, Color col)
    {
        if ((b - a).Cross(c - a).Dot(n) > 0f) (b, c) = (c, b);
        foreach (var p in new[] { a, b, c })
        {
            st.SetColor(col);
            st.SetNormal(n);
            st.AddVertex(p);
        }
    }

    private static Vector3 At(float s, float x, float r, float a) => new(s * x, r * Mathf.Cos(a), r * Mathf.Sin(a));

    /// <summary>A cylinder wall round the axle from x0 to x1 (in the outboard frame), facing out.</summary>
    private static void Tube(SurfaceTool st, float s, float x0, float x1, float r, Color col, int sides = 28)
    {
        for (int i = 0; i < sides; i++)
        {
            float a0 = Mathf.Tau * i / sides, a1 = Mathf.Tau * (i + 1) / sides, am = 0.5f * (a0 + a1);
            var n = new Vector3(0f, Mathf.Cos(am), Mathf.Sin(am));
            Vector3 p00 = At(s, x0, r, a0), p01 = At(s, x0, r, a1), p10 = At(s, x1, r, a0), p11 = At(s, x1, r, a1);
            Tri(st, p00, p10, p11, n, col);
            Tri(st, p00, p11, p01, n, col);
        }
    }

    /// <summary>A flat ring at x from r0 to r1, facing +x (outboard, side = +1) or -x (side = -1).</summary>
    private static void Annulus(SurfaceTool st, float s, float x, float r0, float r1, Color col, float side, int sides = 28)
    {
        var n = new Vector3(s * side, 0f, 0f);
        for (int i = 0; i < sides; i++)
        {
            float a0 = Mathf.Tau * i / sides, a1 = Mathf.Tau * (i + 1) / sides;
            Vector3 i0 = At(s, x, r0, a0), i1 = At(s, x, r0, a1), o0 = At(s, x, r1, a0), o1 = At(s, x, r1, a1);
            Tri(st, i0, o0, o1, n, col);
            if (r0 > 0f) Tri(st, i0, o1, i1, n, col);
        }
    }

    /// <summary>A box from its centre and three half-axes (the x half-axis is along the axle, outboard frame).</summary>
    private static void Box(SurfaceTool st, float s, Vector3 c, Vector3 hx, Vector3 hy, Vector3 hz, Color col)
    {
        Vector3 M(Vector3 p) => new(s * p.X, p.Y, p.Z);
        Vector3[] axes = { hx, hy, hz };
        for (int k = 0; k < 3; k++)
        {
            Vector3 u = axes[k], v = axes[(k + 1) % 3], w = axes[(k + 2) % 3];
            foreach (float sign in new[] { -1f, 1f })
            {
                Vector3 f = c + u * sign;
                Vector3 a = M(f - v - w), b = M(f + v - w), cc = M(f + v + w), d = M(f - v + w);
                Vector3 n = M(u * sign).Normalized();
                Tri(st, a, b, cc, n, col);
                Tri(st, a, cc, d, n, col);
            }
        }
    }
}
