using System;
using System.Collections.Generic;
using Godot;

namespace DestructionRacing.Vehicles;

/// <summary>
/// Paint scheme of one car (plan §9.4; see assets/shaders/car_paint.gdshader): base colour, a second colour by
/// <see cref="Mode"/> (0 solid, 1 twin stripes, 2 lower two-tone, 3 side band, 4 cabin two-tone, 5 side flash),
/// the race number, a fictional sponsor (assets/textures/sponsors.png cell, -1 none), wear and dirt, a panel
/// in primer (a node name such as "Door_FL", or null) and the division roof colour.
/// </summary>
public sealed record Livery(
    Color Paint,
    Color Paint2,
    int Mode = 0,
    float Metallic = 0f,
    float Roughness = 0.38f,
    float Wear = 0f,
    float Dirt = 0f,
    int Number = -1,
    Color? Roof = null,
    int Sponsor = -1,
    string? Primer = null)
{
    public const int Modes = 6;
    public static readonly string[] ModeNames = { "SOLID", "TWIN STRIPES", "TWO-TONE", "SIDE BAND", "CABIN TWO-TONE", "SIDE FLASH" };
    public const int Sponsors = 8;

    /// <summary>DD division roof colour (plan §4.5); a vinyl roof is painted over when it is set.</summary>
    public bool HasRoofColour => Roof != null;

    public static readonly Livery KitsuneRed = new(new Color(0.62f, 0.05f, 0.035f), new Color(0.93f, 0.93f, 0.9f), 0, Number: 12);
    public static readonly Livery BroughamBlue = new(new Color(0.08f, 0.12f, 0.26f), new Color(0.8f, 0.8f, 0.82f), 0, Metallic: 0.7f, Roughness: 0.4f, Number: 1);
    public static readonly Livery NordvikBeige = new(new Color(0.62f, 0.55f, 0.40f), new Color(0.1f, 0.1f, 0.1f), 0, Number: 5);
}

/// <summary>
/// Where a car's livery decals go, in model space (X right, Y up, -Z forward): front-door roundel centre and
/// digit height, roof roundel centre z, the lowest y that counts as roof, and digit height (0 = none); the window
/// sill (cabin two-tone); the fender sponsor's centre z, y and height; the hood sponsor's centre z and height.
/// </summary>
public readonly record struct NumberPlacement(float DoorZ, float DoorY, float DoorHeight, float RoofZ, float RoofMinY, float RoofHeight,
    float Belt, float SideZ, float SideY, float SideHeight, float HoodZ, float HoodHeight, bool VinylRoof = false)
{
    public static readonly Dictionary<string, NumberPlacement> ByCar = new()
    {
        ["kitsune"] = new(0.12f, 0.61f, 0.28f, 0.58f, 1.15f, 0.36f, 0.82f, -1.21f, 0.725f, 0.07f, -1.31f, 0.13f),
        // No roof number on the vinyl.
        ["brougham"] = new(-0.165f, 0.60f, 0.40f, 0.70f, 1.30f, 0.50f, 0.93f, -1.475f, 0.835f, 0.065f, -1.50f, 0.18f, VinylRoof: true),
        ["homestead"] = new(-0.165f, 0.60f, 0.40f, 1.00f, 1.35f, 0.55f, 0.94f, -1.475f, 0.835f, 0.065f, -1.50f, 0.18f),
        ["interceptor"] = new(0.01f, 0.69f, 0.26f, 0.70f, 1.18f, 0.34f, 0.88f, -1.28f, 0.78f, 0.07f, -1.32f, 0.14f),
        ["nordvik"] = new(-0.07f, 0.74f, 0.28f, 0.74f, 1.33f, 0.50f, 0.945f, -1.32f, 0.81f, 0.085f, -1.30f, 0.16f),
        ["adler"] = new(-0.31f, 0.70f, 0.27f, 0.29f, 1.26f, 0.40f, 0.90f, -1.285f, 0.77f, 0.08f, -1.48f, 0.13f),
        ["volant"] = new(-0.14f, 0.675f, 0.26f, 0.595f, 1.25f, 0.42f, 0.88f, -1.21f, 0.75f, 0.08f, -1.385f, 0.13f),
        ["hikari"] = new(-0.06f, 0.69f, 0.22f, 0.56f, 1.20f, 0.36f, 0.86f, -1.20f, 0.74f, 0.075f, -1.31f, 0.14f),
        ["ryujin"] = new(-0.40f, 0.735f, 0.25f, 0.37f, 1.28f, 0.45f, 0.92f, -1.31f, 0.78f, 0.09f, -1.545f, 0.14f),
    };
}

