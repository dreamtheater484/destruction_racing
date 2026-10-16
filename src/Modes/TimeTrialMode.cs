using System;
using System.Collections.Generic;
using DestructionRacing.AI;
using DestructionRacing.Cameras;
using DestructionRacing.Core;
using DestructionRacing.Tracks;
using DestructionRacing.UI;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Modes;

/// <summary>
/// Time Trial (plan §4.4): one car alone at Crossroads Speedway, flying laps against the clock. The stopwatch runs
/// at the bottom of the screen (DD1), with three sector splits against your best lap, the ghost of that lap, medal
/// targets and a local leaderboard per layout, direction and car. R restarts at once. No damage (it is about the
/// line, not the walls). Headless with the PlayerProxy it measures the medal targets (<see cref="TimeTrialSim"/>).
/// </summary>
public partial class TimeTrialMode : Node3D, IRaceView, Audio.IEventSound
{
    public SpeedwayLayout LayoutId { get; set; } = SpeedwayLayout.Figure8;
    public bool Reverse { get; set; }
    public string CarId { get; set; } = "kitsune";
    public BowlPreset Preset { get; set; } = BowlPreset.Afternoon;
    public Livery? PlayerLivery { get; set; }
    public bool Headless { get; set; }
    public bool ProxyPlayer { get; set; }
    public Personality ProxyProfile { get; set; } = Personality.AverageFan;
    public int Seed { get; set; } = 1;
    /// <summary>Sims: stop after this many timed laps (0 = never).</summary>
    public int MaxLaps { get; set; }
    /// <summary>Sims: stop after this long anyway.</summary>
    public double MaxDuration { get; set; } = 900;
    /// <summary>Keep records and ghosts in user:// (off in sims).</summary>
    public bool Persist { get; set; } = true;

    public event Action<TimeTrialMode>? Finished;

    /// <summary>The car starts this far before the line: every timed lap is a flying lap.</summary>
    private const float StartBack = 40f;

    private Speedway _venue = null!;
    private RaceLayout _layout = null!;
    private Car _car = null!;
    private Car? _ghostCar;
    private CameraRig? _rig;
    private TimeTrialHud? _hud;
    private TimeTrialRecords _records = null!;
    private string _key = "";
    private readonly List<Car> _carList = new();

    private float _s, _lat, _progress;
    private double _clock;
    private int _lineK;
    private double? _lapStart;
    private int _sectorsDone;
    private readonly double[] _splits = new double[2];
    private bool _invalid;
    private bool _restartPending;
    private Ghost _recording = new();
    private float _nextSample;
    private bool _ghostStyled;

    // ------------------------------------------------------------------ state for the HUD and the sims

    public RaceLayout Layout => _layout;
    public string CarName => CarCatalog.Find(CarId)?.FullName ?? CarId;
    public string Key => _key;
    public TimeTrialRecords Records => _records;
    public TimeTrialTargets.Medals Medals { get; private set; }
    public Ghost? BestGhost { get; private set; }
    public bool GhostVisible { get; set; } = true;
    public double? CurrentLapTime => _lapStart is double t0 ? _clock - t0 : null;
    public double? LastLap { get; private set; }
    public double? BestLap { get; private set; }
    /// <summary>The best lap's splits (end of sectors 1 and 2), for the deltas.</summary>
    public double[]? BestSplits { get; private set; }
    /// <summary>This lap's splits so far (NaN = not reached yet).</summary>
    public double[] Splits => _splits;
    public int SectorsDone => _sectorsDone;
    public bool LapInvalid => _invalid;
    public int LapsTimed { get; private set; }
    /// <summary>Every lap timed this session (sims).</summary>
    public List<double> LapTimes { get; } = new();
    public Car Car => _car;

    /// <summary>A lap was completed: its time, whether it counted, its board place (0 = none) and medal.</summary>
    public event Action<double, bool, int, int>? LapCompleted;
    /// <summary>A sector split (1 or 2) against the best lap's (NaN = no best yet).</summary>
    public event Action<int, double>? SectorSplit;

