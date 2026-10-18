using System.Collections.Generic;
using Godot;

namespace DestructionRacing.Fx;

/// <summary>
/// Pooled one-shot effects for one scene (plan §8.3): spark bursts, glass shards, paint chips and dust puffs on
/// GPU particles that bounce off the floor (y = 0). Also builds the continuous emitters a car carries (smoke,
/// steam, fire, scrape sparks). All look-dev is procedural: see assets/shaders/fx_*.gdshader.
/// </summary>
public partial class FxPool : Node3D
{
    private enum Burst { Sparks, Glass, Chips, Dust }

    private readonly Dictionary<Burst, List<GpuParticles3D>> _pools = new();
    private readonly Dictionary<Burst, int> _next = new();
    private static Shader? _spark, _shard, _smoke, _fire;

    private static readonly NodePath PoolPath = "FxPool";

    /// <summary>The pool of the current scene, created on first use (asked for every frame: the path is made once).</summary>
    public static FxPool Get(Node from)
    {
        var tree = from.GetTree();
        Node scene = tree.CurrentScene ?? tree.Root;
        if (scene.GetNodeOrNull<FxPool>(PoolPath) is { } pool) return pool;
        pool = new FxPool { Name = "FxPool" };
        scene.AddChild(pool);
        return pool;
    }

    public override void _Ready()
    {
        // Floor collision for every particle in the arena (the Bowl and the tracks are flat at y = 0).
        AddChild(new GpuParticlesCollisionBox3D { Size = new Vector3(600f, 2f, 600f), Position = new Vector3(0f, -1f, 0f) });
        Fill(Burst.Sparks, 12, SparkBurst);
        Fill(Burst.Glass, 6, () => ShardBurst(true));
        Fill(Burst.Chips, 8, () => ShardBurst(false));
        Fill(Burst.Dust, 6, DustBurst);
    }

    private void Fill(Burst kind, int count, System.Func<GpuParticles3D> make)
    {
        var list = new List<GpuParticles3D>();
        for (int i = 0; i < count; i++)
        {
            var p = make();
            p.OneShot = true;
            p.Emitting = false;
            AddChild(p);
            list.Add(p);
        }
        _pools[kind] = list;
        _next[kind] = 0;
    }

    private GpuParticles3D Take(Burst kind, Vector3 point, Vector3 up)
    {
        var list = _pools[kind];
        int i = _next[kind];
        _next[kind] = (i + 1) % list.Count;
        var p = list[i];
        p.GlobalTransform = new Transform3D(BasisAlong(up), point);
        return p;
    }

    private static void Fire(GpuParticles3D p, float ratio)
    {
        p.AmountRatio = Mathf.Clamp(ratio, 0.05f, 1f);
        p.Restart();
        p.Emitting = true;
    }

    /// <summary>A basis whose Y axis is <paramref name="up"/>.</summary>
    public static Basis BasisAlong(Vector3 up)
    {
        if (up.LengthSquared() < 1e-6f) up = Vector3.Up;
        up = up.Normalized();
        Vector3 helper = Mathf.Abs(up.Y) > 0.95f ? Vector3.Right : Vector3.Up;
        Vector3 x = helper.Cross(up).Normalized();
        Vector3 z = x.Cross(up).Normalized();
        return new Basis(x, up, z);
    }

    // ------------------------------------------------------------------ public bursts

    /// <summary>Spark burst at a hit (<paramref name="strength"/> 0..1 scales the count).</summary>
    public void Sparks(Vector3 point, Vector3 normal, float strength) =>
        Fire(Take(Burst.Sparks, point, (normal + Vector3.Up * 0.8f).Normalized()), strength);

    /// <summary>Glass shards from a shattered window or lamp, thrown along <paramref name="direction"/>.</summary>
    public void Glass(Vector3 point, Vector3 direction, float amount) =>
        Fire(Take(Burst.Glass, point, (direction + Vector3.Up * 0.5f).Normalized()), amount);

    /// <summary>Paint chips knocked off a panel.</summary>
    public void Chips(Vector3 point, Vector3 normal, Color paint, float amount)
    {
        var p = Take(Burst.Chips, point, (normal + Vector3.Up * 0.6f).Normalized());
        ((ParticleProcessMaterial)p.ProcessMaterial).Color = paint;
        Fire(p, amount);
    }

