using System.Collections.Generic;
using System.Linq;
using DestructionRacing.Core;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Tools;

/// <summary>
/// Look-dev of the Bowl (afternoon preset): the baked arena from BowlLookBuilder.gd with the generated cars
/// lined up in the start ring, some of them already battered.
/// Batch: --shots=out/bowl [--views=hero,stands,...]. Interactive: W/S move, arrows turn, V next view, P photo.
/// </summary>
public partial class BowlLook : Node3D
{
    private sealed record View(string Name, Vector3 Eye, Vector3 Target, float Fov = 50f);

    // The staged pileup sits on the hero camera's line of sight (stands, scoreboard and mountains behind it).
    private static readonly Vector3 PileCentre = new(-14f, 0f, 12f);
    private static readonly Vector3 PileFwd = new Vector3(0.644f, 0f, -0.765f).Normalized();
    private static readonly Vector3 PileRight = PileFwd.Cross(Vector3.Up);

    /// <summary>A point in the pileup's frame: metres to the camera's right and away from it.</summary>
    private static Vector3 Pile(float right, float fwd) => PileCentre + PileRight * right + PileFwd * fwd;

    private static readonly View[] Views =
    {
        new("pileup", Pile(-0.4f, -9.5f) + Vector3.Up * 1.9f, Pile(0.2f, 1.5f) + Vector3.Up * 0.55f, 44f),
        new("closeup", Pile(1.4f, -4.4f) + Vector3.Up * 1.2f, Pile(-0.9f, 0.3f) + Vector3.Up * 0.55f, 42f),
        new("hero", new Vector3(-24f, 1.5f, 24f), new Vector3(8f, 2.5f, -14f), 45f),
        new("pack", new Vector3(14f, 1.2f, 8f), new Vector3(30f, 0.6f, -6f), 40f),
        new("stands", new Vector3(6f, 12.5f, 38.5f), new Vector3(-6f, 0f, 0f), 55f),
        new("low", new Vector3(-36f, 0.6f, -4f), new Vector3(-20f, 0.8f, -14f), 38f),
        new("overview", new Vector3(0f, 46f, 78f), new Vector3(0f, 0f, 2f), 50f),
    };

    private static readonly Color[] Paints =
    {
        new(0.75f, 0.06f, 0.04f), new(0.05f, 0.22f, 0.62f), new(0.95f, 0.70f, 0.05f), new(0.08f, 0.42f, 0.18f),
        new(0.92f, 0.38f, 0.04f), new(0.85f, 0.85f, 0.83f), new(0.12f, 0.12f, 0.13f), new(0.48f, 0.10f, 0.48f),
    };

    /// <summary>Just the arena (no parked cars or pileup): a backdrop for other test scenes.</summary>
    public bool Empty { get; set; }

    private Camera3D _camera = null!;
    private readonly List<CarModel> _cars = new();
    private int _view;

    public override void _Ready()
    {
        _camera = new Camera3D { Current = !Empty, Near = 0.05f, Far = 2000f };
        AddChild(_camera);
        GetViewport().Msaa3D = Viewport.Msaa.Msaa4X;
        GetViewport().ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Smaa;
        BuildTyreBarrier();
        BuildFencePosts();
        if (!Empty) SpawnCars();
        ApplyView(Views[0]);
        var dev = DevTools.Instance;
        if (dev?.Arg("nolightmap") != null && GetNodeOrNull<LightmapGI>("LightmapGI") is { } lm) lm.LightData = null;
        if (!Empty && dev?.Arg("shots") is { } dir) RunShots(dir, dev);
        else if (GetTree().CurrentScene == this) KeyGuide.Add(this, "The Bowl, afternoon (baked lighting)\nV next view   W/S move   arrows turn   P photo (out/bowl)   Esc quit");
    }

    private const float A = 55f, B = 38f;

    private static Vector3 Ellipse(float t, float off)
    {
        var p = new Vector3(A * Mathf.Cos(t), 0f, B * Mathf.Sin(t));
        var n = new Vector3(Mathf.Cos(t) / A, 0f, Mathf.Sin(t) / B).Normalized();
        return p + n * off;
    }

