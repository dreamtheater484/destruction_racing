using System;
using System.Collections.Generic;

namespace DestructionRacing.Rules;

/// <summary>Where a hit landed on one car: its damage zones and, near a corner, the wheel it reached.</summary>
public readonly record struct HitSide(ZoneSplit Zone, Wheel? Wheel);

/// <summary>
/// One car-car contact as measured by the game (plan §5.3): energy from pre-solve velocities,
/// who drove into whom, where each car was hit, and how fast each car was going just before.
/// </summary>
public readonly record struct CarHit(
    int CarA,
    int CarB,
    float Energy,
    HitAttacker Attacker,
    HitSide SideA,
    HitSide SideB,
    float SpeedA,
    float SpeedB);

/// <summary>Per-step state of one car that the referee needs.</summary>
public readonly record struct CarSample(float YawRate, float Speed, bool WantsToMove);

public enum RefereeEventKind
{
    /// <summary>A hit counted: Car = victim, Other = attacker, Amount = energy (J).</summary>
    Hit,
    /// <summary>A car lost health: Car = damaged car, Zone, Amount = health points.</summary>
    Damage,
    /// <summary>A wheel came off: Car, Wheel.</summary>
    WheelLost,
    /// <summary>A spin passed a new tier (live popup): Car = victim, Other = attacker, Tier.</summary>
    SpinTier,
    /// <summary>A spin was scored: Car = scorer, Other = victim, Tier, Points (after cap), Doubled.</summary>
    SpinScored,
    /// <summary>A car was wrecked: Car = victim, Other = wrecker or -1, Points to the wrecker (after cap), Doubled.</summary>
    Wrecked,
    /// <summary>A bonus was added (finish or survivor): Car, Points.</summary>
    Bonus,
}

public readonly record struct RefereeEvent(
    RefereeEventKind Kind,
    double Time,
    int Car,
    int Other = -1,
    float Amount = 0f,
    DamageZone Zone = DamageZone.Front,
    Wheel Wheel = Wheel.FrontLeft,
    SpinTier Tier = SpinTier.None,
    int Points = 0,
    bool Doubled = false);

/// <summary>
/// Turns measured hits into DD's consequences for every scoring mode (plan §5, §6): damage per zone and wheel,
/// the front-destroyed wreck rule, the immobilised rule, live spin tracking and attribution, wreck credit,
/// ×2 for damaging the leader, and the per-event points cap. Pure C#; the game feeds it and reads its events.
/// </summary>
public sealed class Referee
{
    private sealed class CarRecord
    {
        public required int Id;
        public required float Toughness;
        public required DamageState Damage;
        public required SpinTracker Spin;
        public required WreckAttribution Attribution;
        public required EventScore Score;
        public float ImmobileTime;
        /// <summary>Whether this car led at the moment each attacker last hit it (×2 for that attacker's spin).</summary>
        public readonly Dictionary<int, bool> LeaderAtHit = new();
    }

    private readonly RulesConfig _rules;
    private readonly GameMode _mode;
    private readonly Dictionary<int, CarRecord> _cars = new();
    private readonly List<RefereeEvent> _events = new();
    private readonly List<SpinEvent> _spinScratch = new();
    private readonly Dictionary<int, int> _pointsScratch = new();
    private readonly Dictionary<long, double> _lastContact = new();
    private int? _raceLeader;

    /// <summary>Damage factor for a contact between two bodies (-1 = a wall): grinding repeats deal less.</summary>
    private float GrindFactor(double time, int a, int b)
    {
        long key = ((long)Math.Min(a, b) << 32) ^ (uint)Math.Max(a, b);
        bool grinding = _lastContact.TryGetValue(key, out double last) && time - last < _rules.GrindWindow;
        _lastContact[key] = time;
        return grinding ? _rules.GrindDamageScale : 1f;
    }