/// <summary>
/// A generated car (tools/carsmith -> assets/cars/&lt;id&gt;/&lt;id&gt;.glb) with game materials applied by
/// material name. Rig-spec nodes (Body, Hood, Door_FL, Wheel_FR ...) are looked up by name.
/// </summary>
public sealed class CarModel
{
    public Node3D Root { get; }
    public ShaderMaterial Paint { get; }
    /// <summary>Simulated wood (the Homestead's sides): the paint shader with grain, so decals and damage match.</summary>
    public ShaderMaterial Wood { get; }
    public ShaderMaterial Glass { get; }
    public ShaderMaterial TailLens { get; }
    public CarDamageVisual Damage { get; }
    /// <summary>Zone crush shapes (front, rear, left, right; 0..1), see <see cref="SetCrush"/>.</summary>
    public Vector4 Crush { get; private set; }
    private readonly Dictionary<string, Node3D> _nodes = new();
    private Dictionary<string, Material> _materials = new();
    private readonly List<ShaderMaterial> _deforming = new();
    private readonly List<(Label3D Label, Vector3 RestPosition)> _attached = new();
    private Vector3 _crushLo, _crushHi;
    private float _crushSeed;
    private int _attachedVersion = -1;
    private bool _attachedDirty;
    private MeshInstance3D? _primed;
    private static int _cars;

    private static readonly Dictionary<string, Texture2D> TextureCache = new();
    private static Shader? _paintShader, _glassShader, _surfaceShader, _contactShader;