    // ------------------------------------------------------------------ IEventSound (engine, tyres, wall hits)

    public event Action<Impact>? ImpactHappened;
    public event Action<Rules.RefereeEvent>? RefereeEventRaised;
    public event Action<int>? CountdownTick;
    public event Action? Started;
    public event Action<int>? IntensityChanged;
    public event Action? Ended;
    public Car PlayerCar => _car;
    public bool PlayerOut => false;
    public CameraRig? Rig => _rig;
    public EventHud? Hud => null;
    public float CrowdExcitement => _venue.Look?.Crowd?.Excitement ?? 0.15f;

    // ------------------------------------------------------------------ IRaceView (for the PlayerProxy)

    public IReadOnlyList<Car> Cars => _carList;
    public bool IsRunning(Car car) => car == _car;
    public TrackPath Track => _layout.Path;
    public double Time => _clock;
    public Car? Player => _car;
    public int? LeaderId => null;
    public (float S, float Lateral) TrackPos(Car car) => (_s, _lat);
    public IReadOnlyList<float> CrossoverPasses => _layout.CrossoverPasses;
    public float CatchUp(Car car) => 1f;

    // ------------------------------------------------------------------ set-up

    public override void _Ready()
    {
        if (GetParent() == GetTree().Root && RaceSetup.Chosen is { Event: SpeedwayEvent.TimeTrial } chosen)
        {
            LayoutId = chosen.Layout;
            Reverse = chosen.Reverse;
            CarId = chosen.Car;
            PlayerLivery = chosen.Livery;
            Preset = chosen.Preset;
        }
        if (GetParent() == GetTree().Root && DevTools.Instance is { } dev)
        {
            if (dev.Arg("layout") is { } layout) LayoutId = layout.ToLowerInvariant() == "oval" ? SpeedwayLayout.Oval : SpeedwayLayout.Figure8;
            if (dev.Arg("reverse") != null) Reverse = true;
            if (dev.Arg("car") is { } car) CarId = car;
            if (dev.Arg("preset") is { } preset) Preset = Speedway.ParsePreset(preset);
            if (dev.Arg("proxy") != null) ProxyPlayer = true;
            if (int.TryParse(dev.Arg("seed"), out int seed)) Seed = seed;
        }
        if (CarCatalog.Find(CarId) == null) CarId = "kitsune";
        if (!Headless) Controls.Apply(Controls.Current);

        _venue = new Speedway { Name = "Speedway", LayoutId = LayoutId, Reverse = Reverse, Visuals = !Headless, Preset = Preset };
        AddChild(_venue);
        _layout = _venue.Layout;
        _key = TimeTrialRecords.Key(LayoutId, Reverse, CarId);
        _records = Persist ? TimeTrialRecords.Load() : TimeTrialRecords.Empty();
        Medals = TimeTrialTargets.For(_key, _layout.Path);
        if (_records.Best(_key) is { } best)
        {
            BestLap = best.Time;
            BestSplits = best.Splits;
        }
        BestGhost = Persist ? Ghost.Load(Ghost.PathFor(_key)) : null;
        if (BestGhost != null && BestGhost.Car != CarId) BestGhost = null;

        var livery = (PlayerLivery ?? DerbyMode.DefaultPlayerLivery) with { Number = 7, Roof = DerbyMode.RoofColor(4) };
        _car = new Car
        {
            Id = 1,
            Number = 7,
            PaintColor = livery.Paint,
            RoofColor = livery.Roof ?? Colors.Yellow,
            Livery = livery,
            ModelId = CarId,
            BuildVisuals = !Headless,
            Transform = StartPose(),
        };
        AddChild(_car);
        _carList.Add(_car);
        _car.RightingPose = c => _layout.ResetPose(_s, _lat);
        _car.Driver = ProxyPlayer
            ? new PlayerProxyInput(new RaceBrain(ProxyProfile, Seed * 1000 + 1) { Anticipation = 0.25f }, this, Seed * 7919 + 3)
            : new PlayerInput();
        ResetTracking();

        var impacts = new ImpactSystem { Clock = () => _clock };
        AddChild(impacts);
        impacts.Register(_car);
        impacts.Impacted += i => ImpactHappened?.Invoke(i);
        // --wallhits: every hit above 6 kJ with where it happened (the sims).
        if (Core.DevTools.Instance?.Arg("wallhits") != null)
            impacts.Impacted += i =>
            {
                if (i.Energy < 6_000f) return;
                var (hs, hl) = TrackPos(_car);
                GD.Print($"[TT] hit t={_clock:0.0} s={hs:0} lat={hl:0.0} v={i.A.PreLinearVelocity.Length():0.0} E={i.Energy / 1000f:0}kJ {(i.B == null ? "wall" : "car")}");
            };

        if (Headless) return;
        _ghostCar = new Car
        {
            Id = 99,
            Number = 0,
            PaintColor = new Color(0.55f, 0.75f, 1f),
            Livery = new Livery(new Color(0.55f, 0.75f, 1f), new Color(0.9f, 0.95f, 1f), Number: 0),
            ModelId = CarId,
            Visible = false,
            Transform = StartPose(),
        };
        AddChild(_ghostCar);
        // After _Ready (which puts every car on the car layer): the ghost touches nothing.
        _ghostCar.CollisionLayer = 0;
        _ghostCar.CollisionMask = 0;
        _ghostCar.BeginReplay();
        _rig = new CameraRig { Target = _car };
        AddChild(_rig);
        if (Enum.TryParse(DevTools.Instance?.Arg("view") ?? "", out CameraRig.View view)) _rig.Mode = view;
        _hud = new TimeTrialHud { Mode = this };
        AddChild(_hud);
        AddChild(new Audio.EventAudio { Mode = this });
        if (_venue.FloodlightsOn) AddChild(new HeadlightRig { Cars = _carList, Beams = _venue.IsNight, Focus = () => _car });
        CallDeferred(MethodName.AnnounceStart);
    }

