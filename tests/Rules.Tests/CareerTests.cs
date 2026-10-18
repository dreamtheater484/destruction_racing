using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DestructionRacing.Rules;
using Xunit;

namespace DestructionRacing.Rules.Tests;

public class CareerTests
{
    /// <summary>19 AI drivers: 5 each in divisions 1–3 and 4 in division 4, beside the player.</summary>
    private static List<(string, int)> Field() =>
        Enumerable.Range(0, 19).Select(i => ($"AI{i}", i < 15 ? 1 + i / 5 : 4)).ToList();

    private static Career New(ChampionshipKind kind = ChampionshipKind.Wreckin) => new(kind, RulesConfig.Standard, Field(), seed: 5);

    /// <summary>Plays the rest of the season with the player scoring <paramref name="player"/> and AI<paramref name="rival"/> <paramref name="rivalPoints"/> per event.</summary>
    private static void PlaySeason(Career c, int player, string rival = "", int rivalPoints = 0)
    {
        while (!c.SeasonComplete)
        {
            var results = new List<EventResult> { new(Career.PlayerId, player, 0, 1) };
            if (c.Find(rival) is { } r) results.Add(new EventResult(r.Id, rivalPoints, 0, 2));
            c.RecordEvent(results);
        }
    }

    [Fact]
    public void A_wreckin_season_is_four_track_events_and_the_bowl()
    {
        for (int div = 1; div <= 4; div++)
        {
            var events = Calendar.For(ChampionshipKind.Wreckin, div);
            Assert.Equal(5, events.Count);
            Assert.All(events.Take(4), e => Assert.Equal(GameMode.WreckinRacing, e.Mode));
            Assert.True(events[4].IsDerby);
            Assert.Equal(Venue.Bowl, events[4].Venue);
        }
    }

    [Fact]
    public void A_stock_car_season_is_four_track_events()
    {
        for (int div = 1; div <= 4; div++)
        {
            var events = Calendar.For(ChampionshipKind.StockCar, div);
            Assert.Equal(4, events.Count);
            Assert.All(events, e => Assert.Equal(GameMode.StockCarRacing, e.Mode));
        }
    }

    [Fact]
    public void Every_division_races_both_layouts()
    {
        for (int div = 1; div <= 4; div++)
        {
            var events = Calendar.For(ChampionshipKind.StockCar, div);
            Assert.Contains(events, e => e.Venue == Venue.Oval);
            Assert.Contains(events, e => e.Venue == Venue.Figure8);
        }
    }

    [Fact]
    public void Each_promotion_opens_new_combinations_and_all_twelve_are_open_at_the_top()
    {
        Assert.True(Unlocks.IsOpen(4, Venue.Figure8, false, Light.Afternoon));
        Assert.False(Unlocks.IsOpen(4, Venue.Oval, true, Light.Afternoon));
        Assert.False(Unlocks.IsOpen(4, Venue.Bowl, false, Light.Night));
        for (int div = 3; div >= 1; div--) Assert.NotEmpty(Unlocks.OpenedBy(div));
        foreach (var venue in new[] { Venue.Oval, Venue.Figure8 })
            foreach (bool rev in new[] { false, true })
                foreach (var light in new[] { Light.Afternoon, Light.GoldenHour, Light.Night })
                    Assert.True(Unlocks.IsOpen(1, venue, rev, light), $"{venue} {rev} {light}");
        foreach (var light in new[] { Light.Afternoon, Light.GoldenHour, Light.Night })
            Assert.True(Unlocks.IsOpen(1, Venue.Bowl, false, light));
    }

    [Fact]
    public void Events_advance_and_the_season_ends_after_the_finale()
    {
        var c = New();
        Assert.Equal(4, c.PlayerDivision);
        Assert.Equal(0, c.EventIndex);
        for (int i = 0; i < 4; i++)
        {
            Assert.False(c.IsFinale);
            c.RecordEvent(new[] { new EventResult(Career.PlayerId, 10, 0, 3) });
        }
        Assert.True(c.IsFinale);
        Assert.True(c.NextEvent!.IsDerby);
        c.RecordEvent(new[] { new EventResult(Career.PlayerId, 10, 0, 3) });
        Assert.True(c.SeasonComplete);
        Assert.Throws<System.InvalidOperationException>(() => c.RecordEvent(new[] { new EventResult(0, 1, 0, 1) }));
    }