    private CarModel(Node3D root, Livery livery, string carId)
    {
        Root = root;
        _paintShader ??= GD.Load<Shader>("res://assets/shaders/car_paint.gdshader");
        _glassShader ??= GD.Load<Shader>("res://assets/shaders/car_glass.gdshader");
        _surfaceShader ??= GD.Load<Shader>("res://assets/shaders/car_surface.gdshader");
        Paint = Deforming(new ShaderMaterial { Shader = _paintShader });
        Paint.SetShaderParameter("rust_albedo", Tex("rust_coarse_01/rust_coarse_01_diff_2k.jpg"));
        Paint.SetShaderParameter("dirt_albedo", Tex("brown_mud_dry/brown_mud_dry_diff_2k.jpg"));
        Paint.SetShaderParameter("number_atlas", GD.Load<Texture2D>("res://assets/textures/race_digits_sdf.png"));
        Paint.SetShaderParameter("sponsor_atlas", GD.Load<Texture2D>("res://assets/textures/sponsors.png"));
        if (NumberPlacement.ByCar.TryGetValue(carId, out var np))
        {
            Paint.SetShaderParameter("door_number", new Vector3(np.DoorZ, np.DoorY, np.DoorHeight));
            bool onVinyl = np.VinylRoof && !livery.HasRoofColour;
            Paint.SetShaderParameter("roof_number", new Vector3(np.RoofZ, np.RoofMinY, onVinyl ? 0f : np.RoofHeight));
            Paint.SetShaderParameter("belt_line", np.Belt);
            Paint.SetShaderParameter("side_sponsor", new Vector3(np.SideZ, np.SideY, np.SideHeight));
            Paint.SetShaderParameter("hood_sponsor", new Vector2(np.HoodZ, np.HoodHeight));
        }
        ApplyLivery(livery);
        Wood = Deforming((ShaderMaterial)Paint.Duplicate());
        Wood.SetShaderParameter("wood_grain", 1f);
        Glass = Deforming(new ShaderMaterial { Shader = _glassShader });
        TailLens = Lens(new Color(0.55f, 0.015f, 0.01f), 2);

        // Wheels live in their own node space and do not dent: plain materials.
        var shared = new Dictionary<string, Material>
        {
            ["paint"] = Paint,
            ["wood"] = Wood,
            ["glass"] = Glass,
            ["lens_red"] = TailLens,
            ["lens_amber"] = Lens(new Color(0.9f, 0.38f, 0.02f), 2),
            ["lens_clear"] = HeadlampGlass(),
            ["plate"] = Surface(new Color(0.86f, 0.86f, 0.82f), 0.45f, scuff: 0.5f),
            ["trim_black"] = Surface(new Color(0.022f, 0.022f, 0.024f), 0.5f, scuff: 0.6f),
            ["bumper"] = Surface(new Color(0.035f, 0.035f, 0.037f), 0.62f, scuff: 1f),
            ["rubber"] = Surface(new Color(0.012f, 0.012f, 0.012f), 0.75f),
            ["wheel_well"] = Surface(new Color(0.018f, 0.018f, 0.018f), 0.95f),
            ["underbody"] = Surface(new Color(0.025f, 0.024f, 0.022f), 0.95f),
            ["interior"] = Surface(new Color(0.07f, 0.065f, 0.06f), 0.9f),
            // A derby car with a division roof colour gets its vinyl painted over.
            ["vinyl"] = livery.HasRoofColour ? Paint : Surface(new Color(0.045f, 0.03f, 0.026f), 0.55f, clearcoat: 0.15f),
            ["chrome"] = Surface(new Color(0.9f, 0.9f, 0.92f), 0.05f, 1f),
            ["exhaust"] = Surface(new Color(0.16f, 0.15f, 0.14f), 0.5f, 0.85f, scuff: 0.3f),
            ["tyre"] = Std(new Color(0.028f, 0.028f, 0.03f), 0.88f),
            ["rim"] = Std(new Color(0.72f, 0.73f, 0.74f), 0.32f, 1f),
            ["hubcap"] = Std(new Color(0.62f, 0.63f, 0.65f), 0.22f, 1f),
        };
        _materials = shared;
        foreach (var mi in Descendants<MeshInstance3D>(root))
        {
            if (mi.Mesh == null) continue;
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                string name = mi.Mesh.SurfaceGetMaterial(s)?.ResourceName ?? "";
                if (shared.TryGetValue(name, out var mat)) mi.SetSurfaceOverrideMaterial(s, mat);
                else GD.PushWarning($"[CarModel] {mi.Name}: no game material for '{name}'");
            }
        }
        foreach (var n in Descendants<Node3D>(root)) _nodes.TryAdd(n.Name, n);