    /// <summary>Old tyres stacked three high along the inside of the wall.</summary>
    private void BuildTyreBarrier()
    {
        var tyre = new TorusMesh { InnerRadius = 0.2f, OuterRadius = 0.38f, Rings = 20, RingSegments = 10,
            Material = new StandardMaterial3D { AlbedoColor = new Color(0.025f, 0.025f, 0.027f), Roughness = 0.9f } };
        var xs = new List<Transform3D>();
        var rng = new RandomNumberGenerator { Seed = 7 };
        var prev = Ellipse(0f, -0.45f);
        float run = 0f, next = 0f;
        const int steps = 4000;
        for (int i = 0; i <= steps; i++)
        {
            float t = Mathf.Tau * i / steps;
            var p = Ellipse(t, -0.45f);
            run += p.DistanceTo(prev);
            prev = p;
            if (run < next) continue;
            next += 0.78f;
            var tangent = (Ellipse(t + 0.001f, -0.45f) - p).Normalized();
            for (int k = 0; k < 3; k++)
            {
                var basis = new Basis(tangent, Vector3.Up, tangent.Cross(Vector3.Up)).Orthonormalized()
                            * new Basis(Vector3.Up, rng.Randf() * Mathf.Tau);
                var jitter = new Vector3(rng.RandfRange(-0.04f, 0.04f), 0f, rng.RandfRange(-0.04f, 0.04f));
                xs.Add(new Transform3D(basis, p + jitter + Vector3.Up * (0.1f + 0.19f * k)));
            }
        }
        AddMulti("TyreBarrier", tyre, xs);
    }

