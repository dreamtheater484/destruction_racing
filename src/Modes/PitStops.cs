using System;
using System.Collections.Generic;
using DestructionRacing.Rules;
using DestructionRacing.Tracks;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Modes;

/// <summary>
/// Pit stops (plan §6.0, DD2): a car that turns into the pit lane is guided to a free box. There the crew repairs one
/// zone at a time for at most 5 s: the player picks the zone with ←/→ and mashes Enter or Space, the AI repairs its
/// worst zone. Then the car is released down the lane (the player gets control back after about 2 s, the AI at the
/// end of the lane). A car in the pit lane is a ghost to the rest of the field until it is back on the track and
/// clear of other cars. Wreckin': one stop per race; Stock Car: as many as you like (<see cref="RulesConfig"/>).
/// </summary>
public sealed class PitStops
{
    public enum Stage { Approach, Lane, Repair, Release }

    /// <summary>Pitting cars move to their own collision layer: they still hit the walls, never another car.</summary>
    public const uint PitLayer = 16;
    /// <summary>Speeds along the lane (m/s): the entry, the lane itself, and the exit back onto the track ✱.</summary>
    public const float EntrySpeed = 20f, LaneSpeed = 16f, ExitSpeed = 24f;
    /// <summary>The human gets the wheel back this long after the release (plan §6.0: "about 2 s").</summary>
    public const float ManualHandBack = 2f;

    /// <summary>The zones around the car, in the order → steps through them (clockwise from above).</summary>
    public static readonly DamageZone[] ZoneOrder = { DamageZone.Front, DamageZone.Right, DamageZone.Rear, DamageZone.Left };

    public sealed class Stop
    {
        public required Car Car { get; init; }
        /// <summary>The car's own driver, handed back after the stop.</summary>
        public required ICarInput Driver { get; init; }
        /// <summary>The human player: repairs come from the keyboard and control returns early.</summary>
        public bool Manual { get; init; }
        /// <summary>Automatic repairs: presses per second.</summary>
        public float PressRate { get; init; } = 7f;
        public Stage Stage { get; internal set; }
        public int Box { get; internal set; } = -1;
        public double StageStart { get; internal set; }
        public int Selected { get; internal set; }
        public DamageZone SelectedZone => ZoneOrder[Selected];
        public double LastPress { get; internal set; } = double.MinValue;
        public float Repaired { get; internal set; }
        public int Presses { get; internal set; }
        public bool ReleaseRequested { get; internal set; }
        /// <summary>Seconds the autopilot has been stuck (not moving outside the box).</summary>
        public float StuckTime { get; internal set; }
        public double ReverseUntil { get; internal set; } = double.MinValue;
        public int Reversals { get; internal set; }
    }

    private readonly RaceLayout _layout;
    private readonly RulesConfig _rules;
    private readonly int _allowed;
    private readonly Func<Car, (float S, float Lateral)> _trackPos;
    private readonly Dictionary<int, Stop> _stops = new();
    private readonly Dictionary<int, int> _used = new();
    private readonly HashSet<int> _ghosts = new();
    /// <summary>Ghosts for good: cars that took the flag (they cruise round, out of everyone's way).</summary>
    private readonly HashSet<int> _permanent = new();
    /// <summary>Cars that drove into the lane without a stop left (or just finished one): not caught again until they leave it.</summary>
    private readonly HashSet<int> _passing = new();
    private readonly float _entryLateral;
    private double _time;

    public PitStops(RaceLayout layout, RulesConfig rules, GameMode kind, Func<Car, (float S, float Lateral)> trackPos)
    {
        _layout = layout;
        _rules = rules;
        _trackPos = trackPos;
        _allowed = !rules.PitStops ? 0 : kind == GameMode.StockCarRacing ? rules.StockCarPitStopsAllowed : rules.WreckinPitStopsAllowed;
        var lane = layout.Pit;
        _entryLateral = layout.Path.Project(lane.PositionAt(0f), lane.EntryS, 20f).Lateral;
    }

    public PitLane Lane => _layout.Pit;
    public float TimeLimit => _rules.PitStopTimeLimit;
    /// <summary>Stops per race (−1 = unlimited).</summary>
    public int Allowed => _allowed;
    public int TotalStops { get; private set; }

    /// <summary>A pit stop happened (the car stopped in its box), for the HUD and the commentary.</summary>
    public event Action<Stop>? Stopped;
    /// <summary>The crew is done (time up, or nothing left to fix): the car leaves its box.</summary>
    public event Action<Stop>? Released;
    /// <summary>A car drove into the pit lane with no stop left.</summary>
    public event Action<Car>? Refused;

