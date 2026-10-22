using System.Collections.Generic;
using System.Linq;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Tracks;

/// <summary>
/// Crossroads Speedway as a race venue (plan §7.3): the terrain (a heightfield with the banked turns), the walls (the
/// outer wall with its catch fence, the infield islands, and the chosen layout's barriers that close off the other
/// layout's roads), the surface grip for the cars, and the look. Headless runs build the collision only, so sims
/// behave exactly like the visual game.
/// </summary>
public partial class Speedway : Node3D
{
    public SpeedwayLayout LayoutId { get; set; } = SpeedwayLayout.Figure8;
    public bool Reverse { get; set; }
    public bool Visuals { get; set; } = true;
    /// <summary>The lighting (the same three as the Bowl's).</summary>
    public BowlPreset Preset { get; set; } = BowlPreset.Afternoon;
    /// <summary>The baked look (null in headless runs, or before tools/dev builds it: then the grey box).</summary>
    public Tools.SpeedwayLook? Look { get; private set; }
    public bool IsNight => Preset == BowlPreset.Night;
    public bool FloodlightsOn => Preset != BowlPreset.Afternoon;

    public static BowlPreset ParsePreset(string? name) => name?.ToLowerInvariant() switch
    {
        "golden" or "goldenhour" or "golden_hour" => BowlPreset.GoldenHour,
        "night" => BowlPreset.Night,
        _ => BowlPreset.Afternoon,
    };

    public SpeedwayData Data { get; private set; } = null!;
    public RaceLayout Layout { get; private set; } = null!;

    private const float WallHeight = 1.2f;
    private const float FenceHeight = 4.2f;
    public const float CushionDamageScale = 0.3f;
    /// <summary>How far back from the nose of a pit wall the cushion reaches.</summary>
    public const float CushionLength = 4.5f;
    /// <summary>
    /// The crash cushions: the noses of the pit walls and the tips of the islands at the crossover, each with the
    /// direction it faces (out of the island, into the traffic). SpeedwayLook draws the barrels there.
    /// </summary>
    public List<(Vector2 At, Vector2 Out)> CushionPoints { get; } = new();

    /// <summary>Experiment (RaceSim --nobarrels): the cushions without their barrel colliders.</summary>
    public static bool BarrelColliders { get; set; } = true;

    /// <summary>The three water barrels of a cushion: just outside the nose, two across its face and one in front.</summary>
    public static IEnumerable<Vector2> CushionBarrels(Vector2 at, Vector2 dir)
    {
        Vector2 side = new(-dir.Y, dir.X);
        foreach (var (o, l) in new[] { (0.05f, -0.42f), (0.05f, 0.42f), (0.6f, 0f) })
            yield return at + dir * o + side * l;
    }