    private void BuildFencePosts()
    {
        var post = new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.035f, Height = 2.7f, RadialSegments = 8,
            Material = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.56f, 0.57f), Metallic = 0.8f, Roughness = 0.45f } };
        var xs = new List<Transform3D>();
        for (int i = 0; i < 100; i++)
            xs.Add(new Transform3D(Basis.Identity, Ellipse(Mathf.Tau * i / 100f, 0.48f) + Vector3.Up * (1.42f + 1.35f)));
        AddMulti("FencePosts", post, xs);
    }

    private void AddMulti(string name, Mesh mesh, List<Transform3D> xs)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = xs.Count };
        for (int i = 0; i < xs.Count; i++) mm.SetInstanceTransform(i, xs[i]);
        AddChild(new MultiMeshInstance3D { Name = name, Multimesh = mm });
    }

    private void SpawnCars()
    {
        string[] models = { "kitsune", "brougham", "nordvik" };
        var rng = new RandomNumberGenerator { Seed = 1977 };
        const int count = 12;
        for (int i = 0; i < count; i++)
        {
            float t = Mathf.Tau * i / count + 0.2f;
            var pos = new Vector3(55f * 0.74f * Mathf.Cos(t), 0f, 38f * 0.74f * Mathf.Sin(t));
            string id = models[i % models.Length];
            var paint = Paints[i % Paints.Length];
            var livery = new Livery(paint, new Color(0.95f, 0.95f, 0.93f), (i / 3) % 4,
                Metallic: i % 4 == 2 ? 0.7f : 0f, Wear: 0.25f + 0.1f * (i % 3), Dirt: 0.3f + 0.15f * (i % 4), Number: i + 1);
            var car = CarModel.Load(id, livery);
            car.SetPlate($"DD {i + 1:00}");
            AddChild(car.Root);
            car.Root.Position = pos;
            car.Root.LookAt(new Vector3(0f, 0f, 0f), Vector3.Up);
            if (i % 3 == 1) Batter(car, rng, 0.5f);
            if (i % 4 == 3) Batter(car, rng, 1f);
            _cars.Add(car);
        }
        StagePileup();
    }

    /// <summary>
    /// A mid-derby pileup framed like a DD replay: a broadside Brougham with its side stoved in, a Kitsune nosed
    /// into its rear quarter, a Nordvik T-boning its far side, a Kitsune limping away on a crushed tail and a
    /// second Brougham coming in for more. Positions are in the pileup frame (see <see cref="Pile"/>).
    /// </summary>
    private void StagePileup()
    {
        var victim = Stage("brougham", Livery.BroughamBlue with { Wear = 0.35f, Dirt = 0.5f }, "ALD 400", 0.3f, 0.8f, 1f, 0f);
        victim.SetCrush(0.15f, 0f, 0.55f, 0.75f);
        CrushSide(victim, +1f, -0.35f, 0.6f);
        CrushSide(victim, +1f, 1.75f, 0.5f);
        CrushSide(victim, -1f, 0.1f, 0.5f);
        CrushFront(victim, 0.35f, -0.5f);
        victim.Damage.AgeRust(0.15f);

        // Undented contact points on the victim, then each attacker's undeformed nose a little inside them.
        Vector3 rearQuarter = Pile(0.3f - 1.75f, 0.8f - 0.995f), farSide = Pile(0.3f - 0.1f, 0.8f + 0.995f);
        var kitsune = StageNose("kitsune", Livery.KitsuneRed with { Wear = 0.3f, Dirt = 0.45f }, "KTS 12", rearQuarter, 0.5f, 0.85f, 0.85f);
        kitsune.SetCrush(0.9f, 0f, 0f, 0.15f);
        CrushFront(kitsune, 0.6f, 0.6f);
        var nordvik = StageNose("nordvik", Livery.NordvikBeige with { Wear = 0.45f, Dirt = 0.55f }, "FJD 805", farSide, -0.3f, -0.95f, 0.8f);
        nordvik.SetCrush(0.85f, 0.1f, 0f, 0f);
        CrushFront(nordvik, 0.5f, -0.4f);
        nordvik.Damage.AddSoot(new Vector3(0f, 0.8f, -1.8f), 0.5f, 0.4f);

        var limper = Stage("kitsune", new Livery(new Color(0.95f, 0.70f, 0.05f), new Color(0.03f, 0.03f, 0.03f), 1, Wear: 0.4f, Dirt: 0.6f, Number: 7),
            "DD 07", 4.1f, 2.4f, 0.3f, 1f);
        limper.SetCrush(0f, 0.9f, 0.3f, 0f);
        CrushRear(limper, 0.6f, 0.5f);
        CrushSide(limper, -1f, 0.6f, 0.5f);
        var incoming = Stage("brougham", new Livery(new Color(0.86f, 0.86f, 0.84f), new Color(0.6f, 0.05f, 0.04f), 3, Wear: 0.3f, Dirt: 0.4f, Number: 3),
            "DD 03", -5.6f, 8.2f, 0.45f, -0.9f);
        incoming.SetCrush(0.35f, 0f, 0f, 0f);
        CrushFront(incoming, 0.3f, 0.3f);
        foreach (var c in new[] { victim, kitsune, nordvik, limper, incoming }) c.Flush();
    }

    private CarModel Stage(string id, Livery livery, string plate, float right, float fwd, float headRight, float headFwd)
    {
        var car = CarModel.Load(id, livery);
        car.SetPlate(plate);
        AddChild(car.Root);
        var heading = (PileRight * headRight + PileFwd * headFwd).Normalized();
        car.Root.GlobalTransform = new Transform3D(Basis.LookingAt(heading, Vector3.Up), Pile(right, fwd));
        _cars.Add(car);
        return car;
    }

    /// <summary>Places a car so its undeformed nose sits <paramref name="inside"/> metres past a contact point.</summary>
    private CarModel StageNose(string id, Livery livery, string plate, Vector3 contact, float headRight, float headFwd, float inside)
    {
        var car = Stage(id, livery, plate, 0f, 0f, headRight, headFwd);
        var heading = (PileRight * headRight + PileFwd * headFwd).Normalized();
        float nose = -Extents(car).Lo.Z;
        car.Root.GlobalPosition = contact + heading * (inside - nose);
        return car;
    }

    private static (Vector3 Lo, Vector3 Hi) Extents(CarModel car) =>
        (car.Damage.Min + Vector3.One * 0.12f, car.Damage.Min + car.Damage.Size - Vector3.One * 0.12f);

    /// <summary>Front-end crush (model space, -Z forward); <paramref name="bias"/> -1..1 picks the harder-hit corner.</summary>
    private static void CrushFront(CarModel car, float k, float bias)
    {
        var (lo, hi) = Extents(car);
        var d = car.Damage;
        d.AddDent(new Vector3(bias * hi.X * 0.5f, 0.5f, lo.Z), new Vector3(-bias * 0.05f, 0.02f, 0.38f) * k, 0.7f);
        d.AddDent(new Vector3(-bias * hi.X * 0.6f, 0.45f, lo.Z + 0.05f), new Vector3(0f, 0f, 0.18f) * k, 0.45f);
        d.AddDent(new Vector3(0f, 0.62f * hi.Y, lo.Z + 0.9f), new Vector3(0f, 0.07f, 0.1f) * k, 0.4f);   // hood buckles up
        d.AddScrape(new Vector3(bias * 0.3f, 0.5f, lo.Z), 0.55f, 0.4f * k);
    }

    private static void CrushRear(CarModel car, float k, float bias)
    {
        var (_, hi) = Extents(car);
        var d = car.Damage;
        d.AddDent(new Vector3(bias * hi.X * 0.4f, 0.55f, hi.Z), new Vector3(0f, 0.02f, -0.35f) * k, 0.7f);
        d.AddDent(new Vector3(0f, 0.62f * hi.Y, hi.Z - 0.6f), new Vector3(0f, 0.06f, -0.08f) * k, 0.35f); // boot lid buckles
        d.AddScrape(new Vector3(bias * 0.3f, 0.55f, hi.Z), 0.55f, 0.4f * k);
    }

    /// <summary>Side impact at model z on side +1 (right) or -1 (left).</summary>
    private static void CrushSide(CarModel car, float side, float z, float k)
    {
        var (_, hi) = Extents(car);
        var d = car.Damage;
        var p = new Vector3(side * hi.X, 0.55f, z);
        d.AddDent(p, new Vector3(-side * 0.3f, 0f, 0f) * k, 0.7f);
        d.AddDent(p + Vector3.Up * 0.3f, new Vector3(-side * 0.1f, -0.02f, 0f) * k, 0.4f);
        d.AddScrape(p, 0.45f, 0.25f * k);
    }

    private static void Batter(CarModel car, RandomNumberGenerator rng, float k)
    {
        var d = car.Damage;
        for (int n = 0; n < 4; n++)
        {
            float side = rng.Randf() < 0.5f ? -1f : 1f;
            switch (rng.RandiRange(0, 2))
            {
                case 0:
                    d.AddDent(new Vector3(side * rng.RandfRange(0.2f, 0.7f), 0.5f, -2.0f), new Vector3(-side * 0.04f, 0f, 0.22f) * k, 0.5f);
                    break;
                case 1:
                    d.AddDent(new Vector3(side * rng.RandfRange(0.1f, 0.6f), 0.55f, 2.2f), new Vector3(0f, 0f, -0.2f) * k, 0.55f);
                    break;
                default:
                    float z = rng.RandfRange(-1.5f, 1.5f);
                    d.AddDent(new Vector3(side * 0.88f, 0.5f, z), new Vector3(-side * 0.15f, 0f, 0f) * k, 0.5f);
                    d.AddScrape(new Vector3(side * 0.88f, 0.5f, z), 0.4f, 0.45f * k);
                    break;
            }
        }
        car.SetCrush(rng.Randf() * 0.6f * k, rng.Randf() * 0.5f * k, rng.Randf() * 0.4f * k, rng.Randf() * 0.4f * k);
        d.AgeRust(0.2f * k);
        d.Flush();
    }

    private async void RunShots(string dir, DevTools dev)
    {
        string abs = ProjectSettings.GlobalizePath(dir.StartsWith("res://") ? dir : "res://" + dir);
        DirAccess.MakeDirRecursiveAbsolute(abs);
        var names = (dev.Arg("views") ?? string.Join(",", Views.Select(v => v.Name))).Split(',');
        GetViewport().Scaling3DScale = 1.5f;
        foreach (string n in names)
        {
            ApplyView(Views.First(v => v.Name == n));
            for (int i = 0; i < 20; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            string path = $"{abs}/bowl_{n}.png";
            GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print($"[BowlLook] {path}");
        }
        DevTools.QuitClean(GetTree(), 0);
    }

    private void ApplyView(View v)
    {
        _camera.Fov = v.Fov;
        _camera.LookAtFromPosition(v.Eye, v.Target, Vector3.Up);
    }

    public override void _Process(double delta)
    {
        foreach (var c in _cars) c.Flush();
        float dt = (float)delta;
        var t = _camera.Transform;
        if (Input.IsKeyPressed(Key.W)) t.Origin -= t.Basis.Z * 12f * dt;
        if (Input.IsKeyPressed(Key.S)) t.Origin += t.Basis.Z * 12f * dt;
        if (Input.IsKeyPressed(Key.Left)) t.Basis = t.Basis.Rotated(Vector3.Up, 1.2f * dt);
        if (Input.IsKeyPressed(Key.Right)) t.Basis = t.Basis.Rotated(Vector3.Up, -1.2f * dt);
        _camera.Transform = t;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        if (k.Keycode == Key.V) ApplyView(Views[_view = (_view + 1) % Views.Length]);
        if (k.Keycode == Key.P)
        {
            string dir = ProjectSettings.GlobalizePath("res://out/bowl");
            DirAccess.MakeDirRecursiveAbsolute(dir);
            GetViewport().GetTexture().GetImage().SavePng($"{dir}/photo_{Time.GetTicksMsec()}.png");
        }
        if (k.Keycode == Key.Escape) GetTree().Quit();
    }
}
