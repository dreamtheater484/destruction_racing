using Godot;

namespace DestructionRacing.Tracks;

/// <summary>
/// Grey-box Bowl for M0/M1: an oval arena with walls, built in code, plus the 20-car start ring
/// (placeholder boxes facing the centre, as in DD's opening crunch). Static collision is included.
/// </summary>
public partial class GreyBoxArena : Node3D
{
    [Export] public float SemiAxisX { get; set; } = 55f;
    [Export] public float SemiAxisZ { get; set; } = 38f;
    [Export] public int WallSegments { get; set; } = 96;
    [Export] public int StartCars { get; set; } = 20;
    [Export] public bool WithOverviewCamera { get; set; } = true;

    private static readonly Color[] Liveries =
    {
        new(0.80f, 0.08f, 0.06f), new(0.05f, 0.25f, 0.70f), new(0.95f, 0.70f, 0.05f), new(0.10f, 0.50f, 0.20f),
        new(0.90f, 0.40f, 0.05f), new(0.55f, 0.10f, 0.55f), new(0.90f, 0.90f, 0.88f), new(0.12f, 0.12f, 0.13f),
        new(0.20f, 0.65f, 0.80f), new(0.70f, 0.55f, 0.35f),
    };

    public override void _Ready()
    {
        BuildEnvironment();
        BuildGround();
        BuildWalls();
        if (StartCars > 0) BuildStartRing();
        if (WithOverviewCamera) BuildCamera();
    }

    private void BuildEnvironment()
    {
        var sky = new Sky { SkyMaterial = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.22f, 0.42f, 0.72f),
            SkyHorizonColor = new Color(0.72f, 0.70f, 0.66f),
            GroundBottomColor = new Color(0.20f, 0.17f, 0.14f),
            GroundHorizonColor = new Color(0.62f, 0.58f, 0.52f),
            SunAngleMax = 25f,
        } };

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            SsaoEnabled = true,
            SsaoRadius = 1.5f,
            SsaoIntensity = 1.6f,
            GlowEnabled = true,
            GlowIntensity = 0.4f,
            FogEnabled = true,
            FogLightColor = new Color(0.75f, 0.72f, 0.66f),
            FogDensity = 0.0025f,
            FogAerialPerspective = 0.4f,
        };
        AddChild(new WorldEnvironment { Environment = env });

        var sun = new DirectionalLight3D
        {
            LightEnergy = 1.6f,
            LightColor = new Color(1.0f, 0.93f, 0.82f),
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 220f,
        };
        sun.RotationDegrees = new Vector3(-38f, -55f, 0f);
        AddChild(sun);
    }

    private void BuildGround()
    {
        // DD1's Bowl has a grey asphalt floor; outside the walls is dry grass.
        var body = new StaticBody3D { Name = "Ground" };
        body.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(400f, 400f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.40f, 0.45f, 0.26f), Roughness = 0.95f },
        });
        body.AddChild(new MeshInstance3D
        {
            Name = "Asphalt",
            Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 0.02f, RadialSegments = 96, Rings = 1 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.34f, 0.34f, 0.35f), Roughness = 0.9f },
            Scale = new Vector3(SemiAxisX + 1f, 1f, SemiAxisZ + 1f),
        });
        body.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D() });
        AddChild(body);
    }

    private void BuildWalls()
    {
        var wallMaterial = new StandardMaterial3D { AlbedoColor = new Color(0.72f, 0.71f, 0.68f), Roughness = 0.85f };
        var tyreMaterial = new StandardMaterial3D { AlbedoColor = new Color(0.06f, 0.06f, 0.06f), Roughness = 0.9f };
        var walls = new Node3D { Name = "Walls" };
        AddChild(walls);

        for (int i = 0; i < WallSegments; i++)
        {
            float t0 = Mathf.Tau * i / WallSegments;
            float t1 = Mathf.Tau * (i + 1) / WallSegments;
            var p0 = new Vector3(SemiAxisX * Mathf.Cos(t0), 0f, SemiAxisZ * Mathf.Sin(t0));
            var p1 = new Vector3(SemiAxisX * Mathf.Cos(t1), 0f, SemiAxisZ * Mathf.Sin(t1));
            var mid = (p0 + p1) * 0.5f;
            float length = p0.DistanceTo(p1) + 0.05f;

            var segment = new StaticBody3D { Name = $"Wall{i}" };
            walls.AddChild(segment);
            segment.Position = mid;
            segment.LookAt(mid + (p1 - p0), Vector3.Up); // local -Z along the wall

            var size = new Vector3(1.0f, 1.6f, length);
            segment.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = size },
                MaterialOverride = wallMaterial,
                Position = new Vector3(0, size.Y * 0.5f, 0),
            });
            segment.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = size },
                Position = new Vector3(0, size.Y * 0.5f, 0),
            });

            // A row of tyres in front of the concrete (inside the bowl), DD style.
            var inward = -mid.Normalized() * 0.9f;
            segment.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.38f, BottomRadius = 0.38f, Height = 0.75f },
                MaterialOverride = tyreMaterial,
                Position = segment.ToLocal(segment.GlobalPosition + inward) + new Vector3(0, 0.38f, 0),
            });
        }
    }

    private void BuildStartRing()
    {
        var cars = new Node3D { Name = "StartRing" };
        AddChild(cars);
        float scale = 0.78f;
        for (int i = 0; i < StartCars; i++)
        {
            float t = Mathf.Tau * i / StartCars;
            var pos = new Vector3(SemiAxisX * scale * Mathf.Cos(t), 0f, SemiAxisZ * scale * Mathf.Sin(t));
            var car = new Node3D { Name = $"Car{i + 1:00}", Position = pos };
            cars.AddChild(car);
            car.LookAt(Vector3.Zero, Vector3.Up); // nose (-Z) points at the centre

            var paint = new StandardMaterial3D
            {
                AlbedoColor = Liveries[i % Liveries.Length],
                Metallic = 0.3f,
                Roughness = 0.35f,
                Clearcoat = 0.8f,
            };
            var roof = new StandardMaterial3D { AlbedoColor = (i % 4) switch
            {
                0 => new Color(0.95f, 0.80f, 0.10f), // Div 4 yellow
                1 => new Color(0.15f, 0.40f, 0.85f), // Div 3 blue
                2 => new Color(0.80f, 0.10f, 0.10f), // Div 2 red
                _ => new Color(0.05f, 0.05f, 0.05f), // Div 1 black
            }, Roughness = 0.4f };

            car.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.8f, 0.75f, 4.6f) }, MaterialOverride = paint, Position = new Vector3(0, 0.62f, 0) });
            car.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.55f, 0.5f, 2.2f) }, MaterialOverride = roof, Position = new Vector3(0, 1.24f, 0.25f) });
        }
    }

    private void BuildCamera()
    {
        var cam = new Camera3D { Fov = 55f, Current = true };
        AddChild(cam);
        cam.Position = new Vector3(0f, 32f, 70f);
        cam.LookAt(new Vector3(0f, 0f, 4f), Vector3.Up);
    }
}
