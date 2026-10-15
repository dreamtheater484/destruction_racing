using System;
using System.Collections.Generic;
using System.Linq;
using DestructionRacing.Rules;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Modes;

/// <summary>One derby's engagement and frustration numbers (plan §17.4).</summary>
public sealed class DerbyMetricsResult
{
    public int Seed { get; init; }
    public double EventLength { get; set; }
    public double FirstContactTime { get; set; } = double.NaN;
    public double LongestIdle { get; set; }
    public double PlayerSurvival { get; set; }
    public bool PlayerWrecked { get; set; }
    public int PlayerPlace { get; set; }
    public int PlayerPoints { get; set; }
    public int PlayerSpinPoints { get; set; }
    public int PlayerWreckPoints { get; set; }
    public int PlayerHitsDealt { get; set; }
    public int PlayerHitsTaken { get; set; }
    public double PlayerHitsPerMinute => PlayerSurvival > 1 ? (PlayerHitsDealt + PlayerHitsTaken) / (PlayerSurvival / 60.0) : 0;
    public double PlayerHitsDealtPerMinute => PlayerSurvival > 1 ? PlayerHitsDealt / (PlayerSurvival / 60.0) : 0;
    /// <summary>Share of the player's points from deliberate play: aimed hits, T5 last-second turns and T4 squeezes.</summary>
    public double DeliberatePointsShare { get; set; } = double.NaN;
    /// <summary>The plan's narrow reading: aimed attacking hits only (throttle on, heading at the victim).</summary>
    public double AimedPointsShare { get; set; } = double.NaN;
    public int PlayerPointsDeliberate { get; set; }
    /// <summary>Points from cars that rammed the player's side just after a last-second turn (DD Code T5).</summary>
    public int PlayerPointsT5 { get; set; }
    /// <summary>Points from grinding a car against the wall or a wreck with the throttle on (DD Code T4).</summary>
    public int PlayerPointsSqueeze { get; set; }
    /// <summary>The player ended the event with more points than anyone else.</summary>
    public bool PlayerTopPoints { get; set; }
    /// <summary>The player's state when wrecked (zone health F, R, L, Rt in %, wheels lost, hits taken in the last 10 s), or at the end.</summary>
    public string PlayerWreckState { get; set; } = "";
    /// <summary>
    /// Diagnostics: the player's front health lost in the first 60 s, by cause: own attacks, hits taken,
    /// mutual hits (both attacking, e.g. head-ons), walls, grinding (no contact that step).
    /// </summary>
    public float[] PlayerFrontLoss60 { get; } = new float[5];
    public static readonly string[] FrontLossCauses = { "own attacks", "hits taken", "mutual", "walls", "grinding" };
    public int PlayerWreckAttackers { get; set; }
    /// <summary>Player points from victims the player never hit as an attacker (passive credit).</summary>
    public int PlayerPointsPassive { get; set; }
    /// <summary>Player points from attacking hits that failed the aim/throttle test.</summary>
    public int PlayerPointsNotAimed { get; set; }
    public double PlayerStuckTime { get; set; }
    public int Winner { get; set; }
    public bool WinnerSurvived { get; set; }
    public bool TimedOut { get; set; }
    public int TotalHits { get; set; }
    public int Spins { get; set; }
    public int Wrecks { get; set; }
}

/// <summary>
/// Collects the derby metrics from impacts and referee events. "Hit" here is the plan's metric definition:
/// car-car contact at ≥ 9 kJ (a deliberate ram at ≥ 20 km/h closing speed), not the lower spin threshold.
/// </summary>
public sealed class DerbyMetrics
{
    public const float MetricHitEnergy = 9_000f;

    private readonly Car _player;
    private readonly DerbyMetricsResult _r;
    /// <summary>How long before a spin or wreck the player's part in it still counts (the attribution window).</summary>
    private const double CreditWindow = 5.0;
    private readonly Dictionary<int, List<(double Time, bool Deliberate)>> _playerAttacks = new();
    private readonly Dictionary<int, double> _t5 = new(), _squeeze = new(), _lastPlayerContact = new();
    private double _contactAt = -100;
    private int _contactRole;
    private readonly List<(double Time, int Attacker)> _hitsOnPlayer = new();
    private readonly Queue<(double Time, bool Throttle, float Steer)> _throttle = new();
    private double _lastHit;
    private double _now;
    private int _scoredPoints;
    private float _stuckFor;

    public DerbyMetrics(Car player, int seed)
    {
        _player = player;
        _r = new DerbyMetricsResult { Seed = seed };
    }

    public DerbyMetricsResult Result => _r;

