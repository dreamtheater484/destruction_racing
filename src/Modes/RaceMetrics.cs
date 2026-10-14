using System;
using System.Collections.Generic;
using System.Linq;
using DestructionRacing.Rules;
using DestructionRacing.Tracks;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Modes;

/// <summary>One Wreckin' race as the sim harness sees it (plan §17.4).</summary>
public sealed class RaceMetricsResult
{
    public int Seed { get; init; }
    public string Profile { get; set; } = "";
    public double RaceLength { get; set; }
    public double LeaderLaps { get; set; }
    public int Spins { get; set; }
    public int Wrecks { get; set; }
    public double SpinsPerLap => LeaderLaps > 0.1 ? Spins / LeaderLaps : 0;
    public int CrossoverIncidents { get; set; }
    public int LeaderSpins { get; set; }
    public int PlayerPositionChanges { get; set; }
    public double PlayerCloseShare { get; set; }
    public bool PlayerWrecked { get; set; }
    public double PlayerWreckedAtDistance { get; set; } = double.NaN;
    public bool PlayerWreckedBeforeHalf => PlayerWrecked && PlayerWreckedAtDistance < 0.5;
    public bool PlayerFinished { get; set; }
    public int PlayerPlace { get; set; }
    public int PlayerPoints { get; set; }
    public int PlayerSpinPoints { get; set; }
    public int PlayerWreckPoints { get; set; }
    public int PlayerFinishPoints { get; set; }
    public int PlayerHitsDealt { get; set; }
    public int PlayerHitsTaken { get; set; }
    public double PlayerStuckTime { get; set; }
    public double PlayerDrivingTime { get; set; }
    public int Finishers { get; set; }
    public int Dnfs { get; set; }
    public bool TimedOut { get; set; }
    public double WinnerTime { get; set; } = double.NaN;
}

/// <summary>
/// Collects the Wreckin' metrics. A crossover incident is one or more hits of ≥ 9 kJ inside the crossover between
/// cars whose headings differ by more than 35° (crossing traffic, not a rear-ender), counted once per 2 s.
/// </summary>
public sealed class RaceMetrics
{
    private readonly Car _player;
    private readonly RaceMetricsResult _r;
    private readonly TrackPath _path;
    private readonly Vector3 _crossover;
    private readonly float _crossoverRadius;
    private double _lastCrossIncident = -100;
    private int _committedPosition;
    private int _candidatePosition;
    private double _candidateSince;
    private double _closeTime;
    private float _stuckFor;
    private float _raceDistance;

    public RaceMetrics(Car player, int seed, TrackPath path, Vector3 crossover, float crossoverRadius)
    {
        _player = player;
        _path = path;
        _crossover = crossover;
        _crossoverRadius = crossoverRadius;
        _r = new RaceMetricsResult { Seed = seed };
    }

    public RaceMetricsResult Result => _r;

    public void Step(double time, float dt, RaceStandings standings, IReadOnlyDictionary<int, float> lapPos,
        IReadOnlyDictionary<int, float> progress, Func<Car, bool> isRunning)
    {
        _raceDistance = standings.RaceDistance;
        if (!isRunning(_player) || standings.HasFinished(_player.Id)) return;
        _r.PlayerDrivingTime += dt;

        // Position changes, ignoring flicker: a new position counts once it has held for a second.
        int pos = standings.PositionOf(_player.Id);
        if (_committedPosition == 0) _committedPosition = _candidatePosition = pos;
        if (pos != _candidatePosition)
        {
            _candidatePosition = pos;
            _candidateSince = time;
        }
        else if (pos != _committedPosition && time - _candidateSince >= 1.0)
        {
            _r.PlayerPositionChanges += Math.Abs(pos - _committedPosition);
            _committedPosition = pos;
        }

        // Is any running car within 3 s of the player on the road (lapped cars count: they are traffic)?
        float window = 3f * MathF.Max(_player.ForwardSpeed, 12f);
        float me = lapPos[_player.Id];
        bool close = false;
        foreach (var (id, s) in lapPos)
        {
            if (id == _player.Id) continue;
            if (MathF.Abs(_path.Delta(me, s)) < window && standings.IsRacing(id))
            {
                close = true;
                break;
            }
        }
        if (close) _closeTime += dt;

        var c = _player.LastControls;
        bool trying = c.Throttle > 0.5f || c.Reverse;
        if (trying && _player.LinearVelocity.Length() < 1f) _stuckFor += dt;
        else _stuckFor = 0f;
        if (_stuckFor > 1f) _r.PlayerStuckTime += dt;
    }