    /// <summary>
    /// TV cameras for the replay and the wrecked player's view (plan §15.5): high on the turn and infield floodlight
    /// towers, above the front of the stand roofs and above the outer wall where the straights meet the turns, and
    /// low on the wall tops every 55 m round the layout, on its outside. The director skips a post when one of the
    /// venue's buildings (<see cref="TvBlockers"/>) is in the way.
    /// </summary>
    public List<Vector3> TvPosts()
    {
        var posts = new List<Vector3>();
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                // Not on the floodlight towers behind the stands: from there the stand roof hid the whole straight
                // and filled the long lens with corrugated sheet.
                posts.Add(new Vector3(sx * 110f, 12f, sz * 80f));
                posts.Add(new Vector3(sx * 204f, 18f, sz * 18f));
            }
        // High enough on the infield towers to look over the pit garages' roofs at the main straight.
        posts.Add(new Vector3(0f, 22f, 30f));
        posts.Add(new Vector3(0f, 22f, -30f));
        // Just in front of and below the front edge of the stand roofs (9.5 m, right above the outer wall): from on
        // top of the roof, the roof filled the side of the frame whenever the car was near the stand. Not at x = 0:
        // the oval's start gantry spans the straight there, its beam right under the lens.
        foreach (float x in new[] { -45f, -15f, 45f }) posts.Add(new Vector3(x, 8f, 76f));
        foreach (float x in new[] { -25f, 25f }) posts.Add(new Vector3(x, 8f, -76f));
        var path = Layout.Path;
        float half = path.Width * 0.5f + 0.4f;
        for (float s = 0f; s < path.Length; s += 55f)
        {
            Vector3 a = path.PositionAt(s, half), b = path.PositionAt(s, -half);
            Vector3 p = new Vector2(a.X, a.Z).Length() > new Vector2(b.X, b.Z).Length() ? a : b;
            posts.Add(new Vector3(p.X, Data.HeightAt(p.X, p.Z) + 2.2f, p.Z));
        }
        return posts;
    }

    /// <summary>The buildings the TV cameras can't see through (none in the grey box or headless).</summary>
    public IReadOnlyList<Cameras.TvDirector.Blocker>? TvBlockers() => Look?.TvBlockers();

    /// <summary>
    /// --tvcheck: along the racing line, for each TV post placed well for a car there, how often a building hides
    /// the car or fills a side or the bottom of the frame; and how often the best-placed post was blocked (the director
    /// without its line-of-sight check) or no post had a clear shot.
    /// </summary>
    private void TvCheck()
    {
        var posts = TvPosts();
        if (TvBlockers() is not { } blockers) { GD.Print("[TvCheck] no look, no blockers"); return; }
        var path = Layout.Path;
        int n = posts.Count, samples = 0, wasBlocked = 0, gaps = 0;
        var good = new int[n];
        var hidden = new int[n];
        var filled = new int[n];
        var by = new Dictionary<string, int>[n];
        for (int i = 0; i < n; i++) by[i] = new Dictionary<string, int>();
        for (float s = 0f; s < path.Length; s += 4f)
            foreach (float lat in new[] { -0.3f, 0f, 0.3f })
            {
                Vector3 p = path.PositionAt(s, lat * path.Width * 0.5f);
                Vector3 car = new(p.X, Data.HeightAt(p.X, p.Z) + 1.1f, p.Z);
                samples++;
                int oldBest = 0;
                float oldScore = -1f, newScore = -1f;
                for (int i = 0; i < n; i++)
                {
                    float placed = Cameras.TvDirector.Score(posts[i], car, car, -1f, null);
                    int h = Cameras.TvDirector.Hidden(posts[i], car, blockers, 16f / 9f, out string? what);
                    if (placed > 0.5f)
                    {
                        good[i]++;
                        if (h == 1) hidden[i]++;
                        else if (h == 2) filled[i]++;
                        if (what != null) by[i][what] = by[i].GetValueOrDefault(what) + 1;
                    }
                    if (placed > oldScore) { oldScore = placed; oldBest = i; }
                    newScore = Mathf.Max(newScore, h == 0 ? placed : 0f);
                }
                if (Cameras.TvDirector.Hidden(posts[oldBest], car, blockers) != 0) wasBlocked++;
                if (newScore < 0.3f) gaps++;
            }
        GD.Print($"[TvCheck] {Layout.Name}: {n} posts, {blockers.Count} blockers, {samples} samples; the best-placed post was blocked at " +
                 $"{100f * wasBlocked / samples:0}%, no clear shot scoring 0.3 at {100f * gaps / samples:0}%");
        for (int i = 0; i < n; i++)
            GD.Print($"[TvCheck]   post {i,2} ({posts[i].X:0.0}, {posts[i].Y:0.0}, {posts[i].Z:0.0}): good for {good[i],4} samples, " +
                     $"car hidden {(good[i] > 0 ? 100f * hidden[i] / good[i] : 0f),3:0}%, frame filled {(good[i] > 0 ? 100f * filled[i] / good[i] : 0f),3:0}%" +
                     (by[i].Count > 0 ? $" ({string.Join(", ", by[i].OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{kv.Key} {kv.Value}"))})" : ""));
    }

    private bool BarrelsClear(Vector2 at, Vector2 dir)
    {
        var path = Layout.Path;
        var lane = Layout.Pit;
        foreach (var b in CushionBarrels(at, dir))
        {
            var p = new Vector3(b.X, 0f, b.Y);
            if (Mathf.Abs(path.Project(p, -1f).Lateral) < path.Width * 0.5f + 0.8f) return false;
            Vector3 c = lane.PositionAt(lane.Project(p));
            if (new Vector2(c.X - p.X, c.Z - p.Z).Length() < 3.6f) return false;
        }
        return true;
    }

    /// <summary>The way a nose points: from the middle of the ring 4 m either side of it, out through the tip.</summary>
    private static Vector2 NoseDirection(Vector2[] ring, Vector2 nose)
    {
        int n = ring.Length, i = 0;
        for (int k = 1; k < n; k++) if (ring[k].DistanceSquaredTo(nose) < ring[i].DistanceSquaredTo(nose)) i = k;
        Vector2 a = ring[i], b = ring[i];
        for (int k = 1; k < n && a.DistanceTo(nose) < 4f; k++) a = ring[(i - k + n) % n];
        for (int k = 1; k < n && b.DistanceTo(nose) < 4f; k++) b = ring[(i + k) % n];
        Vector2 d = nose - (a + b) * 0.5f;
        return d.LengthSquared() > 1e-6f ? d.Normalized() : Vector2.Right;
    }

    /// <summary>
    /// A pit wall is a long, thin island (the strip between the track and a pit lane): its two ends, the pair of
    /// ring points furthest apart, are noses that face traffic in one direction or the other. Other islands: none.
    /// </summary>
    private static List<Vector2> PitWallNoses(Vector2[] ring)
    {
        var list = new List<Vector2>();
        if (ring.Length < 3) return list;
        float area = 0f, perimeter = 0f;
        for (int i = 0; i < ring.Length; i++)
        {
            Vector2 a = ring[i], b = ring[(i + 1) % ring.Length];
            area += a.X * b.Y - b.X * a.Y;
            perimeter += a.DistanceTo(b);
        }
        area = Mathf.Abs(area) * 0.5f;
        // Mean width = 2 × area / perimeter: the pit walls are about 2.5 m wide, the other islands 17 m and more.
        if (2f * area / Mathf.Max(perimeter, 1f) > 5f) return list;
        float best = 0f;
        Vector2 p0 = ring[0], p1 = ring[0];
        for (int i = 0; i < ring.Length; i++)
            for (int j = i + 1; j < ring.Length; j++)
            {
                float d = ring[i].DistanceSquaredTo(ring[j]);
                if (d > best) { best = d; p0 = ring[i]; p1 = ring[j]; }
            }
        list.Add(p0);
        list.Add(p1);
        return list;
    }

    public override void _Ready()
    {
        Data = SpeedwayData.Load();
        Layout = new RaceLayout(Data, LayoutId, Reverse);
        BuildTerrainCollision();
        BuildWalls();
        Car.SurfaceGripAt = Data.GripAt;
        if (Visuals) LoadLook();
        if (Core.DevTools.Instance?.Arg("speedwaycheck") != null) CallDeferred(MethodName.Probe);
        if (Core.DevTools.Instance?.Arg("tvcheck") != null) CallDeferred(MethodName.TvCheck);
    }

    /// <summary>--speedwaycheck: the physics ground against the data, and the grip, at points around the lap.</summary>
    private async void Probe()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        var space = GetWorld3D().DirectSpaceState;
        var path = Layout.Path;
        var pit = Layout.Pit;
        GD.Print($"[Speedway] {Layout.Name}: lap {path.Length:0} m, pit entry s={pit.EntryS:0} exit s={pit.ExitS:0} " +
                 $"(track {path.Wrap(pit.ExitS - pit.EntryS):0} m, lane {pit.Length:0} m), boxes at {string.Join(", ", System.Linq.Enumerable.Select(pit.Boxes, b => b.ToString("0")))}");
        foreach (var (at, dir) in CushionPoints)
            foreach (var b in CushionBarrels(at, dir))
            {
                var bp = new Vector3(b.X, 0f, b.Y);
                var (bs, bl) = path.Project(bp, -1f);
                Vector3 c = pit.PositionAt(pit.Project(bp));
                GD.Print($"[Speedway] barrel at ({b.X:0.0}, {b.Y:0.0}): s={bs:0} lat={bl:0.0}, {new Vector2(c.X - bp.X, c.Z - bp.Z).Length():0.0} m from the pit lane line");
            }
        for (float s = 0f; s < path.Length; s += path.Length / 16f)
            foreach (float lat in new[] { -5f, 0f, 5f })
            {
                var p = path.PositionAt(s, lat);
                var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(p + Vector3.Up * 20f, p + Vector3.Down * 20f));
                float y = hit.Count > 0 ? ((Vector3)hit["position"]).Y : float.NaN;
                string what = hit.Count > 0 ? ((Node)hit["collider"]).Name : "-";
                GD.Print($"[Speedway] s={s:0} lat={lat:0} data={Data.HeightAt(p.X, p.Z):0.00} physics={y:0.00} ({what}) grip={Data.GripAt(p.X, p.Z):0.00} bank={Mathf.RadToDeg(path.BankAt(s)):0} v={path.SpeedAt(s):0.0}");
            }
    }

    public override void _ExitTree()
    {
        if (Car.SurfaceGripAt == Data.GripAt) Car.SurfaceGripAt = null;
    }

    // ------------------------------------------------------------------ collision

    private void BuildTerrainCollision()
    {
        var g = Data.Height;
        var body = new StaticBody3D { Name = "Terrain" };
        body.SetMeta("surface", "asphalt");
        AddChild(body);
        // HeightMapShape3D cells are 1 unit and the shape is centred on its node: scale the node to the cell size
        // (uniformly, so the heights are stored divided by it).
        var data = new float[g.Data.Length];
        for (int i = 0; i < data.Length; i++) data[i] = g.Data[i] / g.Cell;
        var shape = new HeightMapShape3D { MapWidth = g.Nx, MapDepth = g.Nz, MapData = data };
        body.AddChild(new CollisionShape3D
        {
            Shape = shape,
            Scale = Vector3.One * g.Cell,
            Position = new Vector3(g.X0 + (g.Nx - 1) * g.Cell * 0.5f, 0f, g.Z0 + (g.Nz - 1) * g.Cell * 0.5f),
        });
        // Beyond the raster: a floor at grade, so nothing falls out of the world.
        var outside = new StaticBody3D { Name = "Outfield" };
        outside.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D { Plane = new Plane(Vector3.Up, -0.05f) } });
        AddChild(outside);
    }

    private void BuildWalls()
    {
        var outer = new StaticBody3D { Name = "OuterWall" };
        outer.SetMeta("surface", "concrete");
        AddChild(outer);
        AddWall(outer, Data.OuterWall, closed: true, fence: true);

        var islands = new StaticBody3D { Name = "Islands" };
        islands.SetMeta("surface", "tyres");
        AddChild(islands);
        // The noses of the pit walls face the traffic at the edge of the track: crash cushions there (water
        // barrels in front of the concrete), a hit on them does a third of the damage ✱.
        var cushions = new StaticBody3D { Name = "PitWallCushions" };
        cushions.SetMeta("surface", "tyres");
        cushions.SetMeta("damage_scale", CushionDamageScale);
        AddChild(cushions);
        foreach (var ring in Data.Islands)
        {
            var noses = PitWallNoses(ring);
            // The island tips at the crossover face the crossing traffic too ✱.
            foreach (var corner in Data.CrossoverCorners)
                foreach (var p in ring)
                    if (p.DistanceTo(corner) < 1.5f) { noses.Add(p); break; }
            if (noses.Count == 0)
            {
                AddWall(islands, ring, closed: true, fence: false);
                continue;
            }
            // Split the ring: the segments near a nose go to the cushion body.
            for (int i = 0; i < ring.Length; i++)
            {
                Vector2 a = ring[i], b = ring[(i + 1) % ring.Length];
                bool nose = false;
                foreach (var p in noses) nose |= ((a + b) * 0.5f).DistanceTo(p) < CushionLength;
                AddWall(nose ? cushions : islands, new[] { a, b }, closed: false, fence: false);
            }
            foreach (var p in noses)
            {
                Vector2 dir = NoseDirection(ring, p);
                // Barrels only where none of them stands on this layout's road or pit lane: a nose of the other
                // layout's pit lane, or a crossover tip, is the very edge of the road we race on. There the concrete
                // nose alone softens the hit.
                if (!BarrelsClear(p, dir)) continue;
                CushionPoints.Add((p, dir));
                // The barrels stand in front of the concrete: they are what a car hits first.
                if (BarrelColliders)
                foreach (var b in CushionBarrels(p, dir))
                {
                    float y = Data.HeightAt(b.X, b.Y);
                    cushions.AddChild(new CollisionShape3D
                    {
                        Shape = new CylinderShape3D { Radius = 0.4f, Height = 1.0f },
                        Position = new Vector3(b.X, y + 0.5f, b.Y),
                    });
                }
            }
        }

        var barriers = new StaticBody3D { Name = "Barriers" };
        barriers.SetMeta("surface", "tyres");
        AddChild(barriers);
        foreach (var line in Data.Barriers(LayoutId)) AddWall(barriers, line, closed: false, fence: false);
    }

    /// <summary>Boxes along a polyline, standing to its right (the pavement is on its left), on the terrain.</summary>
    private void AddWall(StaticBody3D body, Vector2[] pts, bool closed, bool fence)
    {
        float t = Data.WallThickness;
        int count = closed ? pts.Length : pts.Length - 1;
        for (int i = 0; i < count; i++)
        {
            Vector2 a = pts[i], b = pts[(i + 1) % pts.Length];
            Vector2 d = b - a;
            float len = d.Length();
            if (len < 0.05f) continue;
            Vector2 dir = d / len;
            Vector2 right = new(-dir.Y, dir.X);
            Vector2 mid = (a + b) * 0.5f + right * (t * 0.5f);
            float ha = Data.HeightAt(a.X, a.Y), hb = Data.HeightAt(b.X, b.Y);
            float bottom = Mathf.Min(ha, hb) - 0.6f;
            float top = Mathf.Max(ha, hb) + WallHeight;
            var basis = Basis.LookingAt(new Vector3(dir.X, 0f, dir.Y), Vector3.Up);
            body.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(t, top - bottom, len + 0.08f) },
                Transform = new Transform3D(basis, new Vector3(mid.X, (top + bottom) * 0.5f, mid.Y)),
            });
            if (fence)
                body.AddChild(new CollisionShape3D
                {
                    Shape = new BoxShape3D { Size = new Vector3(0.15f, FenceHeight - WallHeight, len + 0.08f) },
                    Transform = new Transform3D(basis, new Vector3(mid.X + right.X * (t * 0.5f - 0.1f), top + (FenceHeight - WallHeight) * 0.5f,
                        mid.Y + right.Y * (t * 0.5f - 0.1f))),
                });
        }
    }

    private void LoadLook()
    {
        string path = Tools.SpeedwayLook.ScenePath(Preset);
        if (!ResourceLoader.Exists(path))
        {
            GD.PushWarning($"{path} missing (run tools/dev/speedway_look.ps1): the grey-box look");
            BuildGreyBoxLook();
            return;
        }
        Look = GD.Load<PackedScene>(path).Instantiate<Tools.SpeedwayLook>();
        AddChild(Look);
        Look.BuildBarriers(Data, LayoutId);
        Look.BuildCushions(Data, CushionPoints);
    }

    // ------------------------------------------------------------------ grey-box look (when the baked look is missing)

    private void BuildGreyBoxLook()
    {
        AddChild(new WorldEnvironment { Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color(0.22f, 0.42f, 0.72f),
                SkyHorizonColor = new Color(0.72f, 0.70f, 0.66f),
                GroundBottomColor = new Color(0.20f, 0.17f, 0.14f),
                GroundHorizonColor = new Color(0.62f, 0.58f, 0.52f),
            } },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            SsaoEnabled = true,
            GlowEnabled = true,
            GlowIntensity = 0.4f,
        } });
        var sun = new DirectionalLight3D { LightEnergy = 1.6f, LightColor = new Color(1.0f, 0.93f, 0.82f), ShadowEnabled = true, DirectionalShadowMaxDistance = 250f };
        sun.RotationDegrees = new Vector3(-40f, -35f, 0f);
        AddChild(sun);

        // Terrain at 1 m from the heightfield, coloured by surface.
        var g = Data.Height;
        int step = Mathf.Max(1, (int)Mathf.Round(1f / g.Cell));
        int nx = (g.Nx - 1) / step + 1, nz = (g.Nz - 1) / step + 1;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var verts = new Vector3[nx * nz];
        var cols = new Color[nx * nz];
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                float x = g.X0 + i * step * g.Cell, z = g.Z0 + j * step * g.Cell;
                verts[j * nx + i] = new Vector3(x, g.Data[j * step * g.Nx + i * step], z);
                cols[j * nx + i] = Data.Surface.Nearest(x, z) switch
                {
                    SpeedwayData.Asphalt => new Color(0.30f, 0.30f, 0.31f),
                    SpeedwayData.Pit => new Color(0.38f, 0.37f, 0.36f),
                    _ => new Color(0.33f, 0.43f, 0.22f),
                };
            }
        for (int j = 0; j < nz - 1; j++)
            for (int i = 0; i < nx - 1; i++)
            {
                int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                foreach (int k in new[] { a, b, c, b, d, c })
                {
                    st.SetColor(cols[k]);
                    st.AddVertex(verts[k]);
                }
            }
        st.GenerateNormals();
        AddChild(new MeshInstance3D
        {
            Name = "TerrainLook",
            Mesh = st.Commit(),
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.9f },
        });

        // Walls as boxes (white concrete outside, black tyres on the islands, hazard red barriers).
        var wallMat = new StandardMaterial3D { AlbedoColor = new Color(0.72f, 0.71f, 0.68f), Roughness = 0.85f };
        var tyreMat = new StandardMaterial3D { AlbedoColor = new Color(0.07f, 0.07f, 0.07f), Roughness = 0.9f };
        var barrierMat = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.12f, 0.08f), Roughness = 0.7f };
        foreach (var child in GetChildren())
        {
            if (child is not StaticBody3D body || body.Name == "Terrain" || body.Name == "Outfield") continue;
            var mat = body.Name == "OuterWall" ? wallMat : body.Name == "Islands" ? tyreMat : barrierMat;
            foreach (var c in body.GetChildren())
                if (c is CollisionShape3D cs && cs.Shape is BoxShape3D box)
                    body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = box.Size }, MaterialOverride = mat, Transform = cs.Transform });
        }

        // The pit boxes of this layout: a yellow frame on the lane at each.
        var lineMat = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.78f, 0.12f), Roughness = 0.8f };
        var lane = Layout.Pit;
        foreach (float u in lane.Boxes)
        {
            Vector3 c = lane.PositionAt(u), t = lane.TangentAt(u);
            Vector3 r = new(-t.Z, 0f, t.X);
            float y = Data.HeightAt(c.X, c.Z) + 0.02f;
            var basis = Basis.LookingAt(t, Vector3.Up);
            foreach (var (off, size) in new[]
            {
                (r * 1.55f, new Vector3(0.15f, 0.02f, 5.6f)), (-r * 1.55f, new Vector3(0.15f, 0.02f, 5.6f)),
                (t * 2.75f, new Vector3(3.25f, 0.02f, 0.15f)), (-t * 2.75f, new Vector3(3.25f, 0.02f, 0.15f)),
            })
                AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = lineMat, Transform = new Transform3D(basis, new Vector3(c.X + off.X, y, c.Z + off.Z)) });
        }
    }
}
