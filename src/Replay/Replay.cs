using System;
using System.Collections.Generic;
using System.Linq;
using DestructionRacing.Cameras;
using DestructionRacing.Rules;
using DestructionRacing.UI;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Replay;

/// <summary>One car at one moment of the recording.</summary>
public struct CarFrame
{
    public Vector3 Position;
    public Quaternion Rotation;
    public float Steer;
    public float Spin0, Spin1, Spin2, Spin3;
    public float HealthF, HealthR, HealthL, HealthRt;
    public float Speed;
    public bool Wrecked;
}

/// <summary>A recorded hit, for the replay's sound and its camera cuts.</summary>
public readonly record struct ReplayImpact(double Time, Vector3 Point, float Energy, int A, int B);

/// <summary>
/// Records an event for replays (plan §15.5, basic in M4): every car's pose, wheels and zone health at 30 Hz, and
/// the hits. Damage shapes, smoke and fire are driven by the recorded health; dents and parts that came off show
/// as they ended (the full damage re-application is M8).
/// </summary>
public partial class ReplayRecorder : Node
{
    public const double Rate = 30.0;

    public IReadOnlyList<Car> Cars { get; set; } = Array.Empty<Car>();
    public Func<double> Clock { get; set; } = () => 0;
    public bool Recording { get; set; } = true;

    public readonly List<double> Times = new();
    public readonly List<CarFrame[]> Frames = new();
    public readonly List<ReplayImpact> Impacts = new();
    private double _next;

    public void OnImpact(Impact i)
    {
        if (Recording && i.Energy >= 4_000f) Impacts.Add(new ReplayImpact(Clock(), i.Point, i.Energy, i.A.Id, i.B?.Id ?? -1));
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Recording) return;
        double t = Clock();
        if (t < _next) return;
        _next = t + 1.0 / Rate;
        var frame = new CarFrame[Cars.Count];
        for (int i = 0; i < Cars.Count; i++)
        {
            var c = Cars[i];
            var xf = c.GlobalTransform;
            frame[i] = new CarFrame
            {
                Position = xf.Origin,
                Rotation = xf.Basis.GetRotationQuaternion(),
                Steer = c.SteerAngle,
                Spin0 = c.WheelSpin(0), Spin1 = c.WheelSpin(1), Spin2 = c.WheelSpin(2), Spin3 = c.WheelSpin(3),
                HealthF = c.Health(DamageZone.Front), HealthR = c.Health(DamageZone.Rear),
                HealthL = c.Health(DamageZone.Left), HealthRt = c.Health(DamageZone.Right),
                Speed = c.ForwardSpeed,
                Wrecked = c.IsWrecked,
            };
        }
        Times.Add(t);
        Frames.Add(frame);
    }

    public double Duration => Times.Count > 0 ? Times[^1] : 0;
}

/// <summary>
/// Plays a recording back on the event's own cars (frozen): TV cameras or a chase camera on the focus car, normal
/// speed, half or quarter speed (DD2's slow-mo), pause and seek. Keys: Space pause, S speed, ←/→ seek 10 s,
/// F next car, C camera, Esc/Enter back.
/// </summary>
public partial class ReplayPlayer : Node
{
    public ReplayRecorder Recording { get; set; } = null!;
    public IReadOnlyList<Car> Cars { get; set; } = Array.Empty<Car>();
    public Car? StartFocus { get; set; }
    public double StartTime { get; set; }
    public CameraRig? Rig { get; set; }
    /// <summary>The venue's TV camera posts (the Bowl's when not set).</summary>
    public IReadOnlyList<Vector3>? TvPosts { get; set; }

    /// <summary>A recorded hit was passed (for its sound).</summary>
    public event Action<ReplayImpact, Car?, Car?>? ImpactPlayed;
    public event Action? Closed;

    private static readonly float[] Speeds = { 1f, 0.5f, 0.25f };
    private TvDirector _tv = null!;
    private CanvasLayer _overlay = null!;
    private Label _status = null!;
    private double _t;
    private int _speed;
    private bool _paused, _tvMode = true;
    private int _focus;
    private int _nextImpact;
    private double _focusHeldUntil;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        foreach (var c in Cars) c.BeginReplay();
        _tv = new TvDirector { Posts = TvPosts };
        AddChild(_tv);
        _tv.Current = true;
        _focus = Math.Max(0, StartFocus != null ? Cars.ToList().IndexOf(StartFocus) : 0);
        _t = Math.Clamp(StartTime, 0, Recording.Duration);
        Seek(_t);
        _tv.Focus = Cars.Count > 0 ? Cars[_focus] : null;
        _tv.Snap();

        _overlay = new CanvasLayer { Layer = 6 };
        AddChild(_overlay);
        var root = new Control { Theme = HudStyle.Theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(root);
        var tag = HudStyle.Place(new PanelContainer(), 0f, 0f, new Vector2(36, 26), new Vector2(0, 0));
        tag.AddThemeStyleboxOverride("panel", HudStyle.Panel(HudStyle.Red));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", -6);
        tag.AddChild(box);
        box.AddChild(HudStyle.Label("● REPLAY", 46, HudStyle.Red, "ExtraBold", outline: 0));
        _status = HudStyle.Label("", 22, HudStyle.White, "SemiBold", outline: 0);
        box.AddChild(_status);
        root.AddChild(tag);
        root.AddChild(HudStyle.Place(HudStyle.Label("SPACE pause  ·  S slow-mo  ·  ←/→ −/+10 s  ·  F next car  ·  C camera  ·  ESC back", 22, HudStyle.White, "SemiBold", outline: 6),
            0f, 1f, new Vector2(36, -48), new Vector2(1400, 30)));
    }

