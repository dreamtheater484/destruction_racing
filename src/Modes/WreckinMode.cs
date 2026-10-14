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
/// Wreckin' Racing on the figure-8 (plan §4.2), grey-box M1c: 20 cars on a 3-wide grid, a 3-2-1 countdown, a fixed
/// number of laps, spins and wrecks score, ×2 for hitting the car in 1st place (the leader beacon), wrecks are
/// DNFs and stay on track, finishing bonus 25/15/10. The race ends when the player finishes; when the player is
/// wrecked they can watch, speed up or skip to the results. Also runs headless with the PlayerProxy for sims.
/// </summary>
public partial class WreckinMode : Node3D, IRaceView, IHudMode
{
    private static readonly Color[] Liveries =
    {
        new(0.80f, 0.08f, 0.06f), new(0.05f, 0.25f, 0.70f), new(0.95f, 0.70f, 0.05f), new(0.10f, 0.50f, 0.20f),
        new(0.90f, 0.40f, 0.05f), new(0.55f, 0.10f, 0.55f), new(0.90f, 0.90f, 0.88f), new(0.12f, 0.12f, 0.13f),
        new(0.20f, 0.65f, 0.80f), new(0.70f, 0.55f, 0.35f),
    };

    public int CarCount { get; set; } = 20;
    /// <summary>Laps per race, data-driven per layout (plan §4.2: 6–10).</summary>
    public int Laps { get; set; } = 6;
    /// <summary>The player's grid slot (0 = pole).</summary>
    public int PlayerGridSlot { get; set; } = 12;
    public bool Headless { get; set; }
    public bool ProxyPlayer { get; set; }
    public Personality ProxyProfile { get; set; } = Personality.AverageFan;
    public int Seed { get; set; } = 1;
    public RulesConfig Rules { get; set; } = RulesConfig.Standard;
    /// <summary>
    /// AI pace for the player's division (plan §10.5) ✱: Div 4 is 0.97, so an average player who races cleanly can
    /// fight for the top three (at 1.0 the AI racers out-pace an average player and only hunting pays).
    /// </summary>
    public float AiPaceScale { get; set; } = 0.97f;
    /// <summary>Back-marker catch-up (plan §4.2/§10.5; off in Purist).</summary>
    public bool CatchUpEnabled { get; set; } = true;
    public double MaxDuration { get; set; } = 900;
    public float CountdownTime { get; set; } = 3f;
    public double DumpEvery { get; set; }
    /// <summary>Debug: print every wall hit above the damage threshold.</summary>
    public bool DebugWallHits { get; set; }
    /// <summary>Debug: print every hit on the player's car above the damage threshold.</summary>
    public bool DebugPlayerHits { get; set; }

    public event Action<WreckinMode>? Finished;

    private enum Phase { Countdown, Running, Over }

    private readonly List<Car> _cars = new();
    private readonly Dictionary<int, Car> _byId = new();
    private readonly Dictionary<int, RaceBrain> _brains = new();
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
    private FigureEightTrack _track = null!;
    private Car _player = null!;
    private CameraRig? _rig;
    private EventHud? _hud;
    private Node3D? _beacon;
    private Random _rng = null!;
    private bool _skipping;
    private int _spectateIndex;
    private float _wrongWayTime;

    public Referee Referee => _referee;
    public RaceStandings Standings => _standings;
    public RaceMetricsResult MetricsResult => _metrics.Result;
    public FigureEightTrack Layout => _track;
    public bool IsOver => _phase == Phase.Over;
    public bool PlayerOut => _referee.IsWrecked(_player.Id);
    public double CountdownLeft => _countdown;
    public string DriverName(Car car) => car == _player ? "You" : _brains.TryGetValue(car.Id, out var b) ? b.Personality.Archetype : "?";
    public float ProgressOf(Car car) => _progress[car.Id];

    // ------------------------------------------------------------------ IRaceView

