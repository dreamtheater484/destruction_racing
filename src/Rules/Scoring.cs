using System;
using System.Collections.Generic;

namespace DestructionRacing.Rules;

public enum GameMode
{
    DestructionDerby,
    WreckinRacing,
    StockCarRacing,
    TimeTrial,
}

public enum SpinTier
{
    None = 0,
    Quarter = 90,
    Half = 180,
    Full = 360,
}

/// <summary>Point values per ruleset (plan §4.0, §5.1).</summary>
public static class Scoring
{
    public static bool ScoresDamage(RulesConfig rules, GameMode mode) => mode switch
    {
        GameMode.WreckinRacing => true,
        GameMode.DestructionDerby => true,
        _ => false,
    };

    public static bool ScoresSpins(RulesConfig rules, GameMode mode) => mode switch
    {
        GameMode.WreckinRacing => true,
        GameMode.DestructionDerby => rules.BowlScoresSpins,
        _ => false,
    };

    public static int SpinPoints(RulesConfig rules, SpinTier tier, bool victimLeading)
    {
        int basePoints = tier switch
        {
            SpinTier.Quarter => rules.Spin90Points,
            SpinTier.Half => rules.Spin180Points,
            SpinTier.Full => rules.Spin360Points,
            _ => 0,
        };
        return victimLeading ? basePoints * rules.LeaderMultiplier : basePoints;
    }

    public static int WreckPoints(RulesConfig rules, bool victimLeading) =>
        victimLeading ? rules.WreckPoints * rules.LeaderMultiplier : rules.WreckPoints;

    /// <summary>Wreckin' Racing finishing bonus for a 1-based finishing position.</summary>
    public static int WreckinFinishBonus(RulesConfig rules, int position) =>
        position >= 1 && position <= rules.WreckinFinishBonus.Length ? rules.WreckinFinishBonus[position - 1] : 0;

    /// <summary>Stock Car points for a 1-based finishing position.</summary>
    public static int StockCarPoints(RulesConfig rules, int position) =>
        position >= 1 && position <= rules.StockCarFinishPoints.Length ? rules.StockCarFinishPoints[position - 1] : 0;

    public static SpinTier TierFor(float accumulatedDegrees, float toleranceDeg)
    {
        float a = MathF.Abs(accumulatedDegrees) + toleranceDeg;
        if (a >= 360f) return SpinTier.Full;
        if (a >= 180f) return SpinTier.Half;
        if (a >= 90f) return SpinTier.Quarter;
        return SpinTier.None;
    }

    /// <summary>
    /// The car that counts as "in the lead" in the Bowl (✱ A4): the unique points leader with more than 0 points.
    /// Returns null when nobody leads (all zero or a tie).
    /// </summary>
    public static int? BowlLeader(IReadOnlyDictionary<int, int> pointsByCar)
    {
        int? leader = null;
        int best = 0;
        bool tie = false;
        foreach (var (car, points) in pointsByCar)
        {
            if (points > best)
            {
                best = points;
                leader = car;
                tie = false;
            }
            else if (points == best && points > 0)
            {
                tie = true;
            }
        }
        return tie ? null : leader;
    }
}

public enum ScoreCategory
{
    Spin,
    Wreck,
    Finish,
    Survivor,
}

/// <summary>One car's points in one event, with the per-event cap applied.</summary>
public sealed class EventScore
{
    private readonly int _cap;
    private readonly Dictionary<ScoreCategory, int> _byCategory = new();

    public EventScore(RulesConfig rules)
    {
        _cap = rules.EventPointsCap;
    }

    public int Total { get; private set; }
    public int Wrecks { get; private set; }

    public int Get(ScoreCategory category) => _byCategory.TryGetValue(category, out int v) ? v : 0;

    /// <summary>Adds points (clamped by the event cap); returns the points actually added.</summary>
    public int Add(ScoreCategory category, int points)
    {
        if (points <= 0) return 0;
        int added = Math.Min(points, _cap - Total);
        if (added <= 0) return 0;
        Total += added;
        _byCategory[category] = Get(category) + added;
        if (category == ScoreCategory.Wreck) Wrecks++;
        return added;
    }
}