    public Referee(RulesConfig rules, GameMode mode, IEnumerable<(int Id, float Toughness)> cars)
    {
        _rules = rules;
        _mode = mode;
        foreach (var (id, toughness) in cars)
        {
            _cars[id] = new CarRecord
            {
                Id = id,
                Toughness = toughness,
                Damage = new DamageState(),
                Spin = new SpinTracker(rules.Spin),
                Attribution = new WreckAttribution(rules.WreckAttributionWindow),
                Score = new EventScore(rules),
            };
        }
    }

    public RulesConfig Rules => _rules;
    public GameMode Mode => _mode;
    public IEnumerable<int> CarIds => _cars.Keys;

    public DamageState Damage(int car) => _cars[car].Damage;
    public EventScore Score(int car) => _cars[car].Score;
    public bool IsWrecked(int car) => _cars[car].Damage.IsWrecked;
    public bool IsSpinning(int car) => _cars[car].Spin.IsOpen;

    /// <summary>Diagnostics: total zone damage dealt per source ("car hit", "wall hit", "wall scrape", "car scrape").</summary>
    public IReadOnlyDictionary<string, float> DamageBySource => _damageBySource;
    private readonly Dictionary<string, float> _damageBySource = new();
    private void CountSource(string source, float amount) =>
        _damageBySource[source] = _damageBySource.GetValueOrDefault(source) + amount;

    /// <summary>Global damage multiplier set by the mode (e.g. the opening-crunch calibration).</summary>
    public float DamageScale { get; set; } = 1f;

    /// <summary>Races: the car in 1st place, set by the mode every step. Ignored in the Bowl.</summary>
    public void SetRaceLeader(int? car) => _raceLeader = car;

    /// <summary>
    /// The car whose damage counts double: race position 1, or in the Bowl the unique points leader (✱ A4).
    /// </summary>
    public int? Leader
    {
        get
        {
            if (_mode != GameMode.DestructionDerby) return _raceLeader;
            _pointsScratch.Clear();
            foreach (var c in _cars.Values) _pointsScratch[c.Id] = c.Score.Total;
            return Scoring.BowlLeader(_pointsScratch);
        }
    }

    /// <summary>Returns and clears the events raised since the last call.</summary>
    public List<RefereeEvent> DrainEvents()
    {
        var copy = new List<RefereeEvent>(_events);
        _events.Clear();
        return copy;
    }

    public void OnCarHit(double time, in CarHit hit)
    {
        if (!_cars.TryGetValue(hit.CarA, out var a) || !_cars.TryGetValue(hit.CarB, out var b)) return;
        if (hit.Energy < _rules.Spin.HitMinEnergy && hit.Energy < _rules.DamageEnergyThreshold) return;

        // A wrecked car that rolls into someone is debris, not an attacker.
        bool aAttacks = (hit.Attacker is HitAttacker.A or HitAttacker.Both) && !a.Damage.IsWrecked;
        bool bAttacks = (hit.Attacker is HitAttacker.B or HitAttacker.Both) && !b.Damage.IsWrecked;
        int? leader = Leader;

        // Spins first, so a hit that wrecks and spins a car can still score both.
        if (aAttacks) RegisterSpinHit(time, attacker: a, victim: b, hit.Energy, hit.SpeedB, leader);
        if (bAttacks) RegisterSpinHit(time, attacker: b, victim: a, hit.Energy, hit.SpeedA, leader);

        bool aWasWrecked = a.Damage.IsWrecked;
        bool bWasWrecked = b.Damage.IsWrecked;
        // Damage is credited to the other car if it attacked or was at least driving (a brake-check counts),
        // never to a parked car that was rammed.
        bool creditA = !a.Damage.IsWrecked && (aAttacks || hit.SpeedA >= _rules.Spin.MinVictimSpeed);
        bool creditB = !b.Damage.IsWrecked && (bAttacks || hit.SpeedB >= _rules.Spin.MinVictimSpeed);
        float grind = GrindFactor(time, a.Id, b.Id);
        ApplyDamage(time, b, hit.Energy, hit.SideB, creditA ? a.Id : null, grind, "car hit");
        ApplyDamage(time, a, hit.Energy, hit.SideA, creditB ? b.Id : null, grind, "car hit");
        if (!bWasWrecked && b.Damage.IsWrecked) Wreck(time, b, leader);
        if (!aWasWrecked && a.Damage.IsWrecked) Wreck(time, a, leader);
    }

