using System;
using DestructionRacing.Rules;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.AI;

/// <summary>
/// Derby AI for the Bowl (plan §10.4): utility-based targeting (distance, closing angle, exposed side or rear,
/// finishing a wreck, revenge, the ×2 leader, the player-attention budget, extra attention for a player who
/// hides or only rams tail first) and the behaviours Rush (opening crunch), Hunt / Ram, Squeeze (DD Code T4: pin a car against the wall
/// or a wreck), ReverseRam (weak front), LastSecondTurn (T5, <see cref="Behaviour.Evade"/>), UseCover (a wreck as
/// a shield), Circle, wall avoidance and Recover. Uses the same <see cref="CarControls"/> as the player.
/// A <see cref="Strategy"/> other than Normal turns the brain into one of the dominant-strategy test bots
/// (plan §17.4) for the PlayerProxy.
/// </summary>
public sealed class DerbyBrain
{
    public enum Behaviour
    {
        Rush,
        Hunt,
        Squeeze,
        ReverseRam,
        Evade,
        UseCover,
        Circle,
        Hide,
        AvoidWall,
        Recover,
    }

    /// <summary>The dominant-strategy test bots (plan §17.4); Normal is the real AI and the "average fan".</summary>
    public enum Strategy { Normal, Hide, PureRam, ReverseOnly, SpinFarm }

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
    private float _pushTime;
    private int _pushCycles;
    private Car? _pushVictim;
    private double _noSqueezeUntil = -10;
    private readonly System.Collections.Generic.List<double> _stuckAt = new();
    private bool _recoverTurn;
    private double _coverUntil = -10, _nextCoverCheck;
    private Vector3 _coverPoint;
    private Car? _farmVictim;
    private double _farmRestUntil = -10;
    private Vector3 _hideSpot;
    private double _nextHideSpot;

    public DerbyBrain(Personality personality, int seed, Strategy strategy = Strategy.Normal)
    {
        _p = personality;
        _rng = new Random(seed);
        Plan = strategy;
        _phase = (float)_rng.NextDouble() * 10f;
        _rushTime = 1.6f + (float)_rng.NextDouble() * 0.6f;
        _rushOffset = new Vector3((float)_rng.NextDouble() * 6f - 3f, 0f, (float)_rng.NextDouble() * 6f - 3f);
        _circleDir = _rng.Next(2) == 0 ? 1 : -1;
    }

    public Personality Personality => _p;
    /// <summary>Experiments (--nosqueeze): the T4 squeeze off for every brain.</summary>
    public static bool SqueezeEnabled { get; set; } = true;
    /// <summary>Experiments (--nocover): UseCover off for every brain.</summary>
    public static bool CoverEnabled { get; set; } = true;
    public Strategy Plan { get; }
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

        // Recovery from being stuck (against a wall or in a pile). A car that keeps getting stuck in the same
        // spot backs out further, with full lock, and the DD1 turn-around (the immobilised rule wrecks a car
        // that rocks in place for 20 s).
        if (_recoverTime > 0f)
        {
            _recoverTime -= dt;
            Current = Behaviour.Recover;
            float lockSteer = MathF.Abs(_lastSteer) > 0.3f ? -MathF.Sign(_lastSteer) : 1f;
            if (_recoverTurn)
                return new CarControls { Throttle = 1f, TurnAround = true, Steer = lockSteer };
            return _recoverForward
                ? new CarControls { Throttle = 1f, Steer = -_lastSteer }
                : new CarControls { Reverse = true, Steer = _stuckAt.Count >= 2 ? lockSteer : -_lastSteer };
        }

        // The opening crunch: everyone floors it for the middle (plan §4.1). The hide bot stays out of it.
        if (t < _rushTime && Plan != Strategy.Hide)
        {
            Current = Behaviour.Rush;
            return Drive(me, view, fwd, _rushOffset - pos, speed, throttle: 1f);
        }

        CarControls c;
        if (Plan == Strategy.Hide)
        {
            Target = null;
            var evadeHide = Evade(me, view, fwd, speed, t, dt);
            if (evadeHide.HasValue) { Current = Behaviour.Evade; c = evadeHide.Value; }
            else { Current = Behaviour.Hide; c = Hide(me, view, fwd, speed, t); }
            return Finish(me, view, fwd, speed, t, dt, c);
        }

