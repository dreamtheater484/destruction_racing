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
/// Destruction Derby in the Bowl (plan §4.1): 20 cars in a ring facing the centre, a 3-2-1 countdown, everyone
/// meets in the middle, spins and wrecks score, the last car running wins (+ survivor bonus). When the player is
/// wrecked they can watch another car or skip to the results. Since M4 it runs in the final Bowl
/// (<see cref="Bowl"/>, three lighting presets); presentation (crowd, audio, announcer, banners) listens to the
/// events below. Also runs headless with the PlayerProxy bot for the sim harness.
/// </summary>
public partial class DerbyMode : Node3D, IArenaView, IHudMode, Audio.IEventSound
{
    /// <summary>The paint colours of the field (the player's red first).</summary>
    public static readonly Color[] Liveries =
    {
        new(0.80f, 0.08f, 0.06f), new(0.05f, 0.25f, 0.70f), new(0.95f, 0.70f, 0.05f), new(0.10f, 0.50f, 0.20f),
        new(0.90f, 0.40f, 0.05f), new(0.55f, 0.10f, 0.55f), new(0.90f, 0.90f, 0.88f), new(0.12f, 0.12f, 0.13f),
        new(0.20f, 0.65f, 0.80f), new(0.70f, 0.55f, 0.35f),
    };

    /// <summary>Roof colour per division (DD1 PC scheme): Div 4 yellow, 3 blue, 2 red, 1 black.</summary>
    public static Color RoofColor(int division) => division switch
    {
        4 => new Color(0.95f, 0.80f, 0.10f),
        3 => new Color(0.15f, 0.40f, 0.85f),
        2 => new Color(0.80f, 0.10f, 0.10f),
        _ => new Color(0.05f, 0.05f, 0.05f),
    };

    public int CarCount { get; set; } = 20;
    /// <summary>The player's model (--car=&lt;id&gt;, see <see cref="CarCatalog"/>); the AI drive their own (<see cref="Drivers"/>).</summary>
    public string PlayerModel { get; set; } = "kitsune";
    /// <summary>The player's paint (DerbySetup's palette); null = the default red #7.</summary>
    public Livery? PlayerLivery { get; set; }
    /// <summary>Balance sims (--randomcars): every AI driver gets a random model, so each car meets every archetype.</summary>
    public bool RandomCars { get; set; }
    public bool Headless { get; set; }
    /// <summary>Drive the player's car with the PlayerProxy bot (sims).</summary>
    public bool ProxyPlayer { get; set; }
    public Personality ProxyProfile { get; set; } = Personality.AverageFan;
    /// <summary>The PlayerProxy's strategy: Normal, or one of the dominant-strategy test bots (plan §17.4).</summary>
    public DerbyBrain.Strategy ProxyStrategy { get; set; } = DerbyBrain.Strategy.Normal;
    public int Seed { get; set; } = 1;
    public RulesConfig Rules { get; set; } = RulesConfig.Standard;
    /// <summary>Safety stop for sims; the Bowl itself has no time limit (✱ A8).</summary>
    public double MaxDuration { get; set; } = 600;
    /// <summary>The Single Event time limit (s, 0 = none, plan §4.1): then the most points wins, then the least damage.</summary>
    public double TimeLimit { get; set; }
    public double? TimeLeft => TimeLimit > 0 ? Math.Max(0.0, TimeLimit - _time) : null;
    public float CountdownTime { get; set; } = 3f;
    /// <summary>Debug: print every running car's state every N seconds (0 = off).</summary>
    public double DumpEvery { get; set; }
    /// <summary>The Bowl's lighting (--preset=afternoon|golden|night).</summary>
    public BowlPreset Preset { get; set; } = BowlPreset.Afternoon;
    /// <summary>The championship division of the event (4 = lowest); scales the player-attention budget.</summary>
    public int Division { get; set; } = 4;
    /// <summary>Difficulty: AI allowed on the player at once beyond the division's budget (−1 easy, +1 hard).</summary>
    public int AttentionBonus { get; set; }
    public int PlayerNumber { get; set; } = 7;
    /// <summary>Championship: each AI driver's division by name (their roof colours); null = spread over the four.</summary>
    public IReadOnlyDictionary<string, int>? DriverDivisions { get; set; }
    /// <summary>Total Destruction (DD1/DD2): every AI hunts the player; it ends when the player is wrecked.</summary>
    public bool TotalDestruction { get; set; }
    /// <summary>Duel (DD1): one rival, by name; the field is the two of you.</summary>
    public string? Rival { get; set; }
    /// <summary>The face-off start: two facing rows instead of the ring (the champion's bonus variant).</summary>
    public bool FaceOff { get; set; }
    /// <summary>Practice: the AI cars stand still as targets (a free damage test).</summary>
    public bool ParkedTargets { get; set; }
    public bool HuntPlayerOnly => TotalDestruction;
    /// <summary>Total Destruction: the run just ended beat the best score (the announcer's record call).</summary>
    public bool TotalDestructionRecord { get; private set; }

