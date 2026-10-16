using System;
using System.Collections.Generic;
using System.Linq;
using DestructionRacing.Rules;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.AI;

/// <summary>
/// Racing AI for Wreckin' and Stock Car Racing (plan §10.3): pure pursuit on one of three lanes with a curvature
/// speed profile scaled by skill, car-following, lane-switch overtaking (only into a clear lane), steering round
/// wrecks, recovery after spins, crossover behaviour by personality (brake, gamble or aim), and the DD Code
/// (plan §4.7). On offence: T1 (brake-check a tailgater), T2 (turn into the rear corner of a car overtaking us), T3
/// (tap a car braking for a bend), T4 (squeeze a car beside us into the wall or a wreck) and the occasional bump on
/// the car ahead; the ×2 leader draws attacks from opportunists. On defence: skilled drivers dodge a car turning
/// into them and block the inside into a bend; low-skill drivers tailgate in the draft and see the car ahead
/// brake a moment late, so a brake-check catches them (and they brake predictably into bends: T3 bait).
/// </summary>
public sealed class RaceBrain
{
    public enum Behaviour { Race, Overtake, Attack, CrossoverBrake, CrossoverAim, TurnRound, Recover, Shove, BrakeCheck, Tailgate, Evade }

    private static readonly float[] Lanes = { -4f, 0f, 4f };
    private const float CarLength = 4.6f;
    /// <summary>Two cars overlap sideways when their centres are closer than this (car width plus a margin).</summary>
    private const float SideBySide = 2.3f;

    private readonly Personality _p;
    private readonly Random _rng;
    private bool _laneSet;
    private float _laneTarget;
    private float _lane;
    private float _stuckTime;
    private float _recoverTime;
    private float _lastSteer;
    private double _attackUntil;
    private Car? _attackTarget;
    private bool _leaderHunt;
    private double _nextAttackRoll;
    private double _crossDecisionUntil;
    private int _crossChoice; // 0 none, 1 yield, 2 gamble, 3 aim
    private Car? _crossTarget;
    private float _crossYieldSpeed;
    private double _nextLaneChange;
    private float _blockedTime;
    private float _shoveTime;
    private float _shoveLateral;
    private bool _squeeze;
    /// <summary>The speed we last saw each car ahead going (and when): following reacts with a lag.</summary>
    private readonly Dictionary<int, (float Speed, double Time)> _seen = new();
    private Car? _tailgateTarget;
    private double _tailgateUntil;
    private double _nextTailgateRoll;
    private double _brakeCheckUntil;
    private double _nextBrakeCheckRoll;
    private double _nextDefenceCheck;
    private double _evadeUntil;
    private double _nextBlock;

    public RaceBrain(Personality personality, int seed)
    {
        _p = personality;
        _rng = new Random(seed);
        _nextAttackRoll = 10.0 + _rng.NextDouble() * 4.0;
    }

    public Personality Personality => _p;
    public Behaviour Current { get; private set; }
    /// <summary>
    /// Steer from where the car will be this many seconds ahead (pose extrapolated with its velocity and yaw rate).
    /// The PlayerProxy uses it to anticipate its own reaction lag, as a player does; the AI reacts instantly.
    /// </summary>
    public float Anticipation { get; set; }
    /// <summary>Division pace scale for the AI (plan §10.5): Div 4 drives a little below its skill's pace.</summary>
    public float PaceScale { get; set; } = 1f;
    /// <summary>
    /// Stock Car (plan §10.3): contact scores nothing, so most drivers race clean. Bruisers and psychos still bump
    /// (the manual's "Bouncer"), nobody hunts the leader, and only psychos aim at crossing traffic.
    /// </summary>
    public bool StockCar { get; set; }
    public Car? AttackTarget => _attackTarget;

    /// <summary>Experiments (RaceSim --nodefence, --nolag, --notailgate, --nobrakecheck): switch a DD Code behaviour off.</summary>
    public static bool DefenceEnabled { get; set; } = true;
    public static bool LagEnabled { get; set; } = true;
    public static bool TailgateEnabled { get; set; } = true;
    public static bool BrakeCheckEnabled { get; set; } = true;
    public static bool SqueezeEnabled { get; set; } = true;
    /// <summary>
    /// Stock Car is pack racing (plan §4.3): everyone follows closer and drafts more ✱. Following gap in seconds,
    /// and the share of rolls on which even a clean driver tucks into the draft.
    /// </summary>
    public static float StockCarHeadway { get; set; } = 0.3f;
    public static float StockCarDraft { get; set; } = 0f;
    /// <summary>How far behind the leader (m) a car still goes for it, and how far ahead of it (being lapped) ✱.</summary>
    public static float LeaderHuntBehind { get; set; } = 40f;
    public static float LeaderHuntAhead { get; set; } = 10f;
    /// <summary>Stock Car: the lap of the leader on which the bumping starts (the pack races clean for two laps) ✱.</summary>
    public static int StockCarBumpLap { get; set; } = 3;

