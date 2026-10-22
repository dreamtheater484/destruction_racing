using System.Collections.Generic;
using DestructionRacing.Tracks;
using Godot;

namespace DestructionRacing.Cameras;

/// <summary>
/// DD2-style TV cameras around the Bowl (plan §15.5, §4.1 "Spectate"): towers, grandstand roofs, the scoreboard
/// and low trackside posts. It follows <see cref="Focus"/> with a long lens, zooms to keep the car framed, and cuts
/// to a better-placed camera after a few seconds (or at once when the car leaves the shot, or a building hides it).
/// </summary>
public partial class TvDirector : Camera3D
{
    public Node3D? Focus { get; set; }
    /// <summary>The venue's camera positions (posts below 4 m are low trackside ones); the Bowl's when not set.</summary>
    public IReadOnlyList<Vector3>? Posts { get; set; }
    /// <summary>The venue's buildings: a post that sees one of them in front of the car is skipped.</summary>
    public IReadOnlyList<Blocker>? Blockers { get; set; }

    /// <summary>
    /// A box the cameras can't see through: a stand roof, a pit garage, a billboard. The venue's look has no
    /// collision, so the line of sight is tested against these boxes (taken from its meshes) instead of the physics.
    /// A backdrop (the stands' terraces and back walls) only counts when it hides the car: the crowd at the side of
    /// the frame is a good shot.
    /// </summary>
    public readonly struct Blocker
    {
        private readonly Transform3D _toBox;
        private readonly Vector3 _half;
        public string Name { get; }
        public bool Backdrop { get; }

        public Blocker(string name, Transform3D box, Vector3 size, bool backdrop)
        {
            Name = name;
            _toBox = box.AffineInverse();
            _half = size * 0.5f;
            Backdrop = backdrop;
        }

        /// <summary>Whether the segment from <paramref name="a"/> to <paramref name="b"/> passes through the box.</summary>
        public bool Hits(Vector3 a, Vector3 b)
        {
            Vector3 p = _toBox * a, d = _toBox * b - p;
            float t0 = 0f, t1 = 1f;
            for (int k = 0; k < 3; k++)
            {
                float h = _half[k];
                if (Mathf.Abs(d[k]) < 1e-6f)
                {
                    if (Mathf.Abs(p[k]) > h) return false;
                    continue;
                }
                float u = (-h - p[k]) / d[k], v = (h - p[k]) / d[k];
                t0 = Mathf.Max(t0, Mathf.Min(u, v));
                t1 = Mathf.Min(t1, Mathf.Max(u, v));
                if (t0 > t1) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// The blockers of a venue's look: a box round each mesh under <paramref name="root"/> that <paramref name="pick"/>
    /// names, false for a solid one, true for a backdrop.
    /// </summary>
    public static List<Blocker> BlockersIn(Node root, System.Func<string, bool?> pick)
    {
        var list = new List<Blocker>();
        foreach (var node in root.FindChildren("*", nameof(MeshInstance3D), true, false))
            if (node is MeshInstance3D { Mesh: { } mesh } mi && pick(mi.Name) is bool backdrop)
            {
                var box = mesh.GetAabb();
                list.Add(new Blocker(mi.Name, mi.GlobalTransform.TranslatedLocal(box.GetCenter()), box.Size, backdrop));
            }
        return list;
    }

    private readonly List<Vector3> _posts = new();
    private int _current = -1;
    private double _sinceCut;
    private Vector3 _look;
    private float _fov = 30f;

    public override void _Ready()
    {
        Near = 0.1f;
        Far = 4000f;
        if (Posts != null)
        {
            _posts.AddRange(Posts);
            return;
        }
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                _posts.Add(new Vector3(sx * 49f, 14f, sz * 44f));                          // floodlight towers
        // Above the front edge of the grandstand roofs (the roof starts ~1.8 m behind the wall at 9.4 m; a post on the
        // roof itself saw only corrugated sheet when the action was under the stand).
        _posts.Add(new Vector3(0f, 13f, Bowl.SemiAxisZ + 1.2f));                             // grandstand roofs
        _posts.Add(new Vector3(0f, 13f, -Bowl.SemiAxisZ - 1.2f));
        _posts.Add(new Vector3(Bowl.SemiAxisX + 8f, 8f, 0f));                                // the scoreboard
        for (int i = 0; i < 6; i++)
        {
            float t = Mathf.Tau * (i + 0.5f) / 6f;
            // Trackside, low: on the wall cap, inside the catch fence (at 0.48 m). Behind it, 12 cm from the wire, the long
            // lens blew every diamond of the mesh up to a quarter of the frame.
            _posts.Add(Bowl.Ellipse(t, 0.2f) + Vector3.Up * 2.4f);
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
        Vector3 car = Focus.GlobalPosition + Vector3.Up * 0.6f;
        Vector3 target = car;
        if (Focus is RigidBody3D body) target += body.LinearVelocity * 0.25f;

        // Score the cameras: a good distance, not too close to the last cut, the car not hidden behind the wall
        // (the low posts only see cars near them) or a building.
        int best = _current;
        float current = _current >= 0 ? Score(_posts[_current], target, car, -1f, Blockers) : 0f;
        float bestScore = _current >= 0 ? current * 1.35f : -1f;
        for (int i = 0; i < _posts.Count; i++)
        {
            if (i == _current) continue;
            float s = Score(_posts[i], target, car, bestScore, Blockers);
            if (s > bestScore) { bestScore = s; best = i; }
        }
        bool lost = _current < 0 || _posts[_current].DistanceTo(target) > 95f || current < 0.05f;
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
        // A long lens: the car sharp, the stands behind it soft.
        Fx.Focus.On(this, car, 0.06f);
    }

    private static float FovFor(float distance) => Mathf.Clamp(Mathf.RadToDeg(2f * Mathf.Atan(7.5f / Mathf.Max(distance, 1f))), 9f, 60f);

    /// <summary>
    /// How good a shot of the car <paramref name="post"/> gives; the line of sight is only tested when the placement
    /// alone beats <paramref name="floor"/> (a post that can't win needn't be checked).
    /// </summary>
    public static float Score(Vector3 post, Vector3 target, Vector3 car, float floor, IReadOnlyList<Blocker>? blockers)
    {
        float d = post.DistanceTo(target);
        bool low = post.Y < 4f;
        if (low && d > 30f) return 0f;                 // the wall hides far cars from the low posts
        float ideal = low ? 14f : 38f;
        float s = 1f / (1f + Mathf.Abs(d - ideal) / 15f);
        if (low) s *= 1.15f;                           // low shots are the exciting ones
        if (s <= floor || blockers == null) return s;
        return Hidden(post, car, blockers) == 0 ? s : 0f;
    }

    /// <summary>
    /// Whether a building stands between <paramref name="post"/> and the car: 0 clear, 1 the car is hidden, 2 the car
    /// shows but a building fills a side or the bottom of the frame (a roof just in front of or beside the long lens
    /// covers most of it). The frame is tested at five points 60% of the way from the car to its edges.
    /// </summary>
    public static int Hidden(Vector3 post, Vector3 car, IReadOnlyList<Blocker> blockers, float aspect = 16f / 9f) =>
        Hidden(post, car, blockers, aspect, out _);

    /// <inheritdoc cref="Hidden(Vector3, Vector3, IReadOnlyList{Blocker}, float)"/>
    public static int Hidden(Vector3 post, Vector3 car, IReadOnlyList<Blocker> blockers, float aspect, out string? by)
    {
        by = null;
        Vector3 view = car - post;
        float d = view.Length();
        if (d < 0.5f) return 0;
        view /= d;
        Vector3 up = Vector3.Up - view * view.Y;
        if (up.LengthSquared() < 1e-4f) up = Vector3.Forward;
        up = up.Normalized();
        Vector3 right = view.Cross(up);
        float h = 0.6f * d * Mathf.Tan(Mathf.DegToRad(FovFor(d) * 0.5f)), w = h * aspect;
        System.Span<Vector3> frame = stackalloc Vector3[]
        {
            car - up * h, car - up * h - right * w, car - up * h + right * w, car - right * w, car + right * w,
        };
        int hidden = 0;
        for (int i = 0; i < blockers.Count; i++)        // not foreach: no enumerator boxed every frame
        {
            var b = blockers[i];
            if (b.Hits(post, car))
            {
                by = b.Name;
                return 1;
            }
            if (hidden != 0 || b.Backdrop) continue;
            foreach (var p in frame)
                if (b.Hits(post, p))
                {
                    hidden = 2;
                    by = b.Name;
                    break;
                }
        }
        return hidden;
    }
}
