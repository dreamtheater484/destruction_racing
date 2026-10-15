using System.Collections.Generic;
using System.Linq;
using DestructionRacing.Core;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Tools;

/// <summary>
/// Look-development stage for the generated cars: three lighting presets, a turntable, zebra and clay checks.
/// Interactive: arrows orbit, W/S zoom, 1-3 preset, N/Z/C normal/zebra/clay, L livery, T turntable, P photo,
/// D random dent, B derby damage, X repair, Tab next car, H hide the key guide.
/// Batch: --shots=out/studio [--car=kitsune] [--presets=studio,outdoor] [--modes=normal,zebra] [--damage=derby]
///        [--views=q34f,side,...] renders every combination and quits.
/// </summary>
public partial class PhotoStudio : Node3D
{
    private sealed record View(string Name, float Azimuth, float Elevation, float Distance, Vector3 Target, float Fov = 30f);

    private static readonly View[] Views =
    {
        new("q34f", 38f, 11f, 6.4f, new Vector3(0f, 0.55f, 0f)),
        new("side", 90f, 3f, 7.6f, new Vector3(0f, 0.6f, 0f)),
        new("q34r", 142f, 13f, 6.6f, new Vector3(0f, 0.55f, 0f)),
        new("front", 0f, 5f, 7.0f, new Vector3(0f, 0.6f, 0f)),
        new("close", 52f, 5f, 3.3f, new Vector3(0.45f, 0.5f, -1.15f), 34f),
        new("high", 62f, 48f, 7.8f, new Vector3(0f, 0.4f, 0f)),
        new("rearlow", 200f, 4f, 4.2f, new Vector3(-0.2f, 0.55f, 1.5f), 34f),
    };

    private static readonly Livery[] Liveries =
    {
        Livery.KitsuneRed,
        new(new Color(0.9f, 0.9f, 0.88f), new Color(0.03f, 0.05f, 0.25f), 1),
        new(new Color(0.06f, 0.16f, 0.42f), new Color(0.75f, 0.76f, 0.78f), 2, Metallic: 0.7f, Roughness: 0.42f),
        new(new Color(0.95f, 0.72f, 0.05f), new Color(0.02f, 0.02f, 0.02f), 3),
        new(new Color(0.3f, 0.32f, 0.3f), new Color(0.5f, 0.1f, 0.05f), 0, Metallic: 0.8f, Wear: 0.6f, Dirt: 0.7f),
    };

    private readonly string[] _presets = { "studio", "outdoor", "arena" };
    private CarModel _car = null!;
    private Node3D _turntable = null!;
    private Camera3D _camera = null!;
    private WorldEnvironment _world = null!;
    private DirectionalLight3D _sun = null!;
    private MeshInstance3D _floor = null!;
    private float _az = 38f, _el = 11f, _dist = 6.4f, _fov = 30f;
    private Vector3 _target = new(0f, 0.55f, 0f);
    private bool _spin;
    private int _livery, _mode, _shot, _number = 12;
    private string _carId = "kitsune";
    private Label? _help;
    private string _preset = "studio";