    /// <summary>Called every physics step while the derby runs.</summary>
    public void Step(double time, float dt, int carsRunning, bool playerRunning)
    {
        _now = time;
        if (carsRunning >= 5) _r.LongestIdle = Math.Max(_r.LongestIdle, time - _lastHit);
        if (!playerRunning) return;

        var c = _player.LastControls;
        _throttle.Enqueue((time, c.Throttle > 0.5f || c.Reverse, c.Steer));
        while (_throttle.Count > 0 && _throttle.Peek().Time < time - 0.5) _throttle.Dequeue();

        bool trying = c.Throttle > 0.5f || c.Reverse;
        if (trying && _player.LinearVelocity.Length() < 1f && !Shoving(c.Reverse)) _stuckFor += dt;
        else _stuckFor = 0f;
        if (_stuckFor > 1f) _r.PlayerStuckTime += dt;
    }

    /// <summary>
    /// A shoving match is not being stuck: the player is pushing a running car that is in front of them (behind,
    /// when reversing), e.g. squeezing it into the wall. Being pinned from the side still counts as stuck.
    /// </summary>
    private bool Shoving(bool reversing)
    {
        foreach (var body in _player.GetCollidingBodies())
        {
            if (body is not Car other || other.IsWrecked) continue;
            float z = _player.ToLocal(other.GlobalPosition).Z;
            if (reversing ? z > 1.2f : z < -1.2f) return true;
        }
        return false;
    }

    public void OnImpact(Impact i)
    {
        if (i.B == null) return;
        // Taps that can score spins count for the deliberate-points share even below the metric hit bar.
        if (i.A == _player || i.B == _player)
        {
            bool playerIsA = i.A == _player;
            Car victim = playerIsA ? i.B : i.A;
            bool playerAttacks = playerIsA ? i.Attacker is HitAttacker.A or HitAttacker.Both : i.Attacker is HitAttacker.B or HitAttacker.Both;
            bool victimAttacks = playerIsA ? i.Attacker is HitAttacker.B or HitAttacker.Both : i.Attacker is HitAttacker.A or HitAttacker.Both;
            _lastPlayerContact[victim.Id] = i.Time;
            _contactAt = i.Time;
            _contactRole = playerAttacks && !victimAttacks ? 0 : victimAttacks && !playerAttacks ? 1 : 2;
            if (playerAttacks)
            {
                if (!_playerAttacks.TryGetValue(victim.Id, out var list)) _playerAttacks[victim.Id] = list = new List<(double, bool)>();
                list.Add((i.Time, IsDeliberate(i.Point)));
                list.RemoveAll(e => i.Time - e.Time > CreditWindow);
            }
            // DD Code T5: the player turned at the last moment and the other car's front hit the player's side.
            if (victimAttacks && !playerAttacks && IsLastSecondTurn(victim, i.Point)) _t5[victim.Id] = i.Time;
        }
        if (i.Energy < MetricHitEnergy) return;
        _r.TotalHits++;
        if (double.IsNaN(_r.FirstContactTime)) _r.FirstContactTime = i.Time;
        _lastHit = i.Time;

        bool aAttacks = i.Attacker is HitAttacker.A or HitAttacker.Both;
        bool bAttacks = i.Attacker is HitAttacker.B or HitAttacker.Both;
        if (i.A == _player || i.B == _player)
        {
            Car other = i.A == _player ? i.B : i.A;
            bool playerAttacks = i.A == _player ? aAttacks : bAttacks;
            bool otherAttacks = i.A == _player ? bAttacks : aAttacks;
            if (playerAttacks) _r.PlayerHitsDealt++;
            if (otherAttacks)
            {
                _r.PlayerHitsTaken++;
                _hitsOnPlayer.Add((i.Time, other.Id));
            }
        }
    }

    /// <summary>
    /// Deliberate (plan §17.4): in the 0.5 s before the hit the player had throttle (or reverse, for a reverse-ram)
    /// on and was moving at the victim (closing angle under 45°).
    /// </summary>
    private bool IsDeliberate(Vector3 contactPoint)
    {
        bool throttle = _throttle.Any(e => e.Throttle);
        // Angle to where the cars touched (a tap on a rear corner beside you is still aimed).
        Vector3 to = contactPoint - _player.GlobalPosition;
        to.Y = 0f;
        bool reversing = _player.LastControls.Reverse || _player.ForwardSpeed < -0.5f;
        Vector3 body = reversing ? _player.GlobalTransform.Basis.Z : -_player.GlobalTransform.Basis.Z;
        Vector3 heading = _player.PreLinearVelocity.LengthSquared() > 4f ? _player.PreLinearVelocity : body;
        heading.Y = 0f;
        if (to.LengthSquared() < 1e-4f || heading.LengthSquared() < 1e-4f) return throttle;
        return throttle && heading.Normalized().Dot(to.Normalized()) > Mathf.Cos(Mathf.DegToRad(45f));
    }

    /// <summary>
    /// The T5 signature: hard steering in the 0.5 s before the hit, the contact on the player's flank and on the
    /// attacker's nose.
    /// </summary>
    private bool IsLastSecondTurn(Car attacker, Vector3 contactPoint)
    {
        bool turned = _throttle.Any(e => MathF.Abs(e.Steer) > 0.6f);
        if (!turned) return false;
        Vector3 mine = _player.GlobalTransform.AffineInverse() * contactPoint;
        Vector3 theirs = attacker.GlobalTransform.AffineInverse() * contactPoint;
        bool onMyFlank = MathF.Abs(mine.X) > 0.55f && MathF.Abs(mine.Z) < 1.9f;
        bool onTheirNose = theirs.Z < -1.4f;
        return onMyFlank && onTheirNose;
    }