    // DD Code counters for the sims (plan §4.7: the AI uses the tactics and falls for them).
    public int BrakeChecks { get; private set; }
    public Car? BrakeCheckVictim { get; private set; }
    public double BrakeCheckTime { get; private set; } = double.MinValue;
    public int Squeezes { get; private set; }
    public int Tailgates { get; private set; }
    public int Evasions { get; private set; }
    public int Blocks { get; private set; }

    /// <summary>
    /// How late we see the car ahead change speed: a skilled driver 0.06 s, an aggressive rookie a quarter of a
    /// second. The PlayerProxy, standing in for a human watching the road, 0.15 s.
    /// </summary>
    private float ReactionLag => !LagEnabled ? 0.001f : Anticipation > 0f ? 0.15f : 0.06f + 0.45f * (1f - _p.Skill) * _p.Aggression;
    /// <summary>Following gap in seconds of speed (plus 2 m): the impatient follow closer.</summary>
    private float Headway => (StockCar ? StockCarHeadway : 0.3f) - (LagEnabled ? 0.15f * (1f - _p.Skill) * _p.Aggression : 0f);

    public CarControls Think(Car me, IRaceView view, float dt)
    {
        if (!view.IsRunning(me)) return CarControls.Parked;
        var track = view.Track;
        double t = view.Time;
        var (s, lateral) = view.TrackPos(me);
        Vector3 pos = me.GlobalPosition;
        Vector3 fwd = FlatDir(-me.GlobalTransform.Basis.Z);
        Vector3 tangent = track.TangentAt(s);
        float headingErr = SignedAngle(fwd, tangent);       // + = the track runs to our left
        float speed = me.ForwardSpeed;
        if (!_laneSet)
        {
            _lane = _laneTarget = SnapLane(lateral);
            _laneSet = true;
        }

        if (_recoverTime > 0f)
        {
            _recoverTime -= dt;
            Current = Behaviour.Recover;
            return new CarControls { Reverse = true, Steer = -_lastSteer };
        }

        // Spun round or facing the wrong way: slow down, then the DD1 turn-around.
        if (MathF.Abs(headingErr) > 1.75f)
        {
            Current = Behaviour.TurnRound;
            var c = MathF.Abs(speed) > 6f
                ? new CarControls { Brake = 1f, Hold = true }
                : new CarControls { Throttle = 1f, TurnAround = true, Steer = -MathF.Sign(headingErr) };
            UpdateStuck(c, MathF.Abs(speed), dt);
            return c;
        }

        Current = Behaviour.Race;
        ChooseLane(me, view, s, lateral, speed, t);
        Defend(me, view, s, lateral, speed, t);
        _lane = Mathf.MoveToward(_lane, _laneTarget, (t < _evadeUntil ? 6f : 2.5f) * dt);
        Tailgate(me, view, s, lateral, speed, t);
        bool brakeCheck = BrakeCheck(me, view, s, lateral, speed, t);

        // Default: pure pursuit on the lane.
        float lookahead = Mathf.Clamp(7f + 0.55f * speed, 8f, 30f);
        Vector3 aim = track.PositionAt(s + lookahead, _lane);
        // Stock Car is pack racing: skill counts for less pace (4 % from rookie to racer, against 6.5 %) ✱.
        float skillFactor = StockCar ? 0.91f + 0.09f * _p.Skill : 0.86f + 0.14f * _p.Skill;
        float targetSpeed = track.SpeedAt(s + 4f) * skillFactor * PaceScale * view.CatchUp(me);
        bool ram = false;

        // DD Code on offence.
        var attack = PickAttack(me, view, s, lateral, speed, t);
        if (attack != null)
        {
            Current = Behaviour.Attack;
            Vector3 aFwd = FlatDir(-attack.GlobalTransform.Basis.Z);
            Vector3 aRight = new(-aFwd.Z, 0f, aFwd.X);
            Vector3 rel = pos - attack.GlobalPosition;
            float along = rel.Dot(aFwd), across = rel.Dot(aRight);
            // Come at them from the side with more road; once beside their rear quarter, turn in (DD Code T2): a
            // sideways tap 1.7 m behind their centre spins them far better than a push on the bumper. (Scaling the
            // offsets with the target's length was tried in M6: it did not change the short Volant's Stock Car lead
            // and wrecked the average fan more often on the oval, 14% before half distance against 8%.)
            float side = MathF.Abs(across) > 1.0f ? MathF.Sign(across) : (view.TrackPos(attack).Lateral > 0f ? -1f : 1f);
            Vector3 ahead = attack.GlobalPosition + attack.LinearVelocity * 0.15f;
            bool beside = along > -4.5f && MathF.Abs(across) > 1.2f;
            if (_squeeze)
            {
                // T4: lean on their side at their speed and pin them against the wall or the wreck.
                aim = ahead + aFwd * 0.6f;
                targetSpeed = attack.ForwardSpeed + 0.6f;
                if (MathF.Abs(along) > 4f) _attackUntil = 0;
            }
            else if (beside)
            {
                aim = ahead - aFwd * 1.7f + aRight * side * 0.3f;
                targetSpeed = attack.ForwardSpeed + 1.5f;
            }
            else
            {
                aim = ahead - aFwd * 5.5f + aRight * side * 2.3f;
                float gap = -along;
                targetSpeed = attack.ForwardSpeed + Mathf.Clamp(gap * 0.4f, 2f, 7f);
            }
            targetSpeed = MathF.Min(MathF.Max(targetSpeed, 8f), track.SpeedAt(s) * 1.08f);
            ram = true;
        }

        // The crossover: yield, gamble (floor it) or aim (by personality).
        int cross = Crossover(me, view, s, speed, t);
        if (cross == 1)
        {
            Current = Behaviour.CrossoverBrake;
            targetSpeed = MathF.Min(targetSpeed, _crossYieldSpeed);
        }
        else if (cross == 2)
        {
            targetSpeed = MathF.Max(targetSpeed, speed + 5f);
        }
        else if (cross == 3 && _crossTarget != null && view.IsRunning(_crossTarget))
        {
            Current = Behaviour.CrossoverAim;
            aim = _crossTarget.GlobalPosition + _crossTarget.LinearVelocity * 0.35f;
            ram = true;
        }

        // Car-following: don't run into the car in front unless that is the plan.
        if (!ram)
        {
            float follow = FollowSpeed(me, view, s, lateral, speed);
            if (t < _evadeUntil && follow > speed) targetSpeed = MathF.Min(targetSpeed + 3f, track.SpeedAt(s) * 1.05f);
            // Held up behind a wreck or a jam for 2 s: shove through the widest gap at walking pace (DD style:
            // wrecks get pushed aside). If even that fails, the immobilised rule ends it after 10 s.
            if (follow < 3f && speed < 2f) _blockedTime += dt;
            else _blockedTime = MathF.Max(0f, _blockedTime - dt);
            if (_shoveTime <= 0f && _blockedTime > 2f)
            {
                _shoveTime = 3.5f;
                _shoveLateral = WidestGap(me, view, s);
                _blockedTime = 0f;
            }
            if (_shoveTime > 0f)
            {
                _shoveTime -= dt;
                Current = Behaviour.Shove;
                aim = track.PositionAt(s + 9f, _shoveLateral);
                targetSpeed = 5f;
            }
            else targetSpeed = MathF.Min(targetSpeed, follow);
        }

        Vector3 seenPos = pos, seenFwd = fwd;
        if (Anticipation > 0f)
        {
            seenPos += Flat(me.LinearVelocity) * Anticipation;
            seenFwd = fwd.Rotated(Vector3.Up, me.AngularVelocity.Y * Anticipation);
        }
        Vector3 dir = Flat(aim - seenPos);
        float err = dir.LengthSquared() > 1e-4f ? SignedAngle(seenFwd, dir.Normalized()) : 0f;
        float steer = Mathf.Clamp(-err * 2.2f, -1f, 1f);
        float dv = targetSpeed - speed;
        var controls = new CarControls
        {
            Throttle = dv > -1f ? Mathf.Clamp(dv * 0.5f + 0.25f, 0f, 1f) : 0f,
            Brake = dv < -2f ? Mathf.Clamp(-dv * 0.2f, 0f, 1f) : 0f,
            Hold = true,
            Steer = steer,
            FastSteer = ram && MathF.Abs(err) > 0.4f && speed < 22f,
        };
        if (brakeCheck)
        {
            Current = Behaviour.BrakeCheck;
            controls.Throttle = 0f;
            controls.Brake = 1f;
        }
        UpdateStuck(controls, MathF.Abs(speed), dt);
        _lastSteer = controls.Steer;
        return controls;
    }

