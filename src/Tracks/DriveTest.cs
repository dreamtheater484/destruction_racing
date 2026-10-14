using System;
using System.Collections.Generic;
using DestructionRacing.Cameras;
using DestructionRacing.Core;
using DestructionRacing.UI;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Tracks;

/// <summary>
/// M1 driving test: the grey-box Bowl, the player's car, a few parked cars to bump into,
/// the camera rig, the HUD and the tuning panel. With --scenario=NAME it instead runs a scripted
/// handling test headlessly, prints the measurements and quits (accel, brake, circle, slalom, turnaround).
/// </summary>
public partial class DriveTest : Node3D
{
    private static readonly Color[] Paints =
    {
        new(0.05f, 0.25f, 0.70f), new(0.95f, 0.70f, 0.05f), new(0.10f, 0.50f, 0.20f),
        new(0.90f, 0.40f, 0.05f), new(0.55f, 0.10f, 0.55f), new(0.90f, 0.90f, 0.88f),
    };

    private Car _player = null!;

    public override void _Ready()
    {
        Controls.Apply(ControlPreset.Arrows);
        AddChild(new GreyBoxArena { StartCars = 0, WithOverviewCamera = false });

        string? scenario = DevTools.Instance?.Arg("scenario");
        if (scenario != null)
        {
            RunScenario(scenario);
            return;
        }

        var input = new PlayerInput();
        // --demo: an autopilot circles the bowl (for screenshots and video captures).
        ICarInput driver = DevTools.Instance?.Arg("demo") != null
            ? new Autopilot { Script = t => new CarControls { Throttle = t < 1f ? 0f : 0.75f, Steer = t < 2.2f ? 0f : -0.55f } }
            : input;
        _player = Spawn(new Vector3(0f, 1.0f, 26f), Vector3.Right, new Color(0.80f, 0.08f, 0.06f), 7, driver);
        _player.ApplyTuning(CarTuning.LoadOrDefault(TuningPanel.TuningPath));

        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Tau * i / 6f + 0.4f;
            var pos = new Vector3(Mathf.Cos(a) * 24f, 1.0f, Mathf.Sin(a) * 16f);
            Spawn(pos, new Vector3(Mathf.Sin(a * 3f), 0f, Mathf.Cos(a * 3f)), Paints[i], 11 + i * 7, null);
        }