    /// <summary>A puff of arena dirt (heavy landings, wheels torn off, parts hitting the ground).</summary>
    public void Dust(Vector3 point, float amount) => Fire(Take(Burst.Dust, point, Vector3.Up), amount);

    // ------------------------------------------------------------------ emitter factories

    private static ShaderMaterial Mat(ref Shader? cache, string name)
    {
        cache ??= GD.Load<Shader>($"res://assets/shaders/{name}.gdshader");
        return new ShaderMaterial { Shader = cache };
    }

    private static Gradient Ramp(params (float At, Color C)[] stops)
    {
        var g = new Gradient();
        g.SetOffset(0, stops[0].At);
        g.SetColor(0, stops[0].C);
        g.SetOffset(1, stops[^1].At);
        g.SetColor(1, stops[^1].C);
        for (int i = 1; i < stops.Length - 1; i++) g.AddPoint(stops[i].At, stops[i].C);
        return g;
    }

    private static CurveTexture Curve(params (float X, float Y)[] pts)
    {
        var c = new Curve { MaxValue = 4f };
        foreach (var (x, y) in pts) c.AddPoint(new Vector2(x, y));
        return new CurveTexture { Curve = c };
    }

    private static GpuParticles3D Particles(int amount, float lifetime, ParticleProcessMaterial process, Mesh draw)
    {
        process.CollisionMode = ParticleProcessMaterial.CollisionModeEnum.Rigid;
        process.CollisionUseScale = true;
        return new GpuParticles3D
        {
            Amount = amount,
            Lifetime = lifetime,
            LocalCoords = false,
            ProcessMaterial = process,
            DrawPass1 = draw,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-12f, -4f, -12f), new Vector3(24f, 16f, 24f)),
            CollisionBaseSize = 0.02f,
        };
    }

    private static Mesh SparkMesh()
    {
        var m = Mat(ref _spark, "fx_spark");
        return new QuadMesh { Size = new Vector2(0.032f, 0.26f), Material = m };
    }

    private static GpuParticles3D SparkBurst()
    {
        var pm = new ParticleProcessMaterial
        {
            Direction = Vector3.Up,
            Spread = 70f,
            InitialVelocityMin = 3.5f,
            InitialVelocityMax = 12f,
            Gravity = new Vector3(0f, -9.8f, 0f),
            DampingMin = 0.5f,
            DampingMax = 2f,
            ParticleFlagAlignY = true,
            ScaleMin = 0.5f,
            ScaleMax = 1.3f,
            ScaleCurve = Curve((0f, 1f), (1f, 0.25f)),
            ColorRamp = new GradientTexture1D { Gradient = Ramp((0f, new Color(1f, 0.97f, 0.8f, 1f)), (0.35f, new Color(1f, 0.62f, 0.15f, 1f)), (1f, new Color(0.7f, 0.12f, 0.02f, 0f))) },
            CollisionBounce = 0.35f,
            CollisionFriction = 0.15f,
            LifetimeRandomness = 0.5,
        };
        var p = Particles(72, 0.7f, pm, SparkMesh());
        p.Explosiveness = 0.92f;
        return p;
    }

    /// <summary>Continuous sparks for a scrape (enable while metal grinds; aim Y along the throw).</summary>
    public static GpuParticles3D ScrapeSparks()
    {
        var pm = new ParticleProcessMaterial
        {
            Direction = Vector3.Up,
            Spread = 35f,
            InitialVelocityMin = 3f,
            InitialVelocityMax = 9f,
            Gravity = new Vector3(0f, -9.8f, 0f),
            DampingMin = 0.5f,
            DampingMax = 1.5f,
            ParticleFlagAlignY = true,
            ScaleMin = 0.4f,
            ScaleMax = 1f,
            ScaleCurve = Curve((0f, 1f), (1f, 0.3f)),
            ColorRamp = new GradientTexture1D { Gradient = Ramp((0f, new Color(1f, 0.95f, 0.75f, 1f)), (0.4f, new Color(1f, 0.55f, 0.12f, 1f)), (1f, new Color(0.6f, 0.1f, 0.02f, 0f))) },
            CollisionBounce = 0.3f,
            CollisionFriction = 0.2f,
            LifetimeRandomness = 0.5,
        };
        var p = Particles(72, 0.45f, pm, SparkMesh());
        p.Emitting = false;
        return p;
    }

    private static GpuParticles3D ShardBurst(bool glass)
    {
        var pm = new ParticleProcessMaterial
        {
            Direction = Vector3.Up,
            Spread = 75f,
            InitialVelocityMin = glass ? 1.5f : 1.2f,
            InitialVelocityMax = glass ? 4.5f : 3.5f,
            Gravity = new Vector3(0f, -9.8f, 0f),
            ParticleFlagRotateY = true,
            AngleMin = 0f,
            AngleMax = 360f,
            AngularVelocityMin = -720f,
            AngularVelocityMax = 720f,
            ScaleMin = 0.5f,
            ScaleMax = 1.6f,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = glass ? 0.25f : 0.12f,
            ColorRamp = new GradientTexture1D { Gradient = Ramp((0f, Colors.White), (0.85f, Colors.White), (1f, new Color(1f, 1f, 1f, 0f))) },
            CollisionBounce = 0.15f,
            CollisionFriction = 0.8f,
            LifetimeRandomness = 0.3,
        };
        var mat = Mat(ref _shard, "fx_shard");
        mat.SetShaderParameter("glass", glass ? 1f : 0f);
        Mesh mesh = glass
            ? new PrismMesh { Size = new Vector3(0.035f, 0.03f, 0.006f), Material = mat }
            : new QuadMesh { Size = new Vector2(0.022f, 0.018f), Material = mat };
        var p = Particles(glass ? 64 : 40, glass ? 3f : 2.5f, pm, mesh);
        p.Explosiveness = 0.95f;
        return p;
    }

    private static GpuParticles3D DustBurst()
    {
        var pm = new ParticleProcessMaterial
        {
            Direction = Vector3.Up,
            Spread = 85f,
            InitialVelocityMin = 0.6f,
            InitialVelocityMax = 2.6f,
            Gravity = new Vector3(0f, 0.15f, 0f),
            DampingMin = 1.5f,
            DampingMax = 2.5f,
            AngleMin = 0f,
            AngleMax = 360f,
            ScaleMin = 0.7f,
            ScaleMax = 1.3f,
            ScaleCurve = Curve((0f, 0.6f), (1f, 3f)),
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.5f,
            ColorRamp = new GradientTexture1D { Gradient = Ramp((0f, new Color(0.56f, 0.46f, 0.34f, 0f)), (0.12f, new Color(0.56f, 0.46f, 0.34f, 0.5f)), (1f, new Color(0.6f, 0.52f, 0.42f, 0f))) },
        };
        var p = Particles(18, 2.2f, pm, PuffMesh(1.0f, 1f));
        p.Explosiveness = 0.9f;
        return p;
    }

    private static Mesh PuffMesh(float size, float billow)
    {
        var m = Mat(ref _smoke, "fx_smoke");
        m.SetShaderParameter("billow", billow);
        m.SetShaderParameter("noise_tex", SmokeNoise());
        return new QuadMesh { Size = new Vector2(size, size), Material = m };
    }

    private static ImageTexture? _smokeNoise;

    /// <summary>
    /// The smoke's billow noise (fx_smoke.gdshader): 4 octaves of value noise (smoothstep between hashed lattice
    /// values, amplitudes 0.5, 0.25, 0.125, 0.0625), 8 cells across and tiling, baked once.
    /// </summary>
    public static ImageTexture SmokeNoise()
    {
        if (_smokeNoise != null) return _smokeNoise;
        const int size = 256, cells = 8;
        var data = new byte[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float sum = 0f, amp = 0.5f;
                for (int o = 0; o < 4; o++)
                {
                    int period = cells << o;
                    float px = (float)x / size * period, py = (float)y / size * period;
                    int ix = (int)px, iy = (int)py;
                    float fx = px - ix, fy = py - iy;
                    fx = fx * fx * (3f - 2f * fx);
                    fy = fy * fy * (3f - 2f * fy);
                    float a = Hash(ix, iy, period, o), b = Hash(ix + 1, iy, period, o);
                    float c = Hash(ix, iy + 1, period, o), d = Hash(ix + 1, iy + 1, period, o);
                    sum += amp * Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
                    amp *= 0.5f;
                }
                data[y * size + x] = (byte)Mathf.Clamp(Mathf.RoundToInt(sum * 255f), 0, 255);
            }
        var image = Image.CreateFromData(size, size, false, Image.Format.R8, data);
        image.GenerateMipmaps();
        _smokeNoise = ImageTexture.CreateFromImage(image);
        return _smokeNoise;
    }

    private static float Hash(int x, int y, int period, int octave)
    {
        x = ((x % period) + period) % period;
        y = ((y % period) + period) % period;
        uint h = (uint)(x * 374761393 + y * 668265263 + octave * 1442695041);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / 16777216f;
    }

    /// <summary>
    /// The engine plume of a damaged car (steam, then grey, then black smoke). Set <see cref="ParticleProcessMaterial.Color"/>
    /// for tint and density; it trails in world space as the car moves.
    /// </summary>
    public static GpuParticles3D Smoke()
    {
        var pm = new ParticleProcessMaterial
        {
            Direction = Vector3.Up,
            Spread = 18f,
            InitialVelocityMin = 0.8f,
            InitialVelocityMax = 1.8f,
            Gravity = new Vector3(0f, 0.9f, 0f),
            DampingMin = 0.3f,
            DampingMax = 0.8f,
            AngleMin = 0f,
            AngleMax = 360f,
            ScaleMin = 0.6f,
            ScaleMax = 1.1f,
            ScaleCurve = Curve((0f, 0.7f), (1f, 3.6f)),
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.25f,
            ColorRamp = new GradientTexture1D { Gradient = Ramp((0f, new Color(1f, 1f, 1f, 0f)), (0.1f, Colors.White), (1f, new Color(1f, 1f, 1f, 0f))) },
            Color = new Color(0.9f, 0.9f, 0.9f, 0.4f),
        };
        pm.CollisionMode = ParticleProcessMaterial.CollisionModeEnum.Disabled;
        var p = new GpuParticles3D
        {
            Amount = 96,              // dense enough to stay a plume, not a dotted line, behind a car at speed
            Lifetime = 2.6f,
            LocalCoords = false,
            ProcessMaterial = pm,
            DrawPass1 = PuffMesh(1.0f, 1f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-20f, -2f, -20f), new Vector3(40f, 20f, 40f)),
            Emitting = false,
        };
        return p;
    }

    /// <summary>
    /// Engine fire: flame tongues (pair it with a flickering light with volumetric fog energy 0). Local
    /// coordinates: keep the emitter's basis world-aligned and lean the flames into the wind via the gravity.
    /// </summary>
    public static GpuParticles3D Flames()
    {
        var pm = new ParticleProcessMaterial
        {
            Direction = Vector3.Up,
            Spread = 12f,
            InitialVelocityMin = 0.6f,
            InitialVelocityMax = 1.6f,
            Gravity = new Vector3(0f, 2.5f, 0f),
            DampingMin = 0.5f,
            DampingMax = 1f,
            AngleMin = 0f,
            AngleMax = 360f,
            ScaleMin = 0.55f,
            ScaleMax = 1.0f,
            ScaleCurve = Curve((0f, 0.7f), (0.3f, 1f), (1f, 0.35f)),
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(0.35f, 0.05f, 0.25f),
            ColorRamp = new GradientTexture1D { Gradient = Ramp((0f, new Color(1f, 1f, 1f, 0f)), (0.15f, Colors.White), (1f, new Color(1f, 1f, 1f, 0f))) },
        };
        var m = Mat(ref _fire, "fx_fire");
        var p = new GpuParticles3D
        {
            Amount = 36,
            Lifetime = 0.6f,
            LocalCoords = true,       // flames ride with the car (lean them back with gravity); smoke trails behind
            ProcessMaterial = pm,
            DrawPass1 = new QuadMesh { Size = new Vector2(0.55f, 0.55f), Material = m },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-10f, -2f, -10f), new Vector3(20f, 10f, 20f)),
            Emitting = false,
        };
        return p;
    }
}