    // ------------------------------------------------------------------ traffic

    /// <summary>
    /// The fastest speed from which we can still slow to the speed of each car in our path (wrecks: 0 m/s) at a
    /// comfortable 6 m/s² before reaching a safe gap behind it.
    /// </summary>
    private float FollowSpeed(Car me, IRaceView view, float s, float lateral, float speed)
    {
        const float decel = 6f;
        float limit = float.MaxValue;
        float range = 10f + speed * speed / (2f * decel) + speed * 0.5f;
        double t = view.Time;
        float lag = ReactionLag;
        bool tailgating = t < _tailgateUntil;
        foreach (var c in view.Cars)
        {
            if (c == me || view.IsGhost(c)) continue;
            var (cs, cl) = view.TrackPos(c);
            float ds = view.Track.Delta(s, cs);
            if (ds <= 0f || ds > range || MathF.Abs(cl - lateral) > SideBySide) continue;
            float actual = view.IsRunning(c) ? MathF.Max(0f, c.LinearVelocity.Dot(view.Track.TangentAt(cs))) : 0f;
            // We see the car ahead change speed a moment late (DD Code T1: that is how a brake-check catches you).
            float theirs = actual;
            if (_seen.TryGetValue(c.Id, out var seen) && t - seen.Time < 0.25)
                theirs = seen.Speed + (actual - seen.Speed) * (1f - MathF.Exp(-(float)(t - seen.Time) / lag));
            _seen[c.Id] = (theirs, t);
            // Tailgating in the draft: right on their bumper (under 2 m at race speed).
            float buffer = tailgating && c == _tailgateTarget ? 0.8f + speed * 0.03f : 2f + speed * Headway;
            float room = ds - CarLength - buffer;
            limit = MathF.Min(limit, MathF.Sqrt(theirs * theirs + 2f * decel * MathF.Max(room, 0f)));
        }
        return limit;
    }

