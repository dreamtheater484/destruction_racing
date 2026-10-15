using System;
using System.Collections.Generic;
using System.Linq;
using DestructionRacing.Core;
using DestructionRacing.Rules;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Tools;

/// <summary>
/// The 20-car pileup stress scene (plan §16, run at every milestone). Twenty model cars start in a ring and
/// drive flat out into the middle, keep shoving for a while, a 21st car is dropped onto the pile, then
/// everyone lets go and the pile must settle. Checks Jolt stability: nothing launched or fallen through the
/// floor, no explosive speeds, no jitter once settled, no cars left inside each other; reports the cost per
/// physics tick. Headless by default; --visual runs it in the baked Bowl with all damage effects and debris
/// (use with --perf for the frame-time capture); it then shows the verdict on screen and waits (R again, Esc quit)
/// unless --quit is given. --seed=N. --twice runs it a second time in the same process (first-use costs such as
/// shader compiles show up only in the first run).
/// </summary>
public partial class PileupTest : Node3D
{
    private sealed class Push : ICarInput
    {
        public float Throttle = 1f;
        public CarControls GetControls(Car car, float delta) => new() { Throttle = Throttle };
    }

    private const double DriveUntil = 7.0, DropAt = 4.0, ReleaseAt = 7.0, End = 14.0;
    private readonly List<Car> _cars = new();
    private readonly List<Push> _drivers = new();
    private ImpactSystem _impacts = null!;
    private Referee _referee = null!;
    private Camera3D? _camera;
    private Label? _status;
    private string _verdict = "";
    private bool _visual;
    private double _time;
    private int _ticks;
    private double _tickUsec;
    private ulong _lastTick;
    private float _maxSpeed, _maxSpin, _maxHeight, _minHeight = 99f;
    private readonly List<float> _settleSpeeds = new();
    private int _hits;
    private Random _rng = new(1);