    public override void _Ready()
    {
        var dev = DevTools.Instance;
        string carId = dev?.Arg("car") ?? "kitsune";
        _turntable = new Node3D { Name = "Turntable" };
        AddChild(_turntable);
        LoadCar(carId);
        if (int.TryParse(dev?.Arg("livery"), out int liv) && liv > 0) SetLivery(liv);
        if (dev?.Arg("damage") is { } dmg) ApplyDamagePreset(dmg);

        _world = new WorldEnvironment();
        AddChild(_world);
        _sun = new DirectionalLight3D { ShadowEnabled = true, DirectionalShadowMaxDistance = 22f, ShadowBlur = 1.2f };
        AddChild(_sun);
        _floor = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(80f, 80f) }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_floor);
        _camera = new Camera3D { Current = true, Near = 0.05f, Far = 500f };
        AddChild(_camera);
        GetViewport().Msaa3D = Viewport.Msaa.Msaa4X;
        GetViewport().ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Smaa;

        ApplyPreset(dev?.Arg("preset") ?? "studio");
        ApplyView(Views[0]);
        string? shots = dev?.Arg("shots");
        if (shots != null) RunShots(shots, dev!);
        else
        {
            _help = KeyGuide.Add(this, "");
            UpdateHelp();
        }
    }

    private static string[] Cars => Vehicles.CarCatalog.Ids;

    /// <summary>Showroom paint per model (L then cycles the test liveries and the drivers' signature liveries).</summary>
    private static Livery Showroom(string carId) => carId switch
    {
        "brougham" => Livery.BroughamBlue,
        "nordvik" => Livery.NordvikBeige,
        "homestead" => new(new Color(0.12f, 0.2f, 0.14f), new Color(0.9f, 0.9f, 0.88f), 0, Metallic: 0.6f, Roughness: 0.4f, Number: 2),
        "interceptor" => new(new Color(0.03f, 0.03f, 0.035f), new Color(0.78f, 0.6f, 0.22f), 1, Number: 3),
        "adler" => new(new Color(0.9f, 0.9f, 0.88f), new Color(0.1f, 0.25f, 0.6f), 0, Number: 6),
        "volant" => new(new Color(0.72f, 0.06f, 0.05f), new Color(0.93f, 0.93f, 0.9f), 0, Number: 7),
        "hikari" => new(new Color(0.92f, 0.92f, 0.9f), new Color(0.03f, 0.03f, 0.035f), 2, Number: 9),
        "ryujin" => new(new Color(0.42f, 0.43f, 0.46f), new Color(0.12f, 0.12f, 0.13f), 2, Metallic: 0.7f, Roughness: 0.4f, Number: 11),
        _ => Liveries[0],
    };

    private List<Livery> LiveriesFor(string carId)
    {
        var list = new List<Livery> { Showroom(carId) };
        list.AddRange(Liveries.Skip(1).Select(l => l with { Number = _number }));
        list.AddRange(AI.Drivers.All.Where(d => d.Car == carId).Select(d => d.LiveryFor(DestructionRacing.Modes.DerbyMode.RoofColor(2))));
        return list;
    }

    private void LoadCar(string carId)
    {
        _carId = carId;
        _car?.Root.QueueFree();
        var info = Vehicles.CarCatalog.Find(carId);
        _number = info?.Number ?? 1;
        _car = CarModel.Load(carId, Showroom(carId) with { Number = _number });
        _turntable.AddChild(_car.Root);
        _livery = 0;
        _car.SetPlate(info != null ? $"{info.Make[..3].ToUpperInvariant()} {info.Number:00}" : "DD 01");
        _car.SetDebugMode(_mode);
        UpdateHelp();
    }

    private void UpdateHelp()
    {
        if (_help == null) return;
        string mode = _mode switch { 1 => "zebra", 2 => "clay", _ => "paint" };
        _help.Text = string.Join(KeyGuide.NewLine,
            $"#{_number} {Vehicles.CarCatalog.Find(_carId)?.FullName ?? _carId}    livery {_livery}    light: {_preset}    view: {mode}",
            "Tab next car   1/2/3 studio/outdoor/arena light   arrows orbit   W/S zoom   T turntable",
            "B derby damage   D random dent   X repair   L livery   N/Z/C paint/zebra/clay   P photo (out/studio)   H hide   Esc quit");
    }

    private async void RunShots(string dir, DevTools dev)
    {
        string abs = ProjectSettings.GlobalizePath(dir.StartsWith("res://") || dir.StartsWith("user://") ? dir : "res://" + dir);
        DirAccess.MakeDirRecursiveAbsolute(abs);
        string[] presets = (dev.Arg("presets") ?? "studio,outdoor").Split(',');
        string[] modes = (dev.Arg("modes") ?? "normal").Split(',');
        var views = (dev.Arg("views") ?? string.Join(",", Views.Select(v => v.Name))).Split(',')
            .Select(n => Views.First(v => v.Name == n)).ToList();
        GetViewport().Scaling3DScale = 1.5f;
        foreach (string preset in presets)
        {
            ApplyPreset(preset);
            foreach (string mode in modes)
            {
                _car.SetDebugMode(mode switch { "zebra" => 1, "clay" => 2, _ => 0 });
                foreach (var v in views)
                {
                    ApplyView(v);
                    for (int i = 0; i < 12; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    string path = $"{abs}/{preset}_{mode}_{v.Name}.png";
                    GetViewport().GetTexture().GetImage().SavePng(path);
                    GD.Print($"[PhotoStudio] {path}");
                }
            }
        }
        DevTools.QuitClean(GetTree(), 0);
    }

    private void ApplyPreset(string name)
    {
        _preset = name;
        UpdateHelp();
        var env = new Godot.Environment
        {
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            SsaoEnabled = true,
            SsaoRadius = 1.0f,
            SsaoIntensity = 2.2f,
            SsrEnabled = true,
            SsrMaxSteps = 96,
            GlowEnabled = true,
            GlowIntensity = 0.35f,
            GlowBloom = 0.02f,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
        };
        string hdr;
        float skyYaw = 0f, sunEnergy, floorRough;
        Color floorColor;
        bool texturedFloor;
        if (name == "studio")
        {
            hdr = "studio_small_09/studio_small_09_2k.hdr";
            env.BackgroundMode = Godot.Environment.BGMode.Color;
            env.BackgroundColor = new Color(0.16f, 0.16f, 0.17f);
            env.AmbientLightEnergy = 1.0f;
            env.TonemapExposure = 1.1f;
            sunEnergy = 0.9f;
            floorColor = new Color(0.075f, 0.075f, 0.08f);
            floorRough = 0.55f;
            texturedFloor = false;
            skyYaw = 30f;
        }
        else
        {
            hdr = name == "arena" ? "alps_field/alps_field_4k.hdr" : "golden_gate_hills/golden_gate_hills_2k.hdr";
            env.BackgroundMode = Godot.Environment.BGMode.Sky;
            env.TonemapExposure = 0.9f;
            sunEnergy = 2.6f;
            floorColor = Colors.White;
            floorRough = 1f;
            texturedFloor = true;
            skyYaw = name == "arena" ? -60f : 110f;
        }
        var sky = new Sky
        {
            SkyMaterial = new PanoramaSkyMaterial { Panorama = GD.Load<Texture2D>($"res://assets/third_party/polyhaven/{hdr}") },
            RadianceSize = Sky.RadianceSizeEnum.Size1024,
        };
        env.Sky = sky;
        env.SkyRotation = new Vector3(0f, Mathf.DegToRad(skyYaw), 0f);
        _world.Environment = env;

        // Both outdoor HDRIs have the sun at longitude ~36 deg, elevation ~42 deg; the studio key comes from above.
        Vector3 toSun = name == "studio"
            ? new Vector3(0.25f, 0.92f, 0.3f).Normalized()
            : PanoramaDirection(36f, 42f).Rotated(Vector3.Up, Mathf.DegToRad(skyYaw));
        _sun.LookAtFromPosition(toSun * 10f, Vector3.Zero, Mathf.Abs(toSun.Y) > 0.99f ? Vector3.Forward : Vector3.Up);
        _sun.LightEnergy = sunEnergy;
        _sun.LightAngularDistance = name == "studio" ? 9f : 0.6f;
        _sun.LightColor = name == "studio" ? Colors.White : new Color(1f, 0.96f, 0.9f);
        _sun.LightSpecular = name == "studio" ? 0f : 1f;     // the studio's highlights come from its softboxes

        if (!texturedFloor)
        {
            var sm = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/studio_floor.gdshader") };
            sm.SetShaderParameter("color_near", floorColor);
            sm.SetShaderParameter("color_far", env.BackgroundColor);
            sm.SetShaderParameter("roughness_value", floorRough);
            _floor.MaterialOverride = sm;
        }
        else
        {
            var fm = new StandardMaterial3D { AlbedoColor = floorColor, Roughness = floorRough };
            const string a = "res://assets/third_party/polyhaven/asphalt_02/asphalt_02_";
            fm.AlbedoTexture = GD.Load<Texture2D>(a + "diff_2k.jpg");
            fm.NormalEnabled = true;
            fm.NormalTexture = GD.Load<Texture2D>(a + "nor_gl_2k.jpg");
            fm.OrmTexture = GD.Load<Texture2D>(a + "arm_2k.jpg");
            fm.Uv1Scale = new Vector3(20f, 20f, 1f);
            _floor.MaterialOverride = fm;
        }
    }

    /// <summary>Direction of an equirect pixel at (longitude, latitude) degrees in Godot's panorama mapping.</summary>
    private static Vector3 PanoramaDirection(float lonDeg, float latDeg)
    {
        float lon = Mathf.DegToRad(lonDeg), lat = Mathf.DegToRad(latDeg);
        return new Vector3(Mathf.Sin(lon) * Mathf.Cos(lat), Mathf.Sin(lat), -Mathf.Cos(lon) * Mathf.Cos(lat));
    }

    private void ApplyView(View v)
    {
        _az = v.Azimuth;
        _el = v.Elevation;
        _dist = v.Distance;
        _target = v.Target;
        _fov = v.Fov;
        PlaceCamera();
    }

    private void PlaceCamera()
    {
        float az = Mathf.DegToRad(_az), el = Mathf.DegToRad(_el);
        var dir = new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), -Mathf.Cos(az) * Mathf.Cos(el));
        _camera.Fov = _fov;
        _camera.LookAtFromPosition(_target + dir * _dist, _target, Vector3.Up);
    }

    private void SetLivery(int i)
    {
        var list = LiveriesFor(_carId);
        _livery = ((i % list.Count) + list.Count) % list.Count;
        _car.ApplyLivery(_livery == 0 || _livery >= Liveries.Length ? list[_livery] : list[_livery] with { Number = _number });
        UpdateHelp();
    }

    /// <summary>Scripted damage for look-dev: "derby" = a car after a hard derby, "light" = a few knocks.</summary>
    private void ApplyDamagePreset(string name)
    {
        var d = _car.Damage;
        d.Clear();
        if (name == "none") { _car.SetCrush(0f, 0f, 0f, 0f); return; }
        bool heavy = name == "derby";
        float k = heavy ? 1f : 0.45f;
        // Zone crush carries the bulk (front, rear, left, right); the dents add the individual hits.
        if (heavy) _car.SetCrush(0.8f, 0.55f, 0.2f, 0.5f);
        else _car.SetCrush(0.25f, 0f, 0f, 0.15f);
        d.AddDent(new Vector3(-0.62f, 0.50f, -2.05f), new Vector3(0.06f, 0.02f, 0.16f) * k, 0.55f);   // front-left corner
        d.AddDent(new Vector3(0.10f, 0.45f, -2.10f), new Vector3(0f, 0f, 0.06f) * k, 0.45f);          // nose
        d.AddDent(new Vector3(-0.10f, 0.70f, -1.25f), new Vector3(0f, 0.05f, 0.06f) * k, 0.30f);       // hood buckles up
        d.AddDent(new Vector3(0.86f, 0.50f, 0.05f), new Vector3(-0.20f, 0f, 0f) * k, 0.55f);          // right door T-bone
        d.AddDent(new Vector3(0.80f, 0.60f, 1.40f), new Vector3(-0.08f, 0f, 0f) * k, 0.35f);          // right rear quarter
        d.AddDent(new Vector3(-0.25f, 0.55f, 2.25f), new Vector3(0.02f, 0f, -0.12f) * k, 0.60f);      // rear
        if (heavy) d.AddDent(new Vector3(0.15f, 1.25f, 0.45f), new Vector3(0f, -0.09f, 0f), 0.45f);   // roof (rolled)
        for (float z = -1.3f; z <= 1.7f; z += 0.25f)                                                   // left side scrape
            d.AddScrape(new Vector3(-0.86f, 0.52f + 0.04f * Mathf.Sin(z * 3f), z), 0.22f, 0.5f * k);
        d.AddScrape(new Vector3(0.86f, 0.5f, 0.05f), 0.4f, 0.55f * k);
        if (heavy)
        {
            d.AddSoot(new Vector3(0f, 0.75f, -1.55f), 0.55f, 0.55f);
            d.AgeRust(0.35f);
        }
        _car.Flush();
    }

    private void RandomDent()
    {
        var rng = new RandomNumberGenerator();
        float side = rng.RandfRange(-1f, 1f);
        Vector3 p, push;
        switch (rng.RandiRange(0, 3))
        {
            case 0: p = new Vector3(side * 0.7f, 0.5f, -2.05f); push = new Vector3(-side * 0.05f, 0f, 0.2f); break;
            case 1: p = new Vector3(side * 0.6f, 0.55f, 2.2f); push = new Vector3(-side * 0.05f, 0f, -0.2f); break;
            default:
                float sx = Mathf.Sign(side);
                p = new Vector3(sx * 0.86f, rng.RandfRange(0.35f, 0.75f), rng.RandfRange(-1.6f, 1.8f));
                push = new Vector3(-sx * rng.RandfRange(0.06f, 0.18f), 0f, 0f);
                break;
        }
        _car.Damage.AddDent(p, push, rng.RandfRange(0.3f, 0.55f));
        _car.Damage.AddScrape(p, 0.35f, 0.6f);
    }

    public override void _Process(double delta)
    {
        _car.Flush();
        float dt = (float)delta;
        if (_spin) _turntable.RotateY(dt * 0.35f);
        bool moved = false;
        if (Input.IsKeyPressed(Key.Left)) { _az -= 60f * dt; moved = true; }
        if (Input.IsKeyPressed(Key.Right)) { _az += 60f * dt; moved = true; }
        if (Input.IsKeyPressed(Key.Up)) { _el = Mathf.Min(_el + 30f * dt, 85f); moved = true; }
        if (Input.IsKeyPressed(Key.Down)) { _el = Mathf.Max(_el - 30f * dt, -2f); moved = true; }
        if (Input.IsKeyPressed(Key.W)) { _dist = Mathf.Max(_dist - 3f * dt, 1.5f); moved = true; }
        if (Input.IsKeyPressed(Key.S)) { _dist += 3f * dt; moved = true; }
        if (moved) PlaceCamera();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        switch (k.Keycode)
        {
            case Key.Tab: LoadCar(Cars[(System.Array.IndexOf(Cars, _carId) + 1) % Cars.Length]); break;
            case Key.H: if (_help != null) _help.Visible = !_help.Visible; break;
            case Key.Key1: ApplyPreset(_presets[0]); break;
            case Key.Key2: ApplyPreset(_presets[1]); break;
            case Key.Key3: ApplyPreset(_presets[2]); break;
            case Key.N: _car.SetDebugMode(_mode = 0); UpdateHelp(); break;
            case Key.Z: _car.SetDebugMode(_mode = 1); UpdateHelp(); break;
            case Key.C: _car.SetDebugMode(_mode = 2); UpdateHelp(); break;
            case Key.L: SetLivery(_livery + 1); break;
            case Key.T: _spin = !_spin; break;
            case Key.P: SavePhoto(); break;
            case Key.D: RandomDent(); break;
            case Key.X: ApplyDamagePreset("none"); break;
            case Key.B: ApplyDamagePreset("derby"); break;
            case Key.Escape: GetTree().Quit(); break;
        }
    }

    private void SavePhoto()
    {
        string dir = ProjectSettings.GlobalizePath("res://out/studio");
        DirAccess.MakeDirRecursiveAbsolute(dir);
        string path = $"{dir}/photo_{_preset}_{++_shot:000}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"[PhotoStudio] {path}");
    }
}