    public event Action<DerbyMode>? Finished;
    /// <summary>Every measured hit (car-car and car-wall), after the referee saw it.</summary>
    public event Action<Impact>? ImpactHappened;
    /// <summary>Every referee event (hits, spins, damage, wheels lost, wrecks, bonuses).</summary>
    public event Action<RefereeEvent>? RefereeEventRaised;
    /// <summary>The number of cars still running changed (wrecks).</summary>
    public event Action<int>? RemainingChanged;
    /// <summary>GO: the countdown ended.</summary>
    public event Action? Started;
    /// <summary>The countdown ticks: 3, 2, 1.</summary>
    public event Action<int>? CountdownTick;
    /// <summary>The music's intensity from the cars left (5, 3, 2: the final duel).</summary>
    public event Action<int>? IntensityChanged;
    public event Action? Ended;
    public float CrowdExcitement => _arena.Crowd?.Excitement ?? 0.3f;

    private enum Phase { Countdown, Running, Over }

    private readonly List<Car> _cars = new();
    private readonly Dictionary<int, Car> _byId = new();
    private readonly Dictionary<int, DerbyBrain> _brains = new();
    private readonly Dictionary<int, DriverInfo> _drivers = new();
    private Taunts? _taunts;
    private readonly Dictionary<int, (Car? Attacker, double Time)> _lastHitBy = new();
    private readonly Dictionary<int, (float Boost, double Until)> _boost = new();
    private readonly Dictionary<int, double> _lastAttackTime = new();
    private readonly Dictionary<int, List<(double Time, bool Tail)>> _attacks = new();
    private readonly Dictionary<int, CarSample> _samples = new();
    private readonly List<(int Car, int Points)> _wreckBatch = new();

    private Phase _phase = Phase.Countdown;
    private float _countdown;
    private int _lastTick = int.MaxValue;
    private double _time;
    private double _lastHitTime;
    private int? _leader;
    private Referee _referee = null!;
    private DerbyStandings _standings = null!;
    private ImpactSystem _impacts = null!;
    private DerbyMetrics _metrics = null!;
    private Car _player = null!;
    private Bowl _arena = null!;
    private CameraRig? _rig;
    private EventHud? _hud;
    private Node3D? _beacon;
    private Random _rng = null!;
    private bool _skipping;
    private int _spectateIndex;
    private Replay.ReplayRecorder? _recorder;
    private Replay.ReplayPlayer? _replay;
    private TvDirector? _tvLive;
    private bool _spectateTv = true;
    private double _playerWreckTime = -1;

    public Referee Referee => _referee;
    public DerbyStandings Standings => _standings;
    public DerbyMetricsResult MetricsResult => _metrics.Result;
    public bool IsOver => _phase == Phase.Over;
    public bool PlayerOut => _referee.IsWrecked(_player.Id);
    public Bowl Arena => _arena;
    public CameraRig? Rig => _rig;
    public EventHud? Hud => _hud;
    public Car PlayerCar => _player;
    public int Remaining => _standings.Remaining;
    public bool Skipping => _skipping;
    public DerbyPresenter? Presenter { get; private set; }
    public Audio.EventAudio? Sound { get; private set; }
    public Car? CarById(int id) => _byId.GetValueOrDefault(id);
    public double CountdownLeft => _countdown;
    public string DriverName(Car car) => car == _player ? "You" : _drivers.TryGetValue(car.Id, out var d) ? d.Short : "?";
    public DriverInfo? DriverOf(Car car) => _drivers.GetValueOrDefault(car.Id);

    // ------------------------------------------------------------------ IHudMode

    public string HudRank
    {
        get
        {
            int points = _referee.Score(_player.Id).Total;
            return $"{1 + _cars.Count(c => _referee.Score(c.Id).Total > points):00}";
        }
    }
    public string HudStatus => $"{_standings.Remaining}  Still Running";
    public string HudRankLabel => "POSITION";
    public string HudStatusLabel => "STILL RUNNING";
    public string HudStatusValue => $"{_standings.Remaining}";
    public string? HudWarning => null;

    public IReadOnlyList<EventHud.ResultRow> StandingsRows() =>
        _cars.OrderByDescending(c => _referee.Score(c.Id).Total)
            .Select((c, i) => new EventHud.ResultRow(i + 1, c.Number, DriverName(c), c.RoofColor,
                _referee.Score(c.Id).Get(ScoreCategory.Spin), _referee.Score(c.Id).Get(ScoreCategory.Wreck),
                _referee.Score(c.Id).Get(ScoreCategory.Survivor), _referee.Score(c.Id).Total, c == _player,
                _referee.IsWrecked(c.Id) ? "wrecked" : ""))
            .ToList();

