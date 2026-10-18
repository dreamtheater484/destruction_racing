using System;
using System.Collections.Generic;
using System.Linq;
using DestructionRacing.Cameras;
using DestructionRacing.Core;
using DestructionRacing.Fx;
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
    /// <summary>How many damage changes (dents and wear, glass and lamps) the car's journals held by then.</summary>
    public int DamageOps, GlassOps;
}

/// <summary>A recorded hit, for the replay's sound and its camera cuts.</summary>
public readonly record struct ReplayImpact(double Time, Vector3 Point, float Energy, int A, int B);

/// <summary>A candidate for the highlights reel: a big hit, a wreck, a rollover or a part flying off.</summary>
public readonly record struct ReplayMoment(double Time, float Score, int Car, int Other, string What);

/// <summary>
/// A part that came off (a debris piece): where it sat on its car, then its flight, sampled while it moved, and
/// where it came to rest.
/// </summary>
public sealed class DebrisTrack
{
    public DebrisPiece Piece { get; init; } = null!;
    public Car? From { get; init; }
    /// <summary>The piece's transform relative to its car when it came off.</summary>
    public Transform3D OnCar { get; init; }
    public double Born { get; init; }
    public readonly List<double> Times = new();
    public readonly List<Transform3D> Poses = new();
    /// <summary>It came to rest (or was removed): no more samples.</summary>
    public bool Resting { get; set; }

    /// <summary>Where the piece was at <paramref name="t"/>, on or after <see cref="Born"/>.</summary>
    public Transform3D PoseAt(double t)
    {
        int i = Times.BinarySearch(t);
        if (i >= 0) return Poses[i];
        i = ~i;
        if (i <= 0) return Poses[0];
        if (i >= Times.Count) return Poses[^1];
        double span = Times[i] - Times[i - 1];
        float k = span > 1e-6 ? (float)((t - Times[i - 1]) / span) : 1f;
        var a = Poses[i - 1];
        var b = Poses[i];
        return new Transform3D(new Basis(a.Basis.GetRotationQuaternion().Slerp(b.Basis.GetRotationQuaternion(), k)), a.Origin.Lerp(b.Origin, k));
    }
}

/// <summary>
/// Records an event for replays (plan §15.5): every car's pose, wheels and zone health at 30 Hz, the hits, how far
/// each car's damage journals had got (so dents, scrapes, glass and lamps replay at the moment they happened), a
/// track for every part that came off, and the big moments for the highlights reel.
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
    public readonly List<DebrisTrack> Debris = new();
    public readonly List<ReplayMoment> Moments = new();
    private double _next;
    private bool[] _wasWrecked = Array.Empty<bool>();

    public override void _Ready()
    {
        foreach (var c in Cars) c.Visual?.StartJournal();
        _wasWrecked = new bool[Cars.Count];
        DebrisPool.Spawned += OnDebris;
        CarVisual.Moment += OnMoment;
    }

    public override void _ExitTree()
    {
        DebrisPool.Spawned -= OnDebris;
        CarVisual.Moment -= OnMoment;
    }

    public void OnImpact(Impact i)
    {
        if (!Recording || i.Energy < 4_000f) return;
        double t = Clock();
        Impacts.Add(new ReplayImpact(t, i.Point, i.Energy, i.A.Id, i.B?.Id ?? -1));
        if (i.Energy >= 40_000f) Moments.Add(new ReplayMoment(t, i.Energy / 1000f, i.A.Id, i.B?.Id ?? -1, "hit"));
    }

    private void OnDebris(DebrisPiece piece)
    {
        if (!Recording || !IsInstanceValid(piece)) return;
        var car = piece.From as Car;
        if (car == null || !Cars.Contains(car)) return;
        double t = Clock();
        var track = new DebrisTrack
        {
            Piece = piece,
            From = car,
            OnCar = car.GlobalTransform.AffineInverse() * piece.GlobalTransform,
            Born = t,
        };
        track.Times.Add(t);
        track.Poses.Add(piece.GlobalTransform);
        Debris.Add(track);
    }

    private void OnMoment(CarVisual visual, string what, Vector3 where)
    {
        if (!Recording || !Cars.Contains(visual.Car)) return;
        float score = what switch
        {
            "roll" => 150f,
            "wheel" => 90f,
            "hood" => 70f,
            "door" or "trunk" or "bumper" => 35f,
            _ => 0f,
        };
        if (score > 0f) Moments.Add(new ReplayMoment(Clock(), score, visual.Car.Id, -1, what));
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
                DamageOps = c.Visual?.DamageOps ?? 0,
                GlassOps = c.Visual?.GlassOps ?? 0,
            };
            if (c.IsWrecked && !_wasWrecked[i]) Moments.Add(new ReplayMoment(t, 200f, c.Id, -1, "wreck"));
            _wasWrecked[i] = c.IsWrecked;
        }
        Times.Add(t);
        Frames.Add(frame);
        foreach (var d in Debris)
        {
            if (d.Resting) continue;
            if (!IsInstanceValid(d.Piece)) { d.Resting = true; continue; }
            d.Times.Add(t);
            d.Poses.Add(d.Piece.GlobalTransform);
            if (d.Piece.Freeze) d.Resting = true;
        }
    }

    public double Duration => Times.Count > 0 ? Times[^1] : 0;
}

