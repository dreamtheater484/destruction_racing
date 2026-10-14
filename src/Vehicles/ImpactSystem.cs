using System;
using System.Collections.Generic;
using DestructionRacing.Rules;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace DestructionRacing.Vehicles;

/// <summary>One measured impact, for effects, camera shake, AI memory and metrics.</summary>
public readonly record struct Impact(
    double Time,
    Car A,
    Car? B,                 // null for a wall
    Vector3 Point,
    Vector3 NormalAtoB,     // for walls: from the car into the wall
    float ClosingSpeed,
    float Energy,
    HitAttacker Attacker);

/// <summary>
/// Measures hits the DD way (plan §5.3). Before every physics step it reads the contacts of the step that
/// just ran and computes hit energy from the velocities cached <i>before</i> that step's solve (Jolt's own
/// impulses are unreliable in pileups), then refreshes the cache. Runs ahead of the cars every tick.
/// </summary>
public partial class ImpactSystem : Node
{
    /// <summary>Contacts closer together than this (per pair) count as one hit.</summary>
    public float PairCooldown { get; set; } = 0.25f;
    /// <summary>Contact normals steeper than this (|n.y|) are the ground, not a wall.</summary>
    public float WallMaxNormalY { get; set; } = 0.6f;

    public Referee? Referee { get; set; }
    /// <summary>Event time in seconds (the mode's clock).</summary>
    public Func<double>? Clock { get; set; }
    /// <summary>Raised for every hit at or above the damage threshold.</summary>
    public event Action<Impact>? Impacted;

    private readonly List<Car> _cars = new();
    private readonly Dictionary<long, double> _lastPairHit = new();
    private readonly Dictionary<int, double> _lastWallHit = new();
    private readonly Dictionary<long, Candidate> _pairs = new();
    private bool _primed;

    private struct Candidate
    {
        public Car A, B;
        public Vector3 Point, Normal;
        public HitResult Result;
        public Vector3 PointSum;
        public int Points;
    }

    /// <summary>Prints every contact for debugging (set from the scenario runner).</summary>
    public bool DebugContacts { get; set; }

    public IReadOnlyList<Car> Cars => _cars;

    public void Register(Car car)
    {
        _cars.Add(car);
        Cache(car);
    }

    public void Unregister(Car car) => _cars.Remove(car);

    public override void _Ready()
    {
        ProcessPhysicsPriority = -100;
    }

    public override void _PhysicsProcess(double delta)
    {
        double time = Clock?.Invoke() ?? 0.0;
        if (_primed) DetectHits(time, (float)delta);
        foreach (var car in _cars) Cache(car);
        _primed = true;
    }

    private static void Cache(Car car)
    {
        car.PreLinearVelocity = car.LinearVelocity;
        car.PreAngularVelocity = car.AngularVelocity;
        car.PreCenterOfMass = car.GlobalTransform * car.CenterOfMass;
    }

    private static NVector3 N(Vector3 v) => new(v.X, v.Y, v.Z);

    private static BodyState Pre(Car c) => new(N(c.PreLinearVelocity), N(c.PreAngularVelocity), N(c.PreCenterOfMass), c.Mass);

    private readonly List<(Car Car, Vector3 Point, Vector3 Normal)> _wallContacts = new();