    /// <summary>A car hit a wall or other static object: damage only, no credit, no spin points.</summary>
    public void OnWallHit(double time, int car, float energy, HitSide side)
    {
        if (!_cars.TryGetValue(car, out var c) || c.Damage.IsWrecked) return;
        int? leader = Leader;
        ApplyDamage(time, c, energy, side, creditTo: null, GrindFactor(time, car, -1), "wall hit");
        if (c.Damage.IsWrecked) Wreck(time, c, leader);
    }

    /// <summary>
    /// Sustained sliding contact (a wall, a wreck, a squeezing car): grinds the touching zone. Credit goes to the
    /// car doing the squeezing, if any (DD Code T4).
    /// </summary>
    public void OnScrape(double time, int car, ZoneSplit zone, float slideSpeed, float dt, int? creditTo, string source = "scrape")
    {
        if (!_cars.TryGetValue(car, out var c) || c.Damage.IsWrecked) return;
        float excess = slideSpeed - _rules.ScrapeMinSpeed;
        if (excess <= 0f) return;
        int? leader = Leader;
        float amount = _rules.ScrapeRate * excess * dt * DamageScale / MathF.Max(c.Toughness, 0.1f);
        float dealt = c.Damage.ApplyRaw(_rules, zone, amount);
        if (dealt <= 0f) return;
        CountSource(source, dealt);
        _events.Add(new RefereeEvent(RefereeEventKind.Damage, time, car, creditTo ?? -1, dealt, zone.Primary));
        if (creditTo is int attacker && attacker != car && _cars.TryGetValue(attacker, out var a) && !a.Damage.IsWrecked)
            c.Attribution.RecordDamage(attacker, time, dealt);
        if (c.Damage.IsWrecked) Wreck(time, c, leader);
    }

    /// <summary>Advances spin tracking and the immobilised rule by one physics step.</summary>
    public void Step(double time, float dt, IReadOnlyDictionary<int, CarSample> samples)
    {
        int? leader = null;
        bool leaderKnown = false;
        foreach (var c in _cars.Values)
        {
            if (!samples.TryGetValue(c.Id, out var s)) continue;

            _spinScratch.Clear();
            c.Spin.Step(time, dt, s.YawRate, _spinScratch);
            foreach (var e in _spinScratch)
            {
                if (e.Kind == SpinEventKind.TierReached)
                {
                    _events.Add(new RefereeEvent(RefereeEventKind.SpinTier, time, c.Id, e.AttackerId, Tier: e.Tier));
                }
                else if (_cars.TryGetValue(e.AttackerId, out var scorer) && Scoring.ScoresSpins(_rules, _mode))
                {
                    bool doubled = c.LeaderAtHit.TryGetValue(e.AttackerId, out bool led) && led;
                    int added = scorer.Score.Add(ScoreCategory.Spin, Scoring.SpinPoints(_rules, e.Tier, doubled));
                    _events.Add(new RefereeEvent(RefereeEventKind.SpinScored, time, scorer.Id, c.Id, Tier: e.Tier, Points: added, Doubled: doubled));
                }
            }

            if (c.Damage.IsWrecked) continue;
            if (s.WantsToMove && s.Speed < 0.5f) c.ImmobileTime += dt;
            else c.ImmobileTime = 0f;
            if (c.ImmobileTime >= _rules.ImmobilisedWreckTime)
            {
                if (!leaderKnown) { leader = Leader; leaderKnown = true; }
                c.Damage.ForceWreck();
                Wreck(time, c, leader);
            }
        }
    }

