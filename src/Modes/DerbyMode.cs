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
/// Destruction Derby in the Bowl (plan §4.1), grey-box M1b: 20 cars in a ring facing the centre, a 3-2-1
/// countdown, everyone meets in the middle, spins and wrecks score, the last car running wins (+ survivor
/// bonus). When the player is wrecked they can watch another car or skip to the results.
/// Also runs headless with the PlayerProxy bot for the sim harness.
/// </summary>
public partial class DerbyMode : Node3D, IArenaView, IHudMode
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
    /// <summary>The player's model (--car=kitsune|brougham|nordvik); the AI cycle through <see cref="Car.Roster"/>.</summary>
    public string PlayerModel { get; set; } = "kitsune";
    public bool Headless { get; set; }
    /// <summary>Drive the player's car with the PlayerProxy bot (sims).</summary>
    public bool ProxyPlayer { get; set; }
    public Personality ProxyProfile { get; set; } = Personality.AverageFan;
    public int Seed { get; set; } = 1;
    public RulesConfig Rules { get; set; } = RulesConfig.Standard;
    /// <summary>Safety stop for sims; the Bowl itself has no time limit (✱ A8).</summary>
    public double MaxDuration { get; set; } = 600;
    public float CountdownTime { get; set; } = 3f;
    /// <summary>Debug: print every running car's state every N seconds (0 = off).</summary>
    public double DumpEvery { get; set; }

    public event Action<DerbyMode>? Finished;

    private enum Phase { Countdown, Running, Over }

    private readonly List<Car> _cars = new();
    private readonly Dictionary<int, Car> _byId = new();
    private readonly Dictionary<int, DerbyBrain> _brains = new();
    private readonly Dictionary<int, (Car? Attacker, double Time)> _lastHitBy = new();
    private readonly Dictionary<int, (float Boost, double Until)> _boost = new();
    private readonly Dictionary<int, double> _lastAttackTime = new();
    private readonly Dictionary<int, CarSample> _samples = new();
    private readonly List<(int Car, int Points)> _wreckBatch = new();

    private Phase _phase = Phase.Countdown;
    private float _countdown;
    private double _time;
    private double _lastHitTime;
    private int? _leader;
    private Referee _referee = null!;
    private DerbyStandings _standings = null!;
    private ImpactSystem _impacts = null!;
    private DerbyMetrics _metrics = null!;
    private Car _player = null!;
    private GreyBoxArena _arena = null!;
    private CameraRig? _rig;
    private EventHud? _hud;
    private Node3D? _beacon;
    private Random _rng = null!;
    private bool _skipping;
    private int _spectateIndex;

    public Referee Referee => _referee;
    public DerbyStandings Standings => _standings;
    public DerbyMetricsResult MetricsResult => _metrics.Result;
    public bool IsOver => _phase == Phase.Over;
    public bool PlayerOut => _referee.IsWrecked(_player.Id);
    public double CountdownLeft => _countdown;
    public string DriverName(Car car) => car == _player ? "You" : _brains.TryGetValue(car.Id, out var b) ? b.Personality.Archetype : "?";

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
    public string? HudWarning => null;

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

    public IReadOnlyList<Car> Cars => _cars;
    public bool IsRunning(Car car) => _phase != Phase.Over && !_referee.IsWrecked(car.Id);
    public int? LeaderId => _leader;
    public double Time => _time;
    public Vector2 ArenaSemiAxes => new(_arena.SemiAxisX - 0.5f, _arena.SemiAxisZ - 0.5f);
    public Car? Player => _player;
    /// <summary>At most 2 AI hunt the player in the first minute (no early gang-ups), then 3 (plan §10.4).</summary>
    public int PlayerAttentionBudget => _time < 60.0 ? 2 : 3;
    public int CountTargeting(Car target, Car asker) => _brains.Count(kv => kv.Key != asker.Id && kv.Value.Target == target);
    public (Car? Attacker, double Time) LastHitBy(Car victim) => _lastHitBy.TryGetValue(victim.Id, out var h) ? h : (null, -100);
    public float AggressionBoost(Car car) => _boost.TryGetValue(car.Id, out var b) && b.Until > _time ? b.Boost : 0f;

    // ------------------------------------------------------------------ set-up

    public override void _Ready()
    {
        // As the main scene: --proxy lets the PlayerProxy bot drive (demos, captures), --seed=N picks the field.
        if (GetParent() == GetTree().Root && DevTools.Instance is { } dev)
        {
            if (dev.Arg("proxy") != null) ProxyPlayer = true;
            if (int.TryParse(dev.Arg("seed"), out int seed)) Seed = seed;
            if (dev.Arg("car") is { } car) PlayerModel = car;
        }
        // --pinned=S (experiments): how long a car may rock in place before it counts as immobilised.
        if (float.TryParse(DevTools.Instance?.Arg("pinned"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pinned))
            Rules = Rules with { PinnedWreckTime = pinned };
        _rng = new Random(Seed);
        _countdown = CountdownTime;
        if (!Headless) Controls.Apply(Controls.Current);

        _arena = new GreyBoxArena { StartCars = 0, WithOverviewCamera = false };
        AddChild(_arena);

        _impacts = new ImpactSystem { Clock = () => _time };
        AddChild(_impacts);

        SpawnField();
        _referee = new Referee(Rules, GameMode.DestructionDerby, _cars.Select(c => (c.Id, 1f)));
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
            _beacon = BuildBeacon();
            AddChild(_beacon);
        }
    }

    private void SpawnField()
    {
        var numbers = Enumerable.Range(2, 60).Where(n => n != 7).OrderBy(_ => _rng.Next()).ToList();
        var mix = Personality.FieldMix().OrderBy(_ => _rng.Next()).ToList();
        float ringX = _arena.SemiAxisX * 0.78f, ringZ = _arena.SemiAxisZ * 0.78f;

        for (int i = 0; i < CarCount; i++)
        {
            float a = Mathf.Pi * 0.5f + Mathf.Tau * i / CarCount;
            var pos = new Vector3(ringX * Mathf.Cos(a), 0.62f, ringZ * Mathf.Sin(a));
            var heading = new Vector3(-pos.X, 0f, -pos.Z).Normalized();
            int division = i == 0 ? 4 : 4 - (i % 4);
            var car = new Car
            {
                Id = i + 1,
                Number = i == 0 ? 7 : numbers[i],
                // The player's red is unique; the AI cycle through the other liveries.
                PaintColor = i == 0 ? Liveries[0] : Liveries[1 + (i - 1) % (Liveries.Length - 1)],
                RoofColor = RoofColor(division),
                ModelId = i == 0 ? PlayerModel : Car.Roster[i % Car.Roster.Length],
                BuildVisuals = !Headless,
                Transform = new Transform3D(Basis.LookingAt(heading, Vector3.Up), pos),
            };
            AddChild(car);
            _cars.Add(car);
            _byId[car.Id] = car;

            if (i == 0)
            {
                _player = car;
                if (ProxyPlayer)
                {
                    var brain = new DerbyBrain(ProxyProfile, Seed * 1000 + 1);
                    car.Driver = new PlayerProxyInput(brain, this, Seed * 7919 + 3);
                }
                else
                {
                    car.Driver = new PlayerInput();
                }
            }
            else
            {
                var brain = new DerbyBrain(mix[(i - 1) % mix.Count], Seed * 1000 + i + 1);
                _brains[car.Id] = brain;
                car.Driver = new AIDriver(brain, this);
            }
        }
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
                if (_countdown <= 0f)
                {
                    foreach (var c in _cars) c.InputLocked = false;
                    _phase = Phase.Running;
                    _hud?.Popup("GO!", new Color(0.3f, 1f, 0.3f), big: true);
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
            if (e.Kind == RefereeEventKind.Wrecked) _wreckBatch.Add((e.Car, _referee.Score(e.Car).Total));
        }
        if (_wreckBatch.Count > 0) _standings.Eliminate(_time, _wreckBatch);

        _metrics.Step(_time, dt, _standings.Remaining, !_referee.IsWrecked(_player.Id));
        Director();
        UpdateBeacon();

        if (DumpEvery > 0 && (int)(_time / DumpEvery) != (int)((_time - dt) / DumpEvery)) DumpState();
        if (_standings.IsOver) EndEvent(timedOut: false);
        else if (_time >= MaxDuration)
        {
            _standings.EndByPoints(_cars.ToDictionary(c => c.Id, c => _referee.Score(c.Id).Total));
            EndEvent(timedOut: true);
        }
    }

    private void HandleEvent(RefereeEvent e)
    {
        switch (e.Kind)
        {
            case RefereeEventKind.Hit:
                _lastHitBy[e.Car] = (_byId.GetValueOrDefault(e.Other), e.Time);
                if (e.Amount >= DerbyMetrics.MetricHitEnergy)
                {
                    _lastAttackTime[e.Other] = e.Time;
                    _lastHitTime = e.Time;
                }
                break;
            case RefereeEventKind.SpinTier when e.Other == _player.Id:
                _hud?.SpinProgress(e.Tier);
                break;
            case RefereeEventKind.SpinScored when e.Car == _player.Id:
                _hud?.Popup(e.Doubled ? $"LEADER ×2   SPIN {(int)e.Tier}   +{e.Points}" : $"SPIN {(int)e.Tier}   +{e.Points}",
                    e.Doubled ? new Color(1f, 0.8f, 0.2f) : Colors.White);
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
                    _hud?.Popup(e.Doubled ? $"LEADER ×2   WRECKED #{victim.Number}   +{e.Points}" : $"WRECKED #{victim.Number}   +{e.Points}",
                        new Color(1f, 0.45f, 0.2f));
                if (e.Car == _player.Id) OnPlayerWrecked();
                break;
            }
        }
    }

    private void OnImpact(Impact i)
    {
        _metrics.OnImpact(i);
        if (Headless) return;
        if (i.Energy > 6_000f && i.A.Visual == null) SpawnSparks(i.Point, i.Energy);
        if (_rig != null && (i.A == _rig.Target || i.B == _rig.Target))
            _rig.AddShake(Mathf.Clamp(i.Energy / 150_000f, 0.05f, 0.8f));
    }

    /// <summary>
    /// Anti-stall director (plan §10.4): while 5+ cars run and nobody has hit anyone for 6 s, wake the passive ones.
    /// In the final cars, after 12 s without a real hit, every AI is nudged to attack forwards for a while.
    /// </summary>
    private void Director()
    {
        if (_standings.Remaining <= 4)
        {
            if (_time - _lastHitTime < 12.0) return;
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
        if (leader != null)
            _beacon.GlobalPosition = leader.GlobalPosition + Vector3.Up * (2.0f + 0.15f * Mathf.Sin((float)_time * 4f));
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
        _hud?.ShowWrecked();
        SpectateNext(0);
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
        foreach (var c in _cars) c.InputLocked = true;
        Engine.TimeScale = 1.0;
        _skipping = false;
        _hud?.ShowResults("DESTRUCTION DERBY  –  RESULTS", "Survivor", BuildResults());
        Finished?.Invoke(this);
    }

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

    public override void _Process(double delta)
    {
        // Keep following a running car while spectating.
        if (_rig != null && PlayerOut && _phase != Phase.Over && _rig.Target is Car t && !IsRunning(t)) SpectateNext(0);
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