/// <summary>
/// Plays a recording back on the event's own cars (frozen): TV cameras, a chase camera or a free camera on the
/// focus car, at normal speed, half or quarter speed (DD2's slow-mo), with pause and seek. The cars' damage replays
/// as it happened, and the parts that came off fly again. <see cref="Highlights"/> plays the three biggest moments
/// in slow motion instead (plan §15.5, the highlights reel).
/// Keys: Space pause, S speed, ←/→ or ,/. seek 10 s, F next car, C camera, Esc/Enter back; in the free camera the
/// arrows turn it and +/− zoom; in the highlights → skips to the next one.
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
    /// <summary>The highlights reel: the top three moments in slow motion, then back.</summary>
    public bool Highlights { get; set; }

    /// <summary>A recorded hit was passed (for its sound).</summary>
    public event Action<ReplayImpact, Car?, Car?>? ImpactPlayed;
    public event Action? Closed;

    private enum View { Tv, Chase, Free }

    private static readonly float[] Speeds = { 1f, 0.5f, 0.25f };
    private const int HighlightSpeed = 1;   // half speed
    private TvDirector _tv = null!;
    private Camera3D _free = null!;
    private CanvasLayer _overlay = null!;
    private Label _status = null!, _hint = null!, _banner = null!;
    private double _t;
    private int _speed;
    private bool _paused;
    private View _view = View.Tv;
    private float _freeYaw = 0.6f, _freePitch = 0.3f, _freeDist = 8f;
    private int _focus;
    private int _nextImpact;
    private double _focusHeldUntil;
    private readonly List<(double Start, double End, int Car, string Title)> _reel = new();
    private int _reelIndex = -1;
    private readonly Dictionary<DebrisPiece, bool> _pieceWasFrozen = new();

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        foreach (var c in Cars) c.BeginReplay();
        // Debris follows its track: frozen (static pieces stay static, moving ones become kinematic).
        foreach (var d in Recording.Debris)
        {
            if (!IsInstanceValid(d.Piece) || _pieceWasFrozen.ContainsKey(d.Piece)) continue;
            _pieceWasFrozen[d.Piece] = d.Piece.Freeze;
            if (!d.Piece.Freeze) d.Piece.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
            d.Piece.Freeze = true;
        }
        _tv = new TvDirector { Posts = TvPosts };
        AddChild(_tv);
        _free = new Camera3D { Name = "FreeCamera", Fov = 60f, Far = 900f };
        AddChild(_free);
        _focus = Math.Max(0, StartFocus != null ? Cars.ToList().IndexOf(StartFocus) : 0);
        if (DevTools.Instance?.Arg("replaycheck") != null) SelfTest();
        if (Highlights) BuildReel();
        if (_reel.Count == 0) Highlights = false;
        BuildOverlay();
        if (Highlights) StartHighlight(0);
        else
        {
            Highlights = false;
            Seek(Math.Clamp(StartTime, 0, Recording.Duration));
            SetView(View.Tv);
        }
        _tv.Focus = Cars.Count > 0 ? Cars[_focus] : null;
        _tv.Snap();
    }

    private void BuildOverlay()
    {
        _overlay = new CanvasLayer { Layer = 8 };
        AddChild(_overlay);
        var root = new Control { Theme = HudStyle.Theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(root);
        var tag = HudStyle.Place(new PanelContainer(), 0f, 0f, new Vector2(36, 26), new Vector2(0, 0));
        tag.AddThemeStyleboxOverride("panel", HudStyle.Panel(HudStyle.Red));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", -6);
        tag.AddChild(box);
        box.AddChild(HudStyle.Label(Highlights ? "● HIGHLIGHTS" : "● REPLAY", 46, HudStyle.Red, "ExtraBold", outline: 0));
        _status = HudStyle.Label("", 22, HudStyle.White, "SemiBold", outline: 0);
        box.AddChild(_status);
        root.AddChild(tag);
        _banner = HudStyle.Label("", 40, HudStyle.Yellow, "ExtraBold", outline: 8);
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(HudStyle.Place(_banner, 0.5f, 0f, new Vector2(-700, 40), new Vector2(1400, 56)));
        _hint = HudStyle.Label("", 22, HudStyle.White, "SemiBold", outline: 6);
        root.AddChild(HudStyle.Place(_hint, 0f, 1f, new Vector2(36, -48), new Vector2(1700, 30)));
    }

    /// <summary>
    /// --replaycheck: the damage as the event left it, then rewound to the start and rebuilt from the journals,
    /// must be the same, bit for bit (and the start must be undamaged).
    /// </summary>
    private void SelfTest()
    {
        var before = Cars.Select(c => c.Visual?.DamageSignature() ?? "").ToList();
        Seek(0);
        var start = Cars.Select(c => c.Visual?.DamageSignature() ?? "").ToList();
        Seek(Recording.Duration);
        ShowFinalDamage();
        var after = Cars.Select(c => c.Visual?.DamageSignature() ?? "").ToList();
        int same = before.Zip(after).Count(x => x.First == x.Second);
        int changed = before.Zip(start).Count(x => x.First != x.Second);
        int ops = Cars.Sum(c => c.Visual?.DamageOps ?? 0), glass = Cars.Sum(c => c.Visual?.GlassOps ?? 0);
        GD.Print($"[Replay] self-test: {same}/{Cars.Count} cars rebuilt bit for bit, {changed} looked different at the start, " +
                 $"{ops} volume changes, {glass} glass changes, {Recording.Debris.Count} debris tracks, {Recording.Moments.Count} moments");
    }

    // ------------------------------------------------------------------ the highlights reel

    /// <summary>
    /// The three biggest moments: hits, wrecks, rollovers and parts flying off within 3 s count together, the
    /// player's own count half as much again, and two highlights are at least 10 s apart.
    /// </summary>
    private void BuildReel()
    {
        int player = StartFocus?.Id ?? -1;
        var moments = Recording.Moments.OrderBy(m => m.Time).ToList();
        var clusters = new List<(double At, float Score, float Best, int Car, int Other, string What)>();
        foreach (var m in moments)
        {
            float score = m.Score * (m.Car == player || m.Other == player ? 1.5f : 1f);
            if (clusters.Count > 0 && m.Time - clusters[^1].At < 3.0)
            {
                var c = clusters[^1];
                clusters[^1] = score > c.Best
                    ? (m.Time, c.Score + score, score, m.Car, m.Other, m.What)
                    : (c.At, c.Score + score, c.Best, c.Car, c.Other, c.What);
            }
            else clusters.Add((m.Time, score, score, m.Car, m.Other, m.What));
        }
        var picked = new List<(double At, float Score, int Car, int Other, string What)>();
        foreach (var c in clusters.OrderByDescending(c => c.Score))
        {
            if (picked.Count == 3) break;
            if (picked.Any(p => Math.Abs(p.At - c.At) < 10.0)) continue;
            picked.Add((c.At, c.Score, c.Car, c.Other, c.What));
        }
        foreach (var p in picked.OrderBy(p => p.At))
        {
            var car = Cars.FirstOrDefault(c => c.Id == p.Car);
            var other = p.Other >= 0 ? Cars.FirstOrDefault(c => c.Id == p.Other) : null;
            // Follow the player if they were in it, else the car that did it.
            var focus = other == StartFocus && other != null ? other : car;
            string title = p.What switch
            {
                "hit" when p.Other < 0 => "INTO THE WALL",
                "hit" when p.Score >= 150f => "HUGE HIT",
                "hit" => "BIG HIT",
                "wreck" => "WRECKED",
                "roll" => "ROLLOVER",
                "wheel" => "A WHEEL OFF",
                "hood" => "THE HOOD BLOWS OFF",
                _ => "PARTS FLYING",
            };
            string who = car == null ? "" : other != null ? $"  ·  #{car.Number} AND #{other.Number}" : $"  ·  #{car.Number}";
            _reel.Add((Math.Max(0.0, p.At - 3.0), Math.Min(Recording.Duration, p.At + 2.5),
                focus != null ? Cars.ToList().IndexOf(focus) : _focus, title + who));
        }
    }

    private void StartHighlight(int index)
    {
        if (index >= _reel.Count) { Close(); return; }
        _reelIndex = index;
        var h = _reel[index];
        _speed = HighlightSpeed;
        _paused = false;
        Seek(h.Start);
        if (h.Car >= 0 && h.Car < Cars.Count)
        {
            _focus = h.Car;
            _focusHeldUntil = h.End;
            SetFocus(Cars[_focus], snap: true);
        }
        _banner.Text = $"HIGHLIGHT {index + 1} OF {_reel.Count}   ·   {h.Title}";
        SetView(View.Tv);
    }

    // ------------------------------------------------------------------ playback

    private void Seek(double t)
    {
        _t = Math.Clamp(t, Recording.Times.Count > 0 ? Recording.Times[0] : 0, Recording.Duration);
        _nextImpact = Recording.Impacts.FindIndex(i => i.Time >= _t);
        if (_nextImpact < 0) _nextImpact = Recording.Impacts.Count;
        Apply(effects: false);
    }

    private void Apply(bool effects)
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
        // The damage journals as far as the last frame at or before now.
        var fd = times[i1] <= _t ? f1 : f0;
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
            car.Visual?.ReplayDamageTo(fd[c].DamageOps, fd[c].GlassOps, effects);
        }
        // Parts that came off: on their car before, then their flight, then where they came to rest.
        foreach (var d in Recording.Debris)
        {
            if (!IsInstanceValid(d.Piece)) continue;
            if (_t < d.Born)
            {
                if (d.From != null && IsInstanceValid(d.From)) d.Piece.GlobalTransform = d.From.GlobalTransform * d.OnCar;
            }
            else d.Piece.GlobalTransform = d.PoseAt(_t);
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsEcho() || e is not InputEventKey { Pressed: true } key) return;
        bool free = _view == View.Free;
        switch (key.PhysicalKeycode)
        {
            case Key.Space: _paused = !_paused; break;
            case Key.S when !Highlights: _speed = (_speed + 1) % Speeds.Length; break;
            case Key.Right or Key.Period when Highlights: StartHighlight(_reelIndex + 1); break;
            case Key.Left or Key.Comma when Highlights: StartHighlight(Math.Max(0, _reelIndex)); break;
            case Key.Left when !free:
            case Key.Comma:
                Seek(_t - 10);
                break;
            case Key.Right when !free:
            case Key.Period:
                Seek(_t + 10);
                break;
            case Key.F:
                _focus = (_focus + 1) % Math.Max(1, Cars.Count);
                _focusHeldUntil = _t + 1e9;   // the viewer chose: no automatic focus any more
                SetFocus(Cars[_focus], snap: true);
                break;
            case Key.C: SetView(_view == View.Tv ? View.Chase : _view == View.Chase ? View.Free : View.Tv); break;
            case Key.Escape or Key.Enter or Key.KpEnter: Close(); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    private void SetView(View view)
    {
        if (view == View.Chase && Rig == null) view = View.Free;
        _view = view;
        switch (view)
        {
            case View.Tv:
                _tv.Current = true;
                _tv.Snap();
                break;
            case View.Chase:
                Rig!.Target = Cars[_focus];
                Rig.Current = true;
                break;
            case View.Free:
                _free.Current = true;
                break;
        }
        _hint.Text = Highlights
            ? "SPACE pause  ·  → next highlight  ·  F next car  ·  C camera  ·  ESC back"
            : view == View.Free
                ? "←/→ ↑/↓ turn the camera  ·  +/− zoom  ·  ,/. −/+10 s  ·  SPACE pause  ·  S slow-mo  ·  F next car  ·  C camera  ·  ESC back"
                : "SPACE pause  ·  S slow-mo  ·  ←/→ −/+10 s  ·  F next car  ·  C camera  ·  ESC back";
    }

    private void SetFocus(Car car, bool snap)
    {
        _tv.Focus = car;
        if (Rig != null) Rig.Target = car;
        if (snap) _tv.Snap();
    }

    private void Close()
    {
        // Leave the scene as the event ended: every car's final pose and damage, the debris where it lies.
        Seek(Recording.Duration);
        ShowFinalDamage();
        foreach (var (piece, wasFrozen) in _pieceWasFrozen)
            if (IsInstanceValid(piece) && !wasFrozen) piece.Freeze = false;
        Engine.TimeScale = 1.0;
        Closed?.Invoke();
        QueueFree();
    }

    /// <summary>
    /// Every car's damage as it is now. The recording stops at the flag but the cars may scrape on in the cool-down
    /// lap, so the journals can run past the last frame.
    /// </summary>
    private void ShowFinalDamage()
    {
        foreach (var c in Cars) c.Visual?.ReplayDamageTo(int.MaxValue, int.MaxValue, effects: false);
    }

    private void UpdateFreeCamera(float dt)
    {
        if (Cars.Count == 0) return;
        if (Input.IsPhysicalKeyPressed(Key.Left)) _freeYaw += 1.8f * dt;
        if (Input.IsPhysicalKeyPressed(Key.Right)) _freeYaw -= 1.8f * dt;
        if (Input.IsPhysicalKeyPressed(Key.Up)) _freePitch = Mathf.Min(1.35f, _freePitch + 1.1f * dt);
        if (Input.IsPhysicalKeyPressed(Key.Down)) _freePitch = Mathf.Max(-0.05f, _freePitch - 1.1f * dt);
        if (Input.IsPhysicalKeyPressed(Key.Equal) || Input.IsPhysicalKeyPressed(Key.KpAdd) || Input.IsPhysicalKeyPressed(Key.Pageup))
            _freeDist = Mathf.Max(3f, _freeDist - 10f * dt);
        if (Input.IsPhysicalKeyPressed(Key.Minus) || Input.IsPhysicalKeyPressed(Key.KpSubtract) || Input.IsPhysicalKeyPressed(Key.Pagedown))
            _freeDist = Mathf.Min(45f, _freeDist + 10f * dt);
        var car = Cars[_focus];
        // The angle is the car's: the camera stays where you put it as the car turns.
        float heading = Mathf.Atan2(car.GlobalBasis.Z.X, car.GlobalBasis.Z.Z);
        float yaw = heading + _freeYaw;
        var target = car.GlobalPosition + Vector3.Up * 0.7f;
        var eye = target + new Vector3(Mathf.Sin(yaw) * Mathf.Cos(_freePitch), Mathf.Sin(_freePitch), Mathf.Cos(yaw) * Mathf.Cos(_freePitch)) * _freeDist;
        eye.Y = Mathf.Max(eye.Y, 0.35f);
        _free.LookAtFromPosition(eye, target, Vector3.Up);
        Fx.Focus.On(_free, target, 0.05f);
    }

    public override void _Process(double delta)
    {
        double rdt = delta / Math.Max(Engine.TimeScale, 0.01);
        double before = _t;
        if (!_paused) _t += rdt * Speeds[_speed];
        if (Highlights && _reelIndex >= 0 && _t >= _reel[_reelIndex].End)
        {
            StartHighlight(_reelIndex + 1);
            if (!IsInsideTree() || IsQueuedForDeletion()) return;
            before = _t;
        }
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
            if (hit.Energy >= 4_000f) FxPool.Get(this).Sparks(hit.Point, Vector3.Up, Mathf.Clamp(hit.Energy / 60_000f, 0.2f, 1f));
            if (hit.Energy >= 50_000f && _t >= _focusHeldUntil && a != null)
            {
                var pick = b != null && b == StartFocus ? b : a;
                _focus = Cars.ToList().IndexOf(pick);
                _focusHeldUntil = _t + 3.0;
                SetFocus(pick, snap: false);
            }
        }
        // Moving forward at play speed bursts the glass that breaks on the way; a seek just shows the result.
        Apply(effects: _t > before && _t - before < 0.5);
        if (_view == View.Free) UpdateFreeCamera((float)rdt);
        if (Cars.Count == 0) return;
        var ts = TimeSpan.FromSeconds(_t);
        string speed = Speeds[_speed] < 1f ? $"   ×{Speeds[_speed]:0.##}" : "";
        string view = _view switch { View.Tv => "TV", View.Chase => "CHASE", _ => "FREE CAMERA" };
        _status.Text = $"{(int)ts.TotalMinutes}:{ts.Seconds:00}  /  {(int)(Recording.Duration / 60)}:{(int)Recording.Duration % 60:00}{speed}{(_paused ? "   PAUSED" : "")}   ·   #{Cars[_focus].Number}   ·   {view}";
    }
}
