using System.Collections.Generic;
using System.Linq;
using DestructionRacing.Rules;
using Xunit;

namespace DestructionRacing.Rules.Tests;

public class ChampionshipTests
{
    /// <summary>Standard field: 4 divisions × 5; the player (id 0) starts in division 4.</summary>
    private static (Championship champ, List<Driver> drivers) NewStandard()
    {
        var rules = RulesConfig.Standard;
        var drivers = new List<Driver> { new(0, "Player", true, 4) };
        int id = 1;
        for (int div = 1; div <= 4; div++)
        {
            int ai = div == 4 ? 4 : 5;
            for (int i = 0; i < ai; i++) drivers.Add(new Driver(id++, $"AI{id}", false, div));
        }
        return (new Championship(rules, drivers), drivers);
    }

    private static void Give(Championship c, int driverId, int points, int wrecks = 0, int finish = 0) =>
        c.RecordEvent(new[] { new EventResult(driverId, points, wrecks, finish) });

    [Fact]
    public void Rejects_wrong_division_sizes()
    {
        var drivers = new List<Driver> { new(0, "P", true, 4) };
        Assert.Throws<System.ArgumentException>(() => new Championship(RulesConfig.Standard, drivers));
    }

    [Fact]
    public void Topping_the_division_promotes_the_player_and_sizes_stay_constant()
    {
        var (c, drivers) = NewStandard();
        Give(c, 0, 120);
        var outcome = c.EndSeason();

        Assert.True(outcome.PlayerPromoted);
        Assert.Equal(3, drivers[0].Division);
        for (int div = 1; div <= 4; div++)
            Assert.Equal(5, drivers.Count(d => d.Division == div));
    }

    [Fact]
    public void Not_topping_the_division_repeats_the_season()
    {
        var (c, drivers) = NewStandard();
        var rival = drivers.First(d => d.Division == 4 && !d.IsPlayer);
        Give(c, rival.Id, 100);
        Give(c, 0, 50);
        var outcome = c.EndSeason();

        Assert.False(outcome.PlayerPromoted);
        Assert.Equal(4, drivers[0].Division);
        Assert.Equal(3, rival.Division);
    }

    [Fact]
    public void Player_is_never_relegated_in_standard_rules()
    {
        var (c, drivers) = NewStandard();
        Give(c, 0, 200);
        c.EndSeason();                                   // player promoted to div 3
        foreach (var d in drivers.Where(d => d.Division == 3 && !d.IsPlayer))
            Give(c, d.Id, 5);                            // player finishes last in div 3
        var div4Top = drivers.First(d => d.Division == 4);
        Give(c, div4Top.Id, 10);                         // someone from div 4 gets promoted
        var outcome = c.EndSeason();

        Assert.Equal(3, drivers[0].Division);            // player last in div 3 but stays
        Assert.False(outcome.PlayerPromoted);
        for (int div = 1; div <= 4; div++)
            Assert.Equal(5, drivers.Count(d => d.Division == div));
    }

    [Fact]
    public void Tie_break_prefers_more_wrecks_then_best_finish()
    {
        var (c, drivers) = NewStandard();
        var div1 = drivers.Where(d => d.Division == 1).ToList();
        Give(c, div1[0].Id, 50, wrecks: 1, finish: 2);
        Give(c, div1[1].Id, 50, wrecks: 3, finish: 5);
        Give(c, div1[2].Id, 50, wrecks: 3, finish: 1);
        var table = c.Table(1);
        Assert.Equal(div1[2].Id, table[0].Driver.Id);
        Assert.Equal(div1[1].Id, table[1].Driver.Id);
        Assert.Equal(div1[0].Id, table[2].Driver.Id);
    }

    [Fact]
    public void Winning_division_one_wins_the_championship()
    {
        var (c, drivers) = NewStandard();
        for (int season = 0; season < 3; season++)
        {
            Give(c, 0, 500);
            c.EndSeason();
        }
        Assert.Equal(1, drivers[0].Division);
        Give(c, 0, 500);
        Assert.True(c.EndSeason().PlayerWonChampionship);
    }

    [Fact]
    public void Points_reset_after_a_season()
    {
        var (c, _) = NewStandard();
        Give(c, 0, 80, wrecks: 2, finish: 1);
        c.EndSeason();
        Assert.All(c.All, s => Assert.Equal(0, s.Points));
    }
}
