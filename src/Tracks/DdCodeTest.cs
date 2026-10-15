using System;
using System.Collections.Generic;
using System.Linq;
using DestructionRacing.Core;
using DestructionRacing.Rules;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Tracks;

/// <summary>
/// Headless scenario tests for the five "Destruction Derby Code" tactics (plan §4.7, §17.1).
/// Each trial sets up two scripted cars with a little randomness; a tactic passes at ≥ 60% success.
///   --tactic=T1|T2|T3|T4|T5|all|probe   --trials=N (default 10)   --seed=S
/// Prints "[DDCode] T2: 9/10 (90%) PASS" per tactic and quits with exit code 1 if any tactic fails.
/// </summary>
public partial class DdCodeTest : Node3D
{
    private const float Kmh = 1f / 3.6f;

    private sealed class Trial
    {
        public required string Tactic;
        public required Node3D Root;
        public required Referee Referee;
        public required ImpactSystem Impacts;
        public required Car A;
        public required Car B;
        public Car? Extra;
        public Car? Extra2;
        public double Time;
        public readonly Dictionary<string, float> Values = new();
        public readonly List<RefereeEvent> Events = new();
        public readonly List<Impact> ImpactLog = new();
        public float MaxAngularB;
        public float MaxHeightB;
        public bool Finished;
        public bool Success;
        public string Note = "";
    }

    private static readonly string[] AllTactics = { "T1", "T2", "T3", "T4", "T5" };

    private readonly Queue<(string Tactic, int Index)> _queue = new();
    private readonly Dictionary<string, (int Ok, int Total)> _results = new();
    private Trial? _trial;
    private RandomNumberGenerator _rng = new();
    private int _trialsPerTactic = 10;

    public override void _Ready()
    {
        string tactic = DevTools.Instance?.Arg("tactic") ?? "all";
        Car.DebugHits = DevTools.Instance?.Arg("hits") != null;
        if (int.TryParse(DevTools.Instance?.Arg("trials"), out int n)) _trialsPerTactic = Math.Max(1, n);
        _rng.Seed = ulong.TryParse(DevTools.Instance?.Arg("seed"), out ulong seed) ? seed : 1UL;

        var tactics = tactic == "all" ? AllTactics : new[] { tactic };
        foreach (string t in tactics)
            for (int i = 0; i < (t == "probe" ? 1 : _trialsPerTactic); i++) _queue.Enqueue((t, i));

        var sun = new DirectionalLight3D { RotationDegrees = new Vector3(-50f, -30f, 0f) };
        AddChild(sun);
        GD.Print($"[DDCode] running {string.Join(",", tactics)} × {_trialsPerTactic}");
        StartNext();
    }

    private float Jitter(float amount) => _rng.RandfRange(-amount, amount);

    private void StartNext()
    {
        if (_trial != null)
        {
            _trial.Root.QueueFree();
            _trial = null;
        }
        if (_queue.Count == 0)
        {
            Report();
            return;
        }
        var (tactic, index) = _queue.Dequeue();
        _trial = Build(tactic);
    }

    // ------------------------------------------------------------------ set-up

