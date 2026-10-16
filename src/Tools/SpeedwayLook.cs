using System.Collections.Generic;
using DestructionRacing.Core;
using DestructionRacing.Tracks;
using Godot;

namespace DestructionRacing.Tools;

/// <summary>
/// The baked look of Crossroads Speedway (SpeedwayLookBuilder.gd: SpeedwayLook.tscn = afternoon, _Golden, _Night)
/// plus what is added at runtime: the catch-fence posts, the crowd in the two grandstands, a tree line round the
/// venue, the chosen layout's barriers (red and white blocks closing off the other layout's roads) and the start
/// lights. As the main scene it is a look-dev viewer: V next view, P photo (out/speedway), --shots=DIR saves every
/// view and quits.
/// </summary>
public partial class SpeedwayLook : Node3D
{
    private sealed record View(string Name, Vector3 Eye, Vector3 Target, float Fov = 50f);

    private static readonly View[] Views =
    {
        new("start", new Vector3(-38f, 2.2f, 72f), new Vector3(10f, 1.5f, 66f), 50f),
        new("stand", new Vector3(30f, 9f, 86f), new Vector3(-10f, 0f, 60f), 55f),
        new("turn", new Vector3(150f, 4f, 62f), new Vector3(180f, 1.5f, 10f), 55f),
        new("crossover", new Vector3(-30f, 2f, -24f), new Vector3(0f, 1.5f, 0f), 50f),
        new("pits", new Vector3(-50f, 3f, 60f), new Vector3(0f, 1f, 52f), 50f),
        new("fig8pits", new Vector3(-150f, 4f, -40f), new Vector3(-100f, 1f, -50f), 50f),
        new("overview", new Vector3(0f, 170f, 250f), new Vector3(0f, 0f, 0f), 55f),
    };

    public const float WallHeight = 1.2f;
    public Crowd? Crowd { get; private set; }
    public bool IsNight => SceneFilePath.Contains("Night");
    public bool FloodlightsOn => SceneFilePath.Contains("Night") || SceneFilePath.Contains("Golden");

    private Camera3D? _camera;
    private int _view;
    private readonly List<StandardMaterial3D> _startLamps = new();

    public static string ScenePath(BowlPreset preset) => preset switch
    {
        BowlPreset.GoldenHour => "res://scenes/speedway/SpeedwayLook_Golden.tscn",
        BowlPreset.Night => "res://scenes/speedway/SpeedwayLook_Night.tscn",
        _ => "res://scenes/speedway/SpeedwayLook.tscn",
    };

    public override void _Ready()
    {
        var data = SpeedwayData.Load();
        GetViewport().Msaa3D = Viewport.Msaa.Msaa4X;
        GetViewport().ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Smaa;
        if (DevTools.Instance?.Arg("nolightmap") != null && GetNodeOrNull<LightmapGI>("LightmapGI") is { } lm) lm.LightData = null;
        BuildFencePosts(data);
        BuildTrees(data);
        Crowd = new Crowd
        {
            Night = IsNight,
            BaseExcitement = 0.3f,
            Stands = new[]
            {
                // As SpeedwayLookBuilder.gd builds them (origin, facing, length, rows; columns every ~11 m).
                new Crowd.Stand(new Transform3D(Basis.Identity, new Vector3(0f, 0f, 79.6f)), 132f, 14, (int)(132f / 11f) + 1, 1f),
                new Crowd.Stand(new Transform3D(new Basis(Vector3.Up, Mathf.Pi), new Vector3(0f, 0f, -79.6f)), 84f, 10, (int)(84f / 11f) + 1, 1f),
            },
        };
        AddChild(Crowd);
        if (GetTree().CurrentScene == this) StartViewer();
    }

