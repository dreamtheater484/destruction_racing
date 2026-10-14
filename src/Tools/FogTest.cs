using DestructionRacing.Core;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Tools;

/// <summary>
/// Volumetric fog and smoke ghosting test (plan §8.1, M2): in the baked Bowl, a smoking car with a flickering
/// engine fire circles at speed under a moving chase camera, through dust haze. Variants:
///   --variant=default  Godot defaults (temporal reprojection 0.9, 64^3 froxels, fire lights the fog)
///   --variant=tuned    our settings (reprojection 0.5, 160x128 froxels, fog energy 0 on the flickering light)
///   --variant=off      no temporal reprojection (reference: noisy but ghost-free)
/// --out=out/fog --frames=150 saves every 10th frame after warm-up. Run with --fixed-fps 60.
/// --camera=pass starts with the fixed camera. Interactive: C toggles chase / fixed camera, Esc quits.
/// </summary>
public partial class FogTest : Node3D
{
    private CarModel _car = null!;
    private Camera3D _camera = null!;
    private OmniLight3D _fire = null!;
    private GpuParticles3D _smoke = null!;
    private float _angle;
    private int _frame;
    private string _out = "";
    private bool _pass;
    private int _frames = 150;
    private const float Radius = 20f, Speed = 16f;

    public override void _Ready()
    {
        var dev = DevTools.Instance;
        string variant = dev?.Arg("variant") ?? "tuned";
        _out = dev?.Arg("out") ?? "";
        if (int.TryParse(dev?.Arg("frames"), out int f)) _frames = f;
        _pass = dev?.Arg("camera") == "pass";
        if (_pass) _angle = 1.5f * Mathf.Pi - 1.6f;     // reaches the camera's side after 2 s

        var bowl = GD.Load<PackedScene>("res://scenes/bowl/BowlLook.tscn").Instantiate<Node3D>();
        AddChild(bowl);
        var env = bowl.GetNode<WorldEnvironment>("WorldEnvironment").Environment;
        env.VolumetricFogEnabled = true;
        env.VolumetricFogDensity = 0.004f;
        env.VolumetricFogAlbedo = new Color(0.82f, 0.8f, 0.76f);
        env.VolumetricFogAnisotropy = 0.35f;
        env.VolumetricFogLength = 90f;
        env.VolumetricFogTemporalReprojectionEnabled = variant != "off";
        env.VolumetricFogTemporalReprojectionAmount = variant == "default" ? 0.9f : 0.5f;
        if (variant == "tuned") RenderingServer.EnvironmentSetVolumetricFogVolumeSize(160, 128);

        // Dust haze drifting over one side of the arena.
        var noise = new NoiseTexture3D { Width = 64, Height = 32, Depth = 64, Seamless = true,
            Noise = new FastNoiseLite { Frequency = 0.035f, FractalOctaves = 3 } };
        AddChild(new FogVolume
        {
            Size = new Vector3(36f, 5f, 30f),
            Position = new Vector3(8f, 2.5f, -6f),
            Material = new FogMaterial { Density = 0.018f, Albedo = new Color(0.78f, 0.7f, 0.58f), DensityTexture = noise,
                HeightFalloff = 0.4f, EdgeFade = 0.3f },
        });

        _car = CarModel.Load("kitsune", Livery.KitsuneRed);
        _car.SetPlate("KTS 12");
        AddChild(_car.Root);
        _car.Damage.AddDent(new Vector3(-0.5f, 0.5f, -2.05f), new Vector3(0.08f, 0f, 0.25f), 0.55f);
        _car.Damage.AddSoot(new Vector3(0f, 0.75f, -1.5f), 0.6f, 0.6f);
        _car.Flush();

        _fire = new OmniLight3D
        {
            LightColor = new Color(1f, 0.55f, 0.2f),
            OmniRange = 5f,
            LightEnergy = 5f,
            LightVolumetricFogEnergy = variant == "tuned" ? 0f : 1f,
            Position = new Vector3(0f, 0.9f, -1.5f),
        };
        _car.Root.AddChild(_fire);
        _smoke = MakeSmoke();
        _car.Root.AddChild(_smoke);

        _camera = new Camera3D { Current = true, Fov = 60f, Near = 0.05f, Far = 2000f };
        AddChild(_camera);
        GetViewport().Msaa3D = Viewport.Msaa.Msaa4X;
        Place(0f);
        if (_out == "")
            KeyGuide.Add(this, $"Fog and smoke test ({variant} fog settings): a burning car laps through dust haze" + KeyGuide.NewLine +
                "C chase / fixed camera   Esc quit");
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        if (k.Keycode == Key.C) _pass = !_pass;
        if (k.Keycode == Key.Escape) GetTree().Quit();
    }

