using System;
using System.Collections.Generic;
using System.Linq;
using DestructionRacing.Fx;
using DestructionRacing.Rules;
using Godot;

namespace DestructionRacing.Vehicles;

/// <summary>
/// The generated car on a gameplay <see cref="Car"/> (plan §6.2, M3). The wheels follow the suspension, the DD
/// damage zones drive the crush shapes, and every hit dents and scrapes the metal where it landed. The parts
/// react: bumpers hang by one end and fall off, the hood works loose, flaps and blows off, doors swing open,
/// mirrors and wheels come off as physics debris, windows crack and shatter, lamps break. Steam, smoke and fire
/// follow the engine's health (plan §6.1). Model space = car-local space shifted down to the ground.
/// </summary>
public partial class CarVisual : Node3D
{
    private enum PartState { Attached, Loose, Off }
    private enum PartKind { Hood, Trunk, Door, Bumper, Mirror, Wheel, Hubcap, Exhaust }

    private sealed class Part
    {
        public string Name = "";
        public PartKind Kind;
        public PartState State;
        public DamageZone Zone;
        public readonly List<Node3D> Nodes = new();
        public Aabb Box;                   // model space, at rest
        public Node3D? Pivot;              // made when the part works loose
        public Vector3 HingePoint;         // model space
        public Vector3 HingeAxis;
        public float Sign = 1f;            // opening direction about the axis
        public float Angle, Velocity, RestAngle, MaxAngle;
        public float Mass;
        public int Wheel = -1;
        public float LooseTime;
    }

    private sealed class Pane
    {
        public MeshInstance3D Mesh = null!;
        public Aabb Box;
        public float Crack;
        public bool Gone;
        public DamageZone Zone;
    }

    private sealed class Lamp
    {
        public MeshInstance3D Mesh = null!;
        public Aabb Box;
        public bool Broken;
        public bool Front;
        /// <summary>A headlamp (clear glass at the front), as opposed to an indicator or tail lamp.</summary>
        public bool Head;
        /// <summary>A reversing lamp (clear glass at the back): lit only while reversing.</summary>
        public bool Reverse;
    }

    public Car Car { get; }
    public CarModel Model { get; }
    public CarSpec Spec { get; }

    /// <summary>
    /// Damage moments for sound and commentary (glass, lamp, part off with its kind, rollover): the visual, what
    /// happened ("glass", "lamp", "hood", "trunk", "door", "bumper", "mirror", "wheel", "hubcap", "exhaust", "roll") and where.
    /// </summary>
    public static event System.Action<CarVisual, string, Vector3>? Moment;

    /// <summary>How hard the car is grinding against something right now (0..1) and whether it is the wall.</summary>
    public float ScrapeLevel { get; private set; }
    public bool ScrapeOnWall { get; private set; }
    /// <summary>The car is on fire (a wrecked car burns for a while).</summary>
    public bool Burning { get; private set; }

    private readonly Livery _livery;
    private readonly List<Part> _parts = new();
    private readonly List<Pane> _panes = new();
    private readonly List<Lamp> _lamps = new();
    private readonly Node3D?[] _wheelNodes = new Node3D?[4];
    private readonly float[] _crush = new float[4];
    private readonly Random _rng;
    private Vector4 _crushShown = new(-1f, -1f, -1f, -1f);
    private float _colliderFront = -1f, _colliderRear = -1f;
    private Vector3 _enginePoint;
    private GpuParticles3D _smoke = null!, _flames = null!, _scrape = null!;
    /// <summary>The engine plumes (the perf A/B hides them).</summary>
    public static readonly StringName SmokeGroup = "dr_smoke";
    private readonly GpuParticles3D?[] _hubSparks = new GpuParticles3D?[4];
    private OmniLight3D _fireLight = null!;
    private float _scrapeTimer, _fireTime, _time, _wreckAge;
    private bool _wreckSeen, _rolled;
    private Vector3 _lastVelocity;
    private Dictionary<Material, Material>? _frozenThisFrame;