        if (t >= _nextRetarget || Target == null || !view.IsRunning(Target))
        {
            Target = Plan == Strategy.SpinFarm ? FarmTarget(me, view, aggression) : PickTarget(me, view, aggression);
            _nextRetarget = t + 0.3 + (1.0 - _p.Skill) * 0.5 + _rng.NextDouble() * 0.15;
        }

        var evade = Plan is Strategy.PureRam or Strategy.ReverseOnly ? null : Evade(me, view, fwd, speed, t, dt);
        var cover = Plan == Strategy.Normal && CoverEnabled ? UseCover(me, view, fwd, speed, t) : null;
        if (evade.HasValue)
        {
            Current = Behaviour.Evade;
            c = evade.Value;
        }
        else if (cover.HasValue)
        {
            Current = Behaviour.UseCover;
            c = cover.Value;
        }
        else if (Plan == Strategy.SpinFarm && (Target == null || t < _farmRestUntil))
        {
            // Let the victim recover a little between spins, so it keeps driving (a spin needs a moving car).
            Current = Behaviour.Circle;
            c = Circle(me, view, fwd, speed);
        }
        else if (Target == null || (Plan == Strategy.Normal && aggression < 0.35f && Target.GlobalPosition.DistanceTo(pos) > 15f))
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
            if (Plan == Strategy.ReverseOnly) _reverseMode = true;
            else if (Plan is Strategy.PureRam or Strategy.SpinFarm) _reverseMode = false;
            else if (t >= _modeLockedUntil || forcedHunt)
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
                c = Hunt(me, view, fwd, speed, aggression, dt);
            }
        }
        return Finish(me, view, fwd, speed, t, dt, c);
    }

    private CarControls Finish(Car me, IArenaView view, Vector3 fwd, float speed, double t, float dt, CarControls c)
    {
        if (Current is Behaviour.Hunt or Behaviour.Circle or Behaviour.Hide && AvoidWall(me, view, fwd, speed, ref c)) Current = Behaviour.AvoidWall;
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

            float reach = 18f;
            float uFacing = 0.45f + 0.55f * (0.5f + 0.5f * fwd.Dot(dir));
            // Prefer exposed sides and rears; a car facing us head-on is a worse target. A car backing up (DD1
            // reverse-ramming) shows us its engine instead: its front is the end to go for, not a threat.
            Vector3 cFwd = FlatDir(-c.GlobalTransform.Basis.Z);
            float facesMe = cFwd.Dot(-dir);
            float uExposure = c.ForwardSpeed < -1.5f ? (facesMe > 0.3f ? 1.25f : 1f) : facesMe > 0.6f ? 0.55f : 1f;
            float bonus = 1f;
            bonus += (1f - c.Health(DamageZone.Front)) * _p.Opportunism;          // finish a wreck
            if (view.LeaderId == c.Id) bonus += 0.5f;                              // ×2 points
            if (revengeOn == c && view.Time - revengeAt < 10.0) bonus += 0.6f * _p.Vengefulness;
            int budget = view.PlayerAttentionBudget;
            if (c == view.Player)
            {
                if (view.Time > 45.0) bonus += 0.3f;                               // later on, the human is everyone's rival
                // A player who hides gets hunted (plan §10.4): 20 s without landing a real hit slowly draws the
                // field from further and further away, after 30 s one more car may join the hunt and after 50 s
                // another. Normal play (a hit every few seconds) never triggers it.
                double idle = view.Time - Math.Max(view.LastAttackTime(c), 0.0);
                if (view.Time > 30.0 && idle > 20.0)
                {
                    float hiding = Mathf.Clamp((float)(idle - 20.0) / 15f, 0f, 1f);
                    bonus += hiding;
                    reach += hiding * 22f;
                    if (idle > 30.0) budget += 1;
                    if (idle > 50.0) budget += 1;
                }
                // The same for a player who only ever rams tail first to keep the engine safe: the field notices,
                // one more car may join the hunt, and the hunters go for the engine (see Hunt).
                float tail = view.TailRamShare(c);
                if (tail > 0.6f)
                {
                    float tailFirst = (tail - 0.6f) / 0.4f;
                    bonus += tailFirst * (view.Time > 60.0 ? 1.5f : 1f);
                    budget += view.Time > 60.0 && tail > 0.85f ? 2 : 1;
                }
            }

            float uDist = 1f / (1f + d / reach);
            float u = uDist * uFacing * uExposure * bonus;
            if (c == view.Player && c != Target && d > 8f && view.CountTargeting(c, me) >= budget)
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

    /// <summary>The spin-farm bot sticks to one victim and lets it get going again between spins.</summary>
    private Car? FarmTarget(Car me, IArenaView view, float aggression)
    {
        if (_farmVictim != null && view.IsRunning(_farmVictim))
        {
            // Back off for a while once it is close to wrecking (a wreck ends the farm) or after a hit.
            float weakest = MathF.Min(MathF.Min(_farmVictim.Health(DamageZone.Front), _farmVictim.Health(DamageZone.Rear)),
                MathF.Min(_farmVictim.Health(DamageZone.Left), _farmVictim.Health(DamageZone.Right)));
            if (weakest < 0.3f && view.Time >= _farmRestUntil) _farmRestUntil = view.Time + 5.0;
            if (view.LastHitBy(_farmVictim) is ({ } a, var at) && a == me && view.Time - at < 0.2)
                _farmRestUntil = view.Time + 2.5;
            return _farmVictim;
        }
        _farmVictim = PickTarget(me, view, aggression);
        return _farmVictim;
    }

    // ------------------------------------------------------------------ behaviours

    private CarControls Hunt(Car me, IArenaView view, Vector3 fwd, float speed, float aggression, float dt)
    {
        var target = Target!;
        Vector3 pos = me.GlobalPosition;
        Vector3 tPos = target.GlobalPosition;
        Vector3 tFwd = FlatDir(-target.GlobalTransform.Basis.Z);
        Vector3 tRight = FlatDir(target.GlobalTransform.Basis.X);
        float dist = Flat(tPos - pos).Length();

        // DD Code T4: a target against the wall or a wreck gets pinned there — come in from the open side and
        // keep pushing (the grinding damage is credited to the squeezer).
        bool canSqueeze = SqueezeEnabled && Plan is Strategy.Normal or Strategy.PureRam && _p.Skill >= 0.45f && aggression >= 0.45f
                          && view.Time >= _noSqueezeUntil;
        if (target != _pushVictim)
        {
            _pushVictim = target;
            _pushCycles = 0;
        }
        if (canSqueeze && dist < 26f && FindAnvil(target, view, out Vector3 anvil))
        {
            Current = Behaviour.Squeeze;
            Vector3 fromTarget = Flat(pos - tPos);
            bool openSide = fromTarget.Dot(-anvil) > 0.45f * fromTarget.Length();
            if (!openSide && dist < 10f)
            {
                // On the wrong side: swing round to the open side first.
                _pushTime = 0f;
                return Drive(me, view, fwd, tPos - anvil * 6f - pos, speed, throttle: 0.8f);
            }
            if (dist < 4.2f && speed < 3.5f)
            {
                _pushTime += dt;
                if (_pushTime > 1.8f)
                {
                    // Back off and come again rather than grind forever; after two pushes, leave it be for a while.
                    _pushTime = 0f;
                    _recoverTime = 1.1f;
                    _recoverForward = false;
                    _recoverTurn = false;
                    if (++_pushCycles >= 2)
                    {
                        _pushCycles = 0;
                        _noSqueezeUntil = view.Time + 6.0;
                    }
                }
                return new CarControls { Throttle = 1f, Steer = Steer(fwd, Flat(tPos + anvil - pos)) };
            }
            _pushTime = 0f;
            float approach = dist < 12f && speed > 12f ? 0.55f : 1f;
            return Drive(me, view, fwd, tPos + anvil * 0.6f - pos, speed, approach);
        }
        _pushTime = 0f;
        Current = Behaviour.Hunt;

        // Lead the target, then aim for its rear quarter on our side (that spins it, DD Code T2).
        float closing = MathF.Max(speed, 8f);
        float lead = Mathf.Clamp(dist / closing, 0f, 1.0f) * (0.4f + 0.6f * _p.Skill);
        Vector3 predicted = tPos + target.LinearVelocity * lead;
        float side = MathF.Sign((pos - tPos).Dot(tRight));
        if (side == 0f) side = 1f;
        bool inFrontOfIt = (pos - tPos).Dot(tFwd) > 0f;
        bool backingUp = target.ForwardSpeed < -1.5f;
        // Pushed by the director (the last cars, nothing happening): straight at it. Two cars that both go for the
        // other's rear quarter can circle each other at walking pace for minutes.
        bool straightAtIt = view.AggressionBoost(me) >= 0.8f && dist < 14f;
        Vector3 aim = straightAtIt ? predicted : !inFrontOfIt
            ? backingUp
                ? predicted + tRight * side * 2.6f + tFwd * 1.0f     // its tough tail coming at us: swing past it to its flank
                : predicted - tFwd * 1.6f + tRight * side * 0.5f     // from behind: the rear quarter
            : backingUp
                ? predicted + tFwd * 1.5f + tRight * side * 0.3f     // a car backing away: straight into its engine
                : predicted + tRight * side * 1.4f;                  // coming from the front: go for its flank

        float throttle = 1f;
        if (Plan == Strategy.Normal && aggression < 0.6f && dist > 25f) throttle = 0.75f;
        // Don't slam into the wall behind a target parked against it.
        if (Plan == Strategy.Normal && dist < 20f && EllipseScale(tPos, view.ArenaSemiAxes) > 0.85f && speed > 14f) throttle = 0.4f;
        return Drive(me, view, fwd, aim - pos, speed, throttle);
    }

    /// <summary>The wall or a wreck right behind the target, as the direction from the target to it.</summary>
    private static bool FindAnvil(Car target, IArenaView view, out Vector3 dir)
    {
        Vector3 tPos = target.GlobalPosition;
        Vector2 axes = view.ArenaSemiAxes;
        if (EllipseScale(tPos, axes) > 0.88f)
        {
            dir = new Vector3(tPos.X / (axes.X * axes.X), 0f, tPos.Z / (axes.Y * axes.Y)).Normalized();
            return true;
        }
        Car? best = null;
        float bestD = 3.4f;
        foreach (var c in view.Cars)
        {
            if (c == target || view.IsRunning(c)) continue;
            float d = Flat(c.GlobalPosition - tPos).Length();
            if (d < bestD) { bestD = d; best = c; }
        }
        dir = best != null ? Flat(best.GlobalPosition - tPos).Normalized() : Vector3.Zero;
        return best != null;
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
    /// turn at the last moment so the other car's front hits our side. The same against a reverse-rammer's tail.
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
            // Pointing at us: its nose, or its tail when it reverse-rams (the tough rear always wins against a front).
            bool tailRam = other.ForwardSpeed < -2f && oFwd.Dot(dir) > 0.82f;
            if (oFwd.Dot(-dir) < 0.82f && !tailRam) continue;

            if (_evadeFrom == other && t - _evadeDecidedAt < 2.0) return null;      // already decided to take it
            _evadeFrom = other;
            _evadeDecidedAt = t;
            bool weaker = tailRam || me.Health(DamageZone.Front) <= other.Health(DamageZone.Front);
            bool careful = _p.SelfPreservation > 0.45f || weaker || Plan == Strategy.Hide;
            if (!careful || _rng.NextDouble() > _p.Skill * 0.95) return null;

            Vector3 right = FlatDir(me.GlobalTransform.Basis.X);
            float lateral = rel.Dot(right);
            _evadeSteer = lateral > 0.2f ? -1f : lateral < -0.2f ? 1f : (_rng.Next(2) == 0 ? -1f : 1f);
            _evadeTime = 0.45f;
            return new CarControls { Throttle = 0.5f, Steer = _evadeSteer, FastSteer = true };
        }
        return null;
    }

    /// <summary>
    /// A wreck as a shield (plan §10.4): a careful driver with a battered front who is being chased puts the
    /// nearest wreck between itself and the attacker for a moment.
    /// </summary>
    private CarControls? UseCover(Car me, IArenaView view, Vector3 fwd, float speed, double t)
    {
        Vector3 pos = me.GlobalPosition;
        if (t < _coverUntil)
        {
            Vector3 to = Flat(_coverPoint - pos);
            if (to.Length() < 2.2f) return new CarControls { Brake = 1f, Hold = true, Steer = 0f };
            return Drive(me, view, fwd, to, speed, throttle: 0.85f);
        }
        if (t < _nextCoverCheck || _p.SelfPreservation < 0.45f || me.Health(DamageZone.Front) > 0.4f) return null;
        _nextCoverCheck = t + 0.5;

        // The threat: a running car near us, closing fast and pointed at us.
        Car? threat = null;
        foreach (var other in view.Cars)
        {
            if (other == me || !view.IsRunning(other)) continue;
            Vector3 rel = Flat(pos - other.GlobalPosition);
            float d = rel.Length();
            if (d > 18f || d < 1f) continue;
            Vector3 dir = rel / d;
            if (FlatDir(-other.GlobalTransform.Basis.Z).Dot(dir) < 0.8f || Flat(other.LinearVelocity).Dot(dir) < 4f) continue;
            threat = other;
            break;
        }
        if (threat == null) return null;
        Car? wreck = null;
        float best = 25f;
        foreach (var c in view.Cars)
        {
            if (view.IsRunning(c)) continue;
            float d = Flat(c.GlobalPosition - pos).Length();
            if (d < best) { best = d; wreck = c; }
        }
        if (wreck == null) return null;
        Vector3 away = Flat(wreck.GlobalPosition - threat.GlobalPosition);
        if (away.LengthSquared() < 0.01f) return null;
        _coverPoint = wreck.GlobalPosition + away.Normalized() * 3.6f;
        if (EllipseScale(_coverPoint, view.ArenaSemiAxes) > 0.92f) return null;     // no cover against the wall
        _coverUntil = t + 2.5;
        return Drive(me, view, fwd, Flat(_coverPoint - pos), speed, throttle: 0.85f);
    }

    private CarControls Circle(Car me, IArenaView view, Vector3 fwd, float speed)
    {
        Vector3 pos = me.GlobalPosition;
        Vector2 axes = view.ArenaSemiAxes * 0.55f;
        float angle = Mathf.Atan2(pos.Z / axes.Y, pos.X / axes.X) + 0.6f * _circleDir;
        var aim = new Vector3(axes.X * Mathf.Cos(angle), 0f, axes.Y * Mathf.Sin(angle));
        return Drive(me, view, fwd, aim - pos, speed, throttle: 0.6f);
    }

    /// <summary>The hide bot: drives to the emptiest part of the Bowl and keeps moving (so it isn't immobilised).</summary>
    private CarControls Hide(Car me, IArenaView view, Vector3 fwd, float speed, double t)
    {
        Vector3 pos = me.GlobalPosition;
        if (t >= _nextHideSpot || Flat(_hideSpot - pos).Length() < 4f)
        {
            _nextHideSpot = t + 3.0;
            float bestScore = -1f;
            Vector2 axes = view.ArenaSemiAxes * 0.72f;
            for (int i = 0; i < 16; i++)
            {
                float a = Mathf.Tau * i / 16f;
                var p = new Vector3(axes.X * Mathf.Cos(a), 0f, axes.Y * Mathf.Sin(a));
                float nearest = float.MaxValue;
                foreach (var c in view.Cars)
                    if (c != me && view.IsRunning(c)) nearest = MathF.Min(nearest, Flat(c.GlobalPosition - p).Length());
                // Prefer empty spots that aren't on the far side of the pack.
                float score = nearest - 0.15f * Flat(p - pos).Length();
                if (score > bestScore) { bestScore = score; _hideSpot = p; }
            }
        }
        float throttle = Flat(_hideSpot - pos).Length() < 10f ? 0.4f : 0.8f;
        return Drive(me, view, fwd, _hideSpot - pos, speed, throttle);
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

    private double _clock;

    private void UpdateStuck(Car me, CarControls c, float speed, float dt)
    {
        _clock += dt;
        bool trying = (c.Throttle > 0.2f || c.Reverse) && Current != Behaviour.Squeeze;
        if (trying && speed < 1.0f) _stuckTime += dt;
        else _stuckTime = MathF.Max(0f, _stuckTime - dt * 2f);
        if (_stuckTime > 0.8f)
        {
            _stuckTime = 0f;
            _stuckAt.Add(_clock);
            _stuckAt.RemoveAll(x => _clock - x > 10.0);
            int repeats = _stuckAt.Count;
            // Each repeat in the last 10 s: longer, and from the third on, alternate with the turn-around.
            _recoverTime = (0.8f + (float)_rng.NextDouble() * 0.4f) * (repeats >= 2 ? 1.8f : 1f);
            _recoverForward = repeats >= 2 ? !_recoverForward : c.Reverse;
            _recoverTurn = repeats >= 3 && repeats % 2 == 1;
            if (repeats >= 2) Target = null;     // pick something else to do afterwards
        }
    }

    // ------------------------------------------------------------------ steering

    private float Steer(Vector3 fwd, Vector3 dir) =>
        dir.LengthSquared() < 1e-4f ? 0f : Mathf.Clamp(-SignedAngle(fwd, dir.Normalized()) * (2.0f + _p.Skill), -1f, 1f);

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
            // Ease off for a sharp turn at speed; a car that has nearly stopped needs the throttle to turn at all.
            Throttle = absErr < 1.2f || speed < 3f ? throttle : throttle * 0.5f,
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