    public string StandingsText()
    {
        var lines = _cars
            .OrderByDescending(c => _referee.Score(c.Id).Total)
            .Take(12)
            .Select((c, i) => $"{i + 1,2}.  #{c.Number,-3} {DriverName(c),-11} {_referee.Score(c.Id).Total,4} pts" +
                              (_referee.IsWrecked(c.Id) ? "   wrecked" : ""));
        return "STANDINGS (points)\n" + string.Join("\n", lines);
    }

    // ------------------------------------------------------------------ IArenaView

    public List<Car> Cars => _cars;
    public bool IsRunning(Car car) => _phase != Phase.Over && !_referee.IsWrecked(car.Id);
    public int? LeaderId => _leader;
    public double Time => _time;
    public Vector2 ArenaSemiAxes => new(Bowl.SemiAxisX - Bowl.BarrierInset, Bowl.SemiAxisZ - Bowl.BarrierInset);
    public Car? Player => _player;
    /// <summary>
    /// At most N AI hunt the player at once (plan §10.4): N = 2 in Division 4, 3 in Divisions 3 and 2, 4 in Division 1,
    /// and never more than 2 in the first minute (no early gang-ups).
    /// </summary>
    public int PlayerAttentionBudget => TotalDestruction ? CarCount
        : Math.Max(1, (_time < 60.0 ? 2 : Division switch { 4 => 2, 1 => 4, _ => 3 }) + AttentionBonus);
    public double LastAttackTime(Car car) => _lastAttackTime.GetValueOrDefault(car.Id, -100);

    public float TailRamShare(Car car)
    {
        if (!_attacks.TryGetValue(car.Id, out var list)) return 0f;
        list.RemoveAll(a => _time - a.Time > 45.0);
        return list.Count < 3 ? 0f : (float)list.Count(a => a.Tail) / list.Count;
    }
    public int CountTargeting(Car target, Car asker) => _brains.Count(kv => kv.Key != asker.Id && kv.Value.Target == target);
    public (Car? Attacker, double Time) LastHitBy(Car victim) => _lastHitBy.TryGetValue(victim.Id, out var h) ? h : (null, -100);
    public float AggressionBoost(Car car) => _boost.TryGetValue(car.Id, out var b) && b.Until > _time ? b.Boost : 0f;

    // ------------------------------------------------------------------ set-up