    // ------------------------------------------------------------------ DD Code T1: tailgaters and brake-checks

    /// <summary>
    /// The impatient tuck in behind the car ahead on a straight, in its draft and on its bumper, for a few seconds:
    /// fast, and exactly what a brake-check punishes.
    /// </summary>
    private void Tailgate(Car me, IRaceView view, float s, float lateral, float speed, double t)
    {
        if (t < _tailgateUntil)
        {
            var c = _tailgateTarget;
            bool keep = c != null && view.IsRunning(c) && !view.IsGhost(c)
                        && MathF.Abs(view.Track.CurvatureAt(s + 25f)) < 0.006f;
            if (keep)
            {
                var (cs, cl) = view.TrackPos(c!);
                float ds = view.Track.Delta(s, cs);
                keep = ds > 0f && ds < 30f && MathF.Abs(cl - lateral) < 1.8f;
            }
            if (keep)
            {
                if (Current == Behaviour.Race) Current = Behaviour.Tailgate;
                return;
            }
            _tailgateUntil = 0;
            _tailgateTarget = null;
        }
        if (t < _nextTailgateRoll || speed < 15f || !TailgateEnabled) return;
        _nextTailgateRoll = t + 1.5;
        if (MathF.Abs(view.Track.CurvatureAt(s + 30f)) > 0.004f) return;
        foreach (var c in view.Cars)
        {
            if (c == me || !view.IsRunning(c) || view.IsGhost(c)) continue;
            var (cs, cl) = view.TrackPos(c);
            float ds = view.Track.Delta(s, cs);
            if (ds < 5f || ds > 25f || MathF.Abs(cl - lateral) > 1.2f) continue;
            if (_rng.NextDouble() < 0.05f + 0.3f * _p.Aggression * (1f - _p.Skill) + (StockCar ? StockCarDraft : 0f))
            {
                _tailgateTarget = c;
                _tailgateUntil = t + 3.0 + 3.0 * _rng.NextDouble();
                Tailgates++;
            }
            return;
        }
    }