        var rig = new CameraRig { Target = _player };
        AddChild(rig);
        AddChild(new DriveHud { Target = _player, ViewName = () => rig.Mode.ToString() });
        AddChild(new TuningPanel { Target = _player, PlayerControls = input });
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed(Controls.ResetCar) && !e.IsEcho() && IsInstanceValid(_player))
            _player.RightCar();
    }

    private Car Spawn(Vector3 position, Vector3 heading, Color paint, int number, ICarInput? driver)
    {
        var car = new Car
        {
            PaintColor = paint,
            RoofColor = new Color(0.95f, 0.80f, 0.10f),
            Number = number,
            Driver = driver,
            Transform = new Transform3D(Basis.LookingAt(heading.Normalized(), Vector3.Up), position),
        };
        AddChild(car);
        return car;
    }

    // ------------------------------------------------------------------ scripted handling tests

    private sealed class Autopilot : ICarInput
    {
        public Func<float, CarControls> Script = _ => CarControls.Idle;
        public float Time;
        public CarControls GetControls(Car car, float delta)
        {
            Time += delta;
            return Script(Time);
        }
    }

    private Autopilot _pilot = null!;
    private string _scenario = "";
    private readonly Dictionary<string, float> _metrics = new();
    private float _maxRollDeg;
    private float _startYaw;
    private float _accumulatedYaw;

    private void RunScenario(string name)
    {
        _scenario = name;
        _pilot = new Autopilot();
        // Outside the bowl: a long flat runway from x = -180 heading +X.
        _player = Spawn(new Vector3(-180f, 0.9f, -150f), Vector3.Right, Colors.Red, 1, _pilot);
        _pilot.Script = name switch
        {
            "accel" => _ => new CarControls { Throttle = 1f },
            "brake" => t => t < 6f ? new CarControls { Throttle = 1f } : new CarControls { Brake = 1f },
            "circle" => t => new CarControls { Throttle = t < 3f ? 1f : 0.55f, Steer = t < 3f ? 0f : 1f },
            "slalom" => t => new CarControls { Throttle = 1f, Steer = t < 3f ? 0f : (((int)((t - 3f) / 1.2f)) % 2 == 0 ? 1f : -1f) },
            "turnaround" => t => t < 0.5f ? CarControls.Idle : new CarControls { Throttle = 1f, TurnAround = true, Steer = -1f },
            "keys" => _ => CarControls.Idle,
            _ => throw new ArgumentException($"unknown scenario {name}"),
        };
        if (name == "keys")
        {
            // Drive through the real keyboard path: InputMap actions → PlayerInput filter → car.
            var keyboard = new PlayerInput();
            _pilot.Script = t =>
            {
                SetAction(Controls.Accelerate, t < 4f);
                SetAction(Controls.SteerLeft, t > 2f && t < 4f);
                SetAction(Controls.Brake, t >= 4f);
                return keyboard.GetControls(_player, 1f / Engine.PhysicsTicksPerSecond);
            };
        }
        GD.Print($"[Scenario] {name} started");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_scenario == "" || _player == null) return;
        float t = _pilot.Time;
        float speed = _player.ForwardSpeed;
        var up = _player.GlobalTransform.Basis.Y;
        float roll = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(up.Dot(Vector3.Up), -1f, 1f)));
        _maxRollDeg = Mathf.Max(_maxRollDeg, roll);
        _accumulatedYaw += _player.AngularVelocity.Y * (float)delta;

        switch (_scenario)
        {
            case "accel":
                if (Mathf.PosMod(t, 2f) < (float)delta)
                    GD.Print($"[Scenario] t={t:0.0} v={speed * 3.6f:0.0} drive={_player.DriveForceRequested:0}/{_player.DriveForceApplied:0} " +
                             $"loads={_player.WheelLoad(0):0},{_player.WheelLoad(1):0},{_player.WheelLoad(2):0},{_player.WheelLoad(3):0} slipR={_player.RearSlipDeg:0.0}");
                if (!_metrics.ContainsKey("t_0_100") && speed * 3.6f >= 100f) _metrics["t_0_100"] = t;
                if (!_metrics.ContainsKey("t_0_60") && speed * 3.6f >= 60f) _metrics["t_0_60"] = t;
                if (t >= 14f) Finish(("speed_14s_kmh", speed * 3.6f));
                break;
            case "brake":
                if (t >= 6f && !_metrics.ContainsKey("brake_start_kmh"))
                {
                    _metrics["brake_start_kmh"] = speed * 3.6f;
                    _metrics["brake_x"] = _player.GlobalPosition.X;
                }
                if (t > 6.2f && Mathf.Abs(speed) < 0.3f)
                    Finish(("stop_distance_m", _player.GlobalPosition.X - _metrics["brake_x"]), ("stop_time_s", t - 6f));
                if (t > 14f) Finish(("did_not_stop", 1f));
                break;
            case "circle":
                if (t >= 10f)
                {
                    float yawRate = Mathf.RadToDeg(_player.AngularVelocity.Y);
                    float latG = Mathf.Abs(_player.AngularVelocity.Y * _player.LinearVelocity.Length()) / 9.81f;
                    Finish(("speed_kmh", _player.SpeedKmh), ("yaw_rate_dps", yawRate), ("lateral_g", latG), ("body_slip_deg", _player.BodySlipDeg));
                }
                break;
            case "slalom":
                if (t >= 12f) Finish(("speed_kmh", _player.SpeedKmh), ("upright", up.Dot(Vector3.Up)));
                break;
            case "keys":
                if (t >= 3.9f && !_metrics.ContainsKey("speed_at_4s_kmh"))
                {
                    _metrics["speed_at_4s_kmh"] = _player.SpeedKmh;
                    _metrics["yaw_turned_deg"] = Mathf.RadToDeg(_accumulatedYaw);
                }
                if (t >= 8f) Finish(("speed_at_8s_kmh", _player.SpeedKmh), ("gear", _player.Gear));
                break;
            case "turnaround":
                if (t >= 0.5f && !_metrics.ContainsKey("yaw0")) { _metrics["yaw0"] = 1f; _accumulatedYaw = 0f; }
                if (t >= 2.5f) Finish(("yaw_turned_deg", Mathf.RadToDeg(_accumulatedYaw)), ("speed_kmh", _player.SpeedKmh));
                break;
        }
    }

    private static void SetAction(string action, bool pressed)
    {
        if (pressed) Input.ActionPress(action);
        else Input.ActionRelease(action);
    }

    private void Finish(params (string Key, float Value)[] results)
    {
        foreach (var (k, v) in results) _metrics[k] = v;
        _metrics["max_roll_deg"] = _maxRollDeg;
        var parts = new List<string>();
        foreach (var (k, v) in _metrics) if (k != "yaw0" && k != "brake_x") parts.Add($"{k}={v:0.##}");
        GD.Print($"[Scenario] {_scenario}: {string.Join("  ", parts)}");
        _scenario = "";
        GetTree().Quit();
    }
}
