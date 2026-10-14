using System;
using System.Collections.Generic;
using System.Linq;
using DestructionRacing.Rules;
using Xunit;

namespace DestructionRacing.Rules.Tests;

public class SpinTrackerTests
{
    private const float Dt = 1f / 120f;
    private static readonly SpinTuning Tuning = new();
    private static float Rad(float deg) => deg * MathF.PI / 180f;

    /// <summary>Runs the tracker over a yaw-rate profile (deg/s as a function of time) and collects events.</summary>
    private static List<SpinEvent> Run(SpinTracker tracker, double start, double seconds, Func<double, float> yawDegPerSec)
    {
        var events = new List<SpinEvent>();
        int steps = (int)(seconds / Dt);
        for (int i = 0; i < steps; i++)
        {
            double t = start + i * Dt;
            tracker.Step(t, Dt, Rad(yawDegPerSec(t)), events);
        }
        return events;
    }

    [Theory]
    [InlineData(150f, 0.8, SpinTier.Quarter)]   // 120°
    [InlineData(250f, 0.8, SpinTier.Half)]      // 200°
    [InlineData(500f, 0.8, SpinTier.Full)]      // 400°
    public void Awards_tier_for_rotation_after_hit(float rate, double duration, SpinTier expected)
    {
        var tracker = new SpinTracker(Tuning);
        tracker.RegisterHit(attackerId: 7, time: 0, hitEnergy: 20_000f, victimSpeed: 10f, victimWrecked: false);
        var events = Run(tracker, 0.05, duration, _ => rate);
        events.AddRange(Run(tracker, 0.05 + duration, 1.0, _ => 0f));

        var award = Assert.Single(events, e => e.Kind == SpinEventKind.Awarded);
        Assert.Equal(7, award.AttackerId);
        Assert.Equal(expected, award.Tier);
        Assert.False(tracker.IsOpen);
    }

    [Fact]
    public void Live_tier_upgrades_are_reported_in_order()
    {
        var tracker = new SpinTracker(Tuning);
        tracker.RegisterHit(1, 0, 20_000f, 10f, false);
        var events = Run(tracker, 0.02, 1.0, _ => 450f);
        var tiers = events.Where(e => e.Kind == SpinEventKind.TierReached).Select(e => e.Tier).ToArray();
        Assert.Equal(new[] { SpinTier.Quarter, SpinTier.Half, SpinTier.Full }, tiers);
    }

    [Fact]
    public void No_hit_means_no_spin_points()
    {
        var tracker = new SpinTracker(Tuning);
        var events = Run(tracker, 0, 2, _ => 500f);
        Assert.Empty(events);
    }

    [Fact]
    public void Weak_contact_is_not_a_hit()
    {
        var tracker = new SpinTracker(Tuning);
        tracker.RegisterHit(1, 0, Tuning.HitMinEnergy * 0.5f, 10f, false);
        Assert.False(tracker.IsOpen);
    }

    [Fact]
    public void Rotation_starting_too_late_is_not_attributed()
    {
        var tracker = new SpinTracker(Tuning);
        tracker.RegisterHit(1, 0, 20_000f, 10f, false);
        var events = Run(tracker, 0, 1.5, t => t < 1.2 ? 0f : 500f);
        Assert.DoesNotContain(events, e => e.Kind == SpinEventKind.Awarded);
    }

    [Fact]
    public void Hitting_a_parked_or_wrecked_car_opens_nothing()
    {
        var tracker = new SpinTracker(Tuning);
        tracker.RegisterHit(1, 0, 20_000f, victimSpeed: 0.5f, victimWrecked: false);
        Assert.False(tracker.IsOpen);
        tracker.RegisterHit(1, 0, 20_000f, victimSpeed: 10f, victimWrecked: true);
        Assert.False(tracker.IsOpen);
    }

    [Fact]
    public void Big_second_hit_during_spin_steals_attribution()
    {
        var tracker = new SpinTracker(Tuning);
        tracker.RegisterHit(1, 0, 20_000f, 10f, false);
        var events = Run(tracker, 0.01, 0.2, _ => 300f);
        tracker.RegisterHit(2, 0.21, Tuning.StealMinEnergy + 1f, 8f, false);
        events.AddRange(Run(tracker, 0.22, 0.6, _ => 300f));
        events.AddRange(Run(tracker, 0.82, 1.0, _ => 0f));
        Assert.Equal(2, Assert.Single(events, e => e.Kind == SpinEventKind.Awarded).AttackerId);
    }

    [Fact]
    public void Small_second_hit_during_spin_keeps_attribution()
    {
        var tracker = new SpinTracker(Tuning);
        tracker.RegisterHit(1, 0, 20_000f, 10f, false);
        var events = Run(tracker, 0.01, 0.2, _ => 300f);
        tracker.RegisterHit(2, 0.21, Tuning.HitMinEnergy + 1f, 8f, false);
        events.AddRange(Run(tracker, 0.22, 0.6, _ => 300f));
        events.AddRange(Run(tracker, 0.82, 1.0, _ => 0f));
        Assert.Equal(1, Assert.Single(events, e => e.Kind == SpinEventKind.Awarded).AttackerId);
    }

    [Fact]
    public void Fishtailing_back_and_forth_does_not_accumulate()
    {
        var tracker = new SpinTracker(Tuning);
        tracker.RegisterHit(1, 0, 20_000f, 10f, false);
        // ±150°/s alternating every 0.3 s: net rotation stays around ±45°.
        var events = Run(tracker, 0.01, 2.0, t => ((int)(t / 0.3) % 2 == 0) ? 150f : -150f);
        events.AddRange(Run(tracker, 2.01, 1.0, _ => 0f));
        Assert.DoesNotContain(events, e => e.Kind == SpinEventKind.Awarded);
    }

    [Fact]
    public void Max_duration_closes_a_never_ending_spin()
    {
        var tracker = new SpinTracker(Tuning);
        tracker.RegisterHit(3, 0, 20_000f, 10f, false);
        var events = Run(tracker, 0.01, Tuning.MaxDuration + 0.5, _ => 200f);
        var award = Assert.Single(events, e => e.Kind == SpinEventKind.Awarded);
        Assert.Equal(SpinTier.Full, award.Tier);
    }
}
