using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Menus;

/// <summary>
/// The front end's 3D showroom (plan §13): the car on a slow turntable in a dark studio (the Car Studio's look, with
/// a warm rim light), framed to the right of the menu panels.
/// </summary>
public partial class Showroom : Node3D
{
    private Node3D _turntable = null!;
    private CarModel? _model;
    private string _carId = "";
    private float _angle = 0.6f;

    public Camera3D Camera { get; set; } = null!;
    /// <summary>Turntable speed (rad/s).</summary>
    public float Spin { get; set; } = 0.22f;
    /// <summary>How far the car sits right of the screen centre (0 = centred).</summary>
    public float Offset { get; set; } = 1.7f;
    /// <summary>Far framing: the camera further back and looking over the car, so it sits low under wide panels.</summary>
    public bool Far { get; set; }
    public string CarId => _carId;

    public override void _Ready()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.07f, 0.07f, 0.08f),
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            TonemapExposure = 1.1f,
            SsaoEnabled = true,
            SsaoRadius = 1.0f,
            SsaoIntensity = 2.2f,
            SsrEnabled = true,
            SsrMaxSteps = 64,
            GlowEnabled = true,
            GlowIntensity = 0.35f,
            GlowBloom = 0.02f,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            AmbientLightEnergy = 0.9f,
            Sky = new Sky
            {
                SkyMaterial = new PanoramaSkyMaterial { Panorama = GD.Load<Texture2D>("res://assets/third_party/polyhaven/studio_small_09/studio_small_09_2k.hdr") },
                RadianceSize = Sky.RadianceSizeEnum.Size512,
            },
            SkyRotation = new Vector3(0f, Mathf.DegToRad(30f), 0f),
        };
        AddChild(new WorldEnvironment { Environment = env });

        var key = new DirectionalLight3D { ShadowEnabled = true, DirectionalShadowMaxDistance = 22f, ShadowBlur = 1.2f, LightEnergy = 0.9f, LightAngularDistance = 9f, LightSpecular = 0f };
        AddChild(key);
        var toKey = new Vector3(0.25f, 0.92f, 0.3f).Normalized();
        key.LookAtFromPosition(toKey * 10f, Vector3.Zero, Vector3.Up);
        // A warm hazard-yellow rim behind the car and a cool fill from the front left.
        AddChild(new OmniLight3D { Position = new Vector3(-3.5f, 2.2f, -4.5f), LightColor = new Color(1f, 0.72f, 0.25f), LightEnergy = 2.2f, OmniRange = 11f });
        AddChild(new OmniLight3D { Position = new Vector3(4.5f, 1.6f, 4.0f), LightColor = new Color(0.65f, 0.75f, 1f), LightEnergy = 0.6f, OmniRange = 12f });

        var floorMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/studio_floor.gdshader") };
        floorMat.SetShaderParameter("color_near", new Color(0.075f, 0.075f, 0.08f));
        floorMat.SetShaderParameter("color_far", env.BackgroundColor);
        floorMat.SetShaderParameter("roughness_value", 0.55f);
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(80f, 80f) },
            MaterialOverride = floorMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        // The turntable disc.
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 3.3f, BottomRadius = 3.3f, Height = 0.04f, RadialSegments = 96 },
            Position = new Vector3(0f, 0.02f, 0f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.1f, 0.11f), Roughness = 0.35f, Metallic = 0.6f },
        });
        _turntable = new Node3D { Name = "Turntable", Position = new Vector3(0f, 0.04f, 0f) };
        AddChild(_turntable);
    }

    /// <summary>Shows a car in a livery; the model is only rebuilt when the car changes.</summary>
    public void ShowCar(string carId, Livery livery, string plate)
    {
        if (_model == null || carId != _carId)
        {
            _model?.Root.QueueFree();
            _carId = carId;
            _model = CarModel.Load(carId, livery);
            _turntable.AddChild(_model.Root);
            _model.Flush();
        }
        else _model.ApplyLivery(livery);
        _model.SetPlate(plate);
    }

    public override void _Process(double delta)
    {
        if (!Visible || Camera == null) return;
        _angle += (float)delta * Spin;
        _turntable.Rotation = new Vector3(0f, _angle, 0f);
        // A low three-quarter view; the look point sits left of the car so the car stands right of the menus.
        var centre = new Vector3(0f, 0.55f, 0f);
        float az = Mathf.DegToRad(32f), el = Mathf.DegToRad(Far ? 12f : 8f), dist = Far ? 12.5f : 8.6f;
        var eye = centre + new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el)) * dist;
        var right = (centre - eye).Cross(Vector3.Up).Normalized();
        Camera.Fov = 30f;
        Camera.LookAtFromPosition(eye, centre - right * Offset + (Far ? new Vector3(0f, 2.1f, 0f) : Vector3.Zero), Vector3.Up);
        Fx.Focus.On(Camera, centre, 0.05f, near: false);
    }
}