    /// <summary>T1: a car on our bumper, on a straight: stand on the brakes for half a second, and it takes the damage on its front.</summary>
    private bool BrakeCheck(Car me, IRaceView view, float s, float lateral, float speed, double t)
    {
        if (t < _brakeCheckUntil) return true;
        if (t < _nextBrakeCheckRoll || speed < 15f || t < 15.0 || !BrakeCheckEnabled) return false;
        _nextBrakeCheckRoll = t + 1.0;
        if (MathF.Abs(view.Track.CurvatureAt(s + 20f)) > 0.006f) return false;   // braking for a bend is not a check
        foreach (var c in view.Cars)
        {
            if (c == me || !view.IsRunning(c) || view.IsGhost(c)) continue;
            var (cs, cl) = view.TrackPos(c);
            float ds = view.Track.Delta(s, cs);
            if (ds > -2.5f || ds < -8f || MathF.Abs(cl - lateral) > 1.1f || c.ForwardSpeed < speed - 1f) continue;
            float chance = 0.5f * _p.Aggression * (1f - 0.5f * _p.SelfPreservation);
            if (StockCar) chance *= StockCarAggression;
            if (_rng.NextDouble() < chance)
            {
                _brakeCheckUntil = t + 0.4 + 0.2 * _rng.NextDouble();
                BrakeChecks++;
                BrakeCheckVictim = c;
                BrakeCheckTime = t;
                return true;
            }
            return false;
        }
        return false;
    }

    // ------------------------------------------------------------------ DD Code on defence

    /// <summary>
    /// Skilled drivers see it coming: a car turning into their side (T2, T4) makes them jump a lane away (or put
    /// the power down), and a car coming up the inside into a bend finds them already there (the block).
    /// </summary>
    private void Defend(Car me, IRaceView view, float s, float lateral, float speed, double t)
    {
        if (t < _nextDefenceCheck || speed < 8f || !DefenceEnabled) return;
        _nextDefenceCheck = t + 0.25;
        var track = view.Track;
        Vector3 tangent = track.TangentAt(s);
        Vector3 right = new(-tangent.Z, 0f, tangent.X);
        float myLat = me.LinearVelocity.Dot(right);
        foreach (var c in view.Cars)
        {
            if (c == me || !view.IsRunning(c) || view.IsGhost(c)) continue;
            var (cs, cl) = view.TrackPos(c);
            float ds = track.Delta(s, cs), dl = lateral - cl;
            // Beside us or at our rear quarter (where T2 and T4 come from), and the other car itself turning in.
            if (ds < -4.5f || ds > 1.5f || MathF.Abs(dl) < 1.2f || MathF.Abs(dl) > 3.4f) continue;
            float theirs = c.LinearVelocity.Dot(right) * MathF.Sign(dl);
            float closing = theirs - myLat * MathF.Sign(dl);
            if (theirs < 1.5f || closing < 2f) continue;
            if (t < _evadeUntil || _rng.NextDouble() > _p.Skill * _p.Skill) return;
            float away = SnapLane(lateral) + MathF.Sign(dl) * 4f;
            if (MathF.Abs(away) <= 4.01f && LaneClearAlongside(me, view, s, away)) _laneTarget = away;
            _evadeUntil = t + 0.8;
            _nextLaneChange = Math.Max(_nextLaneChange, t + 1.2);
            Evasions++;
            if (Current == Behaviour.Race) Current = Behaviour.Evade;
            return;
        }

        // The block: a bend coming, a quicker car behind on the inside, and the inside lane free: take it first.
        if (t < _nextBlock || _p.Skill < 0.6f) return;
        float k = track.CurvatureAt(s + 40f);
        if (MathF.Abs(k) < 0.006f) return;
        float inside = k > 0f ? Lanes[0] : Lanes[2];
        if (MathF.Abs(_laneTarget - inside) < 0.1f) return;
        float next = _laneTarget + MathF.Sign(inside - _laneTarget) * 4f;
        foreach (var c in view.Cars)
        {
            if (c == me || !view.IsRunning(c) || view.IsGhost(c)) continue;
            var (cs, cl) = view.TrackPos(c);
            float ds = track.Delta(s, cs);
            if (ds < -14f || ds > -3f || MathF.Abs(cl - next) > 1.6f || c.ForwardSpeed < speed + 0.5f) continue;
            _nextBlock = t + 4.0;
            if (_rng.NextDouble() > 0.8f * _p.Skill * (1f - 0.3f * _p.Aggression)) return;
            foreach (var o in view.Cars)
            {
                if (o == me || o == c || view.IsGhost(o)) continue;
                var (os, ol) = view.TrackPos(o);
                float ods = track.Delta(s, os);
                if (ods > -2.5f && ods < 7f && MathF.Abs(ol - next) < SideBySide) return;
            }
            _laneTarget = next;
            _nextLaneChange = t + 2.0;
            Blocks++;
            return;
        }
    }

