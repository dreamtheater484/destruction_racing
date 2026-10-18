using System;
using System.Collections.Generic;
using System.Linq;
using DestructionRacing.AI;
using DestructionRacing.Cameras;
using DestructionRacing.Core;
using DestructionRacing.Rules;
using DestructionRacing.Tracks;
using DestructionRacing.UI;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Modes;

/// <summary>
/// A race at Crossroads Speedway (plan §4.2, §4.3), on the oval or the figure-8, forward or reverse: 20 cars on a
/// 3-wide grid, a 3-2-1 countdown, a fixed number of laps; wrecks are DNFs and stay on track. Pit stops repair damage
/// (<see cref="PitStops"/>); a flipped car is put back on the track after 3 s. The race ends when the player finishes;
/// when the player is wrecked they can watch, speed up or skip to the results. Also runs headless with the
/// PlayerProxy for sims.
/// <list type="bullet">
/// <item><b>Wreckin' Racing:</b> spins and wrecks score, ×2 for hitting the car in 1st place (the leader beacon),
/// finishing bonus 25/15/10.</item>
/// <item><b>Stock Car Racing:</b> points for the finishing position only (50, 40, 35 … 0); contact scores nothing,
/// damage still applies. The AI races faster and cleaner, but bruisers and psychos still bump.</item>
/// </list>
/// </summary>
public partial class RaceMode : Node3D, IRaceView, IHudMode, Audio.IEventSound
{
    public int CarCount { get; set; } = 20;
    /// <summary>Wreckin' Racing or Stock Car Racing.</summary>
    public GameMode Kind { get; set; } = GameMode.WreckinRacing;
    public bool IsStockCar => Kind == GameMode.StockCarRacing;
    /// <summary>AI pace in Stock Car against Wreckin' (plan §10.5: faster in Stock Car, DD1) ✱.</summary>
    public static float StockCarPace { get; set; } = 1.03f;
    /// <summary>The PlayerProxy's pit line (front health, %; the AI's own is 36–52 by self-preservation).</summary>
    public static float ProxyPitLine { get; set; } = 45f;
    /// <summary>Crossroads Speedway layout and direction (plan §7.3).</summary>
    public SpeedwayLayout LayoutId { get; set; } = SpeedwayLayout.Figure8;
    /// <summary>The lighting (--preset=afternoon|golden|night).</summary>
    public BowlPreset Preset { get; set; } = BowlPreset.Afternoon;
    public bool Reverse { get; set; }
    /// <summary>Laps per race, data-driven per layout (plan §4.2: 6–10); 0 = the layout's own count.</summary>
    public int Laps { get; set; }
    /// <summary>The player's grid slot (0 = pole).</summary>
    public int PlayerGridSlot { get; set; } = 12;
    /// <summary>The player's division (the roof colour; plan §4.5).</summary>
    public int PlayerDivision { get; set; } = 4;
    public int PlayerNumber { get; set; } = 7;
    /// <summary>Championship: each AI driver's division by name (their roof colours); null = spread over the four.</summary>
    public IReadOnlyDictionary<string, int>? DriverDivisions { get; set; }
    public bool Headless { get; set; }
    /// <summary>The player's model (--car=&lt;id&gt;, see <see cref="CarCatalog"/>); the AI drive their own (<see cref="Drivers"/>).</summary>
    public string PlayerModel { get; set; } = "kitsune";
    public Livery? PlayerLivery { get; set; }
    /// <summary>Balance sims (--randomcars): every AI driver gets a random model, so each car meets every archetype.</summary>
    public bool RandomCars { get; set; }
    public bool ProxyPlayer { get; set; }
    public Personality ProxyProfile { get; set; } = Personality.AverageFan;
    public int Seed { get; set; } = 1;
    public RulesConfig Rules { get; set; } = RulesConfig.Standard;
    /// <summary>
    /// AI pace for the player's division (plan §10.5) ✱: Div 4 is 0.97, so an average player who races cleanly can
    /// fight for the top three (at 1.0 the AI racers out-pace an average player and only hunting pays).
    /// </summary>
    public float AiPaceScale { get; set; } = 0.97f;
    /// <summary>The AI's division aggression (plan §10.5, <see cref="EventRequest.AiAggression"/>): how often it attacks.</summary>
    public float AiAttackScale { get; set; } = 1f;
    /// <summary>Back-marker catch-up (plan §4.2/§10.5; off in Purist).</summary>
    public bool CatchUpEnabled { get; set; } = true;
    public double MaxDuration { get; set; } = 900;
    /// <summary>
    /// The endurance check (plan §17.1: the AI does 100 laps of every layout, both ways, without getting stuck):
    /// no damage, and every car's stuck resets and longest stop go into its [CarResult] line.
    /// </summary>
    public bool Endurance { get; set; }
    public float CountdownTime { get; set; } = 3f;
    public double DumpEvery { get; set; }
    /// <summary>Debug: print every wall hit above the damage threshold.</summary>
    public bool DebugWallHits { get; set; }
    /// <summary>Debug: print every hit on the player's car above the damage threshold.</summary>
    public bool DebugPlayerHits { get; set; }

    public event Action<RaceMode>? Finished;

    // ------------------------------------------------------------------ IEventSound (the shared event audio)

    public event Action<Impact>? ImpactHappened;
    public event Action<RefereeEvent>? RefereeEventRaised;
    public event Action<int>? CountdownTick;
    public event Action? Started;
    /// <summary>The music: 0 the opening half, 1 the second half, 2 the last two laps, 3 the final lap (of the leader).</summary>
    public event Action<int>? IntensityChanged;
    public event Action? Ended;
    public Car PlayerCar => _player;
    public CameraRig? Rig => _rig;
    public EventHud? Hud => _hud;
    /// <summary>Until the grandstands arrive with the venue art: excitement from what happens on track, decaying.</summary>
    public float CrowdExcitement => _venue.Look?.Crowd?.Excitement ?? _excitement;
    private float _excitement = 0.3f;

    /// <summary>A crowd reaction: big hits about 0.2, a wreck 0.3, the winner 1.</summary>
    private void Pulse(float amount)
    {
        _excitement = MathF.Min(1f, _excitement + amount);
        _venue.Look?.Crowd?.Pulse(amount);
    }
    private int _intensity = -1;
    private int _lastTick = -1;
    public Audio.EventAudio? Sound { get; private set; }

    private enum Phase { Countdown, Running, Over }

    private readonly List<Car> _cars = new();
    private readonly Dictionary<int, Car> _byId = new();
    private readonly Dictionary<int, RaceBrain> _brains = new();
    private Replay.ReplayRecorder? _recorder;
    private Replay.ReplayPlayer? _replay;
    private double _playerWreckTime = -1, _playerFinishTime = -1;
    private readonly HashSet<(int, double)> _t1Hits = new();
    private readonly Dictionary<int, float> _s = new();
    private readonly Dictionary<int, float> _lateral = new();
    private readonly Dictionary<int, float> _progress = new();
    private readonly Dictionary<int, CarSample> _samples = new();
    private readonly List<float> _crossPasses = new();