    private void AnnounceStart()
    {
        Started?.Invoke();
        IntensityChanged?.Invoke(1);
    }

    private Transform3D StartPose() => _layout.PoseAt(_layout.Path.Wrap(-StartBack), 0f);

    private void ResetTracking()
    {
        var (s, lat) = _layout.Path.Project(_car.GlobalPosition, -1f);
        _s = s;
        _lat = lat;
        _progress = _layout.Path.Delta(0f, s);
        _lineK = _progress < 0f ? -1 : 0;
        _lapStart = null;
        _sectorsDone = 0;
        _splits[0] = _splits[1] = double.NaN;
        _invalid = false;
        _recording = new Ghost { Car = CarId };
    }

    /// <summary>Back to the start at once (plan §4.4: instant restart, ≤ 1 s).</summary>
    public void Restart() => _restartPending = true;

    // ------------------------------------------------------------------ flow

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (_restartPending)
        {
            _restartPending = false;
            _car.GlobalTransform = StartPose();
            _car.LinearVelocity = Vector3.Zero;
            _car.AngularVelocity = Vector3.Zero;
            ResetTracking();
            if (_ghostCar != null) _ghostCar.Visible = false;
            return;
        }
        double before = _clock;
        _clock += dt;

        // Lap position, locally (no leg swaps at the crossover); a re-projection jump spoils the lap.
        var path = _layout.Path;
        var (ns, nl) = path.Project(_car.GlobalPosition, _s, 40f);
        bool jumped = false;
        if (MathF.Abs(nl) > path.Width * 0.5f + 4f)
        {
            var (gs, gl) = path.Project(_car.GlobalPosition, -1f);
            if (MathF.Abs(gl) < MathF.Abs(nl) - 2f) { ns = gs; nl = gl; jumped = true; }
        }
        float d = path.Delta(_s, ns);
        float prev = _progress;
        if (!jumped && MathF.Abs(d) < 30f) _progress += d;
        else if (_lapStart != null) _invalid = true;
        _s = ns;
        _lat = nl;