    /// <summary>The lateral offset with the most room ahead (any car, running or wrecked), for squeezing past.</summary>
    private static float WidestGap(Car me, IRaceView view, float s)
    {
        float best = 0f, bestRoom = -1f;
        float half = view.Track.Width * 0.5f - 1.4f;
        for (float lat = -half; lat <= half + 0.01f; lat += 0.5f)
        {
            float room = 30f;
            foreach (var c in view.Cars)
            {
                if (c == me || view.IsGhost(c)) continue;
                var (cs, cl) = view.TrackPos(c);
                float ds = view.Track.Delta(s, cs);
                if (ds > -2f && ds < room && MathF.Abs(cl - lat) < 2.0f) room = MathF.Max(ds, 0f);
            }
            if (room > bestRoom + 0.5f)
            {
                bestRoom = room;
                best = lat;
            }
        }
        return best;
    }

    private void ChooseLane(Car me, IRaceView view, float s, float lateral, float speed, double t)
    {
        if (t < _nextLaneChange) return;
        var track = view.Track;

        // A wreck or a slower car ahead in our lane: move to a clear lane with more room ahead.
        float range = 20f + speed * 1.2f;
        float roomHere = RoomAhead(me, view, s, _laneTarget, range, out bool wreckHere);
        bool slowAhead = roomHere < range && SlowestAhead(me, view, s, _laneTarget, range) < speed - 1.5f;
        if (wreckHere || slowAhead)
        {
            float best = _laneTarget;
            float bestRoom = roomHere + 5f;
            foreach (float lane in Lanes)
            {
                if (lane == _laneTarget || MathF.Abs(lane - _laneTarget) > 4.5f) continue; // one lane at a time
                if (!LaneClearAlongside(me, view, s, lane)) continue;
                float room = RoomAhead(me, view, s, lane, range, out _);
                if (room > bestRoom)
                {
                    bestRoom = room;
                    best = lane;
                }
            }
            if (best != _laneTarget)
            {
                _laneTarget = best;
                _nextLaneChange = t + 1.5;
                Current = Behaviour.Overtake;
            }
            return;
        }

        // Otherwise good racers drift to the inside of the next bend (a simple racing line).
        if (_p.Skill > 0.5f)
        {
            float k = track.CurvatureAt(s + 35f);
            if (MathF.Abs(k) > 0.006f)
            {
                float inside = k > 0f ? Lanes[0] : Lanes[2];
                float next = _laneTarget + MathF.Sign(inside - _laneTarget) * 4f;
                if (next != _laneTarget && LaneClearAlongside(me, view, s, next) && RoomAhead(me, view, s, next, 25f, out _) >= 25f)
                {
                    _laneTarget = next;
                    _nextLaneChange = t + 2.0;
                }
            }
        }
    }

    private static float SnapLane(float lateral) => Lanes.OrderBy(l => MathF.Abs(l - lateral)).First();

    private static bool LaneClearAlongside(Car me, IRaceView view, float s, float lane)
    {
        foreach (var c in view.Cars)
        {
            if (c == me || view.IsGhost(c)) continue;
            var (cs, cl) = view.TrackPos(c);
            float ds = view.Track.Delta(s, cs);
            if (ds > -7f && ds < 7f && MathF.Abs(cl - lane) < SideBySide) return false;
        }
        return true;
    }

    private static float RoomAhead(Car me, IRaceView view, float s, float lane, float range, out bool wreck)
    {
        wreck = false;
        float room = range;
        foreach (var c in view.Cars)
        {
            if (c == me || view.IsGhost(c)) continue;
            var (cs, cl) = view.TrackPos(c);
            float ds = view.Track.Delta(s, cs);
            if (ds > 0f && ds < room && MathF.Abs(cl - lane) < SideBySide)
            {
                room = ds;
                wreck = !view.IsRunning(c);
            }
        }
        return room;
    }

    private static float SlowestAhead(Car me, IRaceView view, float s, float lane, float range)
    {
        float slowest = float.MaxValue;
        foreach (var c in view.Cars)
        {
            if (c == me || view.IsGhost(c)) continue;
            var (cs, cl) = view.TrackPos(c);
            float ds = view.Track.Delta(s, cs);
            if (ds > 0f && ds < range && MathF.Abs(cl - lane) < SideBySide)
                slowest = MathF.Min(slowest, view.IsRunning(c) ? c.ForwardSpeed : 0f);
        }
        return slowest;
    }