    public int StopsBy(Car car) => _used.GetValueOrDefault(car.Id);
    public int StopsLeft(Car car) => _allowed < 0 ? int.MaxValue : Math.Max(0, _allowed - StopsBy(car));
    public bool CanStop(Car car) => !car.IsWrecked && StopsLeft(car) > 0 && !_stops.ContainsKey(car.Id);
    public Stop? StopOf(Car car) => _stops.GetValueOrDefault(car.Id);
    /// <summary>Under pit control (guided by the autopilot).</summary>
    public bool IsManaged(Car car) => _stops.ContainsKey(car.Id);
    /// <summary>No contact with the field (in the pit lane, or not yet clear after it).</summary>
    public bool IsGhost(Car car) => _ghosts.Contains(car.Id);

    /// <summary>A car out of the race but still driving (it took the flag): a ghost from now on, never caught by the pits.</summary>
    public void MakePermanentGhost(Car car)
    {
        _permanent.Add(car.Id);
        if (_stops.TryGetValue(car.Id, out var stop)) Finish(stop);
        if (_ghosts.Add(car.Id)) SetGhost(car, true);
    }
    /// <summary>On the pit lane's own surface (not the track).</summary>
    public bool OnPitSurface(Car car) => _layout.SurfaceAt(car.GlobalPosition) == SpeedwayData.Pit;

    /// <summary>Metres along the track to the pit entry (0..lap length).</summary>
    public float DistanceToEntry(float s) => _layout.Path.Wrap(_layout.Pit.EntryS - s);

    // ------------------------------------------------------------------ AI decisions

    /// <summary>
    /// The AI's pit decision (plan §10.3): enough damage to be worth the time, and enough race left to use the repair.
    /// <paramref name="threshold"/> is the driver's own line (around 45 % front health).
    /// </summary>
    public bool WantsStop(Car car, int lap, int laps, float threshold)
    {
        if (!CanStop(car) || lap >= laps || car.Damage == null) return false;
        var d = car.Damage;
        float front = d.Health(DamageZone.Front), rear = d.Health(DamageZone.Rear);
        float side = MathF.Min(d.Health(DamageZone.Left), d.Health(DamageZone.Right));
        float wheel = 100f;
        foreach (Wheel w in Enum.GetValues<Wheel>())
            if (!d.IsWheelLost(w)) wheel = MathF.Min(wheel, d.WheelHealth(w));
        // One stop only (Wreckin'): save it for real trouble; unlimited stops (Stock Car): come in sooner. With the
        // final lap next, only for an engine about to go.
        float t = threshold * (_allowed == 1 ? 0.85f : 1.15f);
        if (lap == laps - 1) return front < t * 0.5f;
        return front < t || side < t * 0.7f || rear < t * 0.6f || wheel < t * 0.5f;
    }

    /// <summary>Hands a car to the autopilot ahead of the entry (the AI and the PlayerProxy decide; the player drives in).</summary>
    public bool Request(Car car, bool manual, float pressRate)
    {
        if (!CanStop(car) || car.Driver == null) return false;
        Begin(car, Stage.Approach, manual, pressRate);
        return true;
    }

    // ------------------------------------------------------------------ per step

    /// <summary>
    /// Every physics step: catches cars turning into the lane, runs the stops (repairs, release, hand-back) and turns
    /// ghosts solid again once they are clear. <paramref name="manualFor"/> tells whether a car is the human's and
    /// its repair rate (for a car that drove in on its own).
    /// </summary>
    public void Step(double time, IReadOnlyList<Car> cars, Func<Car, (bool Manual, float Rate)> manualFor)
    {
        float dt = (float)Math.Clamp(time - _time, 0.0, 0.1);
        _time = time;
        var lane = _layout.Pit;
        foreach (var car in cars)
        {
            if (_stops.TryGetValue(car.Id, out var stop))
            {
                if (car.IsWrecked) { Finish(stop); continue; }
                StepStop(stop);
                if (_stops.ContainsKey(car.Id)) Unstick(stop, dt);
                continue;
            }
            if (_permanent.Contains(car.Id)) continue;
            bool onPit = OnPitSurface(car);
            if (!onPit) _passing.Remove(car.Id);
            if (onPit && !_passing.Contains(car.Id) && !car.IsWrecked && InLaneHeadingIn(car))
            {
                if (CanStop(car))
                {
                    var (manual, rate) = manualFor(car);
                    Begin(car, Stage.Lane, manual, rate);
                }
                else
                {
                    _passing.Add(car.Id);
                    if (_allowed != 0) Refused?.Invoke(car);
                }
            }
        }

        // Ghosts turn solid once off the pit surface (or past the end of the lane) and clear of every other car.
        if (_ghosts.Count > 0)
        {
            var solid = new List<int>();
            foreach (int id in _ghosts)
            {
                if (_stops.ContainsKey(id) || _permanent.Contains(id)) continue;
                Car? me = null;
                foreach (var c in cars) if (c.Id == id) { me = c; break; }
                if (me == null) { solid.Add(id); continue; }
                if (OnPitSurface(me) && lane.Project(me.GlobalPosition) < lane.Length - 2f) continue;
                bool clear = true;
                foreach (var c in cars)
                    if (c != me && Flat(c.GlobalPosition - me.GlobalPosition).LengthSquared() < 7f * 7f) { clear = false; break; }
                if (clear) solid.Add(id);
            }
            foreach (int id in solid)
            {
                _ghosts.Remove(id);
                foreach (var c in cars) if (c.Id == id) SetGhost(c, false);
            }
        }
    }