    public override void _Ready()
    {
        var dev = DevTools.Instance;
        _visual = dev?.Arg("visual") != null;
        if (int.TryParse(dev?.Arg("seed"), out int seed)) _rng = new Random(seed);
        if (_visual)
        {
            var bowl = GD.Load<PackedScene>("res://scenes/bowl/BowlLook.tscn").Instantiate<BowlLook>();
            bowl.Empty = true;
            AddChild(bowl);
            _camera = new Camera3D { Current = true, Fov = 50f, Far = 2000f };
            AddChild(_camera);
            _camera.LookAtFromPosition(new Vector3(-16f, 9f, 18f), new Vector3(0f, 0.5f, 0f), Vector3.Up);
            if (dev?.Arg("quit") == null) _status = KeyGuide.Add(this, "");
        }
        var ground = new StaticBody3D { Name = "Ground" };
        ground.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D() });
        AddChild(ground);

        _impacts = new ImpactSystem { Clock = () => _time };
        AddChild(_impacts);
        _referee = new Referee(RulesConfig.Standard, GameMode.DestructionDerby, Enumerable.Range(1, 21).Select(id => (id, 1f)));
        _impacts.Referee = _referee;
        _impacts.Impacted += _ => _hits++;
        for (int i = 0; i < 20; i++)
        {
            float a = Mathf.Tau * i / 20f + (float)_rng.NextDouble() * 0.1f;
            float r = 16f + (float)_rng.NextDouble() * 3f;
            var pos = new Vector3(r * Mathf.Cos(a), 0.62f, r * Mathf.Sin(a));
            Vector3 aim = new Vector3((float)_rng.NextDouble() * 2f - 1f, 0f, (float)_rng.NextDouble() * 2f - 1f) * 1.5f;
            var push = new Push();
            Spawn(i + 1, Car.Roster[i % Car.Roster.Length], new Transform3D(Basis.LookingAt((aim - pos with { Y = 0f }).Normalized(), Vector3.Up), pos), push);
            _drivers.Add(push);
        }
        _lastTick = Time.GetTicksUsec();
        GD.Print($"[Pileup] 20 cars, {(_visual ? "visual" : "headless")}, drop at {DropAt}s, release at {ReleaseAt}s, end {End}s");
    }

    private Car Spawn(int id, string model, Transform3D xf, ICarInput? driver)
    {
        var car = new Car
        {
            Id = id,
            Number = id,
            ModelId = model,
            BuildVisuals = _visual,
            PaintColor = Modes.DerbyMode.Liveries[id % Modes.DerbyMode.Liveries.Length],
            RoofColor = Modes.DerbyMode.RoofColor(1 + id % 4),
            Driver = driver,
            Transform = xf,
        };
        AddChild(car);
        car.Damage = _referee.Damage(id);
        _impacts.Register(car);
        _cars.Add(car);
        return car;
    }

    public override void _PhysicsProcess(double delta)
    {
        ulong now = Time.GetTicksUsec();
        if (_time > 0.5) { _tickUsec += now - _lastTick; _ticks++; }
        _lastTick = now;
        _time += delta;

        if (_time >= DropAt && _cars.Count == 20)
        {
            // A car dropped onto the pile from 3 m: stacking.
            var c = Spawn(21, "brougham", new Transform3D(new Basis(Vector3.Up, 0.7f), new Vector3(0.3f, 3.6f, -0.2f)), null);
            c.AngularVelocity = new Vector3(0.4f, 0f, 0.3f);
        }
        if (_time >= ReleaseAt) foreach (var d in _drivers) d.Throttle = 0f;

        foreach (var c in _cars)
        {
            Vector3 p = c.GlobalPosition;
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y)) { _maxSpeed = float.PositiveInfinity; continue; }
            _maxSpeed = MathF.Max(_maxSpeed, c.LinearVelocity.Length());
            _maxSpin = MathF.Max(_maxSpin, c.AngularVelocity.Length());
            if (_time > DropAt + 2.0) _maxHeight = MathF.Max(_maxHeight, p.Y);
            _minHeight = MathF.Min(_minHeight, p.Y);
            if (_time > End - 2.0) _settleSpeeds.Add(c.LinearVelocity.Length());
        }
        if (_time >= End) Report();
    }

    public override void _Process(double delta)
    {
        if (_camera == null) return;
        float a = (float)_time * 0.12f;
        _camera.LookAtFromPosition(new Vector3(-18f * Mathf.Cos(a), 8f, 18f * Mathf.Sin(a) + 4f), new Vector3(0f, 0.6f, 0f), Vector3.Up);
        if (_status == null) return;
        string phase = _time < DropAt ? "everyone drives into the middle"
            : _time < ReleaseAt ? "a car is dropped on the pile, everyone keeps pushing"
            : _time < End ? "everyone lets go: the pile must settle" : "done";
        _status.Text = string.Join(KeyGuide.NewLine,
            $"Pileup test   {Mathf.Min(_time, End):0.0} / {End:0} s   {phase}   {Engine.GetFramesPerSecond():0} fps",
            _verdict == "" ? "R run again   Esc quit" : _verdict + KeyGuide.NewLine + "R run again   Esc quit");
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_status == null || e is not InputEventKey { Pressed: true, Echo: false } k) return;
        if (k.Keycode == Key.R) GetTree().ReloadCurrentScene();
        else if (k.Keycode == Key.Escape) GetTree().Quit();
    }

    private bool _reported;
    private static int _runs;

    private void Report()
    {
        if (_reported) return;
        _reported = true;
        float settleMean = _settleSpeeds.Count > 0 ? _settleSpeeds.Average() : 0f;
        float settleMax = _settleSpeeds.Count > 0 ? _settleSpeeds.Max() : 0f;
        int overlaps = 0;
        for (int i = 0; i < _cars.Count; i++)
            for (int j = i + 1; j < _cars.Count; j++)
                if (_cars[i].GlobalPosition.DistanceTo(_cars[j].GlobalPosition) < 1.1f) overlaps++;
        double msPerTick = _ticks > 0 ? _tickUsec / _ticks / 1000.0 : 0;
        bool ok = true;
        var lines = new List<string>();
        void Check(bool pass, string text)
        {
            ok &= pass;
            GD.Print($"[Pileup] {(pass ? "OK" : "!!")} {text}");
            lines.Add($"{(pass ? "OK" : "!!")} {text}");
        }
        Check(_maxSpeed < 30f, $"max car speed {_maxSpeed:0.0} m/s (limit 30: no explosive separation)");
        Check(_maxSpin <= 14.5f, $"max spin {_maxSpin:0.0} rad/s (limit 14.5)");
        Check(_maxHeight < 4.5f, $"highest car after the drop {_maxHeight:0.00} m (limit 4.5: nothing launched)");
        Check(_minHeight > -0.5f, $"lowest car {_minHeight:0.00} m (limit -0.5: nothing through the floor)");
        Check(settleMean < 0.4f && settleMax < 2.5f, $"settled: mean {settleMean:0.00} m/s, max {settleMax:0.00} m/s in the last 2 s (limits 0.4 / 2.5: no jitter)");
        Check(overlaps == 0, $"cars inside each other at the end: {overlaps}");
        GD.Print($"[Pileup]    hits measured {_hits}, wrecked {_cars.Count(c => c.IsWrecked)}, {(_visual ? "see [Perf] for frame times" : $"cost {msPerTick:0.00} ms per physics tick (headless, all scripts)")}");
        GD.Print($"[Pileup] {(ok ? "PASS" : "FAIL")}");
        lines.Add(ok ? "PASS" : "FAIL");
        _verdict = string.Join(KeyGuide.NewLine, lines);
        if (_status != null) return;
        if (DevTools.Instance?.Arg("twice") != null && ++_runs == 1)
        {
            GD.Print("[Pileup] second run in the same process");
            GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
            return;
        }
        DevTools.QuitClean(GetTree(), ok ? 0 : 1);
    }
}