    private static GpuParticles3D MakeSmoke()
    {
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(0.22f, 0.22f, 0.23f, 0.38f));
        ramp.SetColor(1, new Color(0.5f, 0.5f, 0.5f, 0f));
        var blob = new GradientTexture2D
        {
            Width = 64, Height = 64, Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f),
            Gradient = new Gradient { Colors = new[] { new Color(1, 1, 1, 1), new Color(1, 1, 1, 0) } },
        };
        var process = new ParticleProcessMaterial
        {
            Direction = new Vector3(0f, 1f, 0.3f),
            Spread = 25f,
            InitialVelocityMin = 0.8f,
            InitialVelocityMax = 1.6f,
            Gravity = new Vector3(0f, 0.6f, 0f),
            DampingMin = 0.6f,
            DampingMax = 1.0f,
            ScaleMin = 0.7f,
            ScaleMax = 1.1f,
            ScaleCurve = new CurveTexture { Curve = Grow() },
            ColorRamp = new GradientTexture1D { Gradient = ramp },
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.25f,
        };
        var draw = new QuadMesh { Size = new Vector2(1.2f, 1.2f) };
        draw.Material = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = blob,
            ProximityFadeEnabled = true,
            ProximityFadeDistance = 0.6f,
            Roughness = 1f,
        };
        return new GpuParticles3D
        {
            Amount = 120,
            Lifetime = 2.0f,
            LocalCoords = false,
            ProcessMaterial = process,
            DrawPass1 = draw,
            Position = new Vector3(0f, 0.85f, -1.4f),
            VisibilityAabb = new Aabb(new Vector3(-30f, -2f, -30f), new Vector3(60f, 20f, 60f)),
        };
    }

    private static Curve Grow()
    {
        var c = new Curve();
        c.AddPoint(new Vector2(0f, 0.4f));
        c.AddPoint(new Vector2(1f, 3.2f));
        c.MaxValue = 4f;
        return c;
    }

    private void Place(float dt)
    {
        _angle += Speed / Radius * dt;
        var pos = new Vector3(Radius * Mathf.Cos(_angle), 0f, Radius * Mathf.Sin(_angle));
        var tangent = new Vector3(-Mathf.Sin(_angle), 0f, Mathf.Cos(_angle));
        _car.Root.GlobalTransform = new Transform3D(Basis.LookingAt(tangent, Vector3.Up), pos);
        foreach (string w in new[] { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" })
            if (_car.Node(w) is { } wheel) wheel.RotateObjectLocal(Vector3.Right, -Speed / 0.295f * dt);
        if (_pass)
        {
            // Fixed camera outside the circle: the car sweeps across the frame (trails show up behind it).
            _camera.LookAtFromPosition(new Vector3(0f, 1.8f, -29f), new Vector3(0f, 0.8f, -20f), Vector3.Up);
        }
        else
        {
            var eye = pos - tangent * 8f + Vector3.Up * 3.2f - tangent.Cross(Vector3.Up) * 2.2f;
            _camera.LookAtFromPosition(eye, pos + tangent * 3f + Vector3.Up * 0.6f, Vector3.Up);
        }
        _fire.LightEnergy = 3f + 5f * GD.Randf();
    }

    public override void _Process(double delta)
    {
        Place((float)delta);
        _frame++;
        if (_out == "") return;
        if (_frame > 60 && (_frame % 10 == 0 || (_pass && _frame % 2 == 0)))
        {
            string abs = ProjectSettings.GlobalizePath(_out.StartsWith("res://") ? _out : "res://" + _out);
            DirAccess.MakeDirRecursiveAbsolute(abs);
            GetViewport().GetTexture().GetImage().SavePng($"{abs}/frame_{_frame:000}.png");
        }
        if (_frame >= _frames)
        {
            GD.Print($"[FogTest] saved frames to {_out}");
            DevTools.QuitClean(GetTree(), 0);
        }
    }
}
