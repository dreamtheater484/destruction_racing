using System;
using System.Collections.Generic;
using System.Linq;
using DestructionRacing.Rules;
using Xunit;

namespace DestructionRacing.Rules.Tests;

public class RefereeTests
{
    private const float Dt = 1f / 120f;
    private static readonly RulesConfig Std = RulesConfig.Standard;

    private static readonly HitSide Front = new(new ZoneSplit(DamageZone.Front, DamageZone.Left, 0f), null);
    private static readonly HitSide Rear = new(new ZoneSplit(DamageZone.Rear, DamageZone.Left, 0f), null);
    private static readonly HitSide LeftSide = new(new ZoneSplit(DamageZone.Left, DamageZone.Front, 0f), null);

    private static Referee Make(GameMode mode = GameMode.DestructionDerby, int cars = 4) =>
        new(Std, mode, Enumerable.Range(1, cars).Select(i => (i, 1f)));

    /// <summary>Car 1 drives its front into car 2's side.</summary>
    private static CarHit Ram(int attacker, int victim, float energy, HitSide victimSide) =>
        new(attacker, victim, energy, HitAttacker.A, Front, victimSide, SpeedA: 12f, SpeedB: 8f);

    /// <summary>Steps every car with the same yaw rate (deg/s) for the given duration.</summary>
    private static List<RefereeEvent> Spin(Referee r, double start, double seconds, Func<int, float> yawDeg)
    {
        var events = new List<RefereeEvent>();
        int steps = (int)(seconds / Dt);
        for (int i = 0; i < steps; i++)
        {
            var samples = r.CarIds.ToDictionary(id => id, id => new CarSample(yawDeg(id) * MathF.PI / 180f, 8f, true));
            r.Step(start + i * Dt, Dt, samples);
            events.AddRange(r.DrainEvents());
        }
        return events;
    }

    [Fact]
    public void Rear_quarter_hit_that_spins_the_victim_scores_the_spin_for_the_attacker()
    {
        var r = Make();
        r.OnCarHit(0, Ram(1, 2, 30_000f, Rear));
        r.DrainEvents();
        var events = Spin(r, 0.02, 0.7, id => id == 2 ? 300f : 0f);   // ~210°
        events.AddRange(Spin(r, 0.72, 1.0, _ => 0f));

        var scored = Assert.Single(events, e => e.Kind == RefereeEventKind.SpinScored);
        Assert.Equal(1, scored.Car);
        Assert.Equal(2, scored.Other);
        Assert.Equal(SpinTier.Half, scored.Tier);
        Assert.Equal(Std.Spin180Points, r.Score(1).Total);
    }

    [Fact]
    public void Damaging_the_bowl_points_leader_doubles_the_spin()
    {
        var r = Make();
        // Car 2 becomes the points leader by spinning car 3.
        r.OnCarHit(0, Ram(2, 3, 30_000f, Rear));
        Spin(r, 0.02, 0.5, id => id == 3 ? 250f : 0f);
        Spin(r, 0.52, 1.0, _ => 0f);
        Assert.Equal(2, r.Leader);

        r.OnCarHit(2, Ram(1, 2, 30_000f, Rear));
        var events = Spin(r, 2.02, 0.5, id => id == 2 ? 250f : 0f);
        events.AddRange(Spin(r, 2.52, 1.0, _ => 0f));

        var scored = Assert.Single(events, e => e.Kind == RefereeEventKind.SpinScored);
        Assert.True(scored.Doubled);
        Assert.Equal(Std.Spin90Points * 2, scored.Points);
    }

    [Fact]
    public void Wall_spins_and_light_scrapes_score_nothing()
    {
        var r = Make();
        r.OnWallHit(0, 2, 80_000f, LeftSide);
        r.OnCarHit(0, Ram(1, 3, Std.Spin.HitMinEnergy * 0.5f, Rear));
        var events = Spin(r, 0.02, 1.5, id => id is 2 or 3 ? 400f : 0f);
        events.AddRange(Spin(r, 1.52, 1.0, _ => 0f));

        Assert.DoesNotContain(events, e => e.Kind == RefereeEventKind.SpinScored);
        Assert.True(r.Damage(2).Health(DamageZone.Left) < DamageState.MaxHealth);
    }