    private bool InLaneHeadingIn(Car car)
    {
        var lane = _layout.Pit;
        Vector3 p = car.GlobalPosition;
        float u = lane.Project(p);
        if (u < 2f || u > lane.Length * 0.6f) return false;
        if (Flat(lane.PositionAt(u) - p).Length() > 4.5f) return false;
        Vector3 fwd = Flat(-car.GlobalTransform.Basis.Z);
        return fwd.LengthSquared() > 1e-4f && fwd.Normalized().Dot(lane.TangentAt(u)) > 0.5f;
    }

    private void Begin(Car car, Stage stage, bool manual, float rate)
    {
        var stop = new Stop { Car = car, Driver = car.Driver!, Manual = manual, PressRate = rate, Stage = stage, StageStart = _time };
        _stops[car.Id] = stop;
        car.Driver = new Autopilot(this, stop);
        if (stage == Stage.Lane) EnterLane(stop);
    }

    private void EnterLane(Stop stop)
    {
        stop.Stage = Stage.Lane;
        stop.StageStart = _time;
        stop.Box = PickBox(stop.Car);
        if (_ghosts.Add(stop.Car.Id)) SetGhost(stop.Car, true);
    }

    /// <summary>The first box nobody is using; when all are busy, the last one (ghosts can share it).</summary>
    private int PickBox(Car car)
    {
        var boxes = _layout.Pit.Boxes;
        float u = _layout.Pit.Project(car.GlobalPosition);
        for (int b = 0; b < boxes.Length; b++)
        {
            if (boxes[b] < u + 8f) continue;      // too close to brake for
            bool taken = false;
            foreach (var s in _stops.Values)
                if (s.Car != car && s.Box == b && s.Stage is Stage.Lane or Stage.Repair) { taken = true; break; }
            if (!taken) return b;
        }
        return boxes.Length - 1;
    }

    private void StepStop(Stop stop)
    {
        var car = stop.Car;
        switch (stop.Stage)
        {
            case Stage.Approach:
            {
                var lane = _layout.Pit;
                Vector3 p = car.GlobalPosition;
                float u = lane.Project(p);
                float toEntry = DistanceToEntry(_trackPos(car).S);
                float past = toEntry > _layout.Path.Length * 0.5f ? _layout.Path.Length - toEntry : 0f;  // beyond the entry point
                float fromLane = Flat(lane.PositionAt(u) - p).Length();
                // In the lane: on its surface, or on its line. (Projecting onto the lane is not enough: from the track
                // side of the pit wall the autopilot would steer into the wall.)
                if (OnPitSurface(car) || (u > 0.5f && fromLane < 2.5f)) EnterLane(stop);
                // Missed it: well past the entry, or past it and still on the track side of the wall's nose.
                else if (past > 15f || (past > 5f && fromLane > 4f)) Finish(stop);
                break;
            }
            case Stage.Lane:
            {
                float toBox = _layout.Pit.Boxes[stop.Box] - _layout.Pit.Project(car.GlobalPosition);
                if ((toBox < 0.9f && MathF.Abs(car.ForwardSpeed) < 1.2f) || toBox < -3f)
                {
                    stop.Stage = Stage.Repair;
                    stop.StageStart = _time;
                    _used[car.Id] = StopsBy(car) + 1;
                    TotalStops++;
                    stop.Selected = WorstZone(car);
                    Stopped?.Invoke(stop);
                }
                break;
            }
            case Stage.Repair:
            {
                double elapsed = _time - stop.StageStart;
                if (!stop.Manual)
                {
                    stop.Selected = WorstZone(car);
                    double interval = 1.0 / Math.Clamp(stop.PressRate, 1f, MaxPressRate);
                    if (_time - stop.LastPress >= interval) Press(stop);
                    if (NothingToRepair(car) && elapsed > 0.8) stop.ReleaseRequested = true;
                }
                if (elapsed >= TimeLimit || (stop.ReleaseRequested && elapsed > 0.5))
                {
                    stop.Stage = Stage.Release;
                    stop.StageStart = _time;
                    Released?.Invoke(stop);
                }
                break;
            }
            case Stage.Release:
            {
                float u = _layout.Pit.Project(car.GlobalPosition);
                bool laneEnd = u >= _layout.Pit.Length - 2f;
                if (laneEnd || (stop.Manual && _time - stop.StageStart >= ManualHandBack)) Finish(stop);
                break;
            }
        }
    }

