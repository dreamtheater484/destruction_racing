using System;
using DestructionRacing.Rules;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.AI;

/// <summary>
/// Derby AI for the Bowl (plan §10.4), M1b version: utility-based targeting (distance, closing angle, exposed
/// side or rear, finishing a wreck, revenge, the ×2 leader, the player-attention budget) and the behaviours
/// Rush (opening crunch), Hunt / Ram, ReverseRam (weak front), LastSecondTurn (DD Code T5), Circle, wall
/// avoidance and Recover. Uses the same <see cref="CarControls"/> as the player; no cheating.
/// </summary>
public sealed class DerbyBrain
{
    public enum Behaviour
    {
        Rush,
        Hunt,
        ReverseRam,
        Evade,
        Circle,
        AvoidWall,
        Recover,
    }

    private readonly Personality _p;
    private readonly Random _rng;
    private readonly float _phase;

    private double _nextRetarget;
    private float _stuckTime;
    private float _recoverTime;
    private bool _recoverForward;
    private float _evadeTime;
    private float _evadeSteer;
    private Car? _evadeFrom;
    private double _evadeDecidedAt = -10;
    private float _lastSteer;
    private float _rushTime;
    private Vector3 _rushOffset;
    private int _circleDir;
    private bool _reverseMode;
    private double _modeLockedUntil;

    public DerbyBrain(Personality personality, int seed)
    {
        _p = personality;
        _rng = new Random(seed);
        _phase = (float)_rng.NextDouble() * 10f;
        _rushTime = 1.6f + (float)_rng.NextDouble() * 0.6f;
        _rushOffset = new Vector3((float)_rng.NextDouble() * 6f - 3f, 0f, (float)_rng.NextDouble() * 6f - 3f);
        _circleDir = _rng.Next(2) == 0 ? 1 : -1;
    }

    public Personality Personality => _p;
    public Car? Target { get; private set; }
    public Behaviour Current { get; private set; } = Behaviour.Rush;

    public CarControls Think(Car me, IArenaView view, float dt)
    {
        if (!view.IsRunning(me)) return CarControls.Parked;
        double t = view.Time;
        Vector3 pos = me.GlobalPosition;
        Vector3 fwd = FlatDir(-me.GlobalTransform.Basis.Z);
        float speed = me.LinearVelocity.Length();
        float aggression = Mathf.Clamp(_p.Aggression + view.AggressionBoost(me), 0f, 1.2f);

        // Recovery from being stuck (against a wall or in a pile).
        if (_recoverTime > 0f)
        {
            _recoverTime -= dt;
            Current = Behaviour.Recover;
            return _recoverForward
                ? new CarControls { Throttle = 1f, Steer = -_lastSteer }
                : new CarControls { Reverse = true, Steer = -_lastSteer };
        }

        // The opening crunch: everyone floors it for the middle (plan §4.1).
        if (t < _rushTime)
        {
            Current = Behaviour.Rush;
            return Drive(me, view, fwd, _rushOffset - pos, speed, throttle: 1f);
        }

        if (t >= _nextRetarget || Target == null || !view.IsRunning(Target))
        {
            Target = PickTarget(me, view, aggression);
            _nextRetarget = t + 0.3 + (1.0 - _p.Skill) * 0.5 + _rng.NextDouble() * 0.15;
        }

        CarControls c;
        var evade = Evade(me, view, fwd, speed, t, dt);
        if (evade.HasValue)
        {
            Current = Behaviour.Evade;
            c = evade.Value;
        }
        else if (Target == null || (aggression < 0.35f && Target.GlobalPosition.DistanceTo(pos) > 15f))
        {
            Current = Behaviour.Circle;
            c = Circle(me, view, fwd, speed);
        }
        else
        {
            // Reverse-ramming (DD1): with a weak front, careful drivers hit with the tough rear instead.
            // Committed for a couple of seconds at a time, so the car doesn't dither between the two.
            float frontHealth = me.Health(DamageZone.Front);
            float reverseBelow = 0.2f + _p.SelfPreservation * 0.3f;
            float dist = Target.GlobalPosition.DistanceTo(pos);
            // Two cars backing into each other's tough rears gets nowhere; nor does a final-duel nudge.
            Vector3 targetBack = FlatDir(Target.GlobalTransform.Basis.Z);
            bool targetBacksAtUs = Target.ForwardSpeed < -1f && targetBack.Dot(Flat(pos - Target.GlobalPosition).Normalized()) > 0.5f;
            bool forcedHunt = view.AggressionBoost(me) >= 0.8f;
            if (t >= _modeLockedUntil || forcedHunt)
            {
                bool wantReverse = frontHealth < reverseBelow && dist < 25f && !targetBacksAtUs && !forcedHunt;
                if (wantReverse != _reverseMode)
                {
                    _reverseMode = wantReverse;
                    _modeLockedUntil = t + 2.0 + _rng.NextDouble();
                }
            }
            if (_reverseMode)
            {
                Current = Behaviour.ReverseRam;
                c = ReverseRam(me, fwd, speed);
            }
            else
            {
                Current = Behaviour.Hunt;
                c = Hunt(me, view, fwd, speed, aggression);
            }
        }

        if (Current is Behaviour.Hunt or Behaviour.Circle && AvoidWall(me, view, fwd, speed, ref c)) Current = Behaviour.AvoidWall;

        UpdateStuck(me, c, speed, dt);
        // Imperfect hands: a little steering noise for less skilled drivers.
        c.Steer = Mathf.Clamp(c.Steer + (1f - _p.Skill) * 0.25f * Mathf.Sin((float)t * 2.3f + _phase), -1f, 1f);
        _lastSteer = c.Steer;
        return c;
    }

