using System;
using System.Collections.Generic;
using System.Linq;
using DestructionRacing.Core;
using DestructionRacing.Rules;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Tools;

/// <summary>
/// M3 damage lab in the baked Bowl. A parked target car (any model) takes real hits from ram cars driven at it,
/// so the whole chain runs: ImpactSystem → referee damage → crush, dents, scrapes, parts, glass, lamps, smoke,
/// fire, sparks and debris. Interactive (keys on screen) or scripted: --shots=out/lab [--model=brougham].
/// </summary>
public partial class DamageLab : Node3D
{
    private sealed class Ram : ICarInput
    {
        public float Throttle = 1f;
        public CarControls GetControls(Car car, float delta) => new() { Throttle = Throttle };
    }

    private sealed class Steady : ICarInput
    {
        public float Throttle, Steer;
        public CarControls GetControls(Car car, float delta) => new() { Throttle = Throttle, Steer = Steer };
    }

    private const int RamIds = 60;
    private Referee _referee = null!;
    private ImpactSystem _impacts = null!;
    private Car _target = null!;
    private readonly List<(Car Car, double Born)> _rams = new();
    private Camera3D _camera = null!;
    private Label? _help;
    private int _model, _nextRam;
    private double _time;
    private float _az = 40f, _el = 16f, _dist = 9.5f;
    private string _shots = "";

    private static readonly Color[] Paints =
    {
        new(0.75f, 0.06f, 0.04f), new(0.05f, 0.22f, 0.62f), new(0.95f, 0.70f, 0.05f), new(0.08f, 0.42f, 0.18f),
        new(0.92f, 0.38f, 0.04f), new(0.85f, 0.85f, 0.83f),
    };