    /// <summary>
    /// The autopilot got stuck (a wreck in the lane, a wall): on the approach, give the car back to its driver; in the
    /// lane, back up for a second and try again, three times at most.
    /// </summary>
    private void Unstick(Stop stop, float dt)
    {
        var car = stop.Car;
        bool waiting = stop.Stage == Stage.Repair || _time < stop.ReverseUntil
                       || (stop.Stage == Stage.Lane && _layout.Pit.Boxes[stop.Box] - _layout.Pit.Project(car.GlobalPosition) < 1.5f);
        stop.StuckTime = !waiting && MathF.Abs(car.ForwardSpeed) < 0.5f ? stop.StuckTime + dt : 0f;
        if (stop.StuckTime < 2.5f) return;
        stop.StuckTime = 0f;
        if (stop.Stage == Stage.Approach || stop.Reversals >= 3) { Finish(stop); return; }
        stop.Reversals++;
        stop.ReverseUntil = _time + 1.0;
    }

    private void Finish(Stop stop)
    {
        var car = stop.Car;
        _stops.Remove(car.Id);
        if (car.Driver is Autopilot) car.Driver = stop.Driver;
        _passing.Add(car.Id);
        // An approach that never reached the lane leaves the car solid.
        if (stop.Stage == Stage.Approach && _ghosts.Remove(car.Id)) SetGhost(car, false);
    }

    // ------------------------------------------------------------------ repairs (plan §6.1)

    private float MaxPressRate => _rules.PitMaxPressRate;

    /// <summary>The player's ←/→: the next zone round the car.</summary>
    public void Select(Stop stop, int direction)
    {
        if (stop.Stage != Stage.Repair && stop.Stage != Stage.Lane) return;
        stop.Selected = ((stop.Selected + direction) % ZoneOrder.Length + ZoneOrder.Length) % ZoneOrder.Length;
    }

    /// <summary>
    /// One repair press on the selected zone (and half as much on its two wheels; lost wheels stay lost). Presses
    /// faster than <see cref="RulesConfig.PitMaxPressRate"/> are ignored. Returns whether it counted.
    /// </summary>
    public bool Press(Stop stop)
    {
        if (stop.Stage != Stage.Repair || stop.Car.Damage is not { } d || d.IsWrecked) return false;
        if (_time - stop.LastPress < 1.0 / MaxPressRate - 1e-6) return false;
        stop.LastPress = _time;
        stop.Presses++;
        var zone = stop.SelectedZone;
        float amount = _rules.PitRepairPerPress;
        float before = d.Health(zone);
        d.Repair(zone, amount);
        float done = d.Health(zone) - before;
        foreach (var w in WheelsOf(zone))
        {
            float wb = d.WheelHealth(w);
            d.RepairWheel(w, amount * 0.5f);
            done += d.WheelHealth(w) - wb;
        }
        stop.Repaired += done;
        return true;
    }

    public void RequestRelease(Stop stop)
    {
        if (stop.Stage == Stage.Repair) stop.ReleaseRequested = true;
    }

    public double TimeLeft(Stop stop) => stop.Stage == Stage.Repair ? Math.Max(0.0, TimeLimit - (_time - stop.StageStart)) : TimeLimit;

    private static IEnumerable<Wheel> WheelsOf(DamageZone zone) => zone switch
    {
        DamageZone.Front => new[] { Wheel.FrontLeft, Wheel.FrontRight },
        DamageZone.Rear => new[] { Wheel.RearLeft, Wheel.RearRight },
        DamageZone.Left => new[] { Wheel.FrontLeft, Wheel.RearLeft },
        _ => new[] { Wheel.FrontRight, Wheel.RearRight },
    };