    // ------------------------------------------------------------------ targeting

    private Car? PickTarget(Car me, IArenaView view, float aggression)
    {
        Vector3 pos = me.GlobalPosition;
        Vector3 fwd = FlatDir(-me.GlobalTransform.Basis.Z);
        var (revengeOn, revengeAt) = view.LastHitBy(me);
        Car? best = null;
        float bestU = 0f;
        foreach (var c in view.Cars)
        {
            if (c == me || !view.IsRunning(c)) continue;
            Vector3 to = c.GlobalPosition - pos;
            float d = Flat(to).Length();
            if (d < 0.1f) continue;
            Vector3 dir = Flat(to) / d;

            float uDist = 1f / (1f + d / 18f);
            float uFacing = 0.45f + 0.55f * (0.5f + 0.5f * fwd.Dot(dir));
            // Prefer exposed sides and rears; a car facing us head-on is a worse target.
            Vector3 cFwd = FlatDir(-c.GlobalTransform.Basis.Z);
            float facesMe = cFwd.Dot(-dir);
            float uExposure = facesMe > 0.6f ? 0.55f : 1f;
            float bonus = 1f;
            bonus += (1f - c.Health(DamageZone.Front)) * _p.Opportunism;          // finish a wreck
            if (view.LeaderId == c.Id) bonus += 0.5f;                              // ×2 points
            if (revengeOn == c && view.Time - revengeAt < 10.0) bonus += 0.6f * _p.Vengefulness;
            if (c == view.Player && view.Time > 45.0) bonus += 0.3f;               // later on, the human is everyone's rival

            float u = uDist * uFacing * uExposure * bonus;
            if (c == view.Player && c != Target && d > 8f && view.CountTargeting(c, me) >= view.PlayerAttentionBudget)
                u *= 0.15f;                                                         // attention budget
            if (c == Target) u *= 1.3f;                                             // hysteresis
            if (u > bestU)
            {
                bestU = u;
                best = c;
            }
        }
        return best;
    }

    // ------------------------------------------------------------------ behaviours

    private CarControls Hunt(Car me, IArenaView view, Vector3 fwd, float speed, float aggression)
    {
        var target = Target!;
        Vector3 pos = me.GlobalPosition;
        Vector3 tPos = target.GlobalPosition;
        Vector3 tFwd = FlatDir(-target.GlobalTransform.Basis.Z);
        Vector3 tRight = FlatDir(target.GlobalTransform.Basis.X);
        float dist = Flat(tPos - pos).Length();

        // Lead the target, then aim for its rear quarter on our side (that spins it, DD Code T2).
        float closing = MathF.Max(speed, 8f);
        float lead = Mathf.Clamp(dist / closing, 0f, 1.0f) * (0.4f + 0.6f * _p.Skill);
        Vector3 predicted = tPos + target.LinearVelocity * lead;
        float side = MathF.Sign((pos - tPos).Dot(tRight));
        if (side == 0f) side = 1f;
        bool inFrontOfIt = (pos - tPos).Dot(tFwd) > 0f;
        Vector3 aim = inFrontOfIt
            ? predicted + tRight * side * 1.4f                       // coming from the front: go for its flank
            : predicted - tFwd * 1.6f + tRight * side * 0.5f;        // otherwise the rear quarter

        float throttle = 1f;
        if (aggression < 0.6f && dist > 25f) throttle = 0.75f;
        // Don't slam into the wall behind a target parked against it.
        if (dist < 20f && EllipseScale(tPos, view.ArenaSemiAxes) > 0.85f && speed > 14f) throttle = 0.4f;
        return Drive(me, view, fwd, aim - pos, speed, throttle);
    }