    [Fact]
    public void Topping_the_division_promotes_and_the_new_season_uses_the_new_calendar()
    {
        var c = New();
        PlaySeason(c, 100);
        var outcome = c.EndSeason();
        Assert.True(outcome.PlayerPromoted);
        Assert.Equal(3, c.PlayerDivision);
        Assert.Equal(2, c.Season);
        Assert.Equal(0, c.EventIndex);
        Assert.Equal(1, c.Attempts);
        Assert.Equal(Calendar.For(ChampionshipKind.Wreckin, 3), c.Events);
        Assert.Equal(new SeasonRecord(1, 4, 1, 500, true), c.History.Single());
        for (int div = 1; div <= 4; div++) Assert.Equal(5, c.Drivers.Count(d => d.Division == div));
    }

    [Fact]
    public void Missing_the_top_spot_repeats_the_division_and_counts_the_attempts()
    {
        var c = New();
        PlaySeason(c, 10, "AI15", 50);
        var outcome = c.EndSeason();
        Assert.False(outcome.PlayerPromoted);
        Assert.Equal(4, c.PlayerDivision);
        Assert.Equal(2, c.Attempts);
        Assert.Equal(3, c.Find("AI15")!.Division);            // the division winner went up
        Assert.Equal(2, c.History.Single().Place);
    }

    [Fact]
    public void Winning_division_one_makes_the_player_champion()
    {
        var c = New(ChampionshipKind.StockCar);
        for (int season = 0; season < 4; season++)
        {
            PlaySeason(c, 50);
            c.EndSeason();
        }
        Assert.True(c.Champion);
        Assert.Equal(1, c.PlayerDivision);
    }

    [Fact]
    public void The_last_event_points_are_kept_for_the_tally()
    {
        var c = New();
        c.RecordEvent(new[] { new EventResult(Career.PlayerId, 40, 1, 2), new EventResult(c.Find("AI16")!.Id, 25, 0, 5) });
        Assert.Equal(40, c.LastEventPoints[Career.PlayerId]);
        Assert.Equal(25, c.LastEventPoints[c.Find("AI16")!.Id]);
    }

    [Fact]
    public void A_save_round_trips_through_json()
    {
        var c = New();
        PlaySeason(c, 100);
        c.EndSeason();                                           // the player is in division 3 now
        c.RecordEvent(new[] { new EventResult(Career.PlayerId, 35, 2, 4), new EventResult(c.Find("AI3")!.Id, 60, 1, 1) });

        string json = JsonSerializer.Serialize(c.ToSave());
        var loaded = Career.Load(JsonSerializer.Deserialize<Career.Save>(json)!, RulesConfig.Standard);

        Assert.Equal(c.Kind, loaded.Kind);
        Assert.Equal(c.Season, loaded.Season);
        Assert.Equal(c.EventIndex, loaded.EventIndex);
        Assert.Equal(c.PlayerDivision, loaded.PlayerDivision);
        Assert.Equal(c.EventSeed, loaded.EventSeed);
        Assert.Equal(c.History, loaded.History);
        Assert.Equal(c.PlayerTable.Select(s => (s.Driver.Name, s.Points, s.Wrecks, s.BestFinish)),
                     loaded.PlayerTable.Select(s => (s.Driver.Name, s.Points, s.Wrecks, s.BestFinish)));
        Assert.Equal(35, loaded.LastEventPoints[Career.PlayerId]);
        foreach (var d in c.Drivers) Assert.Equal(d.Division, loaded.Find(d.Name)!.Division);
    }

    [Fact]
    public void A_save_with_broken_divisions_is_refused()
    {
        var save = New().ToSave();
        save.Drivers[1] = save.Drivers[1] with { Division = 4 };  // six in division 4
        Assert.Throws<System.FormatException>(() => Career.Load(save, RulesConfig.Standard));
    }
}