    // ------------------------------------------------------------------ DD Code offence

    private Car? PickAttack(Car me, IRaceView view, float s, float lateral, float speed, double t)
    {
        // Stock Car: the pack settles on the first lap before the bumping starts.
        if (StockCar && (t < 30.0 || view.LeaderLap < StockCarBumpLap)) { _attackTarget = null; return null; }
        if (t < _brakeCheckUntil) return null;
        if (_attackTarget != null)
        {
            var (ts, _) = view.TrackPos(_attackTarget);
            float tds = view.Track.Delta(s, ts);
            if (t < _attackUntil && view.IsRunning(_attackTarget) && !view.IsGhost(_attackTarget) && tds > -3f && tds < (_leaderHunt ? 30f : 16f)) return _attackTarget;
            _attackTarget = null;
        }
        if (t < _nextAttackRoll) return null;
        _nextAttackRoll = t + 1.0;
        // Self-preservation: a car with a weak front stops attacking (Psychos never do).
        if (me.Health(DamageZone.Front) < 0.7f * _p.SelfPreservation) return null;

        // The leader-killer (plan §4.2): sit behind the car in 1st and spin it for double points. Cars that
        // catch the leader, or that the leader comes up to lap, go for its rear quarter. No ×2 in Stock Car.
        if (!StockCar && view.LeaderId is int leaderId && leaderId != me.Id)
        {
            var leader = view.Cars.FirstOrDefault(c => c.Id == leaderId);
            if (leader != null && view.IsRunning(leader) && !view.IsGhost(leader))
            {
                var (ls, ll) = view.TrackPos(leader);
                float lds = view.Track.Delta(s, ls);
                if (lds > -LeaderHuntAhead && lds < LeaderHuntBehind && MathF.Abs(ll - lateral) < 6f
                    && _rng.NextDouble() < 0.15f + 0.5f * _p.Opportunism * _p.Aggression)
                {
                    _attackTarget = leader;
                    _leaderHunt = true;
                    _attackUntil = t + 2.0 + _rng.NextDouble();
                    return leader;
                }
            }
        }
        _leaderHunt = false;

        foreach (var c in view.Cars)
        {
            if (c == me || !view.IsRunning(c) || view.IsGhost(c)) continue;
            var (cs, cl) = view.TrackPos(c);
            float ds = view.Track.Delta(s, cs);
            float dl = cl - lateral;
            if (MathF.Abs(dl) > 4.5f || ds < -3f || ds > 12f) continue;

            float chance;
            bool pinned = MathF.Abs(ds) < 2.5f && MathF.Abs(dl) > 1.5f && c.ForwardSpeed > 8f
                          && SqueezeEnabled && !NearCrossover(view, cs) && Pinned(view, c, cs, cl, MathF.Sign(dl));  // T4
            bool overtakingUs = ds > -3f && ds < 3.5f && c.ForwardSpeed > speed + 1f;               // T2
            bool brakingAhead = ds > 3f && view.Track.SpeedAt(cs + 10f) < c.ForwardSpeed - 2f
                                && speed > c.ForwardSpeed + 1f;                                       // T3
            bool justAhead = ds > 3f && ds < 10f;
            if (pinned) chance = 0.4f * _p.Aggression;
            else if (overtakingUs) chance = 0.45f * _p.Aggression;
            else if (brakingAhead) chance = 0.35f * _p.Aggression;
            else if (justAhead) chance = 0.12f * _p.Aggression * _p.Aggression;
            else continue;
            if (c.Id == view.LeaderId && !StockCar) chance += 0.25f * _p.Opportunism;                 // ×2 points
            if (c == view.Player && view.Time > 20.0) chance += 0.05f;
            if (StockCar) chance *= StockCarAggression;

            if (_rng.NextDouble() < chance)
            {
                _attackTarget = c;
                _squeeze = pinned;
                _attackUntil = t + (pinned ? 1.4 : 1.0) + _rng.NextDouble() * 0.6;
                if (pinned) Squeezes++;
                return c;
            }
        }
        return null;
    }

    /// <summary>T4: the far side of the car is a wall (within a car's width) or a wreck just ahead of it.</summary>
    private static bool Pinned(IRaceView view, Car c, float cs, float cl, float away)
    {
        if (view.Track.Width * 0.5f - away * cl < 2.6f) return true;
        foreach (var w in view.Cars)
        {
            if (w == c || view.IsRunning(w) || view.IsGhost(w)) continue;
            var (ws, wl) = view.TrackPos(w);
            float dsw = view.Track.Delta(cs, ws), side = (wl - cl) * away;
            if (dsw > -2f && dsw < 14f && side > 0.5f && side < 3.5f) return true;
        }
        return false;
    }