    public override void _Ready()
    {
        var dev = DevTools.Instance;
        var bowl = GD.Load<PackedScene>("res://scenes/bowl/BowlLook.tscn").Instantiate<BowlLook>();
        bowl.Empty = true;
        AddChild(bowl);

        var ground = new StaticBody3D { Name = "Ground" };
        ground.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D() });
        AddChild(ground);
        // A concrete barrier to scrape along (the scrape test drives down it).
        var wall = new StaticBody3D { Name = "ScrapeWall", Position = new Vector3(21.2f, 0.6f, 4f) };
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.6f, 1.2f, 40f) } });
        wall.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.6f, 1.2f, 40f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.54f, 0.52f), Roughness = 0.9f },
        });
        AddChild(wall);

        _impacts = new ImpactSystem { Clock = () => _time };
        AddChild(_impacts);
        _camera = new Camera3D { Current = true, Fov = 45f, Near = 0.05f, Far = 2000f };
        AddChild(_camera);
        GetViewport().Msaa3D = Viewport.Msaa.Msaa4X;

        string model = dev?.Arg("model") ?? "kitsune";
        _model = Math.Max(0, Array.IndexOf(Car.Roster, model));
        NewTarget();
        _shots = dev?.Arg("shots") ?? "";
        if (_shots != "") RunScript();
        else
        {
            _help = KeyGuide.Add(this, "");
            UpdateHelp();
        }
    }

    private void UpdateHelp()
    {
        if (_help == null) return;
        string zones = string.Join("  ", new[] { DamageZone.Front, DamageZone.Rear, DamageZone.Left, DamageZone.Right }
            .Select(z => $"{z} {_target.Health(z) * 100f:0}%"));
        _help.Text = string.Join(KeyGuide.NewLine,
            $"Damage lab: {Car.Roster[_model]}    {zones}{(_target.IsWrecked ? "    WRECKED" : "")}",
            "1 front ram   2 rear ram   3 left T-bone   4 right T-bone   5 corner   (Shift = harder)",
            "6 wall scrape   7 roll it   8 wreck it   9 knock a wheel off   R repair / Tab next car   Space slow motion",
            "arrows orbit   W/S zoom   Esc quit");
    }

    /// <summary>A fresh target car (and a fresh referee: new damage for everyone).</summary>
    private void NewTarget()
    {
        foreach (var (c, _) in _rams) if (IsInstanceValid(c)) { _impacts.Unregister(c); c.QueueFree(); }
        _rams.Clear();
        if (_target != null && IsInstanceValid(_target)) { _impacts.Unregister(_target); _target.QueueFree(); }
        if (GetNodeOrNull("DebrisPool") is { } debris) debris.QueueFree();
        _referee = new Referee(RulesConfig.Standard, GameMode.DestructionDerby, Enumerable.Range(1, RamIds + 1).Select(id => (id, 1f)));
        _impacts.Referee = _referee;
        _target = new Car
        {
            Id = 1,
            Number = 7,
            ModelId = Car.Roster[_model],
            PaintColor = Paints[0],
            RoofColor = new Color(0.95f, 0.8f, 0.1f),
            Transform = new Transform3D(Basis.Identity, new Vector3(0f, 0.62f, 0f)),
        };
        AddChild(_target);
        _target.Damage = _referee.Damage(1);
        _impacts.Register(_target);
    }

    /// <summary>Drives a ram car into the target from <paramref name="from"/> (target-local), aiming at <paramref name="aim"/>.</summary>
    private void Launch(Vector3 from, Vector3 aim, float kmh)
    {
        Vector3 start = _target.ToGlobal(from);
        Vector3 goal = _target.ToGlobal(aim);
        start.Y = 0.62f;
        goal.Y = 0.62f;
        Vector3 dir = (goal - start).Normalized();
        int id = 2 + _nextRam++ % RamIds;
        var ram = new Car
        {
            Id = id,
            Number = id,
            ModelId = Car.Roster[(_nextRam + 1) % Car.Roster.Length],
            PaintColor = Paints[1 + _nextRam % (Paints.Length - 1)],
            RoofColor = new Color(0.15f, 0.4f, 0.85f),
            Driver = new Ram(),
            Transform = new Transform3D(Basis.LookingAt(dir, Vector3.Up), start),
        };
        AddChild(ram);
        ram.Damage = _referee.Damage(id);
        _impacts.Register(ram);
        ram.LinearVelocity = dir * kmh / 3.6f;
        _rams.Add((ram, _time));
    }

    private void Strike(int kind, bool hard)
    {
        // Back to the centre spot (damage kept) so every ram lands where it is aimed.
        _target.Driver = null;
        NewTargetKeepDamage(new Vector3(0f, 0.62f, 0f), Vector3.Forward);
        float v = hard ? 70f : 48f;
        var he = _target.HalfExtents;
        switch (kind)
        {
            case 1: Launch(new Vector3(0.25f, 0f, -14f), new Vector3(0f, 0f, 0f), v); break;
            case 2: Launch(new Vector3(-0.2f, 0f, 14f), new Vector3(0f, 0f, 0f), v); break;
            case 3: Launch(new Vector3(-13f, 0f, -0.3f), new Vector3(0f, 0f, -0.3f), v); break;
            case 4: Launch(new Vector3(13f, 0f, 0.2f), new Vector3(0f, 0f, 0.2f), v); break;
            case 5: Launch(new Vector3(-9f, 0f, -11f), new Vector3(-he.X * 0.8f, 0f, -he.Z * 0.85f), v); break;
        }
    }

    private void ScrapeRun()
    {
        NewTargetKeepDamage(new Vector3(19.9f, 0.62f, 22f), new Vector3(0.06f, 0f, -1f).Normalized());
        _target.Driver = new Steady { Throttle = 0.55f, Steer = 0.12f };
        _target.LinearVelocity = new Vector3(0.9f, 0f, -15f);
    }

    /// <summary>Moves the target (keeping its damage) for the scrape run.</summary>
    private void NewTargetKeepDamage(Vector3 position, Vector3 heading)
    {
        _target.GlobalTransform = new Transform3D(Basis.LookingAt(heading, Vector3.Up), position);
        _target.LinearVelocity = Vector3.Zero;
        _target.AngularVelocity = Vector3.Zero;
    }

    /// <summary>Stops the target in the middle (keeping its damage), nose to -Z.</summary>
    private void Park()
    {
        _target.Driver = null;
        NewTargetKeepDamage(new Vector3(0f, 0.62f, 0f), Vector3.Forward);
    }

    private void Roll()
    {
        _target.Driver = null;
        NewTargetKeepDamage(new Vector3(0f, 0.62f, 0f), Vector3.Forward);
        _target.ApplyCentralImpulse(Vector3.Up * _target.Mass * 4.5f);
        _target.ApplyTorqueImpulse(_target.GlobalBasis.Z * _target.Mass * 9f);
    }

    private void Wreck()
    {
        NewTargetKeepDamage(new Vector3(0f, 0.62f, 0f), Vector3.Forward);
        var split = new ZoneSplit(DamageZone.Front, DamageZone.Front, 0f);
        _target.Damage?.ApplyRaw(RulesConfig.Standard, split, 400f);
    }

    private void KnockWheel()
    {
        foreach (var w in new[] { Rules.Wheel.FrontLeft, Rules.Wheel.RearRight, Rules.Wheel.FrontRight, Rules.Wheel.RearLeft })
            if (_target.Damage is { } d && !d.IsWheelLost(w)) { d.DamageWheel(w, 500f, true); break; }
    }

    // ------------------------------------------------------------------ loop

    public override void _PhysicsProcess(double delta)
    {
        _time += delta;
        for (int i = _rams.Count - 1; i >= 0; i--)
        {
            var (car, born) = _rams[i];
            if (!IsInstanceValid(car)) { _rams.RemoveAt(i); continue; }
            // Off the gas once it has hit (or missed); gone after a few seconds.
            if (car.Driver is Ram r && (_time - born > 1.6 || car.GlobalPosition.DistanceTo(_target.GlobalPosition) < 4f)) r.Throttle = 0f;
            if (_time - born > 5.0)
            {
                _impacts.Unregister(car);
                car.QueueFree();
                _rams.RemoveAt(i);
            }
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (Input.IsKeyPressed(Key.Left)) _az -= 60f * dt;
        if (Input.IsKeyPressed(Key.Right)) _az += 60f * dt;
        if (Input.IsKeyPressed(Key.Up)) _el = Mathf.Min(_el + 30f * dt, 80f);
        if (Input.IsKeyPressed(Key.Down)) _el = Mathf.Max(_el - 30f * dt, 2f);
        if (Input.IsKeyPressed(Key.W)) _dist = Mathf.Max(_dist - 4f * dt, 3f);
        if (Input.IsKeyPressed(Key.S)) _dist += 4f * dt;
        PlaceCamera();
        UpdateHelp();
    }

    private void PlaceCamera()
    {
        if (!IsInstanceValid(_target)) return;
        Vector3 target = _target.GlobalPosition + Vector3.Up * 0.2f;
        float az = Mathf.DegToRad(_az), el = Mathf.DegToRad(_el);
        var dir = new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), -Mathf.Cos(az) * Mathf.Cos(el));
        _camera.LookAtFromPosition(target + dir * _dist, target, Vector3.Up);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        bool hard = k.ShiftPressed;
        switch (k.Keycode)
        {
            case >= Key.Key1 and <= Key.Key5: Strike((int)(k.Keycode - Key.Key1) + 1, hard); break;
            case Key.Key6: ScrapeRun(); break;
            case Key.Key7: Roll(); break;
            case Key.Key8: Wreck(); break;
            case Key.Key9: KnockWheel(); break;
            case Key.R: NewTarget(); break;
            case Key.Tab: _model = (_model + 1) % Car.Roster.Length; NewTarget(); break;
            case Key.Space: Engine.TimeScale = Engine.TimeScale < 1.0 ? 1.0 : 0.25; break;
            case Key.Escape: GetTree().Quit(); break;
        }
    }

    public override void _ExitTree() => Engine.TimeScale = 1.0;

    // ------------------------------------------------------------------ scripted run (--shots)

    private async void RunScript()
    {
        string abs = ProjectSettings.GlobalizePath(_shots.StartsWith("res://") ? _shots : "res://" + _shots);
        DirAccess.MakeDirRecursiveAbsolute(abs);
        var steps = new (double At, Action Do)[]
        {
            (0.6, () => Strike(1, false)),
            (2.4, () => Shot(abs, "1_front_ram", 35f, 14f, 8.5f)),
            (2.6, () => Strike(4, true)),
            (4.6, () => Shot(abs, "2_tbone_right", 70f, 14f, 8.5f)),
            (4.8, () => Strike(2, true)),
            (6.8, () => Shot(abs, "3_rear_ram", 205f, 15f, 8.5f)),
            (7.0, () => Strike(3, true)),
            (9.0, () => Shot(abs, "4_left_tbone", -70f, 14f, 8.5f)),
            (9.2, () => Strike(5, false)),
            (11.2, () => Shot(abs, "5_front_battered", 25f, 16f, 8f)),
            (11.3, () => Shot(abs, "5b_front_closeup", 28f, 9f, 4.6f)),
            (11.45, () => Strike(1, false)),
            (13.6, () => Shot(abs, "6_smoking", 35f, 18f, 9f)),
            (13.8, Wreck),
            (15.4, () => Shot(abs, "7_wreck_fire", 30f, 14f, 8f)),
            (15.55, () => Shot(abs, "7b_wreck_closeup", 340f, 11f, 4.8f)),
            (16.4, () => Shot(abs, "8_wreck_fire_later", 300f, 22f, 10f)),
            (16.8, KnockWheel),
            (18.4, () => Shot(abs, "9_wheel_off", 320f, 12f, 8f)),
            (18.8, ScrapeRun),
            // From ahead and inside the barrier: the sparks stream back along the wall behind the car.
            (19.7, () => Shot(abs, "10_scrape", 305f, 20f, 7.5f)),
            (21.0, Park),
            (21.6, () => Shot(abs, "11_scraped_side", 90f, 8f, 6.2f)),
            (21.8, Roll),
            (25.4, () => Shot(abs, "12_rolled", 60f, 25f, 9f)),
        };
        double start = _time;
        foreach (var (at, act) in steps)
        {
            while (_time - start < at) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            act();
        }
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        DevTools.QuitClean(GetTree(), 0);
    }

    private async void Shot(string dir, string name, float az, float el, float dist)
    {
        _az = az;
        _el = el;
        _dist = dist;
        PlaceCamera();
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        // A frame really drawn from the new camera (a minimised or hidden window draws nothing: then this waits).
        for (int i = 0; i < 2; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{dir}/{Car.Roster[_model]}_{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"[DamageLab] {path}  F{_target.Health(DamageZone.Front):0.00} R{_target.Health(DamageZone.Rear):0.00} " +
                 $"L{_target.Health(DamageZone.Left):0.00} Rt{_target.Health(DamageZone.Right):0.00}{(_target.IsWrecked ? " wrecked" : "")}");
    }
}
