using System.Collections.Generic;
using DestructionRacing.Rules;
using Xunit;

namespace DestructionRacing.Rules.Tests;

public class ScoringTests
{
    private static readonly RulesConfig Std = RulesConfig.Standard;
    private static readonly RulesConfig Dd1 = RulesConfig.DD1Classic;

    [Theory]
    [InlineData(SpinTier.Quarter, 10)]
    [InlineData(SpinTier.Half, 25)]
    [InlineData(SpinTier.Full, 50)]
    [InlineData(SpinTier.None, 0)]
    public void Standard_spin_points_match_DD2_PC_manual(SpinTier tier, int expected) =>
        Assert.Equal(expected, Scoring.SpinPoints(Std, tier, victimLeading: false));

    [Theory]
    [InlineData(SpinTier.Quarter, 2)]
    [InlineData(SpinTier.Half, 4)]
    [InlineData(SpinTier.Full, 10)]
    public void Classic_spin_points_match_DD1_PC_manual(SpinTier tier, int expected) =>
        Assert.Equal(expected, Scoring.SpinPoints(Dd1, tier, victimLeading: false));

    [Fact]
    public void Wreck_points_per_ruleset()
    {
        Assert.Equal(25, Scoring.WreckPoints(Std, false));
        Assert.Equal(10, Scoring.WreckPoints(Dd1, false));
    }

    [Fact]
    public void Points_double_when_the_damaged_car_leads()
    {
        Assert.Equal(100, Scoring.SpinPoints(Std, SpinTier.Full, victimLeading: true));
        Assert.Equal(50, Scoring.WreckPoints(Std, victimLeading: true));
        // DD1 FAQ example: spin the leader 360 (20) and wreck him (20) = 40.
        Assert.Equal(40, Scoring.SpinPoints(Dd1, SpinTier.Full, true) + Scoring.WreckPoints(Dd1, true));
    }

    [Fact]
    public void Bowl_scores_spins_and_wrecks_in_both_rulesets()
    {
        Assert.True(Scoring.ScoresSpins(Std, GameMode.DestructionDerby));
        Assert.True(Scoring.ScoresSpins(Dd1, GameMode.DestructionDerby));
        Assert.True(Scoring.ScoresDamage(Std, GameMode.DestructionDerby));
    }

    [Fact]
    public void Stock_car_scores_no_damage_points()
    {
        Assert.False(Scoring.ScoresDamage(Std, GameMode.StockCarRacing));
        Assert.False(Scoring.ScoresSpins(Std, GameMode.StockCarRacing));
    }

    [Fact]
    public void Finish_tables()
    {
        Assert.Equal(25, Scoring.WreckinFinishBonus(Std, 1));
        Assert.Equal(10, Scoring.WreckinFinishBonus(Std, 3));
        Assert.Equal(0, Scoring.WreckinFinishBonus(Std, 4));
        Assert.Equal(10, Scoring.WreckinFinishBonus(Dd1, 1));
        Assert.Equal(50, Scoring.StockCarPoints(Std, 1));
        Assert.Equal(0, Scoring.StockCarPoints(Std, 20));
        Assert.Equal(20, Std.StockCarFinishPoints.Length);
        Assert.Equal(16, Dd1.StockCarFinishPoints.Length);
    }

    [Fact]
    public void Grid_sizes_match_PC_versions()
    {
        Assert.Equal(20, Std.GridSize);
        Assert.Equal(16, Dd1.GridSize);
    }

    [Fact]
    public void Event_cap_is_applied()
    {
        var score = new EventScore(Dd1);
        for (int i = 0; i < 12; i++) score.Add(ScoreCategory.Wreck, 10);
        Assert.Equal(99, score.Total);
        Assert.Equal(0, score.Add(ScoreCategory.Spin, 10));
    }

    [Theory]
    [InlineData(80f, SpinTier.None)]
    [InlineData(86f, SpinTier.Quarter)]
    [InlineData(179f, SpinTier.Half)]
    [InlineData(-200f, SpinTier.Half)]
    [InlineData(356f, SpinTier.Full)]
    [InlineData(720f, SpinTier.Full)]
    public void Tier_thresholds_with_tolerance(float degrees, SpinTier expected) =>
        Assert.Equal(expected, Scoring.TierFor(degrees, 5f));

    [Fact]
    public void Bowl_leader_is_unique_positive_points_leader()
    {
        Assert.Null(Scoring.BowlLeader(new Dictionary<int, int> { [1] = 0, [2] = 0 }));
        Assert.Null(Scoring.BowlLeader(new Dictionary<int, int> { [1] = 25, [2] = 25 }));
        Assert.Equal(2, Scoring.BowlLeader(new Dictionary<int, int> { [1] = 10, [2] = 25, [3] = 0 }));
    }
}