    /// <summary>Wrecks a car for a reason outside the damage model (e.g. it fell out of the world).</summary>
    public void ForceWreck(double time, int car)
    {
        if (!_cars.TryGetValue(car, out var c) || c.Damage.IsWrecked) return;
        int? leader = Leader;
        c.Damage.ForceWreck();
        Wreck(time, c, leader);
    }

    /// <summary>Adds a finishing or survivor bonus (subject to the cap).</summary>
    public int AddBonus(double time, int car, ScoreCategory category, int points)
    {
        if (!_cars.TryGetValue(car, out var c)) return 0;
        int added = c.Score.Add(category, points);
        if (added > 0) _events.Add(new RefereeEvent(RefereeEventKind.Bonus, time, car, Points: added));
        return added;
    }

    private void RegisterSpinHit(double time, CarRecord attacker, CarRecord victim, float energy, float victimSpeed, int? leader)
    {
        if (energy < _rules.Spin.HitMinEnergy || victim.Damage.IsWrecked) return;
        _events.Add(new RefereeEvent(RefereeEventKind.Hit, time, victim.Id, attacker.Id, energy));
        victim.LeaderAtHit[attacker.Id] = leader == victim.Id;
        victim.Spin.RegisterHit(attacker.Id, time, energy, victimSpeed, victimWrecked: false);
    }

    private void ApplyDamage(double time, CarRecord car, float energy, HitSide side, int? creditTo, float scale, string source)
    {
        if (car.Damage.IsWrecked) return;
        float before0 = car.Damage.Health(side.Zone.Primary);
        float before1 = car.Damage.Health(side.Zone.Secondary);
        float dealt = car.Damage.ApplyHit(_rules, energy, side.Zone, car.Toughness / MathF.Max(scale * DamageScale, 0.01f));
        if (dealt <= 0f) return;
        CountSource(source, dealt);

        float d0 = before0 - car.Damage.Health(side.Zone.Primary);
        float d1 = before1 - car.Damage.Health(side.Zone.Secondary);
        if (d0 > 0f) _events.Add(new RefereeEvent(RefereeEventKind.Damage, time, car.Id, creditTo ?? -1, d0, side.Zone.Primary));
        if (d1 > 0f) _events.Add(new RefereeEvent(RefereeEventKind.Damage, time, car.Id, creditTo ?? -1, d1, side.Zone.Secondary));

        if (side.Wheel is Wheel wheel)
        {
            bool rearWheel = wheel is Wheel.RearLeft or Wheel.RearRight;
            float share = _rules.WheelDamageShare;
            if (rearWheel && car.Damage.Health(DamageZone.Rear) < _rules.RearCriticalHealth) share *= 2f;
            bool wasLost = car.Damage.IsWheelLost(wheel);
            dealt += car.Damage.DamageWheel(wheel, dealt * share, _rules.WheelLoss);
            if (!wasLost && car.Damage.IsWheelLost(wheel))
                _events.Add(new RefereeEvent(RefereeEventKind.WheelLost, time, car.Id, creditTo ?? -1, Wheel: wheel));
        }

        if (creditTo is int attacker) car.Attribution.RecordDamage(attacker, time, dealt);
    }

    private void Wreck(double time, CarRecord victim, int? leaderBeforeHit)
    {
        int? wrecker = victim.Attribution.ResolveWrecker(time);
        int added = 0;
        bool doubled = leaderBeforeHit == victim.Id;
        if (wrecker is int w && w != victim.Id && _cars.TryGetValue(w, out var scorer) && Scoring.ScoresDamage(_rules, _mode))
            added = scorer.Score.Add(ScoreCategory.Wreck, Scoring.WreckPoints(_rules, doubled));
        _events.Add(new RefereeEvent(RefereeEventKind.Wrecked, time, victim.Id, wrecker ?? -1, Points: added, Doubled: doubled && added > 0));
    }
}