    private Trial Build(string tactic)
    {
        var root = new Node3D { Name = $"Trial_{tactic}" };
        AddChild(root);
        AddGround(root);

        Car A, B;
        Car? extra = null, extra2 = null;
        switch (tactic)
        {
            case "probe":
                // A drives into a wall at 40 km/h; B T-bones a parked car (extra) at 40 km/h.
                AddWall(root, new Vector3(0f, 0f, 6f), new Vector3(200f, 1.6f, 1f));
                A = Spawn(root, 1, new Vector3(-20f, 0.62f, -8f), Vector3.Back, Colors.Red);
                B = Spawn(root, 2, new Vector3(20f, 0.62f, -30f), Vector3.Back, Colors.Blue);
                extra = Spawn(root, 3, new Vector3(20f, 0.62f, -16f), Vector3.Right, Colors.Gray);
                break;
            case "T3":
                // A bend: the road turns right before a wall across the road at x = 70.
                AddWall(root, new Vector3(70f, 0f, 0f), new Vector3(1f, 1.6f, 80f));
                A = Spawn(root, 1, new Vector3(0f, 0.62f, 0f), Vector3.Right, Colors.Red);          // rammer
                B = Spawn(root, 2, new Vector3(9f, 0.62f, 0f), Vector3.Right, Colors.Blue);         // braking victim
                break;
            case "T4":
                // Victim between a squeezer (left) and a wall or a parked wreck (right, +Z).
                A = Spawn(root, 1, new Vector3(0f, 0.62f, -2.4f), Vector3.Right, Colors.Red);       // squeezer
                B = Spawn(root, 2, new Vector3(0f, 0.62f, 0.6f), Vector3.Right, Colors.Blue);       // victim
                if (_rng.Randf() < 0.5f) AddWall(root, new Vector3(0f, 0f, 2.6f), new Vector3(400f, 1.6f, 1f));
                else
                {
                    // Stationary wreckage: two wrecked cars nose to tail.
                    extra = Spawn(root, 3, new Vector3(22f, 0.62f, 3.0f), Vector3.Right, Colors.Gray);
                    extra2 = Spawn(root, 4, new Vector3(27.2f, 0.62f, 3.0f), Vector3.Right, Colors.DimGray);
                }
                break;
            case "T5":
                A = Spawn(root, 1, new Vector3(0f, 0.62f, 0f), Vector3.Right, Colors.Red);          // tactician
                B = Spawn(root, 2, new Vector3(70f, 0.62f, Jitter(0.3f)), Vector3.Left, Colors.Blue);
                break;
            case "T1":
                A = Spawn(root, 1, new Vector3(0f, 0.62f, 0f), Vector3.Right, Colors.Red);          // brake-checker
                B = Spawn(root, 2, new Vector3(-(2f * HalfLength + 6.5f + Jitter(1f)), 0.62f, Jitter(0.25f)), Vector3.Right, Colors.Blue); // tailgater
                break;
            default: // T2
                A = Spawn(root, 1, new Vector3(0f, 0.62f, 0f), Vector3.Right, Colors.Red);          // defender
                B = Spawn(root, 2, new Vector3(-7f, 0.62f, -(2f * HalfWidth + 1.3f) + Jitter(0.25f)), Vector3.Right, Colors.Blue); // overtaker on the left
                break;
        }

        var cars = new List<Car> { A, B };
        if (extra != null) cars.Add(extra);
        if (extra2 != null) cars.Add(extra2);
        var referee = new Referee(RulesConfig.Standard, GameMode.WreckinRacing, cars.Select(c => (c.Id, 1f)));
        foreach (var c in cars) c.Damage = referee.Damage(c.Id);
        if (tactic == "T4")
            foreach (var wreck in new[] { extra, extra2 })
                if (wreck != null) referee.ForceWreck(0, wreck.Id);

        var trial = new Trial { Tactic = tactic, Root = root, Referee = referee, Impacts = null!, A = A, B = B, Extra = extra, Extra2 = extra2 };
        var impacts = new ImpactSystem { Referee = referee, Clock = () => trial.Time, DebugContacts = DevTools.Instance?.Arg("contacts") != null };
        root.AddChild(impacts);
        foreach (var c in cars) impacts.Register(c);
        impacts.Impacted += i => trial.ImpactLog.Add(i);
        trial.Impacts = impacts;

        // Randomised speeds and timings for this trial.
        trial.Values["jitterV"] = 1f + Jitter(0.08f);
        trial.Values["jitterT"] = Jitter(0.08f);
        return trial;
    }