    [Fact]
    public void Front_rams_cost_the_attacker_front_health()
    {
        var r = Make();
        r.OnCarHit(0, Ram(1, 2, 60_000f, LeftSide));
        Assert.True(r.Damage(1).Health(DamageZone.Front) < DamageState.MaxHealth);
        Assert.True(r.Damage(2).Health(DamageZone.Left) < DamageState.MaxHealth);
        // Side toughness 1.2: the victim's side loses less than the attacker's front.
        Assert.True(r.Damage(2).Health(DamageZone.Left) > r.Damage(1).Health(DamageZone.Front));
    }

    [Fact]
    public void Destroying_the_front_wrecks_and_credits_the_car_that_dealt_most_damage()
    {
        var r = Make();
        r.OnCarHit(0.0, Ram(3, 2, 40_000f, Front with { }));
        r.OnCarHit(0.5, Ram(1, 2, 120_000f, Front));
        RefereeEvent? wreck = null;
        for (int i = 0; i < 20 && wreck == null; i++)
        {
            r.OnCarHit(1.0 + i * 0.1, Ram(1, 2, 120_000f, Front));
            wreck = r.DrainEvents().Cast<RefereeEvent?>().FirstOrDefault(e => e!.Value.Kind == RefereeEventKind.Wrecked);
        }

        Assert.NotNull(wreck);
        Assert.Equal(2, wreck!.Value.Car);
        Assert.Equal(1, wreck.Value.Other);
        Assert.True(r.IsWrecked(2));
        Assert.True(r.Score(1).Get(ScoreCategory.Wreck) >= Std.WreckPoints);
    }

    [Fact]
    public void Hits_on_wrecked_cars_score_nothing()
    {
        var r = Make();
        r.ForceWreck(0, 2);
        r.DrainEvents();
        r.OnCarHit(1, Ram(1, 2, 60_000f, Rear));
        var events = Spin(r, 1.02, 1.0, id => id == 2 ? 400f : 0f);
        events.AddRange(Spin(r, 2.02, 1.0, _ => 0f));
        Assert.DoesNotContain(events, e => e.Kind is RefereeEventKind.SpinScored or RefereeEventKind.Wrecked);
        Assert.Equal(0, r.Score(1).Total);
    }

    [Fact]
    public void Ramming_a_parked_car_until_your_own_front_dies_credits_nobody()
    {
        var r = Make();
        RefereeEvent? wreck = null;
        for (int i = 0; i < 30 && wreck == null; i++)
        {
            r.OnCarHit(i * 0.2, new CarHit(1, 2, 120_000f, HitAttacker.A, Front, Rear, SpeedA: 15f, SpeedB: 0f));
            wreck = r.DrainEvents().Cast<RefereeEvent?>().FirstOrDefault(e => e!.Value.Kind == RefereeEventKind.Wrecked && e.Value.Car == 1);
        }
        Assert.NotNull(wreck);
        Assert.Equal(-1, wreck!.Value.Other);
        Assert.Equal(0, r.Score(2).Total);
    }

    [Fact]
    public void Brake_check_credits_the_driving_car_for_the_tailgaters_front_damage()
    {
        var r = Make();
        // Car 1 (tailgater) runs into car 2's rear; car 2 was driving, so damage to car 1 is credited to car 2.
        RefereeEvent? wreck = null;
        for (int i = 0; i < 30 && wreck == null; i++)
        {
            r.OnCarHit(i * 0.2, new CarHit(1, 2, 120_000f, HitAttacker.A, Front, Rear, SpeedA: 20f, SpeedB: 6f));
            wreck = r.DrainEvents().Cast<RefereeEvent?>().FirstOrDefault(e => e!.Value.Kind == RefereeEventKind.Wrecked && e.Value.Car == 1);
        }
        Assert.NotNull(wreck);
        Assert.Equal(2, wreck!.Value.Other);
    }

    [Fact]
    public void Grinding_contact_deals_less_damage_than_a_fresh_hit()
    {
        var fresh = Make();
        fresh.OnCarHit(0, Ram(1, 2, 40_000f, LeftSide));
        fresh.OnCarHit(1.0, Ram(1, 2, 40_000f, LeftSide));
        var grind = Make();
        grind.OnCarHit(0, Ram(1, 2, 40_000f, LeftSide));
        grind.OnCarHit(0.3, Ram(1, 2, 40_000f, LeftSide));
        Assert.True(grind.Damage(2).Health(DamageZone.Left) > fresh.Damage(2).Health(DamageZone.Left));
    }