    private Phase _phase = Phase.Countdown;
    private float _countdown;
    private double _time;
    private float _leaderProgress;
    private int _playerLap = 1;
    private Referee _referee = null!;
    private RaceStandings _standings = null!;
    private ImpactSystem _impacts = null!;
    private RaceMetrics _metrics = null!;
    private Speedway _venue = null!;
    private RaceLayout _layout = null!;
    private PitStops _pits = null!;
    /// <summary>Each AI driver's pit line: the front health (%) below which they come in.</summary>
    private readonly Dictionary<int, float> _pitLine = new();
    private Car _player = null!;
    private CameraRig? _rig;
    private ScreenFx? _fx;
    private EventHud? _hud;
    private Node3D? _beacon;
    private Random _rng = null!;
    private bool _skipping;
    private int _spectateIndex;
    private float _wrongWayTime;

    public Referee Referee => _referee;
    public RaceStandings Standings => _standings;
    public RaceMetricsResult MetricsResult => _metrics.Result;
    public RaceLayout Layout => _layout;
    public PitStops Pits => _pits;
    public bool IsOver => _phase == Phase.Over;
    public bool PlayerOut => _referee.IsWrecked(_player.Id);
    public double CountdownLeft => _countdown;
    public string DriverName(Car car) => car == _player ? "You" : _drivers.TryGetValue(car.Id, out var d) ? d.Short : "?";
    private readonly Dictionary<int, DriverInfo> _drivers = new();
    private Taunts? _taunts;

    /// <summary>A driver's radio taunt for this event, if any (plan §10.2).</summary>
    private void Taunt(RefereeEvent e)
    {
        _taunts ??= new Taunts(Seed);
        if (_taunts.For(e, _player.Id, id => _drivers.GetValueOrDefault(id)) is var (d, line))
            _hud?.Radio(d.Number, d.Name, Taunts.NationCode(d.Nation), line, d.Livery.Paint);
    }
    public float ProgressOf(Car car) => _progress[car.Id];
    public Car? CarById(int id) => _byId.GetValueOrDefault(id);

    public static GameMode ParseKind(string? name) => name?.ToLowerInvariant() switch
    {
        "stockcar" or "stock" or "stock_car" => GameMode.StockCarRacing,
        _ => GameMode.WreckinRacing,
    };

    // ------------------------------------------------------------------ IRaceView

    public List<Car> Cars => _cars;
    public bool IsRunning(Car car) => _phase != Phase.Over && !_referee.IsWrecked(car.Id);
    public TrackPath Track => _layout.Path;
    public double Time => _time;
    public Car? Player => _player;
    public int? LeaderId => _standings?.Leader;
    public (float S, float Lateral) TrackPos(Car car) => (_s[car.Id], _lateral[car.Id]);
    public IReadOnlyList<float> CrossoverPasses => _crossPasses;
    public bool IsGhost(Car car) => _pits.IsGhost(car);

    public int LeaderLap => _standings.Leader is int lid ? _standings.LapOf(lid) : 1;

    public float CatchUp(Car car)
    {
        if (!CatchUpEnabled || car == _player || _standings.HasFinished(car.Id)) return 1f;
        float gap = _leaderProgress - _progress[car.Id];
        return 1f + 0.06f * Mathf.Clamp((gap - 120f) / 250f, 0f, 1f);
    }

    // ------------------------------------------------------------------ IHudMode

    public string HudRank => $"{_standings.PositionOf(_player.Id):00}";
    public string HudStatus => _standings.HasFinished(_player.Id) ? "FINISHED" : $"Lap  {_standings.LapOf(_player.Id)}/{Laps}";
    public string HudStatusLabel => _standings.HasFinished(_player.Id) ? "" : "LAP";
    public string HudStatusValue => _standings.HasFinished(_player.Id) ? "FINISHED" : $"{_standings.LapOf(_player.Id)}/{Laps}";
    public string? HudWarning => _wrongWayTime > 0.8f && _phase == Phase.Running && !PlayerOut && !_pits.IsManaged(_player) ? "WRONG WAY" : null;

    public EventHud.PitInfo? PitPanel => _pits.StopOf(_player) is { Stage: PitStops.Stage.Repair } s
        ? new EventHud.PitInfo(_pits.TimeLeft(s), s.SelectedZone, s.Manual, s.Presses)
        : null;

    /// <summary>The pit prompt (plan §13 "PIT"): damaged, a stop left, the race not on its last lap, the entry coming up.</summary>
    public string? HudPit
    {
        get
        {
            if (_phase != Phase.Running || PlayerOut || _pits.IsManaged(_player) || !_pits.CanStop(_player)) return null;
            if (_standings.HasFinished(_player.Id) || _standings.LapOf(_player.Id) >= Laps) return null;
            float worst = MathF.Min(MathF.Min(_player.Health(DamageZone.Front), _player.Health(DamageZone.Rear)),
                MathF.Min(_player.Health(DamageZone.Left), _player.Health(DamageZone.Right)));
            if (worst > 0.55f) return null;
            float d = _pits.DistanceToEntry(_s[_player.Id]);
            return d < 400f ? $"PIT  IN  {d:0} M" : null;
        }
    }

    public string StandingsText()
    {
        var order = _standings.Order();
        var lines = order.Take(20).Select((id, i) =>
        {
            var c = _byId[id];
            string state = _standings.HasFinished(id) ? "finished"
                : _standings.IsRetired(id) ? "DNF"
                : $"lap {_standings.LapOf(id)}";
            return $"{i + 1,2}.  #{c.Number,-3} {DriverName(c),-11} {state,-9} {_referee.Score(id).Total,4} pts";
        });
        return "RUNNING ORDER\n" + string.Join("\n", lines);
    }

    // ------------------------------------------------------------------ set-up

