using System;
using System.Collections.Generic;
using System.Linq;

namespace DestructionRacing.Rules;

public sealed class Driver
{
    public Driver(int id, string name, bool isPlayer, int division)
    {
        Id = id;
        Name = name;
        IsPlayer = isPlayer;
        Division = division;
    }

    public int Id { get; }
    public string Name { get; }
    public bool IsPlayer { get; }
    /// <summary>1 = top division.</summary>
    public int Division { get; internal set; }
}

public sealed class Standing
{
    public Standing(Driver driver) => Driver = driver;

    public Driver Driver { get; }
    public int Points { get; internal set; }
    public int Wrecks { get; internal set; }
    /// <summary>Best finishing position this season (lower is better; int.MaxValue = none yet).</summary>
    public int BestFinish { get; internal set; } = int.MaxValue;
}

public readonly record struct EventResult(int DriverId, int Points, int Wrecks, int FinishPosition);

public sealed record SeasonOutcome(
    bool PlayerPromoted,
    bool PlayerWonChampionship,
    IReadOnlyList<(int DriverId, int From, int To)> Moves);

/// <summary>
/// Division championship (plan §4.5). Every event has all drivers; standings are per division.
/// At season end, the top of each division goes up and the bottom of the division above comes down,
/// so division sizes stay constant. The player is never relegated unless the ruleset allows it.
/// </summary>
public sealed class Championship
{
    private readonly RulesConfig _rules;
    private readonly Dictionary<int, Standing> _standings = new();

    public Championship(RulesConfig rules, IEnumerable<Driver> drivers)
    {
        _rules = rules;
        foreach (var d in drivers) _standings[d.Id] = new Standing(d);

        for (int div = 1; div <= rules.DivisionCount; div++)
        {
            int count = _standings.Values.Count(s => s.Driver.Division == div);
            if (count != rules.DriversPerDivision)
                throw new ArgumentException($"Division {div} has {count} drivers, expected {rules.DriversPerDivision}.");
        }
    }

    public IReadOnlyCollection<Standing> All => _standings.Values;

    public void RecordEvent(IEnumerable<EventResult> results)
    {
        foreach (var r in results)
        {
            var s = _standings[r.DriverId];
            s.Points += r.Points;
            s.Wrecks += r.Wrecks;
            if (r.FinishPosition > 0) s.BestFinish = Math.Min(s.BestFinish, r.FinishPosition);
        }
    }

    /// <summary>Division table, sorted: points, then wrecks, then best finish (✱ A10).</summary>
    public IReadOnlyList<Standing> Table(int division) =>
        _standings.Values
            .Where(s => s.Driver.Division == division)
            .OrderByDescending(s => s.Points)
            .ThenByDescending(s => s.Wrecks)
            .ThenBy(s => s.BestFinish)
            .ThenBy(s => s.Driver.Id)
            .ToList();

    /// <summary>Applies promotion and relegation, resets points, and reports what happened.</summary>
    public SeasonOutcome EndSeason()
    {
        var tables = Enumerable.Range(1, _rules.DivisionCount).ToDictionary(d => d, Table);
        var player = _standings.Values.Single(s => s.Driver.IsPlayer).Driver;
        int playerDivision = player.Division;
        bool playerTop = tables[playerDivision][0].Driver.IsPlayer;
        bool playerWon = playerTop && playerDivision == 1;

        var moves = new List<(int, int, int)>();
        for (int div = 2; div <= _rules.DivisionCount; div++)
        {
            var up = tables[div][0].Driver;
            var above = tables[div - 1];
            // The lowest driver above who may be relegated swaps down.
            var down = above.Last(s => _rules.PlayerCanBeRelegated || !s.Driver.IsPlayer).Driver;
            if (down == up) continue;
            moves.Add((up.Id, div, div - 1));
            moves.Add((down.Id, div - 1, div));
        }

        foreach (var (id, _, to) in moves) _standings[id].Driver.Division = to;
        foreach (var s in _standings.Values)
        {
            s.Points = 0;
            s.Wrecks = 0;
            s.BestFinish = int.MaxValue;
        }

        bool promoted = player.Division < playerDivision;
        return new SeasonOutcome(promoted, playerWon, moves);
    }
}