    /// <summary>The chosen layout's barriers: 2 m red and white blocks standing to the right of each barrier line.</summary>
    public void BuildBarriers(SpeedwayData data, SpeedwayLayout layout)
    {
        var red = new List<Transform3D>();
        var white = new List<Transform3D>();
        float t = data.WallThickness;
        foreach (var line in data.Barriers(layout))
        {
            int n = 0;
            for (int i = 0; i < line.Length - 1; i++)
            {
                Vector2 a = line[i], b = line[i + 1];
                float len = a.DistanceTo(b);
                if (len < 0.05f) continue;
                Vector2 dir = (b - a) / len;
                Vector2 right = new(-dir.Y, dir.X);
                int blocks = Mathf.Max(1, Mathf.RoundToInt(len / 2f));
                for (int k = 0; k < blocks; k++)
                {
                    Vector2 c = a + dir * (len * (k + 0.5f) / blocks) + right * (t * 0.5f);
                    float y = data.HeightAt(c.X, c.Y) + WallHeight * 0.5f - 0.02f;
                    var basis = Basis.LookingAt(new Vector3(dir.X, 0f, dir.Y), Vector3.Up);
                    basis.Z *= len / blocks / 2f;      // stretch along the line so the blocks meet
                    (n++ % 2 == 0 ? red : white).Add(new Transform3D(basis, new Vector3(c.X, y, c.Y)));
                }
            }
        }
        var block = new BoxMesh { Size = new Vector3(t, WallHeight, 1.98f) };
        AddMulti("BarriersRed", block, red, new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.07f, 0.05f), Roughness = 0.55f });
        AddMulti("BarriersWhite", block, white, new StandardMaterial3D { AlbedoColor = new Color(0.86f, 0.86f, 0.84f), Roughness = 0.6f });
    }

    /// <summary>Crash cushions on the pit-wall noses and the crossover's island tips: yellow water barrels.</summary>
    public void BuildCushions(SpeedwayData data, IEnumerable<(Vector2 At, Vector2 Out)> noses)
    {
        var xs = new List<Transform3D>();
        foreach (var (at, dir) in noses)
            foreach (var b in Speedway.CushionBarrels(at, dir))
                xs.Add(new Transform3D(Basis.Identity, new Vector3(b.X, data.HeightAt(b.X, b.Y) + 0.5f, b.Y)));
        AddMulti("Cushions", new CylinderMesh { TopRadius = 0.38f, BottomRadius = 0.4f, Height = 1f, RadialSegments = 12 }, xs,
            new StandardMaterial3D { AlbedoColor = new Color(0.92f, 0.72f, 0.06f), Roughness = 0.5f });
    }

    /// <summary>The five start lights of the layout's gantry: <paramref name="lit"/> of them red (the countdown), or all green.</summary>
    public void SetStartLights(SpeedwayLayout layout, int lit, bool green = false)
    {
        var gantry = GetNodeOrNull<Node3D>($"Gantry_{(layout == SpeedwayLayout.Oval ? "oval" : "figure8")}");
        if (gantry == null) return;
        if (_startLamps.Count == 0)
            for (int k = 0; k < 5; k++)
                if (gantry.GetNodeOrNull<MeshInstance3D>($"StartLight_{k}") is { } lamp)
                {
                    var m = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.05f, 0.05f), EmissionEnabled = true, Roughness = 0.3f };
                    lamp.MaterialOverride = m;
                    _startLamps.Add(m);
                }
        for (int k = 0; k < _startLamps.Count; k++)
        {
            bool on = green || k < lit;
            _startLamps[k].Emission = green ? new Color(0.2f, 1f, 0.3f) : new Color(1f, 0.08f, 0.04f);
            _startLamps[k].EmissionEnergyMultiplier = on ? 6f : 0f;
            _startLamps[k].AlbedoColor = on ? _startLamps[k].Emission * 0.5f : new Color(0.05f, 0.05f, 0.05f);
        }
    }

    private void BuildFencePosts(SpeedwayData data)
    {
        var post = new CylinderMesh { TopRadius = 0.045f, BottomRadius = 0.045f, Height = 3.1f, RadialSegments = 8 };
        var xs = new List<Transform3D>();
        var pts = data.OuterWall;
        float t = data.WallThickness, run = 0f;
        for (int i = 0; i < pts.Length; i++)
        {
            Vector2 a = pts[i], b = pts[(i + 1) % pts.Length];
            float len = a.DistanceTo(b);
            Vector2 dir = len > 1e-3f ? (b - a) / len : Vector2.Right;
            Vector2 right = new(-dir.Y, dir.X);
            for (float s = (3f - run % 3f) % 3f; s < len; s += 3f)
            {
                Vector2 p = a + dir * s + right * (t - 0.18f);
                xs.Add(new Transform3D(Basis.Identity, new Vector3(p.X, data.HeightAt(p.X, p.Y) + WallHeight + 1.5f, p.Y)));
            }
            run += len;
        }
        AddMulti("FencePosts", post, xs, new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.56f, 0.57f), Metallic = 0.8f, Roughness = 0.45f });
    }

    /// <summary>A loose belt of trees round the venue, outside the stands and the floodlights.</summary>
    private void BuildTrees(SpeedwayData data)
    {
        var rng = new RandomNumberGenerator { Seed = 1995 };
        var trunks = new List<Transform3D>();
        var crowns = new List<Transform3D>();
        for (int i = 0; i < 260; i++)
        {
            float ang = Mathf.Tau * i / 260f + rng.RandfRange(-0.01f, 0.01f);
            // An ellipse about 35-80 m outside the outer wall.
            float rx = 230f + rng.RandfRange(0f, 60f), rz = 120f + rng.RandfRange(0f, 50f);
            var p = new Vector3(Mathf.Cos(ang) * rx, 0f, Mathf.Sin(ang) * rz);
            if (Mathf.Abs(p.X) < 80f && Mathf.Abs(p.Z) < 104f) continue;    // behind the stands: keep the view open
            float h = rng.RandfRange(6f, 11f);
            var spin = new Basis(Vector3.Up, rng.RandfRange(0f, Mathf.Tau));
            trunks.Add(new Transform3D(spin.Scaled(new Vector3(1f, h / 4f, 1f)), p + Vector3.Up * (h / 2f)));
            float w = rng.RandfRange(0.8f, 1.3f) * h / 8f;
            crowns.Add(new Transform3D(spin.Scaled(new Vector3(w, w * rng.RandfRange(1.1f, 1.5f), w)), p + Vector3.Up * (h * 0.78f)));
        }
        AddMulti("TreeTrunks", new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.2f, Height = 4f, RadialSegments = 6 }, trunks,
            new StandardMaterial3D { AlbedoColor = new Color(0.24f, 0.18f, 0.12f), Roughness = 0.95f });
        AddMulti("TreeCrowns", new SphereMesh { Radius = 2.4f, Height = 4.8f, RadialSegments = 8, Rings = 5 }, crowns,
            new StandardMaterial3D { AlbedoColor = new Color(0.15f, 0.25f, 0.09f), Roughness = 0.9f });
    }

    private void AddMulti(string name, Mesh mesh, List<Transform3D> xs, Material material)
    {
        mesh.SurfaceSetMaterial(0, material);
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = xs.Count };
        for (int i = 0; i < xs.Count; i++) mm.SetInstanceTransform(i, xs[i]);
        AddChild(new MultiMeshInstance3D { Name = name, Multimesh = mm, GIMode = GeometryInstance3D.GIModeEnum.Dynamic });
    }

    // ------------------------------------------------------------------ look-dev viewer

    private void StartViewer()
    {
        _camera = new Camera3D { Current = true, Near = 0.05f, Far = 4000f };
        AddChild(_camera);
        BuildBarriers(SpeedwayData.Load(), SpeedwayLayout.Figure8);
        ApplyView(Views[0]);
        if (DevTools.Instance?.Arg("shots") is { } dir) RunShots(dir);
        else KeyGuide.Add(this, "Crossroads Speedway (baked lighting)\nV next view   P photo (out/speedway)   Esc quit");
    }

    private void ApplyView(View v)
    {
        if (_camera == null) return;
        _camera.Fov = v.Fov;
        _camera.LookAtFromPosition(v.Eye, v.Target, Vector3.Up);
    }

    private async void RunShots(string dir)
    {
        string abs = ProjectSettings.GlobalizePath(dir.StartsWith("res://") ? dir : "res://" + dir);
        DirAccess.MakeDirRecursiveAbsolute(abs);
        string tag = SceneFilePath.Contains("Golden") ? "golden" : SceneFilePath.Contains("Night") ? "night" : "afternoon";
        foreach (var v in Views)
        {
            ApplyView(v);
            for (int f = 0; f < 30; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng($"{abs}/{tag}_{v.Name}.png");
            GD.Print($"[SpeedwayLook] {abs}/{tag}_{v.Name}.png");
        }
        DevTools.QuitClean(GetTree(), 0);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_camera == null || e.IsEcho() || e is not InputEventKey { Pressed: true } key) return;
        if (key.PhysicalKeycode == Key.V) ApplyView(Views[_view = (_view + 1) % Views.Length]);
        else if (key.PhysicalKeycode == Key.P)
        {
            string abs = ProjectSettings.GlobalizePath("res://out/speedway");
            DirAccess.MakeDirRecursiveAbsolute(abs);
            GetViewport().GetTexture().GetImage().SavePng($"{abs}/photo_{Time.GetTicksMsec()}.png");
        }
        else if (key.PhysicalKeycode == Key.Escape) DevTools.QuitClean(GetTree(), 0);
    }
}
