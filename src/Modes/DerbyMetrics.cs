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
    public double DeliberatePointsShare { get; set; } = double.NaN;
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
    private readonly Dictionary<int, bool> _lastPlayerHitDeliberate = new();
    private readonly List<(double Time, int Attacker)> _hitsOnPlayer = new();
    private readonly Queue<(double Time, bool Throttle)> _throttle = new();
    private double _lastHit;
    private int _deliberatePoints;
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
        if (carsRunning >= 5) _r.LongestIdle = Math.Max(_r.LongestIdle, time - _lastHit);
        if (!playerRunning) return;

        var c = _player.LastControls;
        _throttle.Enqueue((time, c.Throttle > 0.5f || c.Reverse));
        while (_throttle.Count > 0 && _throttle.Peek().Time < time - 0.5) _throttle.Dequeue();

        bool trying = c.Throttle > 0.5f || c.Reverse;
        if (trying && _player.LinearVelocity.Length() < 1f) _stuckFor += dt;
        else _stuckFor = 0f;
        if (_stuckFor > 1f) _r.PlayerStuckTime += dt;
    }

    public void OnImpact(Impact i)
    {
        if (i.B == null) return;
        // Taps that can score spins count for the deliberate-points share even below the metric hit bar.
        if (i.A == _player || i.B == _player)
        {
            bool playerIsA = i.A == _player;
            bool playerAttacks = playerIsA ? i.Attacker is HitAttacker.A or HitAttacker.Both : i.Attacker is HitAttacker.B or HitAttacker.Both;
            if (playerAttacks) _lastPlayerHitDeliberate[(playerIsA ? i.B : i.A).Id] = IsDeliberate(i.Point);
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
        Vector3 heading = _player.PreLinearVelocity.LengthSquared() > 4f ? _player.PreLinearVelocity : -_player.GlobalTransform.Basis.Z;
        heading.Y = 0f;
        if (to.LengthSquared() < 1e-4f || heading.LengthSquared() < 1e-4f) return throttle;
        return throttle && heading.Normalized().Dot(to.Normalized()) > Mathf.Cos(Mathf.DegToRad(45f));
    }

    public void OnRefereeEvent(RefereeEvent e)
    {
        switch (e.Kind)
        {
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
                }
                break;
        }
    }

    private void CountPlayerPoints(int victim, int points, bool spin)
    {
        if (points <= 0) return;
        _scoredPoints += points;
        bool attacked = _lastPlayerHitDeliberate.TryGetValue(victim, out bool deliberate);
        if (attacked && deliberate) _deliberatePoints += points;
        else if (attacked) _r.PlayerPointsNotAimed += points;
        else _r.PlayerPointsPassive += points;
        if (spin) _r.PlayerSpinPoints += points;
        else _r.PlayerWreckPoints += points;
    }

    public void Finish(double time, DerbyStandings standings, Referee referee, bool timedOut)
    {
        _r.EventLength = time;
        if (!_r.PlayerWrecked) _r.PlayerSurvival = time;
        _r.PlayerPlace = standings.PlaceOf(_player.Id) ?? 0;
        _r.PlayerPoints = referee.Score(_player.Id).Total;
        _r.Winner = standings.Winner ?? -1;
        _r.WinnerSurvived = standings.WinnerSurvived;
        _r.TimedOut = timedOut;
        _r.DeliberatePointsShare = _scoredPoints > 0 ? (double)_deliberatePoints / _scoredPoints : double.NaN;
    }
}