    private CarControls ReverseRam(Car me, Vector3 fwd, float speed)
    {
        var target = Target!;
        Vector3 pos = me.GlobalPosition;
        Vector3 aimDir = Flat(target.GlobalPosition + target.LinearVelocity * 0.4f - pos);
        Vector3 back = -fwd;
        float errBack = SignedAngle(back, aimDir);

        if (MathF.Abs(errBack) < 1.9f)
        {
            // Rear towards the target: reverse into it. In reverse, steering right swings the tail right (CCW).
            return new CarControls { Reverse = true, Steer = Mathf.Clamp(errBack * 2.2f, -1f, 1f) };
        }
        if (speed > 7f)
            return new CarControls { Brake = 1f, Hold = true };
        // DD1 turn-around: spin the car so the tail faces the target.
        return new CarControls { Throttle = 1f, TurnAround = true, Steer = -MathF.Sign(errBack) };
    }

    /// <summary>
    /// DD Code T5: when a head-on is about to happen and our front is the weaker one (or we are careful),
    /// turn at the last moment so the other car's front hits our side.
    /// </summary>
    private CarControls? Evade(Car me, IArenaView view, Vector3 fwd, float speed, double t, float dt)
    {
        if (_evadeTime > 0f)
        {
            _evadeTime -= dt;
            return new CarControls { Throttle = 0.5f, Steer = _evadeSteer, FastSteer = true };
        }

        Vector3 pos = me.GlobalPosition;
        foreach (var other in view.Cars)
        {
            if (other == me || !view.IsRunning(other)) continue;
            Vector3 rel = Flat(other.GlobalPosition - pos);
            float d = rel.Length();
            if (d > 22f || d < 0.5f) continue;
            Vector3 dir = rel / d;
            float closing = -(Flat(other.LinearVelocity) - Flat(me.LinearVelocity)).Dot(dir);
            if (closing < 7f) continue;
            float ttc = (d - 4.6f) / closing;
            if (ttc < 0.05f || ttc > 0.6f) continue;
            if (fwd.Dot(dir) < 0.82f) continue;                                     // it is ahead of us
            Vector3 oFwd = FlatDir(-other.GlobalTransform.Basis.Z);
            if (oFwd.Dot(-dir) < 0.82f) continue;                                   // and pointing at us

            if (_evadeFrom == other && t - _evadeDecidedAt < 2.0) return null;      // already decided to take it
            _evadeFrom = other;
            _evadeDecidedAt = t;
            bool weaker = me.Health(DamageZone.Front) <= other.Health(DamageZone.Front);
            bool careful = _p.SelfPreservation > 0.45f || weaker;
            if (!careful || _rng.NextDouble() > _p.Skill * 0.95) return null;

            Vector3 right = FlatDir(me.GlobalTransform.Basis.X);
            float lateral = rel.Dot(right);
            _evadeSteer = lateral > 0.2f ? -1f : lateral < -0.2f ? 1f : (_rng.Next(2) == 0 ? -1f : 1f);
            _evadeTime = 0.45f;
            return new CarControls { Throttle = 0.5f, Steer = _evadeSteer, FastSteer = true };
        }
        return null;
    }

    private CarControls Circle(Car me, IArenaView view, Vector3 fwd, float speed)
    {
        Vector3 pos = me.GlobalPosition;
        Vector2 axes = view.ArenaSemiAxes * 0.55f;
        float angle = Mathf.Atan2(pos.Z / axes.Y, pos.X / axes.X) + 0.6f * _circleDir;
        var aim = new Vector3(axes.X * Mathf.Cos(angle), 0f, axes.Y * Mathf.Sin(angle));
        return Drive(me, view, fwd, aim - pos, speed, throttle: 0.6f);
    }