    public IReadOnlyList<Car> Cars => _cars;
    public bool IsRunning(Car car) => _phase != Phase.Over && !_referee.IsWrecked(car.Id);
    public TrackPath Track => _track.Path;
    public double Time => _time;
    public Car? Player => _player;
    public int? LeaderId => _standings?.Leader;
    public (float S, float Lateral) TrackPos(Car car) => (_s[car.Id], _lateral[car.Id]);
    public IReadOnlyList<float> CrossoverPasses => _crossPasses;

    public float CatchUp(Car car)
    {
        if (!CatchUpEnabled || car == _player || _standings.HasFinished(car.Id)) return 1f;
        float gap = _leaderProgress - _progress[car.Id];
        return 1f + 0.06f * Mathf.Clamp((gap - 120f) / 250f, 0f, 1f);
    }

    // ------------------------------------------------------------------ IHudMode

    public string HudRank => $"{_standings.PositionOf(_player.Id):00}";
    public string HudStatus => _standings.HasFinished(_player.Id) ? "FINISHED" : $"Lap  {_standings.LapOf(_player.Id)}/{Laps}";
    public string? HudWarning => _wrongWayTime > 0.8f && _phase == Phase.Running && !PlayerOut ? "WRONG WAY" : null;

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
        if (GetParent() == GetTree().Root && DevTools.Instance is { } dev)
        {
            if (dev.Arg("proxy") != null) ProxyPlayer = true;
            if (int.TryParse(dev.Arg("seed"), out int seed)) Seed = seed;
            if (int.TryParse(dev.Arg("laps"), out int laps)) Laps = Math.Max(1, laps);
        }
        _rng = new Random(Seed);
        _countdown = CountdownTime;
        if (!Headless) Controls.Apply(Controls.Current);

        _track = new FigureEightTrack { Name = "FigureEight" };
        AddChild(_track);
        _crossPasses.Add(_track.CrossoverS1);
        _crossPasses.Add(_track.CrossoverS2);

        _impacts = new ImpactSystem { Clock = () => _time, DebugContacts = DevTools.Instance?.Arg("contacts") != null };
        AddChild(_impacts);
        Car.DebugHits = DevTools.Instance?.Arg("hits") != null;

        SpawnField();
        _referee = new Referee(Rules, GameMode.WreckinRacing, _cars.Select(c => (c.Id, 1f))) { DamageScale = Rules.RaceDamageScale };
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
        _metrics = new RaceMetrics(_player, Seed, Track, _track.CrossoverPoint, _track.CrossoverRadius);