    private void DetectHits(double time, float dt)
    {
        _pairs.Clear();
        _wallContacts.Clear();
        float threshold = MathF.Min(Referee?.Rules.DamageEnergyThreshold ?? 3000f, Referee?.Rules.Spin.HitMinEnergy ?? 3000f);

        foreach (var car in _cars)
        {
            if (!IsInstanceValid(car)) continue;
            var state = PhysicsServer3D.BodyGetDirectState(car.GetRid());
            if (state == null) continue;
            int count = state.GetContactCount();

            HitResult bestWall = default;
            Vector3 wallPoint = Vector3.Zero, wallNormal = Vector3.Zero, wallPointSum = Vector3.Zero;
            int wallPoints = 0;
            for (int i = 0; i < count; i++)
            {
                var collider = state.GetContactColliderObject(i);
                Vector3 point = state.GetContactLocalPosition(i);
                Vector3 normal = state.GetContactLocalNormal(i).Normalized();
                if (DebugContacts)
                    GD.Print($"[Impacts] t={time:0.000} car#{car.Id} contact {i}/{count} with {(collider as Node)?.Name ?? collider?.ToString()} p={point} n={normal}");

                if (collider is Car other)
                {
                    if (other.Id <= car.Id) continue; // each pair once, from the lower id
                    // Normal from car towards other: flip the reported normal if it points the wrong way.
                    Vector3 n = normal.Dot(other.PreCenterOfMass - car.PreCenterOfMass) < 0f ? -normal : normal;
                    var result = HitEnergy.CarVsCar(Pre(car), Pre(other), N(point), N(n));
                    long key = PairKey(car.Id, other.Id);
                    // The contact manifold's points are averaged for the zones; the strongest point sets the energy.
                    _pairs.TryGetValue(key, out var best);
                    if (best.A == null || result.Energy > best.Result.Energy)
                    {
                        best.A = car;
                        best.B = other;
                        best.Normal = n;
                        best.Result = result;
                    }
                    best.PointSum += point;
                    best.Points++;
                    best.Point = best.PointSum / best.Points;
                    _pairs[key] = best;
                }
                else if (MathF.Abs(normal.Y) < WallMaxNormalY)
                {
                    // Static world (walls, barriers). Normal into the wall: away from the car's centre.
                    Vector3 n = normal.Dot(point - car.PreCenterOfMass) < 0f ? -normal : normal;
                    n.Y = 0f;
                    if (n.LengthSquared() < 1e-4f) continue;
                    n = n.Normalized();
                    var result = HitEnergy.CarVsStatic(Pre(car), N(point), N(n));
                    if (result.Energy > bestWall.Energy)
                    {
                        bestWall = result;
                        wallNormal = n;
                    }
                    wallPointSum += point;
                    wallPoints++;
                }
            }

            if (wallPoints > 0)
            {
                wallPoint = wallPointSum / wallPoints;
                if (wallNormal == Vector3.Zero)
                {
                    // No closing contact this step (pure sliding): take the normal of the first wall contact.
                    for (int i = 0; i < count && wallNormal == Vector3.Zero; i++)
                    {
                        Vector3 n = state.GetContactLocalNormal(i).Normalized();
                        if (MathF.Abs(n.Y) >= WallMaxNormalY || state.GetContactColliderObject(i) is Car) continue;
                        if (n.Dot(state.GetContactLocalPosition(i) - car.PreCenterOfMass) < 0f) n = -n;
                        n.Y = 0f;
                        if (n.LengthSquared() > 1e-4f) wallNormal = n.Normalized();
                    }
                }
                if (wallNormal != Vector3.Zero) _wallContacts.Add((car, wallPoint, wallNormal));
            }
            if (bestWall.Energy >= threshold && Cooled(_lastWallHit, car.Id, time))
            {
                _lastWallHit[car.Id] = time;
                Referee?.OnWallHit(time, car.Id, bestWall.Energy, SideOf(car, wallPoint, wallNormal));
                Impacted?.Invoke(new Impact(time, car, null, wallPoint, wallNormal, bestWall.ClosingSpeed, bestWall.Energy, HitAttacker.A));
            }
        }

        foreach (var (key, c) in _pairs)
        {
            if (c.Result.Energy < threshold || !Cooled(_lastPairHit, key, time)) continue;
            _lastPairHit[key] = time;

            var hit = new CarHit(c.A.Id, c.B.Id, c.Result.Energy, c.Result.Attacker,
                SideOf(c.A, c.Point, c.Normal), SideOf(c.B, c.Point, -c.Normal),
                c.A.PreLinearVelocity.Length(), c.B.PreLinearVelocity.Length());
            Referee?.OnCarHit(time, hit);

            // Spin calibration on both cars: estimated impulse = reduced mass × closing speed along the normal.
            float reducedMass = c.A.Mass * c.B.Mass / (c.A.Mass + c.B.Mass);
            Vector3 impulseOnB = c.Normal * (reducedMass * c.Result.ClosingSpeed);
            c.B.ApplyHitReaction(c.Point, impulseOnB);
            c.A.ApplyHitReaction(c.Point, -impulseOnB);

            Impacted?.Invoke(new Impact(time, c.A, c.B, c.Point, c.Normal, c.Result.ClosingSpeed, c.Result.Energy, c.Result.Attacker));
        }

        if (Referee != null) Scrapes(time, dt);
    }

    /// <summary>Sustained sliding contact grinds the touching zone (DD Code T4: squeeze into walls and wrecks).</summary>
    private void Scrapes(double time, float dt)
    {
        foreach (var (car, point, normal) in _wallContacts)
        {
            Vector3 v = car.LinearVelocity;
            float slide = (v - normal * v.Dot(normal)).Length();
            // Whoever is pushing this car into the wall gets the credit.
            Car? squeezer = null;
            foreach (var c in _pairs.Values)
            {
                Car other = c.A == car ? c.B : c.B == car ? c.A : null!;
                if (other != null && !other.IsWrecked) squeezer = other;
            }
            Referee!.OnScrape(time, car.Id, SideOf(car, point, normal).Zone, slide, dt, squeezer?.Id, "wall scrape");
        }
        foreach (var c in _pairs.Values)
        {
            Vector3 rel = c.A.LinearVelocity - c.B.LinearVelocity;
            float slide = (rel - c.Normal * rel.Dot(c.Normal)).Length();
            if (slide <= (Referee!.Rules.ScrapeMinSpeed)) continue;
            Referee.OnScrape(time, c.A.Id, SideOf(c.A, c.Point, c.Normal).Zone, slide, dt, c.B.IsWrecked ? null : c.B.Id, "car scrape");
            Referee.OnScrape(time, c.B.Id, SideOf(c.B, c.Point, -c.Normal).Zone, slide, dt, c.A.IsWrecked ? null : c.A.Id, "car scrape");
        }
    }

    private bool Cooled<TKey>(Dictionary<TKey, double> last, TKey key, double time) where TKey : notnull =>
        !last.TryGetValue(key, out double t) || time - t >= PairCooldown;

    private static long PairKey(int a, int b) => ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);

    /// <summary>
    /// Damage zones (with corner spill-over) and the nearest wheel for a world contact point.
    /// <paramref name="normalOut"/> points from this car towards what hit it.
    /// </summary>
    public static HitSide SideOf(Car car, Vector3 worldPoint, Vector3 normalOut)
    {
        var xf = car.GlobalTransform;
        Vector3 local = xf.AffineInverse() * worldPoint;
        Vector3 localNormal = xf.Basis.Inverse() * normalOut;
        var he = Car.HalfExtents;
        var zone = DamageMath.ZoneFromContact(new NVector3(local.X, local.Y, local.Z), N(localNormal), N(he));
        var wheel = DamageMath.NearestWheel(new NVector3(local.X, local.Y, local.Z), N(he), car.Tuning.FrontAxleZ, car.Tuning.RearAxleZ);
        return new HitSide(zone, wheel);
    }
}