        // The line: a lap runs from one crossing of line k to the first crossing of line k + 1.
        float L = path.Length;
        int k = (int)MathF.Floor(_progress / L);
        if (k > _lineK)
        {
            double tc = before + dt * Math.Clamp((k * L - prev) / Math.Max(_progress - prev, 1e-4f), 0f, 1f);
            CrossLine(tc);
            _lineK = k;
        }
        else if (_lapStart is double t0 && _sectorsDone < 2)
        {
            float w = _progress - _lineK * L;
            float end = _layout.SectorEnds[_sectorsDone];
            if (w >= end)
            {
                double tc = before + dt * Math.Clamp((_lineK * L + end - prev) / Math.Max(_progress - prev, 1e-4f), 0f, 1f);
                _splits[_sectorsDone] = tc - t0;
                _sectorsDone++;
                double delta2 = BestSplits is { } b && b.Length >= _sectorsDone ? _splits[_sectorsDone - 1] - b[_sectorsDone - 1] : double.NaN;
                SectorSplit?.Invoke(_sectorsDone, delta2);
            }
        }

        if (_lapStart is double start)
        {
            float t = (float)(_clock - start);
            if (t >= _nextSample)
            {
                _recording.Record(t, _car);
                _nextSample = t + 1f / Ghost.Rate;
            }
        }

        if ((MaxLaps > 0 && LapsTimed >= MaxLaps) || _clock >= MaxDuration)
        {
            SetPhysicsProcess(false);
            _car.InputLocked = true;
            Finished?.Invoke(this);
        }
    }

    private void CrossLine(double tc)
    {
        if (_lapStart is double t0)
        {
            double lap = tc - t0;
            bool valid = !_invalid && _sectorsDone == 2;
            int place = 0, medal = 0;
            if (valid)
            {
                LapsTimed++;
                LapTimes.Add(lap);
                LastLap = lap;
                medal = Medals.Of(lap);
                var record = new TimeTrialRecords.Lap(lap, new[] { _splits[0], _splits[1] }, TimeTrialRecords.Today());
                place = _records.Submit(_key, record);
                if (BestLap == null || lap < BestLap)
                {
                    BestLap = lap;
                    BestSplits = record.Splits;
                    _recording.LapTime = lap;
                    _recording.Record((float)lap, _car);
                    BestGhost = _recording;
                    if (Persist) BestGhost.Save(Ghost.PathFor(_key));
                }
                if (Persist && place > 0) _records.Save();
            }
            LapCompleted?.Invoke(lap, valid, place, medal);
        }
        _lapStart = tc;
        _sectorsDone = 0;
        _splits[0] = _splits[1] = double.NaN;
        _invalid = false;
        _recording = new Ghost { Car = CarId };
        _nextSample = 0f;
    }

    public override void _Process(double delta)
    {
        if (_ghostCar == null) return;
        bool show = GhostVisible && BestGhost != null && CurrentLapTime is double t && t <= BestGhost.LapTime;
        _ghostCar.Visible = show;
        if (!show) return;
        BestGhost!.Apply((float)CurrentLapTime!.Value, _ghostCar);
        if (!_ghostStyled)
        {
            // A see-through ghost: every mesh of the model blended at 60 %, no shadow.
            _ghostStyled = true;
            foreach (var node in _ghostCar.FindChildren("*", "GeometryInstance3D", true, false))
                if (node is GeometryInstance3D g)
                {
                    g.Transparency = 0.6f;
                    g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                }
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsEcho() || e is not InputEventKey { Pressed: true } key || GetTree().Paused) return;
        if (key.PhysicalKeycode == Godot.Key.R) Restart();
        else if (key.PhysicalKeycode == Godot.Key.G) GhostVisible = !GhostVisible;
        else return;
        GetViewport().SetInputAsHandled();
    }
}