    private static readonly string[] WheelNames = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };

    /// <summary>A glass or lamp change, kept for replays: kind 0 a pane cracks (value), 1 a pane bursts, 2 a lamp breaks.</summary>
    private readonly record struct GlassOp(byte Kind, int Index, float Value);
    private List<GlassOp>? _glassJournal;
    private int _glassShown;

    /// <summary>
    /// Keeps every damage change from now on (the dent and wear volumes, glass, lamps), so a replay can show the
    /// car as it was at any moment (plan §15.5). Parts that come off replay as debris tracks (ReplayRecorder).
    /// </summary>
    public void StartJournal()
    {
        Model.Damage.StartJournal();
        _glassJournal ??= new List<GlassOp>();
    }

    /// <summary>The damage as it is shown now: the volumes' fingerprint and the broken glass and lamps (replay self-test).</summary>
    public string DamageSignature() =>
        $"{Model.Damage.Checksum():x16}/" + string.Concat(_panes.Select(p => p.Gone ? "x" : p.Crack > 0.05f ? "c" : ".")) + "/" +
        string.Concat(_lamps.Select(l => l.Broken ? "x" : "."));

    /// <summary>How many volume and glass changes the journals hold (the recorder stores them per frame).</summary>
    public int DamageOps => Model.Damage.Journal?.Count ?? 0;
    public int GlassOps => _glassJournal?.Count ?? 0;

    /// <summary>
    /// Replays: the damage as it was after the first <paramref name="damageOps"/> volume changes and
    /// <paramref name="glassOps"/> glass changes. Going forward with <paramref name="effects"/> bursts the glass
    /// that breaks on the way (playing); going back puts it together again.
    /// </summary>
    public void ReplayDamageTo(int damageOps, int glassOps, bool effects)
    {
        Model.Damage.ReplayTo(damageOps);
        if (_glassJournal == null) return;
        glassOps = Math.Clamp(glassOps, 0, _glassJournal.Count);
        if (glassOps < _glassShown)
        {
            foreach (var pane in _panes)
            {
                if (pane.Mesh.GetParent() is DebrisPiece) continue;
                pane.Crack = 0f;
                pane.Gone = false;
                pane.Mesh.SetInstanceShaderParameter("pane_crack", 0f);
                pane.Mesh.SetInstanceShaderParameter("pane_gone", 0f);
            }
            foreach (var lamp in _lamps)
            {
                lamp.Broken = false;
                lamp.Mesh.SetInstanceShaderParameter("lamp_broken", 0f);
            }
            _glassShown = 0;
            effects = false;
        }
        for (int n = _glassShown; n < glassOps; n++)
        {
            var op = _glassJournal[n];
            if (op.Kind == 2)
            {
                if (op.Index < 0 || op.Index >= _lamps.Count) continue;
                var lamp = _lamps[op.Index];
                lamp.Broken = true;
                lamp.Mesh.SetInstanceShaderParameter("lamp_broken", 1f);
                if (effects) FxPool.Get(this).Glass(lamp.Mesh.GlobalTransform * lamp.Box.GetCenter(), Car.GlobalBasis.Z * (lamp.Front ? -1f : 1f), 0.15f);
                continue;
            }
            if (op.Index < 0 || op.Index >= _panes.Count) continue;
            var p = _panes[op.Index];
            if (p.Mesh.GetParent() is DebrisPiece) continue;
            if (op.Kind == 0)
            {
                p.Crack = op.Value;
                p.Mesh.SetInstanceShaderParameter("pane_crack", Mathf.Min(op.Value, 0.85f));
            }
            else
            {
                p.Gone = true;
                p.Mesh.SetInstanceShaderParameter("pane_gone", 1f);
                if (effects) FxPool.Get(this).Glass(p.Mesh.GlobalTransform * p.Box.GetCenter(), Vector3.Up * 0.3f, 0.6f);
            }
        }
        _glassShown = glassOps;
    }

    /// <summary>Keeps a glass change; false while a replay shows an earlier moment (the past doesn't change).</summary>
    private bool RecordGlass(GlassOp op)
    {
        if (_glassJournal == null) return true;
        if (_glassShown < _glassJournal.Count) return false;
        _glassJournal.Add(op);
        _glassShown = _glassJournal.Count;
        return true;
    }

    /// <summary>The car's velocity as the effects see it: the recorded speed along the car in a replay.</summary>
    private Vector3 MoveVelocity => Car.IsReplaying ? -Car.GlobalBasis.Z * Car.ReplaySpeed : Car.LinearVelocity;

    public CarVisual(Car car, string modelId, Livery livery, string plate)
    {
        Name = "Visual";
        Car = car;
        Spec = CarSpec.For(modelId);
        _livery = livery;
        _rng = new Random(car.Id * 7349 + 11);
        Model = CarModel.Load(modelId, livery);
        Model.SetPlate(plate);
        Model.Root.Position = new Vector3(0f, car.GroundY, 0f);
        AddChild(Model.Root);
        for (int i = 0; i < 4; i++) _wheelNodes[i] = Model.Node(WheelNames[i]);
        BuildParts();
        BuildGlassAndLamps();
        BuildEngineBay();
        BuildEffects();
    }

    public override void _Ready()
    {
        // Make the shared pools (and compile their shaders) while the arena loads, not at the first crash.
        // Deferred: the scene may still be setting up its children.
        Callable.From(() =>
        {
            if (!IsInsideTree()) return;
            FxPool.Get(this);
            DebrisPool.Get(this);
        }).CallDeferred();
    }

    // ------------------------------------------------------------------ set-up

    private bool Box(string name, out Aabb box) => Spec.TryPart(name, out box);

    private Part? Add(string name, PartKind kind, DamageZone zone, float mass)
    {
        if (Model.Node(name) is not Node3D node || !Box(name, out var box)) return null;
        var p = new Part { Name = name, Kind = kind, Zone = zone, Box = box, Mass = mass };
        p.Nodes.Add(node);
        _parts.Add(p);
        return p;
    }

    /// <summary>Moves small parts whose centre lies on a panel into that panel's group (handles, door glass, plates).</summary>
    private void Adopt(Part part, params string[] prefixes)
    {
        foreach (var (name, box) in Spec.Parts)
        {
            if (name == part.Name || !Array.Exists(prefixes, name.StartsWith)) continue;
            Vector3 c = box.GetCenter();
            var grown = part.Box.Grow(0.12f);
            if (!grown.HasPoint(c) || Model.Node(name) is not Node3D node) continue;
            if (_parts.Exists(q => q != part && q.Nodes.Contains(node))) continue;
            part.Nodes.Add(node);
            part.Box = part.Box.Merge(box);
        }
    }

    private void BuildParts()
    {
        if (Add("Hood", PartKind.Hood, DamageZone.Front, 16f) is { } hood)
        {
            hood.HingePoint = new Vector3(0f, hood.Box.End.Y, hood.Box.End.Z);
            hood.HingeAxis = Vector3.Right;
            hood.Sign = 1f;
            hood.RestAngle = 0.32f;
            hood.MaxAngle = 1.35f;
        }
        if (Add("Trunk", PartKind.Trunk, DamageZone.Rear, 12f) is { } trunk)
        {
            trunk.HingePoint = new Vector3(0f, trunk.Box.End.Y, trunk.Box.Position.Z);
            trunk.HingeAxis = Vector3.Right;
            trunk.Sign = -1f;
            trunk.RestAngle = 0.45f;
            trunk.MaxAngle = 1.2f;
            // A hatch's or wagon's tailgate carries its glass and wiper (a spoiler is part of the lid's mesh).
            Adopt(trunk, "Plate_R", "Light_Tail", "Wiper_Rear", "Glass_Rear");
        }
        foreach (var side in new[] { "L", "R" })
        {
            bool left = side == "L";
            foreach (var row in new[] { "F", "R" })
            {
                if (Add($"Door_{row}{side}", PartKind.Door, left ? DamageZone.Left : DamageZone.Right, 22f) is not { } door) continue;
                door.HingePoint = new Vector3(left ? door.Box.Position.X : door.Box.End.X, door.Box.GetCenter().Y, door.Box.Position.Z);
                door.HingeAxis = Vector3.Up;
                door.Sign = left ? -1f : 1f;
                door.RestAngle = 0.4f;
                door.MaxAngle = 1.25f;
            }
            Add($"Mirror_{side}", PartKind.Mirror, left ? DamageZone.Left : DamageZone.Right, 1.5f);
        }
        foreach (var end in new[] { "F", "R" })
        {
            if (Add($"Bumper_{end}", PartKind.Bumper, end == "F" ? DamageZone.Front : DamageZone.Rear, 14f) is not { } bumper) continue;
            Adopt(bumper, $"Bumper_{end}_Strip", $"Plate_{end}", "Light_Indicator_" + end, "Light_Fog");
            bumper.HingeAxis = Vector3.Back;
            bumper.RestAngle = 0.5f;
            bumper.MaxAngle = 1.1f;
        }
        // Door groups: handles, door glass and (on most cars) the mirror swing with the door. Done after the
        // bumpers and trunk so a plate or tail lamp is never claimed twice.
        foreach (var door in _parts.FindAll(p => p.Kind == PartKind.Door)) Adopt(door, "Handle", "Glass_F", "Glass_R");
        foreach (var mirror in _parts.FindAll(p => p.Kind == PartKind.Mirror))
        {
            var door = _parts.Find(d => d.Kind == PartKind.Door && d.Box.Grow(0.16f).HasPoint(mirror.Box.GetCenter()));
            door?.Nodes.Add(mirror.Nodes[0]);     // shared: it swings with the door until it is knocked off
        }
        for (int i = 0; i < 4; i++)
        {
            if (_wheelNodes[i] is not { } w) continue;
            var p = new Part { Name = WheelNames[i], Kind = PartKind.Wheel, Mass = 18f, Wheel = i, Zone = i < 2 ? DamageZone.Front : DamageZone.Rear };
            p.Nodes.Add(w);
            p.Box = new Aabb(Spec.WheelCentres[i] - new Vector3(Spec.WheelWidth * 0.5f, Spec.WheelRadius, Spec.WheelRadius),
                new Vector3(Spec.WheelWidth, Spec.WheelRadius * 2f, Spec.WheelRadius * 2f));
            _parts.Add(p);
            // Hubcaps and wheel covers fly off on their own (M5): a child of the wheel node, in wheel space.
            string cap = WheelNames[i] + "_Hubcap";
            if (Model.Node(cap) is Node3D capNode && Box(cap, out var capBox))
            {
                var c = new Part { Name = cap, Kind = PartKind.Hubcap, Mass = 1.2f, Wheel = i, Zone = p.Zone, Box = capBox };
                c.Nodes.Add(capNode);
                _parts.Add(c);
            }
        }
        Add("Exhaust", PartKind.Exhaust, DamageZone.Rear, 6f);
    }

    private void BuildGlassAndLamps()
    {
        foreach (var (name, box) in Spec.Parts)
        {
            if (Model.Node(name) is not MeshInstance3D mi) continue;
            if (name.StartsWith("Glass_"))
            {
                var zone = name switch
                {
                    "Glass_Front" => DamageZone.Front,
                    "Glass_Rear" => DamageZone.Rear,
                    _ => name.EndsWith("L") ? DamageZone.Left : DamageZone.Right,
                };
                _panes.Add(new Pane { Mesh = mi, Box = box, Zone = zone });
            }
            else if (name.StartsWith("Light_") && (mi.Mesh.SurfaceGetMaterial(0)?.ResourceName ?? "").StartsWith("lens"))
            {
                string material = mi.Mesh.SurfaceGetMaterial(0)?.ResourceName ?? "";
                bool front = box.GetCenter().Z < 0f, clear = material.StartsWith("lens_clear");
                bool fog = name.StartsWith("Light_Fog");      // fog lamps glow with the lights but do not aim the beam
                _lamps.Add(new Lamp { Mesh = mi, Box = box, Front = front, Head = clear && front && !fog, Reverse = clear && !front });
                if (clear && !front) mi.SetInstanceShaderParameter("lamp_lit", 0f);
            }
        }
    }

    /// <summary>Headlamps and tail lamps lit (dusk and night); a wrecked car's lamps go out.</summary>
    public bool LightsOn
    {
        get => _lightsOn;
        set
        {
            _lightsOn = value;
            UpdateLampGlow();
        }
    }
    private bool _lightsOn;
    private bool _lampsDark;
    private bool _reverseLit;

    /// <summary>Reversing lamps follow the reverse gear (any time of day).</summary>
    private void UpdateReverseLamps()
    {
        bool lit = !Car.IsWrecked && (Car.LastControls.Reverse || Car.ForwardSpeed < -1f);
        if (lit == _reverseLit) return;
        _reverseLit = lit;
        foreach (var lamp in _lamps)
            if (lamp.Reverse) lamp.Mesh.SetInstanceShaderParameter("lamp_lit", lit ? 1f : 0f);
        // The clear lens glows only through its lit instances; by day it needs some energy of its own.
        if (!_lightsOn) (Model.Material("lens_clear") as ShaderMaterial)?.SetShaderParameter("emission_energy", lit ? 3f : 0f);
        foreach (var lamp in _lamps)
            if (lamp.Head && !_lightsOn) lamp.Mesh.SetInstanceShaderParameter("lamp_lit", lit ? 0f : 1f);
    }

    private void UpdateLampGlow()
    {
        bool on = _lightsOn && !_lampsDark;
        (Model.Material("lens_clear") as ShaderMaterial)?.SetShaderParameter("emission_energy", on ? 5f : 0f);
        (Model.Material("lens_red") as ShaderMaterial)?.SetShaderParameter("emission_energy", on ? 1.6f : 0f);
    }

    /// <summary>Share of the headlamps still working (0 when the lights are off or the car is wrecked).</summary>
    public float HeadlightStrength
    {
        get
        {
            if (!_lightsOn || _lampsDark) return 0f;
            int total = 0, working = 0;
            foreach (var lamp in _lamps)
            {
                if (!lamp.Head) continue;
                total++;
                if (!lamp.Broken) working++;
            }
            return total == 0 ? 0f : (float)working / total;
        }
    }

    /// <summary>Where the headlight beam starts, in the car's local space (between the working headlamps).</summary>
    public Vector3 HeadlightOrigin
    {
        get
        {
            Vector3 sum = Vector3.Zero;
            int n = 0;
            foreach (var lamp in _lamps)
                if (lamp.Head && !lamp.Broken) { sum += lamp.Box.GetCenter(); n++; }
            if (n == 0) return new Vector3(0f, 0.65f + Car.GroundY, -2.2f);
            var c = sum / n;
            return new Vector3(c.X, c.Y + Car.GroundY, c.Z - 0.05f);
        }
    }

    /// <summary>
    /// A simple engine under the hood (block, rocker covers, air cleaner, radiator), so a car that lost its
    /// hood shows an engine bay instead of a hole. Built in model space with the car's own materials, so it dents
    /// and crushes with the front.
    /// </summary>
    private void BuildEngineBay()
    {
        if (!Box("Hood", out var hood) || Model.Material("underbody") is not { } block ||
            Model.Material("trim_black") is not { } dark || Model.Material("plate") is not { } lid) return;
        float w = hood.Size.X, l = hood.Size.Z, z0 = hood.Position.Z;
        float floor = Spec.WheelCentreY - 0.05f;
        float top = Mathf.Max(hood.Position.Y - 0.1f, floor + 0.2f);
        float half = Mathf.Min(0.3f, 0.27f * w);
        float zf = z0 + 0.28f * l, zr = z0 + 0.85f * l;

        var mesh = new ArrayMesh();
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        AddBox(st, new Vector3(-half, floor, zf), new Vector3(half, top, zr));
        st.Commit(mesh);
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (float side in new[] { -1f, 1f })
            AddBox(st, new Vector3(side * half * 0.55f - 0.07f, top, zf + 0.06f), new Vector3(side * half * 0.55f + 0.07f, top + 0.05f, zr - 0.06f));
        AddBox(st, new Vector3(-0.36f * w, floor + 0.05f, z0 + 0.04f), new Vector3(0.36f * w, Mathf.Max(hood.Position.Y - 0.03f, floor + 0.3f), z0 + 0.09f));
        st.Commit(mesh);
        st.Begin(Mesh.PrimitiveType.Triangles);
        AddCylinder(st, new Vector3(0f, top + 0.02f, 0.5f * (zf + zr)), Mathf.Min(0.18f, half * 0.75f), 0.07f);
        st.Commit(mesh);
        mesh.SurfaceSetMaterial(0, block);
        mesh.SurfaceSetMaterial(1, dark);
        mesh.SurfaceSetMaterial(2, lid);
        Model.Root.AddChild(new MeshInstance3D { Name = "EngineBay", Mesh = mesh, Layers = CarModel.RenderLayer });
    }

    // Godot's front faces are clockwise; u × v points out of the face.
    private static void Quad(SurfaceTool st, Vector3 c, Vector3 u, Vector3 v)
    {
        Vector3 n = u.Cross(v).Normalized();
        Vector3 c0 = c - u - v, c1 = c + u - v, c2 = c + u + v, c3 = c - u + v;
        foreach (var p in new[] { c0, c3, c2, c0, c2, c1 })
        {
            st.SetNormal(n);
            st.AddVertex(p);
        }
    }

    private static void AddBox(SurfaceTool st, Vector3 min, Vector3 max)
    {
        Vector3 c = (min + max) * 0.5f, e = (max - min) * 0.5f;
        Vector3 x = new(e.X, 0f, 0f), y = new(0f, e.Y, 0f), z = new(0f, 0f, e.Z);
        Quad(st, c + x, y, z);
        Quad(st, c - x, z, y);
        Quad(st, c + y, z, x);
        Quad(st, c - y, x, z);
        Quad(st, c + z, x, y);
        Quad(st, c - z, y, x);
    }

    private static void AddCylinder(SurfaceTool st, Vector3 baseCentre, float radius, float height, int sides = 14)
    {
        Vector3 up = Vector3.Up * height;
        for (int i = 0; i < sides; i++)
        {
            float a0 = Mathf.Tau * i / sides, a1 = Mathf.Tau * (i + 1) / sides;
            Vector3 d0 = new(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            Vector3 b0 = baseCentre + d0 * radius, b1 = baseCentre + d1 * radius;
            Vector3 t0 = b0 + up, t1 = b1 + up;
            Vector3 n = (d0 + d1).Normalized();
            foreach (var p in new[] { b0, t1, t0, b0, b1, t1 })
            {
                st.SetNormal(n);
                st.AddVertex(p);
            }
            foreach (var p in new[] { baseCentre + up, t0, t1 })
            {
                st.SetNormal(Vector3.Up);
                st.AddVertex(p);
            }
        }
    }

    private void BuildEffects()
    {
        // The engine: under the front of the hood (the plume rises from its edges).
        _enginePoint = Box("Hood", out var hood)
            ? new Vector3(0f, hood.End.Y + 0.05f, hood.Position.Z + hood.Size.Z * 0.35f)
            : new Vector3(0f, Spec.Body.Size.Y * 0.65f, Spec.Body.Position.Z + 0.8f);
        _smoke = FxPool.Smoke();
        _smoke.AddToGroup(SmokeGroup);
        AddChild(_smoke);
        _flames = FxPool.Flames();
        AddChild(_flames);
        _scrape = FxPool.ScrapeSparks();
        AddChild(_scrape);
        _fireLight = new OmniLight3D
        {
            LightColor = new Color(1f, 0.55f, 0.18f),
            OmniRange = 5f,
            LightEnergy = 0f,
            LightVolumetricFogEnergy = 0f,    // flickering light: no fog ghosting (plan §8.1)
            ShadowEnabled = false,
            // A 5 m glow: far from the camera it is the flames that show, not the light (a light costs every pixel it reaches).
            DistanceFadeEnabled = true,
            DistanceFadeBegin = 30f,
            DistanceFadeLength = 10f,
            Visible = false,
        };
        AddChild(_fireLight);
    }

    // ------------------------------------------------------------------ space helpers

    // Hits and scrapes come from the physics body, which DD2's slide doesn't turn (Car.DriftAngle): they map onto the
    // model as if it weren't turned, so a dent lands on the panel that was hit.
    private Transform3D ModelFrame => Car.GlobalTransform * Model.Root.Transform;
    private Vector3 ToModel(Vector3 world) => ModelFrame.AffineInverse() * world;
    private Vector3 ToModelDir(Vector3 world) => ModelFrame.Basis.Inverse() * world;
    private Vector3 ToWorld(Vector3 model) => Model.Root.ToGlobal(model);

    private static float Distance(Aabb box, Vector3 p)
    {
        Vector3 q = new(Mathf.Clamp(p.X, box.Position.X, box.End.X), Mathf.Clamp(p.Y, box.Position.Y, box.End.Y), Mathf.Clamp(p.Z, box.Position.Z, box.End.Z));
        return q.DistanceTo(p);
    }

    private float Rand() => (float)_rng.NextDouble();

    private Color PaintColour => _livery.Paint;

    // ------------------------------------------------------------------ hits

    /// <summary>
    /// A hit measured by the ImpactSystem. <paramref name="intoCar"/> points into this car (world space).
    /// Dents and scrapes the metal at the contact and knocks glass, lamps and loose parts.
    /// </summary>
    public void OnHit(Vector3 worldPoint, Vector3 intoCar, float energy)
    {
        float e = Mathf.Clamp((energy - 4000f) / 60000f, 0f, 1f);
        Vector3 into = ToModelDir(intoCar);
        if (into.LengthSquared() < 1e-6f) return;
        into = into.Normalized();
        // Contacts are on the collider: pull the point onto the body box so the dent lands on the panel.
        Vector3 p = ToModel(worldPoint);
        var body = Spec.Body;
        p = new Vector3(Mathf.Clamp(p.X, body.Position.X, body.End.X), Mathf.Clamp(p.Y, body.Position.Y + 0.15f, body.End.Y - 0.25f),
            Mathf.Clamp(p.Z, body.Position.Z, body.End.Z));
        float depth = 0.03f + 0.14f * e, radius = 0.3f + 0.3f * e;
        Model.Damage.AddDent(p, into * depth, radius);
        Model.Damage.AddScrape(p, radius * 0.7f, 0.15f + 0.35f * e);

        var fx = FxPool.Get(this);
        fx.Sparks(worldPoint, -intoCar, 0.25f + 0.75f * e);
        if (e > 0.08f) fx.Chips(worldPoint, -intoCar, PaintColour, 0.2f + 0.8f * e);

        foreach (var pane in _panes)
        {
            if (pane.Gone || pane.Mesh.GetParent() is DebrisPiece) continue;
            float d = Distance(pane.Box, p);
            if (d > 1.1f) continue;
            pane.Crack += e * (1.1f - d) * 1.4f;
            if (pane.Crack >= 1f || (e > 0.5f && d < 0.5f)) Shatter(pane, intoCar);
            else if (pane.Crack > 0.05f && RecordGlass(new GlassOp(0, _panes.IndexOf(pane), pane.Crack)))
                pane.Mesh.SetInstanceShaderParameter("pane_crack", Mathf.Min(pane.Crack, 0.85f));
        }
        foreach (var lamp in _lamps)
            if (!lamp.Broken && e > 0.06f && Distance(lamp.Box, p) < 0.45f) Break(lamp);

        foreach (var part in _parts)
        {
            if (part.State == PartState.Off) continue;
            float d = Distance(part.Box, p);
            if (part.Kind == PartKind.Mirror && part.State == PartState.Attached && d < 0.55f && e > 0.12f)
                Detach(part, -intoCar * 2f + Vector3.Up * 1.5f);
            else if (part.Kind == PartKind.Hubcap && d < 0.5f && e > 0.15f && Rand() < 0.35f + e)
                Detach(part, ToWorldDir(new Vector3(part.Wheel % 2 == 0 ? -3f : 3f, 1.2f, 0f)));
            else if (part.Kind == PartKind.Exhaust && d < 0.45f && e > 0.35f && Rand() < 0.5f)
                Detach(part, Vector3.Down - intoCar);
            else if (part.State == PartState.Loose && d < 1.2f)
            {
                part.Velocity += (2f + 8f * e) * (Rand() - 0.3f);
                if (e > 0.3f && Rand() < 0.5f) Detach(part, -intoCar * 2.5f + Vector3.Up * 2f);
            }
        }
    }

    /// <summary>Sustained sliding contact: grinds a streak into the paint and throws sparks at the contact.</summary>
    public void OnScrape(Vector3 worldPoint, Vector3 normalOut, Vector3 slideVelocity, float dt, bool wall = false)
    {
        float slide = slideVelocity.Length();
        if (slide < 2f) return;
        ScrapeLevel = Mathf.Max(ScrapeLevel, Mathf.Clamp((slide - 2f) / 10f, 0.15f, 1f));
        ScrapeOnWall = wall;
        Vector3 p = ToModel(worldPoint);
        var body = Spec.Body;
        p = new Vector3(Mathf.Clamp(p.X, body.Position.X, body.End.X), Mathf.Clamp(p.Y, body.Position.Y + 0.15f, body.End.Y - 0.3f),
            Mathf.Clamp(p.Z, body.Position.Z, body.End.Z));
        Model.Damage.AddScrape(p, 0.22f, Mathf.Min(0.5f, slide * dt * 0.1f));
        Vector3 throwDir = (normalOut * 0.4f + Vector3.Up * 0.6f - slideVelocity.Normalized() * 0.6f).Normalized();
        _scrape.GlobalTransform = new Transform3D(FxPool.BasisAlong(throwDir), worldPoint);
        _scrape.AmountRatio = Mathf.Clamp(slide / 14f, 0.2f, 1f);
        _scrape.Emitting = true;
        _scrapeTimer = 0.12f;
    }

    private void Shatter(Pane pane, Vector3 intoCar)
    {
        if (!RecordGlass(new GlassOp(1, _panes.IndexOf(pane), 0f))) return;
        pane.Gone = true;
        pane.Mesh.SetInstanceShaderParameter("pane_gone", 1f);
        Vector3 centre = pane.Mesh.GlobalTransform * pane.Box.GetCenter();
        float area = pane.Box.Size.X * pane.Box.Size.Y + pane.Box.Size.Z * pane.Box.Size.Y + pane.Box.Size.X * pane.Box.Size.Z;
        FxPool.Get(this).Glass(centre, intoCar * 0.5f + Vector3.Up * 0.3f, Mathf.Clamp(area * 0.9f, 0.25f, 1f));
        Moment?.Invoke(this, "glass", centre);
    }

    private void Break(Lamp lamp)
    {
        if (!RecordGlass(new GlassOp(2, _lamps.IndexOf(lamp), 0f))) return;
        lamp.Broken = true;
        lamp.Mesh.SetInstanceShaderParameter("lamp_broken", 1f);
        Vector3 c = lamp.Mesh.GlobalTransform * lamp.Box.GetCenter();
        var fx = FxPool.Get(this);
        fx.Glass(c, Car.GlobalBasis.Z * (lamp.Front ? -1f : 1f), 0.15f);
        fx.Sparks(c, Vector3.Up, 0.1f);
        Moment?.Invoke(this, lamp.Head ? "headlamp" : "lamp", c);
    }

    // ------------------------------------------------------------------ parts

    private void Loosen(Part part)
    {
        if (part.State != PartState.Attached) return;
        if (part.Kind is PartKind.Mirror or PartKind.Wheel or PartKind.Hubcap or PartKind.Exhaust) { Detach(part, Vector3.Up); return; }
        if (part.Kind == PartKind.Bumper)
        {
            // Hangs by the end that took less of the hits (or either, by chance).
            bool pivotLeft = Car.Health(DamageZone.Left) > Car.Health(DamageZone.Right) == (Rand() < 0.8f);
            float x = pivotLeft ? part.Box.Position.X + 0.1f : part.Box.End.X - 0.1f;
            part.HingePoint = new Vector3(x, part.Box.End.Y, part.Box.GetCenter().Z);
            part.Sign = pivotLeft ? -1f : 1f;
        }
        var pivot = new Node3D { Name = part.Name + "_Hinge", Position = part.HingePoint };
        Model.Root.AddChild(pivot);
        foreach (var n in part.Nodes) n.Reparent(pivot, true);
        part.Pivot = pivot;
        part.State = PartState.Loose;
        part.Velocity = 3f;
    }

    private void Detach(Part part, Vector3 extraVelocity)
    {
        if (part.State == PartState.Off) return;
        part.State = PartState.Off;
        Moment?.Invoke(this, part.Kind.ToString().ToLowerInvariant(), ToWorld(part.Box.GetCenter()));
        if (part.Kind is not (PartKind.Wheel or PartKind.Hubcap)) _frozenThisFrame ??= Model.FreezeMaterials();
        // A wheel takes its hubcap with it.
        if (part.Kind == PartKind.Wheel)
            foreach (var cap in _parts)
                if (cap.Kind == PartKind.Hubcap && cap.Wheel == part.Wheel) cap.State = PartState.Off;
        foreach (var n in part.Nodes)
        {
            Model.Forget(n);
            if (_frozenThisFrame == null) continue;
            foreach (var mi in CarModel.Descendants<MeshInstance3D>(n))
            {
                if (mi.Mesh == null) continue;
                for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                    if (mi.GetSurfaceOverrideMaterial(s) is { } m && _frozenThisFrame.TryGetValue(m, out var frozen))
                        mi.SetSurfaceOverrideMaterial(s, frozen);
            }
        }
        // A mirror on a door goes with the door; a mirror knocked off first leaves the door's group.
        foreach (var other in _parts)
        {
            if (other == part || other.State == PartState.Off) continue;
            if (other.Nodes.Count > 0 && other.Nodes.TrueForAll(part.Nodes.Contains)) { other.State = PartState.Off; continue; }
            other.Nodes.RemoveAll(part.Nodes.Contains);
        }

        Shape3D shape;
        Vector3 centre;
        Basis basis;
        if (part.Kind == PartKind.Wheel && part.Nodes[0] is Node3D wheel)
        {
            centre = wheel.GlobalPosition;
            basis = wheel.GlobalBasis * new Basis(Vector3.Back, Mathf.Pi * 0.5f);   // cylinder axis = axle
            shape = new CylinderShape3D { Radius = Spec.WheelRadius, Height = Spec.WheelWidth };
        }
        else if (part.Kind == PartKind.Hubcap && part.Nodes[0] is MeshInstance3D capMesh)
        {
            // The cap's mesh is in its wheel's space (which steers and spins), not in model space.
            var local = capMesh.GetAabb();
            centre = capMesh.GlobalTransform * local.GetCenter();
            basis = capMesh.GlobalBasis * new Basis(Vector3.Back, Mathf.Pi * 0.5f);
            shape = new CylinderShape3D { Radius = Mathf.Max(local.Size.Y * 0.5f, 0.05f), Height = Mathf.Max(local.Size.X, 0.03f) };
        }
        else
        {
            // Part meshes hold model-space vertices, so a node's global transform maps the rest box to the world
            // whether it hangs on a hinge or not.
            var node = part.Nodes[0];
            centre = node.GlobalTransform * part.Box.GetCenter();
            basis = node.GlobalBasis;
            var size = part.Box.Size;
            shape = new BoxShape3D { Size = new Vector3(Mathf.Max(size.X, 0.04f), Mathf.Max(size.Y, 0.04f), Mathf.Max(size.Z, 0.04f)) };
        }
        Vector3 v = Car.LinearVelocity + Car.AngularVelocity.Cross(centre - Car.GlobalPosition) + extraVelocity;
        Vector3 w = new Vector3(Rand() - 0.5f, Rand() - 0.5f, Rand() - 0.5f) * 8f;
        if (part.Kind is PartKind.Wheel or PartKind.Hubcap) w = Car.GlobalBasis.X * (-Car.ForwardSpeed / Mathf.Max(Spec.WheelRadius, 0.1f));
        DebrisPool.Get(this).Spawn(part.Kind.ToString(), part.Nodes, centre, basis, shape, part.Mass, v, w, Car);
        part.Pivot?.QueueFree();
        part.Pivot = null;
        var fx = FxPool.Get(this);
        fx.Sparks(centre, Vector3.Up, 0.35f);
        if (part.Kind is PartKind.Wheel or PartKind.Bumper) fx.Dust(new Vector3(centre.X, 0.1f, centre.Z), 0.5f);
    }

    /// <summary>Health thresholds per part (plan §6.2: bumpers hang then fall, the hood flaps then blows off).</summary>
    private void UpdatePartStates()
    {
        float front = Car.Health(DamageZone.Front), rear = Car.Health(DamageZone.Rear);
        foreach (var part in _parts)
        {
            if (part.State == PartState.Off) continue;
            float h = Car.Health(part.Zone);
            switch (part.Kind)
            {
                case PartKind.Bumper:
                    float loose = part.Zone == DamageZone.Front ? 0.55f : 0.45f, off = part.Zone == DamageZone.Front ? 0.3f : 0.2f;
                    if (h < off && part.State == PartState.Loose && part.LooseTime > 0.8f) Detach(part, Vector3.Down);
                    else if (h < loose) Loosen(part);
                    break;
                case PartKind.Hood:
                    if (front < 0.2f || Car.IsWrecked) Loosen(part);
                    if ((front < 0.08f || Car.IsWrecked) && part.LooseTime > 0.4f) Detach(part, Vector3.Up * 6.5f - Car.GlobalBasis.Z * 1.5f);
                    break;
                case PartKind.Trunk:
                    if (rear < 0.25f) Loosen(part);
                    if (rear < 0.1f && part.LooseTime > 1f) Detach(part, Vector3.Up * 2f);
                    break;
                case PartKind.Door:
                    bool frontRow = part.Name.Contains("_F");
                    if (h < (frontRow ? 0.35f : 0.25f)) Loosen(part);
                    if (h < (frontRow ? 0.12f : 0.05f) && part.LooseTime > 1.2f) Detach(part, ToWorldDir(new Vector3(part.Sign, 0f, 0f)) * 2f);
                    break;
                case PartKind.Mirror:
                    if (h < 0.4f) Detach(part, ToWorldDir(new Vector3(part.Zone == DamageZone.Left ? -1f : 1f, 0.5f, 0f)) * 2f);
                    break;
                case PartKind.Wheel:
                    if (Car.Damage != null && Car.Damage.IsWheelLost((Rules.Wheel)part.Wheel))
                        Detach(part, ToWorldDir(new Vector3(part.Wheel % 2 == 0 ? -2.5f : 2.5f, 1.5f, 0f)));
                    break;
                case PartKind.Hubcap:
                    if (Car.Damage != null && Car.Damage.WheelHealth((Rules.Wheel)part.Wheel) < 0.45f * DamageState.MaxHealth)
                        Detach(part, ToWorldDir(new Vector3(part.Wheel % 2 == 0 ? -2.5f : 2.5f, 1f, 0f)));
                    break;
                case PartKind.Exhaust:
                    if (h < 0.3f) Detach(part, Vector3.Down);
                    break;
            }
        }
    }

    private Vector3 ToWorldDir(Vector3 model) => Model.Root.GlobalBasis * model;

    private Vector3 _aerialAccel;
    private Vector2 _aerialSwing, _aerialSwingRate;

    /// <summary>Loose panels swing on their hinges, shaken by the car's accelerations and the wind; so does the aerial.</summary>
    private void SwingParts(float dt, Vector3 accelModel)
    {
        float speed = Car.LinearVelocity.Length();
        // The aerial whips forward under braking, leans back under power and in the wind, and out of the turns: a
        // springy rod on the frame-to-frame acceleration smoothed over a tenth of a second (frames see 0-2 physics steps).
        if (Model.Aerial is { } aerial)
        {
            float step = Mathf.Min(dt, 0.05f);
            _aerialAccel = _aerialAccel.Lerp(accelModel, 1f - Mathf.Exp(-dt / 0.1f));
            var target = new Vector2(-_aerialAccel.Z * 0.008f + speed * 0.0018f, _aerialAccel.X * 0.008f);
            _aerialSwingRate += (160f * (target - _aerialSwing) - 6f * _aerialSwingRate) * step;
            _aerialSwing = (_aerialSwing + _aerialSwingRate * step).Clamp(new Vector2(-0.5f, -0.5f), new Vector2(0.5f, 0.5f));
            aerial.Basis = new Basis(Vector3.Right, 0.14f + _aerialSwing.X) * new Basis(Vector3.Back, _aerialSwing.Y);
        }
        foreach (var part in _parts)
        {
            if (part.State != PartState.Loose || part.Pivot == null) continue;
            part.LooseTime += dt;
            float drive = part.Kind switch
            {
                PartKind.Hood => -accelModel.Y * 0.35f + accelModel.Z * 0.25f,
                PartKind.Trunk => -accelModel.Y * 0.35f - accelModel.Z * 0.2f,
                PartKind.Door => -accelModel.X * part.Sign * 0.6f + accelModel.Z * 0.2f,
                _ => -accelModel.Y * 0.4f + accelModel.X * part.Sign * 0.25f,
            };
            float flutter = speed * 0.25f * Mathf.Sin(_time * (11f + part.Name.Length) + part.Name.Length);
            float acc = 30f * (part.RestAngle - part.Angle) - 3.5f * part.Velocity + drive + flutter;
            part.Velocity += acc * dt;
            part.Angle += part.Velocity * dt;
            if (part.Angle < 0f) { part.Angle = 0f; part.Velocity *= -0.3f; }
            if (part.Angle > part.MaxAngle) { part.Angle = part.MaxAngle; part.Velocity *= -0.3f; }
            part.Pivot.Basis = new Basis(part.HingeAxis, part.Sign * part.Angle);
        }
    }

    // ------------------------------------------------------------------ per frame

    public override void _Process(double delta)
    {
        float dt = Mathf.Max((float)delta, 1e-4f);
        _time += dt;
        _frozenThisFrame = null;
        if (GetViewport().GetCamera3D() is { } camera) CarModel.SetLens(camera.Fov);
        UpdateSlide();
        UpdateWheels();
        UpdateDust(dt);
        UpdateCrush(dt);
        Vector3 accel = (Car.LinearVelocity - _lastVelocity) / dt;
        _lastVelocity = Car.LinearVelocity;
        if (accel.Length() > 200f) accel = accel.Normalized() * 200f;
        // In a replay the parts that came off are debris tracks and the roof dent is in the journal.
        if (!Car.IsReplaying) UpdatePartStates();
        SwingParts(dt, ToModelDir(accel));
        if (!Car.IsReplaying) UpdateRollover();
        UpdateEffects(dt);
        UpdateReverseLamps();
        if (_scrapeTimer > 0f && (_scrapeTimer -= dt) <= 0f) _scrape.Emitting = false;
        Model.Flush(budgeted: true);
    }

    // The dust an event leaves (post-M8 look), per second at 20 m/s: the Bowl's dirt floor, asphalt, off the road.
    private const float DustBowl = 0.0075f, DustRoad = 0.0006f, DustOffRoad = 0.02f, DustMax = 0.85f;
    private float _dust, _dustShown = -1f, _dustPush;

    /// <summary>
    /// Builds up the event's dust: slowly on asphalt, fast off the road and on the Bowl's dirt, faster in a slide
    /// or wheelspin; sent to the materials a few times a second (CarModel.SetDust). A replay shows the last of it.
    /// </summary>
    private void UpdateDust(float dt)
    {
        var grip = global::DestructionRacing.Vehicles.Car.SurfaceGripAt;
        if (!Car.IsReplaying && Car.WheelsGrounded > 0)
        {
            float rate = grip == null ? DustBowl : DustRoad;
            float scuffing = 0f;
            for (int i = 0; i < 4; i++)
            {
                if (!Car.WheelMark(i, out var p, out float skid)) continue;
                scuffing = Mathf.Max(scuffing, skid);
                if (grip != null && grip(p.X, p.Z) < 0.9f) rate = DustOffRoad;
            }
            float speed = Mathf.Min(Car.LinearVelocity.Length() / 20f, 1.5f);
            _dust = Mathf.Min(DustMax, _dust + dt * rate * speed * (1f + 1.5f * scuffing));
        }
        if ((_dustPush -= dt) > 0f || Mathf.Abs(_dust - _dustShown) < 0.004f) return;
        _dustPush = 0.25f;
        _dustShown = _dust;
        Model.SetDust(_dust, grip == null ? CarModel.BowlDust : CarModel.RoadDust);
    }

    /// <summary>DD2's slide: the visible car turns past the physics body about its centre of mass, tail out.</summary>
    private void UpdateSlide()
    {
        float d = Car.DriftAngle;
        if (d == 0f && Transform == Transform3D.Identity) return;
        var turn = new Basis(Vector3.Up, d);
        Vector3 com = Car.CenterOfMass;
        Transform = new Transform3D(turn, com - turn * com);
    }

    private void UpdateWheels()
    {
        Vector3 down = new(0f, Car.GroundY, 0f);
        for (int i = 0; i < 4; i++)
        {
            var node = _wheelNodes[i];
            if (node == null || node.GetParent() is DebrisPiece) continue;
            node.Position = Car.WheelCentre(i) - down;
            float spin = Car.WheelSpin(i);
            node.Basis = new Basis(Vector3.Up, Car.WheelSteer(i)) * new Basis(Vector3.Right, -spin);
            Model.SetBrakeSpin(i, spin);
        }
    }

    private static readonly DamageZone[] CrushZones = { DamageZone.Front, DamageZone.Rear, DamageZone.Left, DamageZone.Right };
    private static readonly float[] CrushMax = { 1f, 0.9f, 0.85f, 0.85f };

    private void UpdateCrush(float dt)
    {
        // Zone health → crush shape (a destroyed front is fully crushed: the radiator is gone, plan §6.0).
        for (int i = 0; i < 4; i++)
        {
            float target = Mathf.Clamp(1f - Car.Health(CrushZones[i]), 0f, 1f) * CrushMax[i];
            _crush[i] = Mathf.MoveToward(_crush[i], target, dt * 3f);     // the crunch lands in about a third of a second
        }
        var c = new Vector4(_crush[0], _crush[1], _crush[2], _crush[3]);
        if ((c - _crushShown).Length() > 0.002f)
        {
            _crushShown = c;
            Model.SetCrush(c.X, c.Y, c.Z, c.W);
        }
        // Crushed cars really get shorter (plan §6.2).
        if (Mathf.Abs(_crush[0] - _colliderFront) > 0.03f || Mathf.Abs(_crush[1] - _colliderRear) > 0.03f)
        {
            _colliderFront = _crush[0];
            _colliderRear = _crush[1];
            Car.SetCrushCollider(0.42f * _crush[0], 0.42f * _crush[1]);
        }
        // Heavy crush takes the lamps and the screens at that end (in a replay the journal does it, on time).
        if (Car.IsReplaying) return;
        foreach (var lamp in _lamps)
            if (!lamp.Broken && (lamp.Front ? _crush[0] : _crush[1]) > 0.5f) Break(lamp);
        foreach (var pane in _panes)
        {
            if (pane.Gone || pane.Mesh.GetParent() is DebrisPiece) continue;
            float k = pane.Zone switch { DamageZone.Front => _crush[0], DamageZone.Rear => _crush[1], DamageZone.Left => _crush[2], _ => _crush[3] };
            if (k > (pane.Zone is DamageZone.Front or DamageZone.Rear ? 0.85f : 0.7f)) Shatter(pane, -Car.GlobalBasis.Y);
        }
    }

    /// <summary>A roll crushes the roof and bursts some glass (plan §11: rollover).</summary>
    private void UpdateRollover()
    {
        float upY = Car.GlobalBasis.Y.Y;
        if (!_rolled && upY < -0.2f)
        {
            _rolled = true;
            Moment?.Invoke(this, "roll", Car.GlobalPosition);
            var b = Spec.Body;
            Model.Damage.AddDent(new Vector3(0f, b.End.Y, b.GetCenter().Z), new Vector3(0f, -0.13f, 0f), 0.75f);
            Model.Damage.AddScrape(new Vector3(0f, b.End.Y, b.GetCenter().Z), 0.8f, 0.6f);
            foreach (var pane in _panes) if (!pane.Gone && Rand() < 0.45f) Shatter(pane, Vector3.Down);
        }
        else if (_rolled && upY > 0.8f) _rolled = false;
    }

    private void UpdateEffects(float dt)
    {
        var fx = FxPool.Get(this);
        float front = Car.Health(DamageZone.Front);
        bool wrecked = Car.IsWrecked;
        if (!wrecked && _wreckSeen && Car.IsReplaying)
        {
            // The replay went back to before the wreck: the lamps come on again, and the wreck can happen again.
            _wreckSeen = false;
            _lampsDark = false;
            _fireTime = 0f;
            _wreckAge = 0f;
            UpdateLampGlow();
        }
        if (wrecked && !_wreckSeen)
        {
            _wreckSeen = true;
            _lampsDark = true;
            UpdateLampGlow();
            // DD2: a wreck burns for a while, then smoulders; longer when the engine bay was already gone. (It used to
            // burn for good below 8 % front: late in a derby a field of fires and their lights cost 3 ms a frame.)
            _fireTime = front < 0.08f ? 25f : 9f;
            fx.Sparks(ToWorld(_enginePoint), Vector3.Up, 0.6f);
            fx.Dust(ToWorld(_enginePoint), 0.6f);
        }
        if (_fireTime > 0f) _fireTime -= dt;
        if (wrecked) _wreckAge += dt;
        bool fire = (!wrecked && front < 0.08f) || _fireTime > 0f;
        Burning = fire;
        ScrapeLevel = Mathf.Max(0f, ScrapeLevel - dt * 6f);

        // The plume: steam (radiator) → grey smoke → black smoke (plan §6.1), from the engine bay.
        var pm = (ParticleProcessMaterial)_smoke.ProcessMaterial;
        Color colour;
        float ratio;
        if (wrecked)
        {
            // Black while it burns, then it smoulders: over 20 s the plume thins to half the puffs (late in a derby a
            // dozen full columns were the most expensive thing on screen, and hid the cars still running).
            float smoulder = Mathf.Snapped(Mathf.Clamp((_wreckAge - 9f) / 20f, 0f, 1f), 0.1f);
            colour = new Color(0.05f, 0.05f, 0.05f, Mathf.Lerp(0.72f, 0.62f, smoulder));
            ratio = Mathf.Lerp(1f, 0.5f, smoulder);
        }
        else if (front < 0.15f) { colour = new Color(0.05f, 0.05f, 0.05f, 0.72f); ratio = 1f; }
        else if (front < 0.35f) { colour = new Color(0.4f, 0.4f, 0.41f, 0.55f); ratio = 0.8f; }
        else { colour = new Color(0.92f, 0.92f, 0.92f, 0.32f); ratio = 0.55f; }
        bool smoking = front < 0.6f || wrecked;
        _smoke.GlobalTransform = new Transform3D(Basis.Identity, ToWorld(_enginePoint));
        if (smoking && pm.Color != colour) { pm.Color = colour; _smoke.AmountRatio = ratio; }
        if (smoking)
        {
            // At speed the puffs are spread along the trail: make them bigger so it stays one plume.
            float k = 1f + Mathf.Clamp(MoveVelocity.Length() / 12f, 0f, 1.2f);
            if (Mathf.Abs(pm.ScaleMax - 1.1f * k) > 0.05f) { pm.ScaleMin = 0.6f * k; pm.ScaleMax = 1.1f * k; }
        }
        if (_smoke.Emitting != smoking) _smoke.Emitting = smoking;

        _flames.GlobalTransform = new Transform3D(Basis.Identity, ToWorld(_enginePoint + Vector3.Down * 0.05f));
        if (_flames.Emitting != fire) _flames.Emitting = fire;
        if (fire)
        {
            // The draught of a moving car lays the flames back over the bodywork.
            var vel = MoveVelocity;
            var wind = new Vector3(-vel.X, 0f, -vel.Z) * 0.5f;
            if (wind.Length() > 9f) wind = wind.Normalized() * 9f;
            ((ParticleProcessMaterial)_flames.ProcessMaterial).Gravity = new Vector3(0f, 2.5f, 0f) + wind;
        }
        _fireLight.Visible = fire;
        if (fire)
        {
            _fireLight.GlobalPosition = ToWorld(_enginePoint + Vector3.Up * 0.45f);
            _fireLight.LightEnergy = 2.2f + 2.4f * Mathf.Abs(Mathf.Sin(_time * 17f) * Mathf.Sin(_time * 6.3f + 1f)) + Rand() * 0.6f;
        }

        // A corner on its hub grinds sparks out of the floor.
        Vector3 move = MoveVelocity;
        float speed = move.Length();
        for (int i = 0; i < 4; i++)
        {
            bool lost = Car.Damage != null && Car.Damage.IsWheelLost((Rules.Wheel)i);
            if (!lost) continue;
            var sparks = _hubSparks[i];
            if (sparks == null)
            {
                sparks = FxPool.ScrapeSparks();
                AddChild(sparks);
                _hubSparks[i] = sparks;
            }
            Vector3 hub = Car.ToGlobal(new Vector3(Car.WheelCentre(i).X, Car.GroundY + 0.08f, Car.WheelCentre(i).Z));
            bool grinding = speed > 1.5f && hub.Y < 0.35f;
            Vector3 back = speed > 0.1f ? -move.Normalized() : Vector3.Zero;
            sparks.GlobalTransform = new Transform3D(FxPool.BasisAlong((Vector3.Up + back * 0.8f).Normalized()), new Vector3(hub.X, 0.05f, hub.Z));
            sparks.AmountRatio = Mathf.Clamp(speed / 10f, 0.2f, 1f);
            if (sparks.Emitting != grinding) sparks.Emitting = grinding;
        }
    }
}
