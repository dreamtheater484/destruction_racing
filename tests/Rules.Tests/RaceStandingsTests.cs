using DestructionRacing.Rules;
using Xunit;

namespace DestructionRacing.Rules.Tests;

public class RaceStandingsTests
{
    private static RaceStandings Make() => new(new[] { 1, 2, 3, 4 }, laps: 3, lapLength: 1000f);

    [Fact]
    public void Order_follows_progress_and_the_grid_starts_behind_the_line()
    {
        var r = Make();
        r.Update(1, -30f, 0);
        r.Update(2, -10f, 0);
        r.Update(3, -20f, 0);
        r.Update(4, -40f, 0);
        Assert.Equal(new[] { 2, 3, 1, 4 }, r.Order());
        Assert.Equal(1, r.LapOf(2));
        r.Update(4, 1500f, 10);
        Assert.Equal(4, r.Leader);
        Assert.Equal(2, r.LapOf(4));
    }

    [Fact]
    public void Finishing_and_retirements_rank_correctly()
    {
        var r = Make();
        Assert.True(r.Update(3, 3000f, 100));
        Assert.True(r.Update(1, 3001f, 101));
        r.Update(2, 2500f, 101);
        r.Retire(4, 50);
        r.Freeze();
        Assert.Equal(new[] { 3, 1, 2, 4 }, r.Order());
        Assert.Equal(3, r.LapOf(3));
        // Frozen: nothing changes any more.
        r.Update(2, 3000f, 120);
        Assert.Equal(3, r.PositionOf(2));
    }

    [Fact]
    public void Later_wrecks_rank_above_earlier_ones_and_wrecked_cars_never_lead()
    {
        var r = Make();
        r.Update(1, 500f, 5);
        r.Retire(1, 10);
        r.Retire(2, 20);
        r.Update(3, 100f, 30);
        r.Update(4, 50f, 30);
        Assert.Equal(new[] { 3, 4, 2, 1 }, r.Order());
        Assert.Equal(3, r.Leader);
        Assert.False(r.Update(1, 900f, 40)); // retired cars don't move up
    }
}