    private void Seek(double t)
    {
        _t = Math.Clamp(t, Recording.Times.Count > 0 ? Recording.Times[0] : 0, Recording.Duration);
        _nextImpact = Recording.Impacts.FindIndex(i => i.Time >= _t);
        if (_nextImpact < 0) _nextImpact = Recording.Impacts.Count;
        Apply();
    }

    private void Apply()
    {
        var times = Recording.Times;
        if (times.Count == 0) return;
        int i = times.BinarySearch(_t);
        if (i < 0) i = ~i;
        int i1 = Math.Clamp(i, 0, times.Count - 1), i0 = Math.Max(0, i1 - 1);
        double span = times[i1] - times[i0];
        float k = span > 1e-6 ? (float)((_t - times[i0]) / span) : 1f;
        var f0 = Recording.Frames[i0];
        var f1 = Recording.Frames[i1];
        for (int c = 0; c < Cars.Count && c < f0.Length; c++)
        {
            var a = f0[c];
            var b = f1[c];
            var car = Cars[c];
            car.GlobalTransform = new Transform3D(new Basis(a.Rotation.Slerp(b.Rotation, k)), a.Position.Lerp(b.Position, k));
            car.ReplaySteer = Mathf.Lerp(a.Steer, b.Steer, k);
            car.ReplaySpin[0] = Mathf.Lerp(a.Spin0, b.Spin0, k);
            car.ReplaySpin[1] = Mathf.Lerp(a.Spin1, b.Spin1, k);
            car.ReplaySpin[2] = Mathf.Lerp(a.Spin2, b.Spin2, k);
            car.ReplaySpin[3] = Mathf.Lerp(a.Spin3, b.Spin3, k);
            car.ReplayHealth[(int)DamageZone.Front] = Mathf.Lerp(a.HealthF, b.HealthF, k);
            car.ReplayHealth[(int)DamageZone.Rear] = Mathf.Lerp(a.HealthR, b.HealthR, k);
            car.ReplayHealth[(int)DamageZone.Left] = Mathf.Lerp(a.HealthL, b.HealthL, k);
            car.ReplayHealth[(int)DamageZone.Right] = Mathf.Lerp(a.HealthRt, b.HealthRt, k);
            car.ReplaySpeed = Mathf.Lerp(a.Speed, b.Speed, k);
            car.ReplayWrecked = k < 0.5f ? a.Wrecked : b.Wrecked;
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsEcho() || e is not InputEventKey { Pressed: true } key) return;
        switch (key.PhysicalKeycode)
        {
            case Key.Space: _paused = !_paused; break;
            case Key.S: _speed = (_speed + 1) % Speeds.Length; break;
            case Key.Left: Seek(_t - 10); break;
            case Key.Right: Seek(_t + 10); break;
            case Key.F:
                _focus = (_focus + 1) % Math.Max(1, Cars.Count);
                _focusHeldUntil = _t + 1e9;   // the viewer chose: no automatic focus any more
                SetFocus(Cars[_focus], snap: true);
                break;
            case Key.C:
                _tvMode = !_tvMode;
                if (_tvMode) { _tv.Current = true; _tv.Snap(); }
                else if (Rig != null) { Rig.Target = Cars[_focus]; Rig.Current = true; }
                break;
            case Key.Escape or Key.Enter or Key.KpEnter: Close(); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    private void SetFocus(Car car, bool snap)
    {
        _tv.Focus = car;
        if (Rig != null) Rig.Target = car;
        if (snap) _tv.Snap();
    }

    private void Close()
    {
        Engine.TimeScale = 1.0;
        Closed?.Invoke();
        QueueFree();
    }

    public override void _Process(double delta)
    {
        double rdt = delta / Math.Max(Engine.TimeScale, 0.01);
        if (!_paused) _t += rdt * Speeds[_speed];
        if (_t >= Recording.Duration)
        {
            _t = Recording.Duration;
            _paused = true;
        }
        // Hits passed: their sound, and the camera may follow the action (a big hit near the focus car).
        while (_nextImpact < Recording.Impacts.Count && Recording.Impacts[_nextImpact].Time <= _t)
        {
            var hit = Recording.Impacts[_nextImpact++];
            var a = Cars.FirstOrDefault(c => c.Id == hit.A);
            var b = hit.B >= 0 ? Cars.FirstOrDefault(c => c.Id == hit.B) : null;
            ImpactPlayed?.Invoke(hit, a, b);
            if (hit.Energy >= 50_000f && _t >= _focusHeldUntil && a != null)
            {
                var pick = b != null && b == StartFocus ? b : a;
                _focus = Cars.ToList().IndexOf(pick);
                _focusHeldUntil = _t + 3.0;
                SetFocus(pick, snap: false);
            }
        }
        Apply();
        var ts = TimeSpan.FromSeconds(_t);
        string speed = Speeds[_speed] < 1f ? $"   ×{Speeds[_speed]:0.##}" : "";
        _status.Text = $"{(int)ts.TotalMinutes}:{ts.Seconds:00}  /  {(int)(Recording.Duration / 60)}:{(int)Recording.Duration % 60:00}{speed}{(_paused ? "   PAUSED" : "")}   ·   #{Cars[_focus].Number}";
    }
}