    /// <summary>Steers back towards the middle when the car is about to hit the wall without a target there.</summary>
    private bool AvoidWall(Car me, IArenaView view, Vector3 fwd, float speed, ref CarControls c)
    {
        Vector2 axes = view.ArenaSemiAxes - new Vector2(4f, 4f);
        Vector3 ahead = me.GlobalPosition + me.LinearVelocity * 0.9f + fwd * 3f;
        float scale = EllipseScale(ahead, axes);
        if (scale < 1f) return false;
        // A target at the wall may be approached (and squeezed into it, T4); the hunt slows down near it.
        if (Target != null && EllipseScale(Target.GlobalPosition, axes) > 0.8f
            && Target.GlobalPosition.DistanceTo(me.GlobalPosition) < 30f) return false;

        var toCentre = -Flat(me.GlobalPosition);
        c = Drive(me, view, fwd, toCentre, speed, throttle: 0.5f);
        if (speed > 16f) c.Brake = 0.6f;
        return true;
    }

    private void UpdateStuck(Car me, CarControls c, float speed, float dt)
    {
        bool trying = c.Throttle > 0.5f || c.Reverse;
        if (trying && speed < 1.0f) _stuckTime += dt;
        else _stuckTime = MathF.Max(0f, _stuckTime - dt * 2f);
        if (_stuckTime > 0.8f)
        {
            _stuckTime = 0f;
            _recoverTime = 0.8f + (float)_rng.NextDouble() * 0.4f;
            _recoverForward = c.Reverse;
        }
    }

    // ------------------------------------------------------------------ steering

    /// <summary>Forward driving towards a direction, with braking and the DD1 turn-around for sharp turns.</summary>
    private CarControls Drive(Car me, IArenaView view, Vector3 fwd, Vector3 toAim, float speed, float throttle)
    {
        Vector3 dir = Flat(toAim);
        if (dir.LengthSquared() < 1e-4f) return new CarControls { Throttle = throttle };
        float aimDist = dir.Length();
        dir = AvoidWrecks(me, view, dir / aimDist, aimDist);
        float err = SignedAngle(fwd, dir);                 // + = aim is to the left (CCW)
        float absErr = MathF.Abs(err);
        float steer = Mathf.Clamp(-err * (2.0f + _p.Skill), -1f, 1f);

        if (absErr > 2.0f && speed < 8f)
            return new CarControls { Throttle = 1f, TurnAround = true, Steer = steer };
        if (absErr > 1.6f && speed > 12f)
            return new CarControls { Brake = 1f, Steer = steer, Hold = true };
        return new CarControls
        {
            Throttle = absErr < 1.2f ? throttle : throttle * 0.5f,
            Steer = steer,
            FastSteer = absErr > 0.6f && speed < 20f && _p.Skill > 0.4f,
        };
    }

    /// <summary>Bends the path around wrecked cars between us and the aim point (wrecks stay as obstacles).</summary>
    private static Vector3 AvoidWrecks(Car me, IArenaView view, Vector3 dir, float aimDist)
    {
        Vector3 pos = me.GlobalPosition;
        Vector3 side = new(-dir.Z, 0f, dir.X); // left of the path
        Vector3 result = dir;
        foreach (var c in view.Cars)
        {
            if (c == me || view.IsRunning(c)) continue;
            Vector3 rel = Flat(c.GlobalPosition - pos);
            float along = rel.Dot(dir);
            if (along < 1f || along > MathF.Min(aimDist, 16f)) continue;
            float lateral = rel.Dot(side);
            float clearance = 3.0f - MathF.Abs(lateral);
            if (clearance <= 0f) continue;
            // Steer to the side the wreck is not on, harder the closer it is.
            float push = clearance / 3.0f * (1.5f - along / 16f);
            result += side * (lateral > 0f ? -push : push);
        }
        return result.Normalized();
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

    /// <summary>A body axis flattened onto the ground and normalised.</summary>
    private static Vector3 FlatDir(Vector3 v)
    {
        var f = new Vector3(v.X, 0f, v.Z);
        return f.LengthSquared() > 1e-6f ? f.Normalized() : Vector3.Forward;
    }

    private static float SignedAngle(Vector3 from, Vector3 to) =>
        Mathf.Atan2(from.Cross(to).Y, from.Dot(to));

    private static float EllipseScale(Vector3 p, Vector2 axes) =>
        Mathf.Sqrt(p.X * p.X / (axes.X * axes.X) + p.Z * p.Z / (axes.Y * axes.Y));
}