    /// <summary>The zone the crew should work on: the front matters most (it is the engine), the tough rear least.</summary>
    private static int WorstZone(Car car)
    {
        if (car.Damage is not { } d) return 0;
        int best = 0;
        float bestScore = float.MaxValue;
        for (int i = 0; i < ZoneOrder.Length; i++)
        {
            var z = ZoneOrder[i];
            float h = d.Health(z);
            foreach (var w in WheelsOf(z))
                if (!d.IsWheelLost(w)) h = MathF.Min(h, 0.5f * (h + d.WheelHealth(w)));
            float score = h * (z == DamageZone.Front ? 0.8f : z == DamageZone.Rear ? 1.3f : 1f);
            if (h < 99.5f && score < bestScore) { bestScore = score; best = i; }
        }
        return best;
    }

    private static bool NothingToRepair(Car car)
    {
        if (car.Damage is not { } d) return true;
        foreach (var z in ZoneOrder) if (d.Health(z) < 95f) return false;
        foreach (Wheel w in Enum.GetValues<Wheel>()) if (!d.IsWheelLost(w) && d.WheelHealth(w) < 90f) return false;
        return true;
    }

    // ------------------------------------------------------------------ guidance

    private static void SetGhost(Car car, bool ghost)
    {
        car.CollisionLayer = ghost ? PitLayer : Car.CarLayer;
        car.CollisionMask = ghost ? Car.WorldLayer : Car.WorldLayer | Car.CarLayer;
    }

    /// <summary>Drives a car through its stop: onto the lane, into its box, out again.</summary>
    private sealed class Autopilot : ICarInput
    {
        private readonly PitStops _pits;
        private readonly Stop _stop;
        public Autopilot(PitStops pits, Stop stop) { _pits = pits; _stop = stop; }
        public CarControls GetControls(Car car, float delta) => _pits.Drive(_stop, car);
    }

    private CarControls Drive(Stop stop, Car car)
    {
        var lane = _layout.Pit;
        var track = _layout.Path;
        Vector3 pos = car.GlobalPosition;
        float speed = car.ForwardSpeed;
        float look = Mathf.Clamp(5f + 0.45f * MathF.Abs(speed), 6f, 16f);
        float u = lane.Project(pos);
        if (_time < stop.ReverseUntil) return new CarControls { Reverse = true, Hold = true };
        Vector3 aim;
        float target;
        switch (stop.Stage)
        {
            case Stage.Approach:
            {
                float s = _trackPos(car).S;
                float toEntry = DistanceToEntry(s);
                if (toEntry > track.Length * 0.5f) toEntry -= track.Length;   // just past the entry point
                aim = toEntry > look ? track.PositionAt(s + look, _entryLateral) : lane.PositionAt(look - toEntry);
                target = MathF.Sqrt(EntrySpeed * EntrySpeed + 2f * 7f * MathF.Max(0f, toEntry));
                break;
            }
            case Stage.Lane:
            {
                float toBox = lane.Boxes[stop.Box] - u;
                aim = lane.PositionAt(u + look);
                target = MathF.Min(u < 20f ? Mathf.Lerp(EntrySpeed, LaneSpeed, u / 20f) : LaneSpeed, MathF.Sqrt(2f * 4.5f * MathF.Max(0f, toBox - 0.4f)));
                if (toBox < 0.9f) return new CarControls { Brake = 1f, Hold = true };
                break;
            }
            case Stage.Repair:
                return new CarControls { Brake = 1f, Hold = true };
            default:
            {
                aim = lane.PositionAt(u + look);
                target = u > lane.Length - 30f ? ExitSpeed : LaneSpeed;
                break;
            }
        }
        Vector3 fwd = Flat(-car.GlobalTransform.Basis.Z);
        Vector3 dir = Flat(aim - pos);
        float err = dir.LengthSquared() > 1e-4f && fwd.LengthSquared() > 1e-4f
            ? Mathf.Atan2(fwd.Normalized().Cross(dir.Normalized()).Y, fwd.Normalized().Dot(dir.Normalized()))
            : 0f;
        float dv = target - speed;
        return new CarControls
        {
            Throttle = dv > -0.5f ? Mathf.Clamp(dv * 0.4f + 0.2f, 0f, 1f) : 0f,
            Brake = dv < -1f ? Mathf.Clamp(-dv * 0.25f, 0f, 1f) : 0f,
            Hold = true,
            Steer = Mathf.Clamp(-err * 2.2f, -1f, 1f),
        };
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);
}