    /// <summary>The figure-8's crossover has no walls to squeeze against.</summary>
    private static bool NearCrossover(IRaceView view, float s)
    {
        foreach (float p in view.CrossoverPasses)
            if (MathF.Abs(view.Track.Delta(s, p)) < 30f) return true;
        return false;
    }

    // ------------------------------------------------------------------ crossover

    /// <summary>
    /// 0 = no conflict, 1 = yield (arrive after the crossing car has cleared), 2 = gamble (floor it and get through
    /// first), 3 = aim for the crossing car (T-bone). Cautious and skilled drivers yield, bruisers gamble, psychos aim.
    /// </summary>
    private int Crossover(Car me, IRaceView view, float s, float speed, double t)
    {
        var passes = view.CrossoverPasses;
        if (passes.Count < 2) return 0;
        if (t < _crossDecisionUntil && _crossChoice != 0) return _crossChoice;
        _crossChoice = 0;

        for (int i = 0; i < passes.Count; i++)
        {
            float toCross = view.Track.Delta(s, passes[i]);
            if (toCross < 0f || toCross > 70f) continue;
            float myEta = toCross / MathF.Max(speed, 5f);
            float otherPass = passes[(i + 1) % passes.Count];
            foreach (var c in view.Cars)
            {
                if (c == me || !view.IsRunning(c) || view.IsGhost(c)) continue;
                var (cs, _) = view.TrackPos(c);
                float theirs = view.Track.Delta(cs, otherPass);
                if (theirs < -8f || theirs > 70f) continue;
                float eta = theirs / MathF.Max(c.ForwardSpeed, 5f);
                if (MathF.Abs(eta - myEta) > 0.9f) continue;

                // Conflict: decide once for this approach.
                double roll = _rng.NextDouble();
                // Stock Car: a crash costs places and points nothing, so everyone but the psychos mostly yields ✱.
                float yield = Mathf.Clamp(0.35f + 0.8f * _p.SelfPreservation - 0.3f * _p.Aggression + 0.3f * _p.Skill + (StockCar ? 0.3f : 0f), 0.1f, 0.95f);
                float aimChance = _p.Aggression >= 0.9f ? 0.5f * _p.Aggression * (1f - _p.SelfPreservation) : 0f;
                if (StockCar && _p.Archetype != "Psycho") aimChance = 0f;
                if (roll < aimChance) { _crossChoice = 3; _crossTarget = c; }
                else if (roll < aimChance + (1f - aimChance) * yield)
                {
                    _crossChoice = 1;
                    // Arrive when the other car is through (car lengths plus a margin at its speed).
                    float clearAt = eta + (2f * CarLength + view.Track.Width) / MathF.Max(c.ForwardSpeed, 5f) + 0.3f;
                    _crossYieldSpeed = MathF.Max(4f, (toCross - 6f) / MathF.Max(clearAt, 0.1f));
                }
                else _crossChoice = 2;
                _crossDecisionUntil = t + myEta + 0.6;
                return _crossChoice;
            }
        }
        return 0;
    }

    private void UpdateStuck(CarControls c, float speed, float dt)
    {
        bool trying = c.Throttle > 0.5f || c.Reverse;
        if (trying && speed < 1.0f) _stuckTime += dt;
        else _stuckTime = MathF.Max(0f, _stuckTime - dt * 2f);
        if (_stuckTime > 1.0f)
        {
            _stuckTime = 0f;
            _recoverTime = 0.8f + (float)_rng.NextDouble() * 0.4f;
        }
    }

    /// <summary>How much of a driver's Wreckin' attacking is left in Stock Car: bruisers and psychos keep bumping.</summary>
    private float StockCarAggression => _p.Archetype switch
    {
        "Bruiser" => 0.5f,
        "Psycho" => 0.6f,
        "Grudge" => 0.25f,
        _ => 0.1f,
    };

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

    private static Vector3 FlatDir(Vector3 v)
    {
        var f = new Vector3(v.X, 0f, v.Z);
        return f.LengthSquared() > 1e-6f ? f.Normalized() : Vector3.Forward;
    }

    private static float SignedAngle(Vector3 from, Vector3 to) => Mathf.Atan2(from.Cross(to).Y, from.Dot(to));
}