    public override void _Ready()
    {
        bool front = GetParent() == GetTree().Root && Session.Request is { IsRace: true };
        if (front && Session.Request is { } req)
        {
            Kind = req.Mode;
            LayoutId = req.Layout;
            Reverse = req.Reverse;
            PlayerModel = req.Car;
            PlayerLivery = req.Livery;
            PlayerNumber = req.Number;
            PlayerDivision = req.Division;
            AiPaceScale = req.AiPace;
            AiAttackScale = req.AiAggression;
            Preset = req.Preset;
            Seed = req.Seed;
            DriverDivisions = req.DriverDivisions;
            CarCount = 1 + Math.Clamp(req.Opponents, 0, 19);
            if (req.Laps > 0) Laps = req.Laps;
            CatchUpEnabled = GameSettings.CatchUp;
        }
        if (GetParent() == GetTree().Root && DevTools.Instance is { } dev)
        {
            if (dev.Arg("proxy") != null) ProxyPlayer = true;
            if (int.TryParse(dev.Arg("seed"), out int seed)) Seed = seed;
            if (int.TryParse(dev.Arg("laps"), out int laps)) Laps = Math.Max(1, laps);
            if (dev.Arg("layout") is { } layout) LayoutId = layout.ToLowerInvariant() == "oval" ? SpeedwayLayout.Oval : SpeedwayLayout.Figure8;
            if (dev.Arg("reverse") != null) Reverse = true;
            if (dev.Arg("kind") is { } kind) Kind = ParseKind(kind);
            if (dev.Arg("preset") is { } preset) Preset = Speedway.ParsePreset(preset);
            if (dev.Arg("car") is { } car) PlayerModel = car;
            if (dev.Arg("randomcars") != null) RandomCars = true;
        }
        // --attackshare=X (experiments): the attacker's share of contact damage.
        if (float.TryParse(DevTools.Instance?.Arg("attackshare"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float share))
            Rules = Rules with { AttackerDamageShare = share };
        _rng = new Random(Seed);
        _countdown = CountdownTime;
        if (!Headless) Controls.Apply(Controls.Current);

        _venue = new Speedway { Name = "Speedway", LayoutId = LayoutId, Reverse = Reverse, Visuals = !Headless, Preset = Preset };
        AddChild(_venue);
        _layout = _venue.Layout;
        if (Laps <= 0) Laps = _layout.Laps;
        _crossPasses.AddRange(_layout.CrossoverPasses);

        _impacts = new ImpactSystem { Clock = () => _time, DebugContacts = DevTools.Instance?.Arg("contacts") != null };
        AddChild(_impacts);
        Car.DebugHits = DevTools.Instance?.Arg("hits") != null;

        SpawnField();
        _referee = new Referee(Rules, Kind, _cars.Select(c => (c.Id, c.RefereeToughness))) { DamageScale = Endurance ? 0f : Rules.RaceDamageScale };
        foreach (var c in _cars)
        {
            c.Damage = _referee.Damage(c.Id);
            c.InputLocked = true;
            _impacts.Register(c);
        }
        _impacts.Referee = _referee;
        _impacts.Impacted += OnImpact;
        _standings = new RaceStandings(_cars.Select(c => c.Id), Laps, Track.Length);
        foreach (var c in _cars) _standings.Update(c.Id, _progress[c.Id], 0);
        _metrics = new RaceMetrics(_player, Seed, Track, _layout.CrossoverPoint, _layout.HasCrossover ? _layout.CrossoverRadius : 0f);
        _pits = new PitStops(_layout, Rules, Kind, TrackPos);
        _pits.Stopped += OnPitStop;
        _pits.Released += stop =>
        {
            var c = stop.Car;
            if (_pitLog)
                GD.Print($"[Pit] t={_time:0} #{c.Number} released: {stop.Presses} presses, +{stop.Repaired:0} health, F{c.Health(DamageZone.Front) * 100:0} " +
                         $"R{c.Health(DamageZone.Rear) * 100:0} L{c.Health(DamageZone.Left) * 100:0} Rt{c.Health(DamageZone.Right) * 100:0}");
            if (c == _player && stop.Repaired > 0.5f) _hud?.Popup($"REPAIRED  +{stop.Repaired:0}", HudStyle.Green);
        };
        _pits.Refused += car =>
        {
            if (car == _player) _hud?.Popup("NO PIT STOPS LEFT", new Color(1f, 0.5f, 0.2f));
        };
        var pitRng = new Random(Seed * 613 + 5);
        foreach (var c in _cars)
        {
            // Racers come in a little earlier than the reckless (self-preservation), ± a personal margin ✱.
            float sp = _brains.TryGetValue(c.Id, out var b) ? b.Personality.SelfPreservation : 0.6f;
            _pitLine[c.Id] = 36f + 16f * sp + (float)(pitRng.NextDouble() * 10.0 - 5.0);
            c.RightingPose = RightingPoseFor;
            c.Flipped += car =>
            {
                if (car == _player && !PlayerOut) _hud?.Popup("ROLLOVER!", new Color(1f, 0.6f, 0.2f));
            };
        }

        if (!Headless)
        {
            _rig = new CameraRig { Target = _player };
            AddChild(_rig);
            _fx = new ScreenFx();
            AddChild(_fx);
            _hud = new EventHud { Mode = this, PlayerCar = _player, ViewName = () => _rig.Mode.ToString() };
            if (front)
            {
                _hud.MenuScene = Session.FrontEndScene;
                _hud.Championship = Session.InChampionship;
                _hud.DivisionTable = Session.DivisionTable(EventPointsOf);
                _hud.DivisionTitle = Session.DivisionTitle;
            }
            _hud.Replay = () => { if (_replay == null) StartReplay(); };
            _hud.Highlights = () => { if (_replay == null) StartReplay(highlights: true); };
            _hud.ReplayActive = () => _replay != null;
            AddChild(_hud);
            _recorder = new Replay.ReplayRecorder { Cars = _cars, Clock = () => _time };
            AddChild(_recorder);
            _beacon = BuildBeacon();
            AddChild(_beacon);
            Sound = new Audio.EventAudio { Mode = this };
            AddChild(Sound);
            // Lamps on at dusk; at night real headlight spots and beams on the cars near the camera.
            if (_venue.FloodlightsOn)
                AddChild(new HeadlightRig { Cars = _cars, Beams = _venue.IsNight, Focus = () => _rig.Target });
            // --overview: a top-down view of the whole layout (screenshots); --view=ChaseFar etc. for the rig.
            if (DevTools.Instance?.Arg("overview") != null)
            {
                float size = float.TryParse(DevTools.Instance?.Arg("overview"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float o) && o > 1f ? o : 240f;
                var top = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = size, Far = 1000f };
                AddChild(top);
                top.GlobalTransform = new Transform3D(Basis.LookingAt(Vector3.Down, Vector3.Forward), new Vector3(0f, 300f, 0f));
                top.CallDeferred(Camera3D.MethodName.MakeCurrent);
            }
            else if (Enum.TryParse(DevTools.Instance?.Arg("view") ?? "", out CameraRig.View view)) _rig.Mode = view;
        }
    }

    private void SpawnField()
    {
        var field = Drivers.Field(_rng, RandomCars);
        int playerSlot = Math.Clamp(PlayerGridSlot, 0, CarCount - 1);
        int aiIndex = 0;

        for (int slot = 0; slot < CarCount; slot++)
        {
            bool isPlayer = slot == playerSlot;
            int id = isPlayer ? 1 : 2 + aiIndex;
            var driver = isPlayer ? null : field[aiIndex % field.Count];
            int division = isPlayer ? PlayerDivision
                : DriverDivisions != null && DriverDivisions.TryGetValue(driver!.Name, out int dd) ? dd : 4 - (aiIndex % 4);
            var roof = DerbyMode.RoofColor(division);
            var livery = driver?.LiveryFor(roof) ?? (PlayerLivery ?? DerbyMode.DefaultPlayerLivery) with { Number = PlayerNumber, Roof = roof };
            var car = new Car
            {
                Id = id,
                Number = livery.Number,
                PaintColor = livery.Paint,
                RoofColor = roof,
                Livery = livery,
                ModelId = driver?.Car ?? PlayerModel,
                BuildVisuals = !Headless,
                Transform = _layout.GridSlot(slot),
            };
            if (driver != null) _drivers[id] = driver;
            AddChild(car);
            _cars.Add(car);
            _byId[id] = car;
            var (s, lateral) = Track.Project(car.GlobalPosition, -1f);
            _s[id] = s;
            _lateral[id] = lateral;
            _progress[id] = Track.Delta(0f, s); // negative: the grid is behind the line

            if (isPlayer)
            {
                _player = car;
                car.Driver = ProxyPlayer
                    ? new PlayerProxyInput(new RaceBrain(ProxyProfile, Seed * 1000 + 1) { Anticipation = 0.25f, StockCar = IsStockCar }, this, Seed * 7919 + 3)
                    : new PlayerInput();
                if (!ProxyPlayer) GameSettings.ApplyAssists(car);
            }
            else
            {
                var brain = new RaceBrain(driver!.Personality, Seed * 1000 + id)
                {
                    PaceScale = AiPaceScale * (IsStockCar ? StockCarPace : 1f),
                    AttackScale = AiAttackScale,
                    StockCar = IsStockCar,
                };
                _brains[id] = brain;
                car.Driver = new RaceDriver(brain, this);
                aiIndex++;
            }
        }
    }

    /// <summary>A driver's points in this event so far, by championship key (the live division table).</summary>
    private int EventPointsOf(string key)
    {
        if (key == DestructionRacing.Rules.Career.PlayerKey) return _referee.Score(_player.Id).Total;
        foreach (var (id, d) in _drivers)
            if (d.Name == key) return _referee.Score(id).Total;
        return 0;
    }

    private static Node3D BuildBeacon()
    {
        var beacon = new Node3D { Name = "LeaderBeacon", Visible = false };
        beacon.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.35f, Height = 0.9f, RadialSegments = 4, Rings = 1 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.78f, 0.15f),
                EmissionEnabled = true,
                Emission = new Color(1f, 0.7f, 0.1f),
                EmissionEnergyMultiplier = 2.5f,
                Metallic = 0.6f,
            },
        });
        beacon.AddChild(new Label3D
        {
            Text = "LEADER ×2",
            FontSize = 64,
            PixelSize = 0.006f,
            OutlineSize = 12,
            Position = new Vector3(0f, 0.85f, 0f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = new Color(1f, 0.85f, 0.25f),
            NoDepthTest = true,
        });
        return beacon;
    }

    // ------------------------------------------------------------------ flow

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        switch (_phase)
        {
            case Phase.Countdown:
                _countdown -= dt;
                int tick = Mathf.CeilToInt(_countdown);
                if (tick != _lastTick && tick >= 1 && tick <= 3)
                {
                    _lastTick = tick;
                    CountdownTick?.Invoke(tick);
                    _venue.Look?.SetStartLights(LayoutId, tick == 3 ? 1 : tick == 2 ? 3 : 5);
                }
                if (_countdown <= 0f)
                {
                    foreach (var c in _cars) c.InputLocked = false;
                    _phase = Phase.Running;
                    _hud?.Popup("GO!", new Color(0.3f, 1f, 0.3f), big: true);
                    Started?.Invoke();
                    SetIntensity(0);
                    _venue.Look?.SetStartLights(LayoutId, 0, green: true);
                    Pulse(0.4f);
                    ReportLoad("green light");
                }
                break;
            case Phase.Running:
                Tick(dt);
                break;
        }
    }

    private void Tick(float dt)
    {
        _time += dt;
        TrackCars();
        _referee.SetRaceLeader(_standings.Leader);

        _samples.Clear();
        foreach (var c in _cars)
        {
            var lc = c.LastControls;
            _samples[c.Id] = new CarSample(c.AngularVelocity.Y, c.LinearVelocity.Length(), lc.Throttle > 0.3f || lc.Reverse,
                c.GlobalPosition.X, c.GlobalPosition.Z);
            if (!_referee.IsWrecked(c.Id) && c.GlobalPosition.Y < -5f) _referee.ForceWreck(_time, c.Id);
        }
        _referee.Step(_time, dt, _samples);

        foreach (var e in _referee.DrainEvents())
        {
            _metrics.OnRefereeEvent(e);
            HandleEvent(e);
            RefereeEventRaised?.Invoke(e);
            if (e.Kind == RefereeEventKind.SpinScored) Pulse(0.15f);
            if (e.Kind == RefereeEventKind.Wrecked) Pulse(0.3f);
            // Each spin passes the quarter turn first: one count per spin, with or without an attacker.
            if (e.Kind == RefereeEventKind.SpinTier && e.Tier == SpinTier.Quarter)
                _spunCount[e.Car] = _spunCount.GetValueOrDefault(e.Car) + 1;
        }
        _excitement = MathF.Max(0.25f, _excitement - dt * 0.05f);
        int leaderLap = _standings.Leader is int lid ? _standings.LapOf(lid) : 1;
        SetIntensity(leaderLap >= Laps ? 3 : leaderLap >= Laps - 1 ? 2 : leaderLap > Laps / 2 ? 1 : 0);

        UpdatePlayerState();
        // --fliptest: 4 s after GO the player's car is put on its roof beside the track (the rollover and the righting).
        if (_flipTest && !_flipped && _time > 4.0)
        {
            _flipped = true;
            var xf = _player.GlobalTransform;
            Vector3 fwd = -xf.Basis.Z;
            _player.GlobalTransform = new Transform3D(xf.Basis.Rotated(fwd.Normalized(), Mathf.Pi), xf.Origin + Vector3.Up * 1.5f);
            _player.LinearVelocity = Vector3.Zero;
            _player.AngularVelocity = Vector3.Zero;
        }
        StepPits();
        CheckStuck();
        if (Endurance) TrackStops();
        UpdateDraft();
        _metrics.Step(_time, dt, _standings, _s, _progress, IsRunning);
        UpdateBeacon();

        if (DumpEvery > 0 && (int)(_time / DumpEvery) != (int)((_time - dt) / DumpEvery)) DumpState();

        // The race ends when the player finishes, or (player wrecked) when the winner takes the flag; everyone else
        // is classified by distance covered.
        int racing = _cars.Count(c => _standings.IsRacing(c.Id));
        if (_standings.HasFinished(_player.Id)) EndRace(timedOut: false);
        else if (PlayerOut && (_standings.FinishedCount >= 1 || racing == 0)) EndRace(timedOut: false);
        else if (_time >= MaxDuration) EndRace(timedOut: true);
    }

    /// <summary>
    /// Lap position and progress for every car. Projection is local (±40 m around the last position) so the
    /// crossover never swaps legs; a car far off its own leg is re-projected globally, and that jump earns no
    /// progress (no short cuts through the crossover).
    /// </summary>
    private void TrackCars()
    {
        var path = Track;
        float lead = float.MinValue;
        foreach (var c in _cars)
        {
            int id = c.Id;
            Vector3 pos = c.GlobalPosition;
            var (ns, nl) = path.Project(pos, _s[id], 40f);
            bool jumped = false;
            // In the pit lane a car is far off the line on purpose: keep it on its own leg.
            if (MathF.Abs(nl) > path.Width * 0.5f + 4f && !_pits.IsGhost(c) && !_pits.OnPitSurface(c))
            {
                var (gs, gl) = path.Project(pos, -1f);
                if (MathF.Abs(gl) < MathF.Abs(nl) - 2f)
                {
                    ns = gs;
                    nl = gl;
                    jumped = true;
                }
            }
            float d = path.Delta(_s[id], ns);
            if (!jumped && MathF.Abs(d) < 30f && !_referee.IsWrecked(id)) _progress[id] += d;
            _s[id] = ns;
            _lateral[id] = nl;

            if (_standings.Update(id, _progress[id], _time)) OnFinished(c);
            if (_standings.IsRacing(id) || _standings.HasFinished(id)) lead = MathF.Max(lead, _progress[id]);
        }
        _leaderProgress = lead;
    }

    private void OnFinished(Car car)
    {
        int place = _standings.FinishedCount;
        if (car == _player) _playerFinishTime = _time;
        if (place == 1 || car == _player) Pulse(place == 1 ? 1f : 0.4f);
        // After the flag: a cool-down lap, as a ghost (a finished car must not pile up in the crossover or block
        // the cars still racing), drawn see-through so the player can tell.
        if (car != _player)
        {
            _pits.MakePermanentGhost(car);
            if (_brains.TryGetValue(car.Id, out var brain)) brain.PaceScale *= 0.7f;
            if (!Headless)
                foreach (var node in car.FindChildren("*", "GeometryInstance3D", true, false))
                    if (node is GeometryInstance3D g) g.Transparency = 0.5f;
        }
        if (car == _player) _hud?.Popup($"FINISHED  P{place}", new Color(0.3f, 1f, 0.4f), big: true);
        else if (place == 1) _hud?.Feed($"#{car.Number} WINS THE RACE");
    }

    private void UpdatePlayerState()
    {
        if (PlayerOut) return;
        int lap = _standings.LapOf(_player.Id);
        if (lap != _playerLap && !_standings.HasFinished(_player.Id))
        {
            if (lap > _playerLap)
                _hud?.Popup(lap == Laps ? "FINAL LAP" : $"LAP {lap}", lap == Laps ? new Color(1f, 0.8f, 0.2f) : Colors.White, big: lap == Laps);
            _playerLap = lap;
        }
        Vector3 fwd = -_player.GlobalTransform.Basis.Z;
        fwd.Y = 0f;
        float along = fwd.Normalized().Dot(Track.TangentAt(_s[_player.Id]));
        // Driving the wrong way for a moment (not just facing backwards in a spin or a turn-around).
        bool wrongWay = along < -0.3f && _player.LinearVelocity.Length() > 3f && _player.ForwardSpeed > 0f;
        _wrongWayTime = wrongWay ? _wrongWayTime + (float)GetPhysicsProcessDeltaTime() : 0f;
    }

    // ------------------------------------------------------------------ pits and rollovers

    /// <summary>
    /// The pit lane (plan §6.0): cars turning in are caught there; the AI and the PlayerProxy decide on damage and
    /// laps left (plan §10.3) and are guided in from just before the entry.
    /// </summary>
    private void StepPits()
    {
        _pits.Step(_time, _cars, c => (c == _player && !ProxyPlayer, RepairRate(c)));
        foreach (var c in _cars)
        {
            if (!IsRunning(c) || _pits.IsManaged(c) || _standings.HasFinished(c.Id)) continue;
            if (c == _player && !ProxyPlayer) continue;
            float toEntry = _pits.DistanceToEntry(_s[c.Id]);
            // Decide early enough to brake from race speed to the entry speed on the way in.
            if (toEntry < 30f || toEntry > 140f) continue;
            float line = c == _player ? ProxyPitLine : _pitLine.GetValueOrDefault(c.Id, 45f);
            bool wants = _pits.WantsStop(c, _standings.LapOf(c.Id), Laps, line)
                         || (_pitTest && c == _player && _pits.StopsBy(c) == 0 && _pits.CanStop(c));
            if (_pitLog && _pitLogged.Add((c.Id, _standings.LapOf(c.Id))))
                GD.Print($"[Pit] t={_time:0} #{c.Number} lap {_standings.LapOf(c.Id)}/{Laps} line {line:0} F{c.Health(DamageZone.Front) * 100:0} R{c.Health(DamageZone.Rear) * 100:0} " +
                         $"L{c.Health(DamageZone.Left) * 100:0} Rt{c.Health(DamageZone.Right) * 100:0} stops {_pits.StopsBy(c)} -> {(wants ? "PIT" : "stay out")}");
            if (wants) _pits.Request(c, manual: false, RepairRate(c));
        }
    }

    private readonly bool _pitLog = DevTools.Instance?.Arg("pitlog") != null;
    /// <summary>--pittest: the PlayerProxy stops on its first pass (to look at the stop).</summary>
    private readonly bool _pitTest = DevTools.Instance?.Arg("pittest") != null;
    private readonly HashSet<(int, int)> _pitLogged = new();

    /// <summary>
    /// Slipstream: a car within 25 m behind another (and in line with it, both at speed) gets less drag, most when
    /// close. It keeps the pack together and sets up passes on the straights.
    /// </summary>
    private void UpdateDraft()
    {
        foreach (var c in _cars)
        {
            float best = 0f;
            if (IsRunning(c) && !_pits.IsGhost(c) && c.ForwardSpeed > 15f)
            {
                float s = _s[c.Id], lat = _lateral[c.Id];
                foreach (var o in _cars)
                {
                    if (o == c || !IsRunning(o) || _pits.IsGhost(o) || o.ForwardSpeed < 15f) continue;
                    float gap = Track.Delta(s, _s[o.Id]);
                    if (gap < 4f || gap > 25f) continue;
                    float dl = MathF.Abs(_lateral[o.Id] - lat);
                    if (dl > 1.8f) continue;
                    best = MathF.Max(best, (1f - (gap - 4f) / 21f) * (1f - dl / 1.8f));
                }
            }
            c.Draft = best;
        }
    }

    private readonly Dictionary<int, (double Since, float Progress)> _offRoad = new();
    private readonly Dictionary<int, int> _stuckResets = new();
    private readonly Dictionary<int, (double Time, float Progress)> _moveMark = new();
    private readonly Dictionary<int, double> _longestStop = new();

    /// <summary>Endurance: the longest time each car went without 5 m of progress (outside the pits).</summary>
    private void TrackStops()
    {
        foreach (var c in _cars)
        {
            int id = c.Id;
            if (!IsRunning(c) || _pits.IsManaged(c) || _standings.HasFinished(id)) { _moveMark.Remove(id); continue; }
            if (!_moveMark.TryGetValue(id, out var m) || _progress[id] - m.Progress > 5f) { _moveMark[id] = (_time, _progress[id]); continue; }
            _longestStop[id] = Math.Max(_longestStop.GetValueOrDefault(id), _time - m.Time);
        }
    }

    private readonly Dictionary<int, double> _inIsland = new();

    /// <summary>
    /// A car stuck off the road (in a gore between a barrier and the road, against a wreck) that has made no progress
    /// for 10 s is put back on the track, like a flipped car. On the road the immobilised and pinned rules apply.
    /// A car thrown over a wall into an infield island (riding up on a car at the crossover) is put back after 3 s:
    /// in there it could drive on along the wall, making progress, until it reached the island's far end.
    /// </summary>
    private void CheckStuck()
    {
        foreach (var c in _cars)
        {
            int id = c.Id;
            if (IsRunning(c) && !_standings.HasFinished(id) && _venue.Data.InsideIsland(c.GlobalPosition.X, c.GlobalPosition.Z))
            {
                if (!_inIsland.TryGetValue(id, out double since)) _inIsland[id] = since = _time;
                if (_time - since >= 3.0)
                {
                    _inIsland.Remove(id);
                    _offRoad.Remove(id);
                    _stuckResets[id] = _stuckResets.GetValueOrDefault(id) + 1;
                    c.RightCar();
                    if (_pitLog) GD.Print($"[Race] t={_time:0} #{c.Number} inside an island at s={_s[id]:0} lat={_lateral[id]:0.0}: back on the track");
                    if (c == _player) _hud?.Popup("BACK ON TRACK", HudStyle.Orange);
                }
                continue;
            }
            _inIsland.Remove(id);
            bool off = MathF.Abs(_lateral[id]) > Track.Width * 0.5f + 1f;
            if (!off || !IsRunning(c) || _pits.IsManaged(c) || _pits.OnPitSurface(c) || _standings.HasFinished(id))
            {
                _offRoad.Remove(id);
                continue;
            }
            if (!_offRoad.TryGetValue(id, out var st) || _progress[id] - st.Progress > 4f)
            {
                _offRoad[id] = (_time, _progress[id]);
                continue;
            }
            if (_time - st.Since < 10.0) continue;
            _offRoad.Remove(id);
            _stuckResets[id] = _stuckResets.GetValueOrDefault(id) + 1;
            c.RightCar();
            if (_pitLog) GD.Print($"[Race] t={_time:0} #{c.Number} stuck off the road at s={_s[id]:0} lat={_lateral[id]:0.0}: back on the track");
            if (c == _player) _hud?.Popup("BACK ON TRACK", HudStyle.Orange);
        }
    }

    /// <summary>Repair presses per second when the crew works automatically (AI, PlayerProxy): 5–9 by skill ✱.</summary>
    private float RepairRate(Car c)
    {
        float skill = c == _player ? ProxyProfile.Skill : _brains.TryGetValue(c.Id, out var b) ? b.Personality.Skill : 0.5f;
        return 5f + 4f * skill;
    }

    private void OnPitStop(PitStops.Stop stop)
    {
        if (_pitLog) GD.Print($"[Pit] t={_time:0} #{stop.Car.Number} stopped in box {stop.Box}");
        if (stop.Car != _player) _hud?.Feed($"#{stop.Car.Number} IN THE PITS");
    }

    /// <summary>
    /// Where a flipped car goes back on its wheels (plan §6.0): the nearest point on the track, inside the road, on a
    /// spot no other car is standing on (further back if need be, so it gains nothing). In the pit lane: in place.
    /// </summary>
    private Transform3D? RightingPoseFor(Car car)
    {
        if (_pits.IsManaged(car) || _pits.OnPitSurface(car)) return null;
        float s = _s[car.Id];
        float half = _layout.Width * 0.5f - 2.2f;
        float lat = Mathf.Clamp(_lateral[car.Id], -half, half);
        foreach (var (ds, l) in new[] { (0f, lat), (0f, 0f), (0f, -lat), (-9f, lat), (-9f, 0f), (-18f, 0f), (-27f, 0f) })
        {
            var pose = _layout.ResetPose(s + ds, l);
            bool clear = true;
            foreach (var o in _cars)
            {
                if (o == car) continue;
                Vector3 d = o.GlobalPosition - pose.Origin;
                d.Y = 0f;
                if (d.Length() < 3.6f) { clear = false; break; }
            }
            if (clear) return pose;
        }
        return _layout.ResetPose(s - 36f, 0f);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // The pit stop mini-game (plan §6.0): ←/→ choose the zone, mash Enter or Space, the gas key leaves early.
        if (_pits?.StopOf(_player) is not { Manual: true } stop || e.IsEcho()) return;
        if (e.IsActionPressed(Controls.PitRepair)) _pits.Press(stop);
        else if (e.IsActionPressed(Controls.PitPrev)) _pits.Select(stop, -1);
        else if (e.IsActionPressed(Controls.PitNext)) _pits.Select(stop, 1);
        else if (e.IsActionPressed(Controls.Accelerate)) _pits.RequestRelease(stop);
        else return;
        GetViewport().SetInputAsHandled();
    }

    private static float OrbitAngle(DamageZone zone) => zone switch
    {
        DamageZone.Front => 0f,
        DamageZone.Right => Mathf.Pi * 0.5f,
        DamageZone.Rear => Mathf.Pi,
        _ => -Mathf.Pi * 0.5f,
    };

    private void HandleEvent(RefereeEvent e)
    {
        if (_hud != null) Taunt(e);
        switch (e.Kind)
        {
            case RefereeEventKind.SpinTier when e.Other == _player.Id:
                _hud?.SpinProgress(e.Tier);
                break;
            case RefereeEventKind.SpinScored when e.Car == _player.Id:
                _hud?.Popup(e.Doubled ? $"LEADER ×2   SPIN {(int)e.Tier}   +{e.Points}" : $"SPIN {(int)e.Tier}   +{e.Points}",
                    e.Doubled ? new Color(1f, 0.8f, 0.2f) : Colors.White);
                if (_hud != null && _byId.TryGetValue(e.Other, out var spun))
                    WorldPopup.Show(this, spun, e.Doubled ? $"×2  +{e.Points}" : $"SPIN {(int)e.Tier}  +{e.Points}", e.Doubled ? new Color(1f, 0.8f, 0.2f) : Colors.White);
                break;
            case RefereeEventKind.Damage when e.Car == _player.Id:
                _hud?.PulseZone(e.Zone);
                break;
            case RefereeEventKind.WheelLost when e.Car == _player.Id:
                _hud?.Popup("YOU'VE LOST A WHEEL!", new Color(1f, 0.5f, 0.2f));
                break;
            case RefereeEventKind.Wrecked:
            {
                var victim = _byId[e.Car];
                _standings.Retire(e.Car, e.Time);
                if (_pitLog) GD.Print($"[Pit] t={_time:0} #{victim.Number} WRECKED lap {_standings.LapOf(e.Car)} s={_s[e.Car]:0} stops {_pits.StopsBy(victim)}");
                string by = e.Other > 0 ? $" by #{_byId[e.Other].Number}" : "";
                _hud?.Feed($"#{victim.Number} WRECKED{by}{(e.Points > 0 ? $"  +{e.Points}" : "")}");
                if (e.Other == _player.Id)
                {
                    _hud?.Popup(e.Doubled ? $"LEADER ×2   WRECKED #{victim.Number}   +{e.Points}" : $"WRECKED #{victim.Number}   +{e.Points}",
                        new Color(1f, 0.45f, 0.2f));
                    if (_hud != null) WorldPopup.Show(this, victim, $"WRECKED  +{e.Points}", new Color(1f, 0.45f, 0.2f));
                }
                if (e.Car == _player.Id)
                {
                    _playerWreckTime = _time;
                    _hud?.ShowWrecked("WRECKED  –  DNF");
                    SpectateNext(0);
                }
                break;
            }
        }
    }

    private void SetIntensity(int level)
    {
        if (level == _intensity) return;
        _intensity = level;
        IntensityChanged?.Invoke(level);
    }

    private void OnImpact(Impact i)
    {
        _metrics.OnImpact(i, _s);
        if (_phase == Phase.Running) _recorder?.OnImpact(i);
        // DD Code T1: the tailgater ran into the car that brake-checked it.
        if (i.B != null && i.ClosingSpeed > 2f)
            foreach (var (checker, victim) in new[] { (i.A, i.B!), (i.B!, i.A) })
                if (_brains.TryGetValue(checker.Id, out var cb) && cb.BrakeCheckVictim == victim && _time - cb.BrakeCheckTime < 1.5
                    && _t1Hits.Add((checker.Id, cb.BrakeCheckTime)))
                    _metrics.Result.BrakeCheckHits++;
        ImpactHappened?.Invoke(i);
        if (i.Energy > 70_000f) Pulse(0.2f);
        if (DebugPlayerHits && (i.A == _player || i.B == _player) && i.Energy > 6_000f)
        {
            Car? other = i.A == _player ? i.B : i.A;
            string who = other == null ? "wall" : $"#{other.Number} {DriverName(other)}{(IsRunning(other) ? "" : " (wreck)")}";
            GD.Print($"[Race] player hit t={i.Time:0.0} lap {_standings.LapOf(_player.Id)} s={_s[_player.Id]:0} lat={_lateral[_player.Id]:0.0} {who} " +
                     $"attacker={i.Attacker} closing={i.ClosingSpeed:0.0} E={i.Energy / 1000f:0}kJ v={_player.PreLinearVelocity.Length():0.0} " +
                     $"F{_player.Health(DamageZone.Front) * 100:0} R{_player.Health(DamageZone.Rear) * 100:0} L{_player.Health(DamageZone.Left) * 100:0} Rt{_player.Health(DamageZone.Right) * 100:0}");
        }
        if (DebugWallHits && i.B == null && i.Energy > 6_000f)
            GD.Print($"[Race] wall hit t={i.Time:0.0} #{i.A.Number} s={_s[i.A.Id]:0} lat={_lateral[i.A.Id]:0.0} v={i.A.PreLinearVelocity.Length():0.0} " +
                     $"closing={i.ClosingSpeed:0.0} E={i.Energy / 1000f:0}kJ F{i.A.Health(DamageZone.Front) * 100:0} L{i.A.Health(DamageZone.Left) * 100:0} Rt{i.A.Health(DamageZone.Right) * 100:0}" +
                     (_venue.CushionPoints.Any(c => new Vector2(i.Point.X, i.Point.Z).DistanceTo(c.At) < 2.5f) ? " CUSHION" : ""));
        if (Headless) return;
        if (i.Energy > 6_000f && i.A.Visual == null) SpawnSparks(i.Point, i.Energy);
        if (_rig != null && (i.A == _rig.Target || i.B == _rig.Target))
        {
            // The derby's juice (DerbyPresenter): shake, a camera punch on big hits, the flash and the blur on huge ones of yours.
            _rig.AddShake(Mathf.Clamp(i.Energy / 150_000f, 0.05f, 0.8f));
            if (i.Energy >= DerbyPresenter.BigHit) _rig.Punch(Mathf.Clamp(i.Energy / 140_000f, 0.25f, 1f));
            if (i.Energy >= DerbyPresenter.HugeHit && (i.A == _player || i.B == _player) && !PlayerOut) _fx?.HugeHit(i.Point, i.Energy);
        }
    }

    private void UpdateBeacon()
    {
        if (_beacon == null) return;
        var leader = _standings.Leader is int id ? _byId[id] : null;
        _beacon.Visible = leader != null && IsRunning(leader) && !_standings.HasFinished(leader.Id);
        if (leader != null)
            _beacon.GlobalPosition = leader.GlobalPosition + Vector3.Up * (2.0f + 0.15f * Mathf.Sin((float)_time * 4f));
    }

    public void DumpState()
    {
        GD.Print($"[Race] t={_time:0} leader progress {_leaderProgress:0} finished {_standings.FinishedCount}");
        foreach (int id in _standings.Order())
        {
            var c = _byId[id];
            string brain = c == _player ? "player" : _brains.TryGetValue(id, out var b) ? $"{b.Personality.Archetype,-8} {b.Current,-14}" : "?";
            GD.Print($"[Race]   P{_standings.PositionOf(id),-2} #{c.Number,-3} {brain} lap {_standings.LapOf(id)} prog {_progress[id]:0} s={_s[id]:0} lat={_lateral[id]:0.0} " +
                     $"v={c.ForwardSpeed:0.0} F{c.Health(DamageZone.Front) * 100:0} R{c.Health(DamageZone.Rear) * 100:0} " +
                     $"L{c.Health(DamageZone.Left) * 100:0} Rt{c.Health(DamageZone.Right) * 100:0}{(_referee.IsWrecked(id) ? " WRECKED" : "")}");
        }
    }

    /// <summary>The replay (results screen, V): from just before the player's wreck, or the run to the flag.</summary>
    private void StartReplay(bool highlights = false)
    {
        if (_recorder == null || _recorder.Times.Count == 0) return;
        double start = _playerWreckTime >= 0 ? _playerWreckTime - 8.0
            : _playerFinishTime >= 0 ? _playerFinishTime - 15.0 : _recorder.Duration - 30.0;
        _replay = new Replay.ReplayPlayer
        {
            Recording = _recorder,
            Cars = _cars,
            StartFocus = _player,
            StartTime = Math.Max(0.0, start),
            Rig = _rig,
            Highlights = highlights,
            TvPosts = _venue.TvPosts(),
        };
        if (Sound != null) _replay.ImpactPlayed += Sound.PlayReplayImpact;
        _replay.Closed += () =>
        {
            _replay = null;
            if (_hud != null) _hud.InEventVisible = true;
            if (_rig != null) { _rig.Target = _player; _rig.Current = true; }
            if (Sound != null) Sound.Music.Muted = false;
        };
        if (_hud != null) _hud.InEventVisible = false;
        if (Sound != null) Sound.Music.Muted = true;
        AddChild(_replay);
    }

    private void EndRace(bool timedOut)
    {
        if (_phase == Phase.Over) return;
        _phase = Phase.Over;
        if (_recorder != null) _recorder.Recording = false;
        _standings.Freeze();
        var order = _standings.Order();
        int position = 0;
        foreach (int id in order)
        {
            position++;
            if (_standings.IsRetired(id)) continue;            // a DNF scores no finishing points
            int bonus = IsStockCar ? Scoring.StockCarPoints(Rules, position) : Scoring.WreckinFinishBonus(Rules, position);
            if (bonus <= 0) continue;
            int added = _referee.AddBonus(_time, id, ScoreCategory.Finish, bonus);
            if (id == _player.Id)
                _hud?.Popup(IsStockCar ? $"P{position}   +{added}" : $"P{position}   FINISH BONUS +{added}", new Color(1f, 0.85f, 0.2f), big: true);
        }
        _referee.DrainEvents();
        _metrics.Finish(_time, _standings, _referee, timedOut);
        _metrics.Result.PitStops = _pits.TotalStops;
        _metrics.Result.PlayerPitStops = _pits.StopsBy(_player);
        foreach (var b in _brains.Values)
        {
            _metrics.Result.BrakeChecks += b.BrakeChecks;
            _metrics.Result.Squeezes += b.Squeezes;
            _metrics.Result.Tailgates += b.Tailgates;
            _metrics.Result.Evasions += b.Evasions;
            _metrics.Result.Blocks += b.Blocks;
        }
        if (Headless || DevTools.Instance?.Arg("carresults") != null) PrintCarResults(order);
        if (GetParent() == GetTree().Root && Session.InChampionship)
            Session.ReportResults(order.Select((id, i) => new DriverResult(
                id == _player.Id ? DestructionRacing.Rules.Career.PlayerKey : _drivers.TryGetValue(id, out var d) ? d.Name : "?",
                _referee.Score(id).Total, _referee.Score(id).Wrecks, i + 1)).ToList());
        foreach (var c in _cars) c.InputLocked = true;
        Engine.TimeScale = 1.0;
        _skipping = false;
        _hud?.ShowResults(IsStockCar ? "STOCK CAR RACING  –  RESULTS" : "WRECKIN' RACING  –  RESULTS", IsStockCar ? "Place" : "Finish", BuildResults());
        // Tests (--autoreplay[=highlights]): open the replay a moment after the results.
        if (!Headless && DevTools.Instance?.Arg("autoreplay") is { } auto)
            GetTree().CreateTimer(1.5, true, false, true).Timeout += () => { if (_replay == null) StartReplay(auto == "highlights"); };
        Finished?.Invoke(this);
        Ended?.Invoke();
    }

    /// <summary>Balance sims: one line per car (model, driver, place, points, finish) for tools/dev/balance_report.py.</summary>
    /// <summary>Times each car was spun (its [CarResult] line: how the cars fare in traffic).</summary>
    private readonly Dictionary<int, int> _spunCount = new();

    private void PrintCarResults(IReadOnlyList<int> order)
    {
        for (int i = 0; i < order.Count; i++)
        {
            var c = _byId[order[i]];
            var score = _referee.Score(c.Id);
            string who = c == _player ? (ProxyPlayer ? "Proxy" : "Player") : _drivers.TryGetValue(c.Id, out var d) ? d.Archetype : "?";
            double finish = _standings.FinishTime(c.Id) ?? -1;
            GD.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"[CarResult] mode={(IsStockCar ? "stockcar" : "race")} seed={Seed} car={c.ModelId} driver={who} place={i + 1} points={score.Total} " +
                $"spin={score.Get(ScoreCategory.Spin)} wreck={score.Get(ScoreCategory.Wreck)} finish={finish:0.0} " +
                $"retired={(_standings.IsRetired(c.Id) ? 1 : 0)} pits={_pits.StopsBy(c)} length={_time:0.0}" +
                $" laps={_standings.LapOf(c.Id)} stuck={_stuckResets.GetValueOrDefault(c.Id)} longeststop={_longestStop.GetValueOrDefault(c.Id):0.0}" +
                $" spun={_spunCount.GetValueOrDefault(c.Id)} who={(_drivers.TryGetValue(c.Id, out var di) ? Array.IndexOf(Drivers.All, di) : -1)}" +
                $" wrecks={score.Wrecks}"));
        }
    }

    public IReadOnlyList<EventHud.ResultRow> BuildResults()
    {
        var order = _standings.Order();
        return order.Select((id, i) =>
        {
            var c = _byId[id];
            var score = _referee.Score(id);
            string note = _standings.FinishTime(id) is double t ? FormatTime(t)
                : _standings.IsRetired(id) ? "DNF"
                : $"lap {_standings.LapOf(id)}";
            return new EventHud.ResultRow(i + 1, c.Number, DriverName(c), c.RoofColor,
                score.Get(ScoreCategory.Spin), score.Get(ScoreCategory.Wreck), score.Get(ScoreCategory.Finish), score.Total,
                c == _player, note);
        }).ToList();
    }

    private static string FormatTime(double t)
    {
        var span = TimeSpan.FromSeconds(t);
        return $"{(int)span.TotalMinutes}:{span.Seconds:00}.{span.Milliseconds / 100}";
    }

    // ------------------------------------------------------------------ player out: spectate / skip

    public void SpectateNext(int direction)
    {
        if (_rig == null) return;
        var running = _cars.Where(IsRunning).ToList();
        if (running.Count == 0) return;
        if (direction == 0)
        {
            var target = running.OrderBy(c => c.GlobalPosition.DistanceSquaredTo(_player.GlobalPosition)).First();
            _spectateIndex = running.IndexOf(target);
        }
        else
        {
            _spectateIndex = ((_spectateIndex + direction) % running.Count + running.Count) % running.Count;
        }
        _rig.Target = running[_spectateIndex];
    }

    public void ToggleDoubleSpeed()
    {
        if (_phase == Phase.Over || _skipping) return;
        Engine.TimeScale = Engine.TimeScale > 1.5 ? 1.0 : 2.0;
    }

    public void SkipToResults()
    {
        if (_phase == Phase.Over) return;
        _skipping = true;
        Engine.MaxPhysicsStepsPerFrame = 16;
        Engine.TimeScale = 8.0;
    }

    private bool _loadPrinted;
    private readonly bool _flipTest = DevTools.Instance?.Arg("fliptest") != null;
    private bool _flipped;

    /// <summary>
    /// --loadtime: prints the time to the first frame of the race (the countdown on screen) and to the green light,
    /// from the launch or from the R that reloaded it; --loadtime=restart presses R itself once, two seconds after the
    /// green light, then quits after the reloaded race starts.
    /// </summary>
    private void ReportLoad(string what)
    {
        if (DevTools.Instance?.Arg("loadtime") is not { } mode) return;
        ulong now = Godot.Time.GetTicksMsec();
        bool reload = DevTools.SceneRequestedAt > 0;
        GD.Print($"[Load] {what}: {(reload ? now - DevTools.SceneRequestedAt : now) / 1000.0:0.00} s {(reload ? "after the restart" : "after launch")}");
        if (what != "green light") return;
        if (mode == "restart" && DevTools.SceneLoads++ == 0)
            GetTree().CreateTimer(2.0).Timeout += () => { DevTools.MarkSceneRequest(); GetTree().ReloadCurrentScene(); };
        else DevTools.QuitClean(GetTree(), 0);
    }

    public override void _Process(double delta)
    {
        if (!_loadPrinted) { _loadPrinted = true; ReportLoad("race on screen"); }
        if (_rig == null) return;
        if (PlayerOut && _phase != Phase.Over && _rig.Target is Car t && !IsRunning(t)) SpectateNext(0);
        // The crowd watches the car the camera follows.
        if (_venue.Look?.Crowd is { } crowd && _rig.Target is Car focus) crowd.ActionPoint = focus.GlobalPosition;
        // The pit stop camera orbits the car to the zone being repaired.
        _rig.Orbit = _rig.Target == _player && _pits.StopOf(_player) is { Stage: PitStops.Stage.Repair } stop ? OrbitAngle(stop.SelectedZone) : null;
    }

    public override void _ExitTree()
    {
        Engine.TimeScale = 1.0;
    }

    // ------------------------------------------------------------------ effects

    private void SpawnSparks(Vector3 point, float energy)
    {
        var sparks = new CpuParticles3D
        {
            OneShot = true,
            Explosiveness = 0.95f,
            Amount = (int)Mathf.Clamp(energy / 4000f, 8, 48),
            Lifetime = 0.55f,
            Direction = Vector3.Up,
            Spread = 75f,
            Gravity = new Vector3(0f, -12f, 0f),
            InitialVelocityMin = 3f,
            InitialVelocityMax = 9f,
            ScaleAmountMin = 0.5f,
            ScaleAmountMax = 1.2f,
            Mesh = new BoxMesh
            {
                Size = new Vector3(0.05f, 0.05f, 0.05f),
                Material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    AlbedoColor = new Color(1f, 0.8f, 0.3f),
                    EmissionEnabled = true,
                    Emission = new Color(1f, 0.6f, 0.15f),
                    EmissionEnergyMultiplier = 4f,
                },
            },
        };
        AddChild(sparks);
        sparks.GlobalPosition = point;
        sparks.Emitting = true;
        GetTree().CreateTimer(1.0).Timeout += sparks.QueueFree;
    }
}