    [Fact]
    public void Car_that_cannot_move_for_ten_seconds_is_wrecked()
    {
        var r = Make();
        var samples = r.CarIds.ToDictionary(id => id, id => new CarSample(0f, id == 3 ? 0.1f : 8f, true));
        RefereeEvent? wreck = null;
        for (int i = 0; i < (int)(10.5f / Dt) && wreck == null; i++)
        {
            r.Step(i * Dt, Dt, samples);
            wreck = r.DrainEvents().Cast<RefereeEvent?>().FirstOrDefault(e => e!.Value.Kind == RefereeEventKind.Wrecked);
        }
        Assert.NotNull(wreck);
        Assert.Equal(3, wreck!.Value.Car);
        Assert.InRange(wreck.Value.Time, Std.ImmobilisedWreckTime - 0.1, Std.ImmobilisedWreckTime + 0.1);
    }

    [Fact]
    public void Corner_hits_damage_the_nearest_wheel_and_heavy_rear_damage_loses_it()
    {
        var r = Make();
        var rearCorner = new HitSide(new ZoneSplit(DamageZone.Rear, DamageZone.Left, 0.4f), Wheel.RearLeft);
        var events = new List<RefereeEvent>();
        for (int i = 0; i < 40 && !r.Damage(2).IsWheelLost(Wheel.RearLeft); i++)
        {
            r.OnCarHit(i * 0.3, Ram(1, 2, 120_000f, rearCorner));
            events.AddRange(r.DrainEvents());
        }
        Assert.True(r.Damage(2).IsWheelLost(Wheel.RearLeft));
        Assert.Contains(events, e => e.Kind == RefereeEventKind.WheelLost && e.Car == 2 && e.Wheel == Wheel.RearLeft);
    }

    [Fact]
    public void Stock_car_contact_never_scores()
    {
        var r = Make(GameMode.StockCarRacing);
        r.SetRaceLeader(2);
        r.OnCarHit(0, Ram(1, 2, 30_000f, Rear));
        var events = Spin(r, 0.02, 1.2, id => id == 2 ? 400f : 0f);
        events.AddRange(Spin(r, 1.22, 1.0, _ => 0f));
        Assert.Equal(0, r.Score(1).Total);
    }

    [Fact]
    public void Wreckin_race_leader_comes_from_the_mode()
    {
        var r = Make(GameMode.WreckinRacing);
        r.SetRaceLeader(4);
        Assert.Equal(4, r.Leader);
    }
}

public class DerbyStandingsTests
{
    [Fact]
    public void Last_car_running_wins_and_order_is_reverse_elimination()
    {
        var s = new DerbyStandings(new[] { 1, 2, 3, 4 });
        s.Eliminate(10, new[] { (3, 0) });
        s.Eliminate(20, new[] { (1, 50) });
        Assert.False(s.IsOver);
        s.Eliminate(30, new[] { (4, 10) });

        Assert.True(s.IsOver);
        Assert.Equal(2, s.Winner);
        Assert.True(s.WinnerSurvived);
        Assert.Equal(new[] { 2, 4, 1, 3 }, s.FinishingOrder());
        Assert.Equal(4, s.PlaceOf(3));
    }

    [Fact]
    public void Simultaneous_wrecks_are_ordered_by_points()
    {
        var s = new DerbyStandings(new[] { 1, 2, 3, 4 });
        var places = s.Eliminate(5, new[] { (1, 30), (2, 10) });
        Assert.Equal(4, places.Single(p => p.Car == 2).Place);
        Assert.Equal(3, places.Single(p => p.Car == 1).Place);
    }

    [Fact]
    public void Last_two_wrecking_each_other_gives_the_win_to_more_points_without_survival()
    {
        var s = new DerbyStandings(new[] { 1, 2, 3 });
        s.Eliminate(5, new[] { (3, 0) });
        s.Eliminate(9, new[] { (1, 25), (2, 75) });
        Assert.True(s.IsOver);
        Assert.Equal(2, s.Winner);
        Assert.False(s.WinnerSurvived);
        Assert.Equal(new[] { 2, 1, 3 }, s.FinishingOrder());
    }

    [Fact]
    public void Ending_by_points_places_the_running_cars()
    {
        var s = new DerbyStandings(new[] { 1, 2, 3 });
        s.Eliminate(5, new[] { (3, 0) });
        s.EndByPoints(new Dictionary<int, int> { [1] = 10, [2] = 40 });
        Assert.Equal(new[] { 2, 1, 3 }, s.FinishingOrder());
        Assert.True(s.IsOver);
    }
}