        if (!Headless)
        {
            _rig = new CameraRig { Target = _player };
            AddChild(_rig);
            _hud = new EventHud { Mode = this, PlayerCar = _player, ViewName = () => _rig.Mode.ToString() };
            AddChild(_hud);
            _beacon = BuildBeacon();
            AddChild(_beacon);
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
        var numbers = Enumerable.Range(2, 60).Where(n => n != 7).OrderBy(_ => _rng.Next()).ToList();
        var mix = Personality.FieldMix().OrderBy(_ => _rng.Next()).ToList();
        int playerSlot = Math.Clamp(PlayerGridSlot, 0, CarCount - 1);
        int aiIndex = 0;

        for (int slot = 0; slot < CarCount; slot++)
        {
            bool isPlayer = slot == playerSlot;
            int id = isPlayer ? 1 : 2 + aiIndex;
            int division = isPlayer ? 4 : 4 - (aiIndex % 4);
            var car = new Car
            {
                Id = id,
                Number = isPlayer ? 7 : numbers[aiIndex],
                PaintColor = isPlayer ? Liveries[0] : Liveries[1 + aiIndex % (Liveries.Length - 1)],
                RoofColor = DerbyMode.RoofColor(division),
                Transform = _track.GridSlot(slot),
            };
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
                    ? new PlayerProxyInput(new RaceBrain(ProxyProfile, Seed * 1000 + 1) { Anticipation = 0.25f }, this, Seed * 7919 + 3)
                    : new PlayerInput();
            }
            else
            {
                var brain = new RaceBrain(mix[aiIndex % mix.Count], Seed * 1000 + id) { PaceScale = AiPaceScale };
                _brains[id] = brain;
                car.Driver = new RaceDriver(brain, this);
                aiIndex++;
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
        TrackCars();
        _referee.SetRaceLeader(_standings.Leader);

        _samples.Clear();
        foreach (var c in _cars)
        {
            var lc = c.LastControls;
            _samples[c.Id] = new CarSample(c.AngularVelocity.Y, c.LinearVelocity.Length(), lc.Throttle > 0.3f || lc.Reverse);
            if (!_referee.IsWrecked(c.Id) && c.GlobalPosition.Y < -5f) _referee.ForceWreck(_time, c.Id);
        }
        _referee.Step(_time, dt, _samples);

        foreach (var e in _referee.DrainEvents())
        {
            _metrics.OnRefereeEvent(e);
            HandleEvent(e);
        }

        UpdatePlayerState();
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
            if (MathF.Abs(nl) > path.Width * 0.5f + 4f)
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

    private void HandleEvent(RefereeEvent e)
    {
        switch (e.Kind)
        {
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
            case RefereeEventKind.WheelLost when e.Car == _player.Id:
                _hud?.Popup("YOU'VE LOST A WHEEL!", new Color(1f, 0.5f, 0.2f));
                break;
            case RefereeEventKind.Wrecked:
            {
                var victim = _byId[e.Car];
                _standings.Retire(e.Car, e.Time);
                string by = e.Other > 0 ? $" by #{_byId[e.Other].Number}" : "";
                _hud?.Feed($"#{victim.Number} WRECKED{by}{(e.Points > 0 ? $"  +{e.Points}" : "")}");
                if (e.Other == _player.Id)
                    _hud?.Popup(e.Doubled ? $"LEADER ×2   WRECKED #{victim.Number}   +{e.Points}" : $"WRECKED #{victim.Number}   +{e.Points}",
                        new Color(1f, 0.45f, 0.2f));
                if (e.Car == _player.Id)
                {
                    _hud?.ShowWrecked("WRECKED  –  DNF");
                    SpectateNext(0);
                }
                break;
            }
        }
    }

    private void OnImpact(Impact i)
    {
        _metrics.OnImpact(i, _s);
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
                     $"closing={i.ClosingSpeed:0.0} E={i.Energy / 1000f:0}kJ F{i.A.Health(DamageZone.Front) * 100:0} L{i.A.Health(DamageZone.Left) * 100:0} Rt{i.A.Health(DamageZone.Right) * 100:0}");
        if (Headless) return;
        if (i.Energy > 6_000f) SpawnSparks(i.Point, i.Energy);
        if (_rig != null && (i.A == _rig.Target || i.B == _rig.Target))
            _rig.AddShake(Mathf.Clamp(i.Energy / 150_000f, 0.05f, 0.8f));
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

    private void EndRace(bool timedOut)
    {
        if (_phase == Phase.Over) return;
        _phase = Phase.Over;
        _standings.Freeze();
        var order = _standings.Order();
        int position = 0;
        foreach (int id in order)
        {
            position++;
            if (_standings.IsRetired(id)) continue;
            int bonus = Scoring.WreckinFinishBonus(Rules, position);
            if (bonus <= 0) break;
            int added = _referee.AddBonus(_time, id, ScoreCategory.Finish, bonus);
            if (id == _player.Id) _hud?.Popup($"P{position}   FINISH BONUS +{added}", new Color(1f, 0.85f, 0.2f), big: true);
        }
        _referee.DrainEvents();
        _metrics.Finish(_time, _standings, _referee, timedOut);
        foreach (var c in _cars) c.InputLocked = true;
        Engine.TimeScale = 1.0;
        _skipping = false;
        _hud?.ShowResults("WRECKIN' RACING  –  RESULTS", "Finish", BuildResults());
        Finished?.Invoke(this);
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

    public override void _Process(double delta)
    {
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