    public void OnImpact(Impact i, IReadOnlyDictionary<int, float> lapPos)
    {
        if (i.B == null || i.Energy < DerbyMetrics.MetricHitEnergy) return;
        bool aAttacks = i.Attacker is HitAttacker.A or HitAttacker.Both;
        bool bAttacks = i.Attacker is HitAttacker.B or HitAttacker.Both;
        if (i.A == _player || i.B == _player)
        {
            bool playerAttacks = i.A == _player ? aAttacks : bAttacks;
            bool otherAttacks = i.A == _player ? bAttacks : aAttacks;
            if (playerAttacks) _r.PlayerHitsDealt++;
            if (otherAttacks) _r.PlayerHitsTaken++;
        }

        Vector3 flat = new(i.Point.X - _crossover.X, 0f, i.Point.Z - _crossover.Z);
        if (flat.Length() > _crossoverRadius) return;
        Vector3 ha = Heading(i.A), hb = Heading(i.B);
        if (ha.Dot(hb) > Mathf.Cos(Mathf.DegToRad(35f))) return;
        if (i.Time - _lastCrossIncident < 2.0) return;
        _lastCrossIncident = i.Time;
        _r.CrossoverIncidents++;
    }

    private static Vector3 Heading(Car c)
    {
        Vector3 v = c.PreLinearVelocity.LengthSquared() > 4f ? c.PreLinearVelocity : -c.GlobalTransform.Basis.Z;
        v.Y = 0f;
        return v.LengthSquared() > 1e-6f ? v.Normalized() : Vector3.Forward;
    }

    public void OnRefereeEvent(RefereeEvent e)
    {
        switch (e.Kind)
        {
            case RefereeEventKind.SpinScored:
                _r.Spins++;
                if (e.Doubled) _r.LeaderSpins++;
                if (e.Car == _player.Id) _r.PlayerSpinPoints += e.Points;
                break;
            case RefereeEventKind.Wrecked:
                _r.Wrecks++;
                if (e.Other == _player.Id) _r.PlayerWreckPoints += e.Points;
                break;
        }
    }

    public void Finish(double time, RaceStandings standings, Referee referee, bool timedOut)
    {
        _r.RaceLength = time;
        _r.TimedOut = timedOut;
        float lead = standings.Cars.Max(c => standings.Progress(c));
        _r.LeaderLaps = Math.Min(lead, standings.RaceDistance) / standings.LapLength;
        _r.PlayerWrecked = standings.IsRetired(_player.Id);
        if (_r.PlayerWrecked) _r.PlayerWreckedAtDistance = standings.Progress(_player.Id) / standings.RaceDistance;
        _r.PlayerFinished = standings.HasFinished(_player.Id);
        _r.PlayerPlace = standings.PositionOf(_player.Id);
        _r.PlayerPoints = referee.Score(_player.Id).Total;
        _r.PlayerFinishPoints = referee.Score(_player.Id).Get(ScoreCategory.Finish);
        _r.PlayerCloseShare = _r.PlayerDrivingTime > 1 ? _closeTime / _r.PlayerDrivingTime : double.NaN;
        _r.Finishers = standings.FinishedCount;
        _r.Dnfs = standings.Cars.Count(standings.IsRetired);
        var first = standings.Order().FirstOrDefault();
        if (standings.FinishTime(first) is double t) _r.WinnerTime = t;
    }
}