        var bounds = (Node("Body") as MeshInstance3D)?.GetAabb() ?? new Aabb(new Vector3(-1f, 0f, -2.5f), new Vector3(2f, 1.5f, 5f));
        Damage = new CarDamageVisual(bounds.Grow(0.12f));
        _crushLo = bounds.Position;
        _crushHi = bounds.End;
        _crushSeed = (_cars++ * 0.618034f) % 1f * 10f;
        foreach (var m in _deforming)
        {
            m.SetShaderParameter("dent_volume", Damage.DentTexture);
            m.SetShaderParameter("dent_min", Damage.Min);
            m.SetShaderParameter("dent_size", Damage.Size);
            m.SetShaderParameter("dent_res", Damage.Resolution);
            m.SetShaderParameter("crush_lo", _crushLo);
            m.SetShaderParameter("crush_hi", _crushHi);
            m.SetShaderParameter("crush_seed", _crushSeed);
        }
        foreach (var m in new[] { Paint, Wood })
        {
            m.SetShaderParameter("damage_volume", Damage.WearTexture);
            m.SetShaderParameter("volume_min", Damage.Min);
            m.SetShaderParameter("volume_size", Damage.Size);
        }
        ApplyPrimer(livery.Primer);
        AddContactShadow();
    }

    /// <summary>
    /// Zone crush shapes, 0..1 each (the DD damage zones): the end of the car shoved in and buckled, or a flank
    /// caved in. Layered under the individual dents; see assets/shaders/car_deform.gdshaderinc.
    /// </summary>
    public void SetCrush(float front, float rear, float left, float right)
    {
        Crush = new Vector4(front, rear, left, right);
        foreach (var m in _deforming) m.SetShaderParameter("crush", Crush);
        _attachedDirty = true;
    }

    /// <summary>
    /// Copies of the deforming materials frozen with the damage as it is now (a dent-volume snapshot and the
    /// current crush), for parts that come off: they keep their dents however the car is bashed afterwards.
    /// </summary>
    public Dictionary<Material, Material> FreezeMaterials()
    {
        var snapshot = Damage.SnapshotDentTexture();
        var map = new Dictionary<Material, Material>();
        foreach (var m in _deforming)
        {
            var copy = (ShaderMaterial)m.Duplicate();
            copy.SetShaderParameter("dent_volume", snapshot);
            copy.SetShaderParameter("crush", Crush);
            map[m] = copy;
        }
        return map;
    }

    /// <summary>Stops tracking attachments (plate text) under a node that left the car.</summary>
    public void Forget(Node subtree) => _attached.RemoveAll(a => !GodotObject.IsInstanceValid(a.Label) || subtree.IsAncestorOf(a.Label));

    /// <summary>
    /// Uploads damage changes and keeps attachments the shader cannot move (the plate text) on the metal.
    /// Call once per frame; <paramref name="budgeted"/> shares the per-frame upload budget between cars.
    /// </summary>
    public void Flush(bool budgeted = false)
    {
        Damage.Flush(budgeted);
        if (!_attachedDirty && _attachedVersion == Damage.DentVersion) return;
        _attachedDirty = false;
        _attachedVersion = Damage.DentVersion;
        foreach (var (label, rest) in _attached)
        {
            if (!GodotObject.IsInstanceValid(label) || label.GetParent() is not Node3D parent) continue;
            var t = parent.Transform;
            Vector3 p = t * rest;
            // A badly crushed end folds the plate up: drop its text rather than let it float or sink.
            float crush = p.Z < 0.5f * (_crushLo.Z + _crushHi.Z) ? Crush.X : Crush.Y;
            label.Visible = crush < 0.4f;
            var outward = new Vector3(0f, 0f, p.Z < 0f ? -1f : 1f) * (0.03f * crush);
            label.Position = rest + t.Basis.Inverse() * (Damage.SampleDent(p) + CrushAt(p) + outward);
        }
    }

    /// <summary>The bulk of the shader's crush field (without its noise and buckles) at a car-local point.</summary>
    private Vector3 CrushAt(Vector3 p)
    {
        var r = Vector3.Zero;
        if (Crush == Vector4.Zero) return r;
        Vector3 ext = _crushHi - _crushLo;
        float hx = MathF.Max(_crushHi.X, -_crushLo.X);
        float lat = Math.Clamp(p.X / hx, -1f, 1f);
        float hgt = (p.Y - _crushLo.Y) / ext.Y;
        float upper = Mathf.SmoothStep(0.42f, 0.62f, hgt);
        for (int e = 0; e < 2; e++)
        {
            float k = e == 0 ? Crush.X : Crush.Y;
            if (k <= 0f) continue;
            float sgn = e == 0 ? 1f : -1f;
            float t = Math.Clamp((p.Z - (e == 0 ? _crushLo.Z : _crushHi.Z)) * sgn / 1.35f, 0f, 1f);
            float w = (1f - t) * (1f - t);
            float bias = MathF.Sin(_crushSeed * 3.1f + e * 1.7f);
            r.Z += sgn * k * 0.42f * w * (1f + 0.35f * bias * lat);
            r.Y -= k * 0.05f * w * (1f - upper);
        }
        for (int s = 0; s < 2; s++)
        {
            float k = s == 0 ? Crush.Z : Crush.W;
            if (k <= 0f) continue;
            float side = s == 0 ? -1f : 1f;
            float u = Math.Clamp((hx - p.X * side) / 0.75f, 0f, 1f);
            float along = (p.Z - 0.5f * (_crushLo.Z + _crushHi.Z)) / (ext.Z * 0.5f);
            float cage = 1f - 0.7f * Mathf.SmoothStep(0.62f, 0.85f, hgt);
            r.X -= side * k * 0.30f * (1f - u) * (1f - u) * MathF.Exp(-along * along * 1.6f) * cage;
        }
        return r;
    }

    /// <summary>Registration text on the Plate_F / Plate_R meshes.</summary>
    public void SetPlate(string text)
    {
        _attached.Clear();
        foreach (var (name, front) in new[] { ("Plate_F", true), ("Plate_R", false) })
        {
            if (Node(name) is not MeshInstance3D plate) continue;
            foreach (var old in plate.GetChildren()) if (old is Label3D) old.QueueFree();
            var box = plate.GetAabb();
            float out_ = front ? -1f : 1f;
            var label = new Label3D
            {
                Text = text,
                FontSize = 96,
                PixelSize = box.Size.Y * 0.62f / 96f,
                Modulate = new Color(0.04f, 0.04f, 0.05f),
                OutlineSize = 0,
                Position = box.GetCenter() + new Vector3(0f, 0f, out_ * (box.Size.Z * 0.5f + 0.0015f)),
                RotationDegrees = new Vector3(0f, front ? 180f : 0f, 0f),
                Shaded = true,
                DoubleSided = false,
            };
            plate.AddChild(label);
            _attached.Add((label, label.Position));
        }
        _attachedDirty = true;
    }

    /// <summary>A soft occlusion blob on the ground under the body and tyres (grounds the car in any light).</summary>
    private void AddContactShadow()
    {
        if (Node("Body") is not MeshInstance3D body) { GD.PushWarning("[CarModel] no Body mesh"); return; }
        var box = body.GetAabb();
        var centre = new Vector3(box.GetCenter().X, 0.004f, box.GetCenter().Z);
        var wheels = new Vector4[4];
        int i = 0;
        foreach (string w in new[] { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" })
        {
            if (Node(w) is { } n && i < 4)
                wheels[i++] = new Vector4(n.Position.X - centre.X, n.Position.Z - centre.Z, 0.42f, 0f);
        }
        _contactShader ??= GD.Load<Shader>("res://assets/shaders/contact_shadow.gdshader");
        var mat = new ShaderMaterial { Shader = _contactShader };
        mat.SetShaderParameter("half_size", new Vector2(box.Size.X * 0.5f - 0.06f, box.Size.Z * 0.5f - 0.12f));
        mat.SetShaderParameter("wheels", wheels);
        Root.AddChild(new MeshInstance3D
        {
            Name = "ContactShadow",
            Mesh = new PlaneMesh { Size = new Vector2(box.Size.X + 1.4f, box.Size.Z + 1.4f) },
            MaterialOverride = mat,
            Position = centre,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    public static CarModel Load(string carId, Livery livery)
    {
        var scene = GD.Load<PackedScene>($"res://assets/cars/{carId}/{carId}.glb");
        return new CarModel(scene.Instantiate<Node3D>(), livery, carId);
    }

    public Node3D? Node(string name) => _nodes.TryGetValue(name, out var n) ? n : null;

    /// <summary>The car's game material for a glb material name ("underbody", "chrome" ...): extra meshes built in
    /// model space with it dent and crush with the car.</summary>
    public Material? Material(string name) => _materials.TryGetValue(name, out var m) ? m : null;

    public void ApplyLivery(Livery l)
    {
        // Wood is still null when the constructor applies the first livery (it is copied from the paint after).
        foreach (var m in Wood == null ? new[] { Paint } : new[] { Paint, Wood })
        {
            m.SetShaderParameter("paint_color", l.Paint);
            m.SetShaderParameter("paint_color_2", l.Paint2);
            m.SetShaderParameter("livery_mode", l.Mode);
            m.SetShaderParameter("metallic_paint", l.Metallic);
            m.SetShaderParameter("paint_roughness", l.Roughness);
            m.SetShaderParameter("wear", l.Wear);
            m.SetShaderParameter("dirt", l.Dirt);
            m.SetShaderParameter("race_number", l.Number);
            m.SetShaderParameter("sponsor", l.Sponsor);
            m.SetShaderParameter("roof_paint", l.HasRoofColour ? 1f : 0f);
            if (l.Roof is { } roof) m.SetShaderParameter("roof_color", roof);
        }
        if (_nodes.Count > 0) ApplyPrimer(l.Primer);
    }

    /// <summary>One panel in grey primer (a junkyard replacement door or fender), or none.</summary>
    private void ApplyPrimer(string? node)
    {
        _primed?.SetInstanceShaderParameter("primer", 0f);
        _primed = node != null ? Node(node) as MeshInstance3D : null;
        _primed?.SetInstanceShaderParameter("primer", 1f);
    }

    /// <summary>0 = normal, 1 = zebra reflections, 2 = clay.</summary>
    public void SetDebugMode(int mode) => Paint.SetShaderParameter("debug_mode", mode);

    private ShaderMaterial Deforming(ShaderMaterial m)
    {
        _deforming.Add(m);
        return m;
    }

    private ShaderMaterial Surface(Color albedo, float roughness, float metallic = 0f, float clearcoat = 0f, float scuff = 0f)
    {
        var m = Deforming(new ShaderMaterial { Shader = _surfaceShader });
        m.SetShaderParameter("albedo", albedo);
        m.SetShaderParameter("roughness", roughness);
        m.SetShaderParameter("metallic", metallic);
        m.SetShaderParameter("clearcoat", clearcoat);
        m.SetShaderParameter("scuff", scuff);
        return m;
    }

    private ShaderMaterial Lens(Color c, int pattern)
    {
        var m = Surface(c, 0.04f, 0.15f, 1f);
        m.SetShaderParameter("clearcoat_roughness", 0.02f);
        m.SetShaderParameter("emission", c);
        m.SetShaderParameter("lens_pattern", pattern);
        return m;
    }

    /// <summary>Clear fluted glass over a chrome reflector: bright and mirror-like, with fine flutes.</summary>
    private ShaderMaterial HeadlampGlass()
    {
        var m = Surface(new Color(0.78f, 0.8f, 0.82f), 0.12f, 0.75f, 1f);
        m.SetShaderParameter("clearcoat_roughness", 0.01f);
        m.SetShaderParameter("emission", new Color(1f, 0.95f, 0.85f));
        m.SetShaderParameter("lens_pattern", 1);
        return m;
    }

    private static StandardMaterial3D Std(Color albedo, float roughness, float metallic = 0f) => new()
    {
        AlbedoColor = albedo,
        Roughness = roughness,
        Metallic = metallic,
    };

    private static Texture2D Tex(string rel)
    {
        string path = $"res://assets/third_party/polyhaven/{rel}";
        if (!TextureCache.TryGetValue(path, out var t)) TextureCache[path] = t = GD.Load<Texture2D>(path);
        return t;
    }

    public static IEnumerable<T> Descendants<T>(Node root) where T : Node
    {
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n is T t) yield return t;
            foreach (var c in n.GetChildren()) stack.Push(c);
        }
    }
}