    public override void _Ready()
    {
        // As the main scene: --proxy lets the PlayerProxy bot drive (demos, captures), --seed=N picks the field.
        if (GetParent() == GetTree().Root && DevTools.Instance is { } dev)
        {
            if (Session.Request is { InBowl: true } req)
            {
                Preset = req.Preset;
                PlayerModel = req.Car;
                Division = req.Division;
                PlayerLivery = req.Livery;
                PlayerNumber = req.Number;
                Seed = req.Seed;
                DriverDivisions = req.DriverDivisions;
                AttentionBonus = req.AttentionBonus;
                FaceOff = req.FaceOff;
                TotalDestruction = req.Kind == EventKind.TotalDestruction;
                Rival = req.Kind == EventKind.Duel ? req.Rival : null;
                CarCount = req.Kind == EventKind.Duel ? 2 : 1 + Math.Clamp(req.Opponents, 0, 19);
                ParkedTargets = req.Parked;
                // The game has no time limit unless you set one (plan §4.1, A8); the sims keep their safety stop.
                TimeLimit = req.TimeLimit;
                MaxDuration = req.TimeLimit > 0 ? req.TimeLimit : double.PositiveInfinity;
            }
            if (dev.Arg("proxy") != null) ProxyPlayer = true;
            if (int.TryParse(dev.Arg("seed"), out int seed)) Seed = seed;
            if (dev.Arg("car") is { } car) PlayerModel = car;
            if (dev.Arg("randomcars") != null) RandomCars = true;
            if (dev.Arg("preset") is { } preset) Preset = Bowl.Parse(preset);
            if (int.TryParse(dev.Arg("division"), out int division)) Division = Math.Clamp(division, 1, 4);
            // --duel[=NAME] and --totaldestruction: the two Bowl variants, started directly (tests, captures).
            if (dev.Arg("duel") is { } duel)
            {
                Rival = Drivers.ByName(duel)?.Name ?? Drivers.All[0].Name;
                CarCount = 2;
            }
            if (dev.Arg("totaldestruction") != null) TotalDestruction = true;
        }
        // --pinned=S (experiments): how long a car may rock in place before it counts as immobilised.
        if (float.TryParse(DevTools.Instance?.Arg("pinned"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pinned))
            Rules = Rules with { PinnedWreckTime = pinned };
        // Experiments: --reartough=X (rear toughness), --opening=SCALE,SECONDS (the opening damage ramp),
        // --attackshare=X (the attacker's share of contact damage).
        if (float.TryParse(DevTools.Instance?.Arg("reartough"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float rear))
            Rules = Rules with { RearToughness = rear };
        if (float.TryParse(DevTools.Instance?.Arg("attackshare"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float share))
            Rules = Rules with { AttackerDamageShare = share };
        if (DevTools.Instance?.Arg("opening")?.Split(',') is [var os, var ot]
            && float.TryParse(os, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float openScale)
            && float.TryParse(ot, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float openTime))
            Rules = Rules with { OpeningCrunchDamageScale = openScale, OpeningCrunchTime = openTime };
        // Practice targets stand still for ever: the immobilised rule must not wreck them.
        if (ParkedTargets) Rules = Rules with { ImmobilisedWreckTime = 1e6f, PinnedWreckTime = 1e6f };
        _rng = new Random(Seed);
        _countdown = CountdownTime;
        if (!Headless) Controls.Apply(Controls.Current);

        _arena = new Bowl { Preset = Preset, Visuals = !Headless };
        AddChild(_arena);

        _impacts = new ImpactSystem { Clock = () => _time };
        AddChild(_impacts);

        SpawnField();
        _referee = new Referee(Rules, GameMode.DestructionDerby, _cars.Select(c => (c.Id, c.RefereeToughness)));
        foreach (var c in _cars)
        {
            c.Damage = _referee.Damage(c.Id);
            c.InputLocked = true;
            _impacts.Register(c);
        }
        _impacts.Referee = _referee;
        _impacts.Impacted += OnImpact;
        _standings = new DerbyStandings(_cars.Select(c => c.Id));
        _metrics = new DerbyMetrics(_player, Seed);

        if (!Headless)
        {
            _rig = new CameraRig { Target = _player };
            AddChild(_rig);
            _hud = new EventHud { Mode = this, PlayerCar = _player, ViewName = () => _rig.Mode.ToString() };
            AddChild(_hud);
            // --radiotest (captures): a taunt from the first driver during the countdown, to check the radio banner.
            if (DevTools.Instance?.Arg("radiotest") != null && _drivers.Values.FirstOrDefault() is { } d)
                _hud.Radio(d.Number, d.Name, Taunts.NationCode(d.Nation), "Out of my way!", d.Livery.Paint);
            _beacon = BuildBeacon();
            AddChild(_beacon);
            AddChild(new Fx.GroundMarks { Cars = _cars, Floor = _arena.Floor });
            // Lamps on at dusk; at night real headlight spots and beams on the cars near the camera.
            if (_arena.FloodlightsOn)
                AddChild(new HeadlightRig { Cars = _cars, Beams = _arena.IsNight, Focus = () => _rig.Target });
            Presenter = new DerbyPresenter { Mode = this, SlowMotion = GameSettings.SlowMotion };
            AddChild(Presenter);
            Sound = new Audio.EventAudio { Mode = this };
            AddChild(Sound);
            _recorder = new Replay.ReplayRecorder { Cars = _cars, Clock = () => _time };
            AddChild(_recorder);
            if (GetParent() == GetTree().Root && Session.Request is { InBowl: true })
            {
                _hud.MenuScene = Session.FrontEndScene;
                _hud.Championship = Session.InChampionship;
                _hud.DivisionTable = Session.DivisionTable(EventPointsOf);
                _hud.DivisionTitle = Session.DivisionTitle;
            }
            _hud.Replay = () => { if (_replay == null) StartReplay(); };
            _hud.Highlights = () => { if (_replay == null) StartReplay(highlights: true); };
            _hud.ReplayActive = () => _replay != null;
        }
    }

    private void SpawnField()
    {
        // The 19 named drivers in a seeded order, each in their own car and signature livery (plan §10.2).
        var field = Drivers.Field(_rng, RandomCars);
        if (Rival != null && Drivers.ByName(Rival) is { } rival) field = new List<DriverInfo> { rival };
        float ringX = Bowl.SemiAxisX * 0.78f, ringZ = Bowl.SemiAxisZ * 0.78f;

        for (int i = 0; i < CarCount; i++)
        {
            float a = Mathf.Pi * 0.5f + Mathf.Tau * i / CarCount;
            var pos = new Vector3(ringX * Mathf.Cos(a), 0.62f, ringZ * Mathf.Sin(a));
            var heading = new Vector3(-pos.X, 0f, -pos.Z).Normalized();
            if (FaceOff) (pos, heading) = FaceOffSlot(i);
            var driver = i == 0 ? null : field[(i - 1) % field.Count];
            int division = i == 0 ? Division
                : DriverDivisions != null && DriverDivisions.TryGetValue(driver!.Name, out int dd) ? dd : 4 - (i % 4);
            var roof = RoofColor(division);
            var livery = driver?.LiveryFor(roof) ?? (PlayerLivery ?? DefaultPlayerLivery) with { Number = PlayerNumber, Roof = roof };
            var car = new Car
            {
                Id = i + 1,
                Number = livery.Number,
                PaintColor = livery.Paint,
                RoofColor = roof,
                Livery = livery,
                ModelId = driver?.Car ?? PlayerModel,
                BuildVisuals = !Headless,
                Transform = new Transform3D(Basis.LookingAt(heading, Vector3.Up), pos),
            };
            if (driver != null) _drivers[car.Id] = driver;
            AddChild(car);
            _cars.Add(car);
            _byId[car.Id] = car;

            if (i == 0)
            {
                _player = car;
                if (ProxyPlayer)
                {
                    var brain = new DerbyBrain(ProxyProfile, Seed * 1000 + 1, ProxyStrategy);
                    car.Driver = new PlayerProxyInput(brain, this, Seed * 7919 + 3);
                }
                else
                {
                    car.Driver = new PlayerInput();
                    GameSettings.ApplyAssists(car);
                }
            }
            else if (ParkedTargets)
            {
                car.Driver = new ParkedDriver();
            }
            else
            {
                var brain = new DerbyBrain(driver!.Personality, Seed * 1000 + i + 1);
                _brains[car.Id] = brain;
                car.Driver = new AIDriver(brain, this);
            }
        }
    }

    /// <summary>
    /// The face-off start: two rows across the Bowl's short axis facing each other, the player in the middle of the
    /// near row and the rest alternating between the rows.
    /// </summary>
    private (Vector3 Pos, Vector3 Heading) FaceOffSlot(int i)
    {
        int row = i % 2;
        int perRow = (CarCount + 1 - row) / 2;
        int slot = i / 2;                                   // 0 = the middle, then right, left, right …
        int side = slot % 2 == 1 ? 1 : -1;
        float x = slot == 0 ? 0f : side * ((slot + 1) / 2) * Mathf.Min(8f, Bowl.SemiAxisX * 1.4f / Mathf.Max(1, perRow));
        float z = (row == 0 ? 1f : -1f) * Bowl.SemiAxisZ * 0.6f;
        return (new Vector3(x, 0.62f, z), new Vector3(0f, 0f, row == 0 ? -1f : 1f));
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
                int tick = (int)Mathf.Ceil(_countdown);
                if (tick < _lastTick && tick >= 1)
                {
                    _lastTick = tick;
                    CountdownTick?.Invoke(tick);
                }
                _countdown -= dt;
                if (_countdown <= 0f)
                {
                    foreach (var c in _cars) c.InputLocked = false;
                    _phase = Phase.Running;
                    _hud?.Popup("GO!", new Color(0.3f, 1f, 0.3f), big: true);
                    Started?.Invoke();
                    // Tests (--autoskip): fast-simulate the whole derby to the results.
                    if (!Headless && DevTools.Instance?.Arg("autoskip") != null) SkipToResults();
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
        _leader = _referee.Leader;
        _referee.DamageScale = Mathf.Lerp(Rules.OpeningCrunchDamageScale, 1f, Mathf.Clamp((float)(_time / Rules.OpeningCrunchTime), 0f, 1f));

        _samples.Clear();
        foreach (var c in _cars)
        {
            var lc = c.LastControls;
            _samples[c.Id] = new CarSample(c.AngularVelocity.Y, c.LinearVelocity.Length(), lc.Throttle > 0.3f || lc.Reverse,
                c.GlobalPosition.X, c.GlobalPosition.Z);
            // A car that fell out of the world or is stuck far from the floor is out.
            if (!_referee.IsWrecked(c.Id) && c.GlobalPosition.Y < -5f) _referee.ForceWreck(_time, c.Id);
        }
        _referee.Step(_time, dt, _samples);

        _wreckBatch.Clear();
        foreach (var e in _referee.DrainEvents())
        {
            _metrics.OnRefereeEvent(e);
            HandleEvent(e);
            RefereeEventRaised?.Invoke(e);
            if (e.Kind == RefereeEventKind.Wrecked) _wreckBatch.Add((e.Car, _referee.Score(e.Car).Total));
        }
        if (_wreckBatch.Count > 0)
        {
            _standings.Eliminate(_time, _wreckBatch);
            _arena.SetScoreboard($"THE BOWL   CARS LEFT {_standings.Remaining}");
            RemainingChanged?.Invoke(_standings.Remaining);
            IntensityChanged?.Invoke(_standings.Remaining switch { <= 2 => 3, <= 3 => 2, <= 5 => 1, _ => 0 });
        }

        _metrics.Step(_time, dt, _standings.Remaining, !_referee.IsWrecked(_player.Id));
        Director();
        UpdateBeacon();
        if (_hud != null) _hud.Hunters = _brains.Count(kv => kv.Value.Target == _player && IsRunning(_byId[kv.Key]));

        if (DumpEvery > 0 && (int)(_time / DumpEvery) != (int)((_time - dt) / DumpEvery)) DumpState();
        if (_standings.IsOver) EndEvent(timedOut: false);
        else if (TotalDestruction && _referee.IsWrecked(_player.Id)) EndEvent(timedOut: false);
        else if (_time >= MaxDuration)
        {
            _standings.EndByPoints(_cars.ToDictionary(c => c.Id, c => _referee.Score(c.Id).Total),
                _cars.ToDictionary(c => c.Id, c => (c.Health(DamageZone.Front) + c.Health(DamageZone.Rear) + c.Health(DamageZone.Left) + c.Health(DamageZone.Right)) / 4f));
            EndEvent(timedOut: true);
        }
    }

    private void HandleEvent(RefereeEvent e)
    {
        if (_hud != null) Taunt(e);
        switch (e.Kind)
        {
            case RefereeEventKind.Hit:
                _lastHitBy[e.Car] = (_byId.GetValueOrDefault(e.Other), e.Time);
                if (e.Amount >= DerbyMetrics.MetricHitEnergy)
                {
                    _lastAttackTime[e.Other] = e.Time;
                    _lastHitTime = e.Time;
                    if (_byId.TryGetValue(e.Other, out var attacker))
                    {
                        if (!_attacks.TryGetValue(e.Other, out var list)) _attacks[e.Other] = list = new();
                        list.Add((e.Time, attacker.LastControls.Reverse || attacker.ForwardSpeed < -1f));
                    }
                }
                break;
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
            case RefereeEventKind.WheelLost:
                if (e.Car == _player.Id) _hud?.Popup("YOU'VE LOST A WHEEL!", new Color(1f, 0.5f, 0.2f));
                break;
            case RefereeEventKind.Wrecked:
            {
                var victim = _byId[e.Car];
                string by = e.Other > 0 ? $" by #{_byId[e.Other].Number}" : "";
                _hud?.Feed($"#{victim.Number} WRECKED{by}{(e.Points > 0 ? $"  +{e.Points}" : "")}");
                if (e.Other == _player.Id)
                {
                    _hud?.Popup(e.Doubled ? $"LEADER ×2   WRECKED #{victim.Number}   +{e.Points}" : $"WRECKED #{victim.Number}   +{e.Points}",
                        new Color(1f, 0.45f, 0.2f));
                    if (_hud != null) WorldPopup.Show(this, victim, $"WRECKED  +{e.Points}", new Color(1f, 0.45f, 0.2f));
                }
                if (e.Car == _player.Id) OnPlayerWrecked();
                break;
            }
        }
    }

    private void OnImpact(Impact i)
    {
        _metrics.OnImpact(i);
        ImpactHappened?.Invoke(i);
        if (_phase == Phase.Running) _recorder?.OnImpact(i);
        if (Headless) return;
        if (i.Energy > 6_000f && i.A.Visual == null) SpawnSparks(i.Point, i.Energy);
        if (_rig != null && (i.A == _rig.Target || i.B == _rig.Target))
            _rig.AddShake(Mathf.Clamp(i.Energy / 150_000f, 0.05f, 0.8f));
    }

    /// <summary>
    /// Anti-stall director (plan §10.4): while 5+ cars run and nobody has hit anyone for 4 s, wake the passive ones.
    /// In the final cars, after 12 s without a real hit (7 s in the final duel), every AI is nudged to attack
    /// forwards for a while — towards the other car, never steered for it.
    /// </summary>
    private void Director()
    {
        if (_standings.Remaining <= 4)
        {
            if (_time - _lastHitTime < (_standings.Remaining == 2 ? 7.0 : 12.0)) return;
            _lastHitTime = _time;
            foreach (var c in _cars.Where(c => c != _player && IsRunning(c))) _boost[c.Id] = (0.8f, _time + 8.0);
            return;
        }
        if (_time - _lastHitTime < 4.0) return;
        _lastHitTime = _time; // re-arm
        var passive = _cars.Where(c => c != _player && IsRunning(c))
            .OrderBy(c => _lastAttackTime.GetValueOrDefault(c.Id, -100))
            .Take(5);
        // A boost of 0.8 or more commits the car to attacking forwards (see DerbyBrain).
        foreach (var c in passive) _boost[c.Id] = (0.8f, _time + 6.0);
    }

    private void UpdateBeacon()
    {
        if (_beacon == null) return;
        var leader = _leader is int id ? _byId[id] : null;
        _beacon.Visible = leader != null && IsRunning(leader);
        if (leader == null) return;
        _beacon.GlobalPosition = leader.GlobalPosition + Vector3.Up * (2.0f + 0.15f * Mathf.Sin((float)_time * 4f));
        // Sized in the world from afar, capped near the lens: from a camera on the wall a few metres away the
        // full-size marker filled a third of the frame.
        float d = GetViewport().GetCamera3D() is { } cam ? cam.GlobalPosition.DistanceTo(_beacon.GlobalPosition) : 20f;
        _beacon.Scale = Vector3.One * Mathf.Clamp(d / 14f, 0.3f, 1f);
    }

    public void DumpState()
    {
        GD.Print($"[Derby] t={_time:0} running={_standings.Remaining} last hit {_time - _lastHitTime:0.0}s ago");
        foreach (var c in _cars.Where(IsRunning))
        {
            string brain = c == _player ? "player" : _brains.TryGetValue(c.Id, out var b)
                ? $"{b.Personality.Archetype,-8} {b.Current,-10} target #{b.Target?.Number}" : "?";
            var p = c.GlobalPosition;
            GD.Print($"[Derby]   #{c.Number,-3} {brain}  v={c.LinearVelocity.Length():0.0} pos=({p.X:0},{p.Y:0.0},{p.Z:0}) " +
                     $"F{c.Health(DamageZone.Front) * 100:0} R{c.Health(DamageZone.Rear) * 100:0} L{c.Health(DamageZone.Left) * 100:0} Rt{c.Health(DamageZone.Right) * 100:0} " +
                     $"wheels lost {c.Damage?.WheelsLost} up={c.GlobalTransform.Basis.Y.Y:0.00} ctl t{c.LastControls.Throttle:0.0} b{c.LastControls.Brake:0.0} r{(c.LastControls.Reverse ? 1 : 0)} s{c.LastControls.Steer:0.0}");
        }
    }

    private void OnPlayerWrecked()
    {
        _playerWreckTime = _time;
        _hud?.ShowWrecked();
        SpectateNext(0);
        // Spectating is on the TV cameras (DD2); T switches to the chase camera and back.
        if (_rig != null && _tvLive == null)
        {
            _tvLive = new TvDirector();
            AddChild(_tvLive);
        }
        SetSpectateCamera(_spectateTv);
    }

    private void SetSpectateCamera(bool tv)
    {
        _spectateTv = tv;
        if (_tvLive == null || _rig == null) return;
        if (tv)
        {
            _tvLive.Focus = _rig.Target;
            _tvLive.Current = true;
            _tvLive.Snap();
        }
        else _rig.Current = true;
    }

    public void ToggleSpectateCamera() => SetSpectateCamera(!_spectateTv);

    /// <summary>The replay (results screen, V): from just before the player's wreck, or the last 30 seconds.</summary>
    private void StartReplay(bool highlights = false)
    {
        if (_recorder == null || _recorder.Times.Count == 0) return;
        double start = _playerWreckTime >= 0 ? _playerWreckTime - 8.0 : _recorder.Duration - 30.0;
        _replay = new Replay.ReplayPlayer
        {
            Recording = _recorder,
            Cars = _cars,
            StartFocus = _player,
            StartTime = Math.Max(0.0, start),
            Rig = _rig,
            Highlights = highlights,
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

    private void EndEvent(bool timedOut)
    {
        if (_phase == Phase.Over) return;
        _phase = Phase.Over;
        if (_standings.WinnerSurvived && _standings.Winner is int w)
        {
            int bonus = Scoring.ScoresDamage(Rules, GameMode.DestructionDerby) ? Rules.BowlSurvivorBonus : 0;
            int added = _referee.AddBonus(_time, w, ScoreCategory.Survivor, bonus);
            if (w == _player.Id) _hud?.Popup($"LAST CAR RUNNING!   SURVIVOR +{added}", new Color(1f, 0.85f, 0.2f), big: true);
        }
        _referee.DrainEvents();
        _metrics.Finish(_time, _standings, _referee, timedOut);
        if (Headless || DevTools.Instance?.Arg("carresults") != null) PrintCarResults();
        if (GetParent() == GetTree().Root && Session.InChampionship)
            Session.ReportResults(_cars.Select(c => new DriverResult(
                c == _player ? DestructionRacing.Rules.Career.PlayerKey : _drivers.TryGetValue(c.Id, out var d) ? d.Name : "?",
                _referee.Score(c.Id).Total, _referee.Score(c.Id).Wrecks, _standings.PlaceOf(c.Id) ?? 0)).ToList());
        foreach (var c in _cars) c.InputLocked = true;
        Engine.TimeScale = 1.0;
        _skipping = false;
        if (_recorder != null) _recorder.Recording = false;
        var results = BuildResults();
        var mine = results.FirstOrDefault(r => r.IsPlayer);
        string summary = mine.Place == 1 ? $"LAST CAR RUNNING!   ·   {mine.Total} POINTS" : $"YOU FINISHED {Ordinal(mine.Place)}   ·   {mine.Total} POINTS";
        string heading = Rival != null ? "Duel  –  Results" : "Destruction Derby  –  Results";
        if (TotalDestruction)
        {
            // Total Destruction (DD1): the score is the seconds survived plus the points.
            double survived = _standings.EliminatedAt(_player.Id) ?? _time;
            int score = (int)survived + mine.Total;
            bool best = score > Profile.Current.TotalDestructionBest;
            TotalDestructionRecord = best;
            if (best && !Headless) Profile.NoteTotalDestruction(score);
            heading = "Total Destruction  –  Results";
            summary = $"YOU SURVIVED {(int)survived / 60}:{(int)survived % 60:00}   ·   {mine.Total} POINTS   ·   SCORE {score}" + (best ? "   ·   NEW BEST!" : "");
        }
        _hud?.ShowResults(heading, "Survivor", results, summary);
        // Tests (--autoreplay): open the replay a moment after the results.
        if (!Headless && DevTools.Instance?.Arg("autoreplay") != null)
            GetTree().CreateTimer(1.5, true, false, true).Timeout += () => { if (_replay == null) StartReplay(DevTools.Instance?.Arg("autoreplay") == "highlights"); };
        Finished?.Invoke(this);
        Ended?.Invoke();
    }

    /// <summary>A driver's radio taunt for this event, if any (plan §10.2).</summary>
    private void Taunt(RefereeEvent e)
    {
        _taunts ??= new Taunts(Seed);
        if (_taunts.For(e, _player.Id, id => _drivers.GetValueOrDefault(id)) is var (d, line))
            _hud?.Radio(d.Number, d.Name, Taunts.NationCode(d.Nation), line, d.Livery.Paint);
    }

    /// <summary>The player's car without a chosen livery: red #7 with a white second colour.</summary>
    public static Livery DefaultPlayerLivery => new(Liveries[0], new Color(0.93f, 0.93f, 0.9f), 0, Wear: 0.15f, Dirt: 0.25f, Number: 7);

    /// <summary>Balance sims: one line per car (model, driver, place, points, survival) for tools/dev/balance_report.py.</summary>
    private void PrintCarResults()
    {
        foreach (var c in _cars)
        {
            var score = _referee.Score(c.Id);
            double survival = _standings.EliminatedAt(c.Id) ?? _time;
            string who = c == _player ? (ProxyPlayer ? "Proxy" : "Player") : _drivers.TryGetValue(c.Id, out var d) ? d.Archetype : "?";
            GD.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"[CarResult] mode=derby seed={Seed} car={c.ModelId} driver={who} place={_standings.PlaceOf(c.Id) ?? 0} " +
                $"points={score.Total} spin={score.Get(ScoreCategory.Spin)} wreck={score.Get(ScoreCategory.Wreck)} " +
                $"survival={survival:0.0} wrecked={(_referee.IsWrecked(c.Id) ? 1 : 0)} length={_time:0.0}" +
                $" who={(_drivers.TryGetValue(c.Id, out var di) ? Array.IndexOf(Drivers.All, di) : -1)} wrecks={score.Wrecks}"));
        }
    }

    private static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "TH" : (n % 10) switch { 1 => "ST", 2 => "ND", 3 => "RD", _ => "TH" });

    public IReadOnlyList<EventHud.ResultRow> BuildResults() =>
        _standings.FinishingOrder()
            .Select(id => _byId[id])
            .Select(c => new EventHud.ResultRow(
                _standings.PlaceOf(c.Id) ?? 0,
                c.Number,
                DriverName(c),
                c.RoofColor,
                _referee.Score(c.Id).Get(ScoreCategory.Spin),
                _referee.Score(c.Id).Get(ScoreCategory.Wreck),
                _referee.Score(c.Id).Get(ScoreCategory.Survivor),
                _referee.Score(c.Id).Total,
                c == _player))
            .ToList();

    // ------------------------------------------------------------------ player out: spectate / skip

    public void SpectateNext(int direction)
    {
        if (_rig == null) return;
        var running = _cars.Where(IsRunning).ToList();
        if (running.Count == 0) return;
        if (direction == 0)
        {
            // Start with the car closest to the wreck.
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

    /// <summary>Fast-simulates the rest of the derby to the final standings.</summary>
    public void SkipToResults()
    {
        if (_phase == Phase.Over) return;
        _skipping = true;
        Engine.MaxPhysicsStepsPerFrame = 16;
        Engine.TimeScale = 8.0;
    }

    private bool _loadPrinted;

    public override void _Process(double delta)
    {
        // --loadtime (plan §17.4): the first frame of the derby, after the launch or the menu's START / R.
        if (!_loadPrinted && !Headless && DevTools.Instance?.Arg("loadtime") != null)
        {
            _loadPrinted = true;
            ulong now = Godot.Time.GetTicksMsec();
            bool reload = DevTools.SceneRequestedAt > 0;
            GD.Print($"[Load] derby on screen: {(reload ? now - DevTools.SceneRequestedAt : now) / 1000.0:0.00} s {(reload ? "after the request" : "after launch")}");
        }
        // Keep following a running car while spectating.
        if (_rig != null && PlayerOut && _phase != Phase.Over && _rig.Target is Car t && !IsRunning(t)) SpectateNext(0);
        if (_tvLive != null && _rig != null && _replay == null) _tvLive.Focus = _rig.Target;
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