    public void OnRefereeEvent(RefereeEvent e)
    {
        switch (e.Kind)
        {
            case RefereeEventKind.Damage when e.Car == _player.Id && e.Zone == DamageZone.Front && e.Time <= 60.0:
                _r.PlayerFrontLoss60[MathF.Abs((float)(e.Time - _contactAt)) < 1e-4f ? _contactRole : e.Other < 0 ? 3 : 4] += e.Amount;
                break;
            case RefereeEventKind.Damage when e.Other == _player.Id:
                // Damage credited to the player with no hit just now is grinding: a squeeze (T4) if on the throttle.
                if (e.Time - _lastPlayerContact.GetValueOrDefault(e.Car, -100) > 0.25 && _throttle.Any(x => x.Throttle))
                    _squeeze[e.Car] = e.Time;
                break;
            case RefereeEventKind.SpinScored:
                _r.Spins++;
                if (e.Car == _player.Id) CountPlayerPoints(e.Other, e.Points, spin: true);
                break;
            case RefereeEventKind.Wrecked:
                _r.Wrecks++;
                if (e.Other == _player.Id) CountPlayerPoints(e.Car, e.Points, spin: false);
                if (e.Car == _player.Id)
                {
                    _r.PlayerWrecked = true;
                    _r.PlayerSurvival = e.Time;
                    _r.PlayerWreckAttackers = _hitsOnPlayer.Where(h => e.Time - h.Time <= 3.0).Select(h => h.Attacker).Distinct().Count();
                    var recent = _hitsOnPlayer.Where(h => e.Time - h.Time <= 10.0).ToList();
                    _r.PlayerWreckState = $"F{_player.Health(DamageZone.Front) * 100:0} R{_player.Health(DamageZone.Rear) * 100:0} " +
                        $"L{_player.Health(DamageZone.Left) * 100:0} Rt{_player.Health(DamageZone.Right) * 100:0} wheels-lost {_player.Damage?.WheelsLost} " +
                        $"hits-10s {recent.Count} from {recent.Select(h => h.Attacker).Distinct().Count()} cars, by {(e.Other > 0 ? "#" + e.Other : "nobody")}";
                }
                break;
        }
    }

    private void CountPlayerPoints(int victim, int points, bool spin)
    {
        if (points <= 0) return;
        _scoredPoints += points;
        bool Recent(Dictionary<int, double> d) => d.TryGetValue(victim, out double t) && _now - t <= CreditWindow;
        var attacks = _playerAttacks.TryGetValue(victim, out var list) ? list.Where(a => _now - a.Time <= CreditWindow).ToList() : new();
        if (attacks.Any(a => a.Deliberate)) _r.PlayerPointsDeliberate += points;
        else if (Recent(_t5)) _r.PlayerPointsT5 += points;
        else if (Recent(_squeeze)) _r.PlayerPointsSqueeze += points;
        else if (attacks.Count > 0) _r.PlayerPointsNotAimed += points;
        else _r.PlayerPointsPassive += points;
        if (spin) _r.PlayerSpinPoints += points;
        else _r.PlayerWreckPoints += points;
    }

    public void Finish(double time, DerbyStandings standings, Referee referee, bool timedOut)
    {
        _r.EventLength = time;
        if (!_r.PlayerWrecked)
        {
            _r.PlayerSurvival = time;
            _r.PlayerWreckState = $"survived with F{_player.Health(DamageZone.Front) * 100:0} R{_player.Health(DamageZone.Rear) * 100:0} " +
                $"L{_player.Health(DamageZone.Left) * 100:0} Rt{_player.Health(DamageZone.Right) * 100:0} wheels-lost {_player.Damage?.WheelsLost}";
        }
        _r.PlayerPlace = standings.PlaceOf(_player.Id) ?? 0;
        _r.PlayerPoints = referee.Score(_player.Id).Total;
        _r.Winner = standings.Winner ?? -1;
        _r.WinnerSurvived = standings.WinnerSurvived;
        _r.TimedOut = timedOut;
        _r.DeliberatePointsShare = _scoredPoints > 0
            ? (double)(_r.PlayerPointsDeliberate + _r.PlayerPointsT5 + _r.PlayerPointsSqueeze) / _scoredPoints : double.NaN;
        _r.AimedPointsShare = _scoredPoints > 0 ? (double)_r.PlayerPointsDeliberate / _scoredPoints : double.NaN;
        int best = 0;
        foreach (int id in referee.CarIds)
            if (id != _player.Id) best = Math.Max(best, referee.Score(id).Total);
        _r.PlayerTopPoints = _r.PlayerPoints > best;
    }
}