    private static void AddGround(Node3D root)
    {
        var ground = new StaticBody3D { Name = "Ground" };
        ground.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D() });
        root.AddChild(ground);
    }

    private static void AddWall(Node3D root, Vector3 position, Vector3 size)
    {
        var wall = new StaticBody3D { Name = "Wall", Position = position + new Vector3(0f, size.Y * 0.5f, 0f) };
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        wall.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size } });
        root.AddChild(wall);
    }

    /// <summary>--model=nordvik runs the tactics with a generated car's collider and axles (no visuals).</summary>
    private static string Model => DevTools.Instance?.Arg("model") ?? "";
    /// <summary>Half length and half width of the test cars (the grey box's, or the model's).</summary>
    private static float HalfLength => Model == "" ? Car.DefaultHalfExtents.Z : CarSpec.For(Model).ZoneHalfExtents.Z;
    private static float HalfWidth => Model == "" ? Car.DefaultHalfExtents.X : CarSpec.For(Model).ZoneHalfExtents.X;

    private static Car Spawn(Node3D root, int id, Vector3 position, Vector3 heading, Color paint)
    {
        var car = new Car
        {
            Id = id,
            Number = id,
            PaintColor = paint,
            ModelId = Model,
            BuildVisuals = false,
            Transform = new Transform3D(Basis.LookingAt(heading.Normalized(), Vector3.Up), position),
        };
        root.AddChild(car);
        return car;
    }

    // ------------------------------------------------------------------ scripted drivers

    private static Vector3 Fwd(Car c) => -c.GlobalTransform.Basis.Z;

    /// <summary>Steering that holds a world heading (steer +1 = right).</summary>
    private static float HoldHeading(Car c, Vector3 dir)
    {
        Vector3 f = Fwd(c);
        f.Y = 0f;
        float err = Mathf.Atan2(f.Cross(dir).Y, f.Dot(dir)); // + = target is to the left (CCW)
        return Mathf.Clamp(-err * 2.5f, -1f, 1f);
    }

    private static CarControls Cruise(Car c, float targetSpeed, Vector3 dir, float steer = float.NaN) => new()
    {
        Throttle = Mathf.Clamp((targetSpeed - c.ForwardSpeed) * 0.6f + 0.15f, 0f, 1f),
        Steer = float.IsNaN(steer) ? HoldHeading(c, dir) : steer,
    };

    private static void Launch(Car c, float speed) => c.LinearVelocity = Fwd(c) * speed;

    public override void _PhysicsProcess(double delta)
    {
        var tr = _trial;
        if (tr == null || tr.Finished) return;
        float dt = (float)delta;
        tr.Time += dt;
        float t = (float)tr.Time;

        // Referee bookkeeping.
        var samples = new Dictionary<int, CarSample>
        {
            [tr.A.Id] = new(tr.A.AngularVelocity.Y, tr.A.LinearVelocity.Length(), false),
            [tr.B.Id] = new(tr.B.AngularVelocity.Y, tr.B.LinearVelocity.Length(), false),
        };
        tr.Referee.Step(tr.Time, dt, samples);
        tr.Events.AddRange(tr.Referee.DrainEvents());
        tr.MaxAngularB = MathF.Max(tr.MaxAngularB, tr.B.AngularVelocity.Length());
        tr.Values["maxYawB"] = MathF.Max(tr.Values.GetValueOrDefault("maxYawB"), MathF.Abs(tr.B.AngularVelocity.Y));
        tr.MaxHeightB = MathF.Max(tr.MaxHeightB, tr.B.GlobalPosition.Y);

        Drive(tr, t);
        Evaluate(tr, t);
        if (tr.Finished)
        {
            var (ok, total) = _results.TryGetValue(tr.Tactic, out var r) ? r : (0, 0);
            _results[tr.Tactic] = (ok + (tr.Success ? 1 : 0), total + 1);
            if (tr.Tactic == "probe" || DevTools.Instance?.Arg("verbose") != null)
                GD.Print($"[DDCode] {tr.Tactic} trial: {(tr.Success ? "ok" : "FAIL")} {tr.Note}");
            CallDeferred(MethodName.StartNext);
        }
    }

    private void Drive(Trial tr, float t)
    {
        float jv = tr.Values["jitterV"];
        float jt = tr.Values["jitterT"];
        Car A = tr.A, B = tr.B;
        bool launch = t >= 0.4f && !tr.Values.ContainsKey("launched");
        if (launch) tr.Values["launched"] = 1f;

        switch (tr.Tactic)
        {
            case "probe":
                if (launch) { Launch(A, 40f * Kmh); Launch(B, 40f * Kmh); }
                A.Driver = Fixed(Cruise(A, 40f * Kmh, Vector3.Back));
                B.Driver = Fixed(Cruise(B, 40f * Kmh, Vector3.Back));
                break;

            case "T1":
            {
                // A brake-checks; B tailgates and reacts late (0.6 s).
                float v = 80f * Kmh * jv;
                if (launch) { Launch(A, v); Launch(B, v + 3f * Kmh); }
                float brakeAt = 1.2f + jt;
                A.Driver = Fixed(t < brakeAt ? Cruise(A, v, Vector3.Right) : new CarControls { Brake = 1f, Steer = HoldHeading(A, Vector3.Right) });
                B.Driver = Fixed(t < brakeAt + 0.6f ? Cruise(B, v + 3f * Kmh, Vector3.Right) : new CarControls { Brake = 1f, Steer = HoldHeading(B, Vector3.Right) });
                break;
            }

            case "T2":
            {
                // B overtakes on A's left; A turns into B's rear corner.
                float vA = 70f * Kmh * jv, vB = 84f * Kmh * jv;
                if (launch) { Launch(A, vA); Launch(B, vB); }
                // Turn in as the overtaker's rear wheels come alongside the defender's front wheels, so the hit lands
                // behind B's rear axle (what spins a car). Axle-anchored so it holds for every car size; for the
                // grey box this is the original trigger (B's centre 2.6 m ahead of A's).
                Vector3 bRearAxle = B.GlobalPosition + Fwd(B) * -B.RearAxleZ;
                Vector3 aFrontAxle = A.GlobalPosition + Fwd(A) * -A.FrontAxleZ;
                if (!tr.Values.ContainsKey("turn") && t > 0.5f && bRearAxle.X > aFrontAxle.X - 0.1f + jt * 5f) tr.Values["turn"] = t;
                float turnAt = tr.Values.TryGetValue("turn", out float ta) ? ta : float.MaxValue;
                // Turn in until contact (or 0.9 s), then straighten up.
                bool touched = tr.ImpactLog.Any(i => i.B != null);
                A.Driver = Fixed(t >= turnAt && t < turnAt + 0.9f && !touched
                    ? new CarControls { Throttle = 1f, Steer = -1f, FastSteer = true }
                    : Cruise(A, vA, Vector3.Right));
                // The overtaker is surprised: after the hit it holds the wheel straight for a second.
                var firstHit = tr.ImpactLog.FirstOrDefault(i => i.B != null);
                bool surprised = touched && t - firstHit.Time < 1.0;
                B.Driver = Fixed(surprised ? new CarControls { Throttle = 0.3f } : Cruise(B, vB, Vector3.Right));
                break;
            }

            case "T3":
            {
                // B brakes for the bend (at x = 20) and turns right at x = 45; A rams it from behind.
                float vB = 90f * Kmh * jv, vA = 98f * Kmh * jv;
                if (launch) { Launch(A, vA); Launch(B, vB); }
                float bx = B.GlobalPosition.X;
                if (!tr.Values.ContainsKey("hitT") && tr.ImpactLog.Any(i => i.B != null)) tr.Values["hitT"] = t;
                bool afterHit = tr.Values.TryGetValue("hitT", out float hitT);
                B.Driver = Fixed(bx < 20f ? Cruise(B, vB, Vector3.Right)
                    : bx < 45f ? new CarControls { Brake = B.ForwardSpeed > 9f ? 1f : 0f, Throttle = B.ForwardSpeed < 8f ? 0.4f : 0f, Steer = HoldHeading(B, Vector3.Right) }
                    : new CarControls { Throttle = 0.5f, Steer = 1f });
                A.Driver = Fixed(!afterHit || t < hitT + 0.6f
                    ? new CarControls { Throttle = 1f, Steer = HoldHeading(A, (B.GlobalPosition - A.GlobalPosition).Normalized()) }
                    : new CarControls { Brake = 1f });
                break;
            }

            case "T4":
            {
                // A squeezes B sideways into the wall (or a parked wreck) on B's right.
                float v = 60f * Kmh * jv;
                if (launch) { Launch(A, v); Launch(B, v); }
                bool squeeze = t > 0.8f + jt && t < 3.2f;
                A.Driver = Fixed(squeeze ? Cruise(A, v, Vector3.Right, 0.55f) : Cruise(A, v, Vector3.Right));
                B.Driver = Fixed(Cruise(B, v, Vector3.Right));
                break;
            }

            case "T5":
            {
                // Head-on; A turns at the last moment and takes the hit on its side.
                float v = 50f * Kmh * jv;
                if (launch) { Launch(A, v); Launch(B, v); }
                float gap = B.GlobalPosition.X - A.GlobalPosition.X - 2f * HalfLength;
                float closing = MathF.Max(A.ForwardSpeed + B.ForwardSpeed, 1f);
                if (!tr.Values.ContainsKey("turn") && t > 0.5f && gap / closing < 0.7f + jt) tr.Values["turn"] = t;
                bool turning = tr.Values.ContainsKey("turn");
                A.Driver = Fixed(turning ? new CarControls { Throttle = 0.3f, Steer = 1f, FastSteer = true } : Cruise(A, v, Vector3.Right));
                B.Driver = Fixed(Cruise(B, v, Vector3.Left));
                break;
            }
        }
    }

    private void Evaluate(Trial tr, float t)
    {
        var dA = tr.Referee.Damage(tr.A.Id);
        var dB = tr.Referee.Damage(tr.B.Id);
        float Lost(DamageState d, DamageZone z) => DamageState.MaxHealth - d.Health(z);

        switch (tr.Tactic)
        {
            case "probe":
                if (Mathf.PosMod(t, 0.5f) < 1f / 120f)
                    GD.Print($"[DDCode] probe t={t:0.0} A={tr.A.GlobalPosition} B={tr.B.GlobalPosition} C={tr.Extra?.GlobalPosition} " +
                             $"Clayer={tr.Extra?.CollisionLayer} Cmask={tr.Extra?.CollisionMask} Blayer={tr.B.CollisionLayer}");
                if (t < 5f) return;
                foreach (var i in tr.ImpactLog)
                    GD.Print($"[DDCode] probe impact t={i.Time:0.00} A=#{i.A.Id} B={(i.B == null ? "wall" : "#" + i.B.Id)} " +
                             $"closing={i.ClosingSpeed * 3.6f:0.0} km/h E={i.Energy / 1000f:0.0} kJ n={i.NormalAtoB} att={i.Attacker}");
                foreach (var e in tr.Events.Where(e => e.Kind is RefereeEventKind.Damage or RefereeEventKind.Hit or RefereeEventKind.SpinScored))
                    GD.Print($"[DDCode] probe {e.Kind} car#{e.Car} other#{e.Other} {e.Zone} {e.Amount:0.0} {e.Tier}");
                tr.Success = tr.ImpactLog.Count > 0;
                break;

            case "T1":
                if (t < 6f) return;
                tr.Note = $"tailgater front -{Lost(dB, DamageZone.Front):0.0}, checker rear -{Lost(dA, DamageZone.Rear):0.0}";
                tr.Success = Lost(dB, DamageZone.Front) >= 3f && Lost(dB, DamageZone.Front) > Lost(dA, DamageZone.Rear);
                break;

            case "T2":
            {
                var spin = tr.Events.FirstOrDefault(e => e.Kind == RefereeEventKind.SpinScored && e.Car == tr.A.Id && e.Other == tr.B.Id);
                bool spun = spin.Kind == RefereeEventKind.SpinScored;
                if (t < 6f && !spun) return;
                var carHits = tr.ImpactLog.Where(i => i.B != null).ToList();
                tr.Note = (spun ? $"overtaker spun {(int)spin.Tier}° at t={spin.Time:0.00}" : "no spin") +
                          $" | hits {carHits.Count}: " + string.Join(", ", carHits.Select(i => $"{i.ClosingSpeed * 3.6f:0.0}km/h {i.Energy / 1000f:0.0}kJ")) +
                          $" | max yaw B {Mathf.RadToDeg(tr.Values.GetValueOrDefault("maxYawB")):0}°/s";
                tr.Success = spun;
                break;
            }

            case "T3":
            {
                bool wallHit = tr.ImpactLog.Any(i => i.B == null && i.A == tr.B && i.Energy >= tr.Referee.Rules.DamageEnergyThreshold);
                if (t < 7f && !wallHit) return;
                tr.Note = wallHit ? $"victim hit the wall, front -{Lost(dB, DamageZone.Front):0.0}" : "victim made the bend";
                tr.Success = wallHit;
                break;
            }

            case "T4":
            {
                if (t < 4.5f) return;
                float side = Lost(dB, DamageZone.Left) + Lost(dB, DamageZone.Right);
                bool stable = tr.MaxAngularB < 6f && tr.MaxHeightB < 3f && tr.B.GlobalTransform.Basis.Y.Y > 0.5f
                              && !float.IsNaN(tr.B.GlobalPosition.X);
                tr.Note = $"{(tr.Extra != null ? "wreck" : "wall")}: victim sides -{side:0.0}, max ω {tr.MaxAngularB:0.0}, stable={stable}";
                tr.Success = side >= 3f && stable;
                break;
            }

            case "T5":
                if (t < 5f) return;
                tr.Note = $"tactician front -{Lost(dA, DamageZone.Front):0.0} side -{Lost(dA, DamageZone.Right) + Lost(dA, DamageZone.Left):0.0}, " +
                          $"other front -{Lost(dB, DamageZone.Front):0.0}";
                // You take side damage, they take front damage.
                tr.Success = Lost(dB, DamageZone.Front) > Lost(dA, DamageZone.Front) && Lost(dB, DamageZone.Front) >= 3f
                             && Lost(dA, DamageZone.Right) + Lost(dA, DamageZone.Left) >= Lost(dA, DamageZone.Front);
                break;
        }
        tr.Finished = true;
    }

    private static ICarInput Fixed(CarControls c) => new FixedInput(c);

    private sealed class FixedInput : ICarInput
    {
        private readonly CarControls _c;
        public FixedInput(CarControls c) => _c = c;
        public CarControls GetControls(Car car, float delta) => _c;
    }

    private void Report()
    {
        bool allPass = true;
        foreach (var (tactic, (ok, total)) in _results.OrderBy(r => r.Key))
        {
            float rate = total > 0 ? (float)ok / total : 0f;
            bool pass = tactic == "probe" ? ok > 0 : rate >= 0.6f;
            allPass &= pass;
            GD.Print($"[DDCode] {tactic}: {ok}/{total} ({rate * 100f:0}%) {(pass ? "PASS" : "FAIL")}");
        }
        DevTools.QuitClean(GetTree(), allPass ? 0 : 1);
    }
}
