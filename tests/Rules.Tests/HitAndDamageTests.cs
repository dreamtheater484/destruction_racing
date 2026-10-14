using System.Numerics;
using DestructionRacing.Rules;
using Xunit;

namespace DestructionRacing.Rules.Tests;

public class HitEnergyTests
{
    private static BodyState Car(Vector3 velocity, Vector3 position, float mass = 1400f) =>
        new(velocity, Vector3.Zero, position, mass);

    [Fact]
    public void Rear_end_hit_attributes_the_faster_car_and_uses_reduced_mass()
    {
        // A (behind, at z=+5) drives forward (-Z) at 20 m/s into B driving at 10 m/s.
        var a = Car(new Vector3(0, 0, -20), new Vector3(0, 0, 5));
        var b = Car(new Vector3(0, 0, -10), new Vector3(0, 0, 0));
        var n = new Vector3(0, 0, -1);
        var r = HitEnergy.CarVsCar(a, b, new Vector3(0, 0, 2.5f), n);

        Assert.Equal(HitAttacker.A, r.Attacker);
        Assert.Equal(10f, r.ClosingSpeed, 3);
        Assert.Equal(0.5f * 700f * 100f, r.Energy, 1);
    }

    [Fact]
    public void Equal_head_on_counts_both_as_attackers()
    {
        var a = Car(new Vector3(0, 0, -15), new Vector3(0, 0, 5));
        var b = Car(new Vector3(0, 0, 15), new Vector3(0, 0, -5));
        var r = HitEnergy.CarVsCar(a, b, Vector3.Zero, new Vector3(0, 0, -1));
        Assert.Equal(HitAttacker.Both, r.Attacker);
    }

    [Fact]
    public void T_bone_attributes_the_moving_car()
    {
        var a = Car(new Vector3(18, 0, 0), new Vector3(-3, 0, 0));
        var b = Car(new Vector3(0, 0, -2), Vector3.Zero);
        var r = HitEnergy.CarVsCar(a, b, new Vector3(-1, 0, 0), new Vector3(1, 0, 0));
        Assert.Equal(HitAttacker.A, r.Attacker);
        Assert.True(r.Energy > 0);
    }

    [Fact]
    public void Separating_contact_has_no_energy()
    {
        var a = Car(new Vector3(0, 0, 5), new Vector3(0, 0, 5));
        var b = Car(new Vector3(0, 0, -5), Vector3.Zero);
        var r = HitEnergy.CarVsCar(a, b, new Vector3(0, 0, 2.5f), new Vector3(0, 0, -1));
        Assert.Equal(0f, r.Energy);
        Assert.Equal(HitAttacker.None, r.Attacker);
    }

    [Fact]
    public void Spinning_body_adds_point_velocity()
    {
        // A stationary car spinning at 2 rad/s about Y; contact point 2 m to its right moves at 4 m/s along -Z.
        var a = new BodyState(Vector3.Zero, new Vector3(0, 2, 0), Vector3.Zero, 1400f);
        Assert.Equal(-4f, a.PointVelocity(new Vector3(2, 0, 0)).Z, 3);
    }

    [Fact]
    public void Wall_hit_uses_full_mass()
    {
        var car = Car(new Vector3(0, 0, -10), Vector3.Zero, 1000f);
        var r = HitEnergy.CarVsStatic(car, new Vector3(0, 0, -2), new Vector3(0, 0, -1));
        Assert.Equal(50_000f, r.Energy, 1);
    }
}

public class DamageTests
{
    private static readonly Vector3 Half = new(0.9f, 0.7f, 2.3f);
    private static readonly RulesConfig Std = RulesConfig.Standard;

    [Theory]
    [InlineData(0f, -2.2f, DamageZone.Front)]
    [InlineData(0f, 2.2f, DamageZone.Rear)]
    [InlineData(0.9f, 0f, DamageZone.Right)]
    [InlineData(-0.9f, 0.3f, DamageZone.Left)]
    public void Zone_classification(float x, float z, DamageZone expected) =>
        Assert.Equal(expected, DamageMath.ZoneFromLocalPoint(new Vector3(x, 0, z), Half).Primary);

    [Fact]
    public void Corner_hits_spill_into_the_neighbour_zone()
    {
        var split = DamageMath.ZoneFromLocalPoint(new Vector3(0.8f, 0, -2.25f), Half);
        Assert.Equal(DamageZone.Front, split.Primary);
        Assert.Equal(DamageZone.Right, split.Secondary);
        Assert.True(split.SecondaryWeight > 0.3f);

        var centre = DamageMath.ZoneFromLocalPoint(new Vector3(0, 0, -2.2f), Half);
        Assert.Equal(0f, centre.SecondaryWeight);
    }

    [Fact]
    public void Rear_is_much_tougher_than_front()
    {
        float front = DamageMath.DamageFor(Std, 60_000f, DamageZone.Front, 1f);
        float rear = DamageMath.DamageFor(Std, 60_000f, DamageZone.Rear, 1f);
        Assert.Equal(2.5f, front / rear, 3);
    }

    [Fact]
    public void Small_hits_cause_no_damage()
    {
        Assert.Equal(0f, DamageMath.DamageFor(Std, Std.DamageEnergyThreshold * 0.9f, DamageZone.Front, 1f));
    }

    [Fact]
    public void Destroyed_front_wrecks_the_car()
    {
        var state = new DamageState();
        var front = new ZoneSplit(DamageZone.Front, DamageZone.Left, 0f);
        int hits = 0;
        while (!state.IsWrecked && hits < 1000)
        {
            state.ApplyHit(Std, 150_000f, front, 1f);
            hits++;
        }
        Assert.True(state.IsWrecked);
        Assert.Equal(0f, state.Health(DamageZone.Front));
        Assert.Equal(0f, state.ApplyHit(Std, 150_000f, front, 1f));
    }

    [Fact]
    public void Destroyed_rear_alone_does_not_wreck()
    {
        var state = new DamageState();
        var rear = new ZoneSplit(DamageZone.Rear, DamageZone.Left, 0f);
        for (int i = 0; i < 500; i++) state.ApplyHit(Std, 150_000f, rear, 1f);
        Assert.Equal(0f, state.Health(DamageZone.Rear));
        Assert.False(state.IsWrecked);
    }

    [Fact]
    public void Pit_repair_restores_zones_but_never_lost_wheels()
    {
        var state = new DamageState();
        state.ApplyHit(Std, 120_000f, new ZoneSplit(DamageZone.Left, DamageZone.Front, 0f), 1f);
        float damaged = state.Health(DamageZone.Left);
        state.Repair(DamageZone.Left, 30f);
        Assert.True(state.Health(DamageZone.Left) > damaged);

        state.DamageWheel(Wheel.RearLeft, 200f, wheelLossEnabled: true);
        Assert.True(state.IsWheelLost(Wheel.RearLeft));
        Assert.Equal(1, state.WheelsLost);
    }
}

public class WreckAttributionTests
{
    [Fact]
    public void Most_damage_in_window_gets_the_wreck()
    {
        var w = new WreckAttribution(5f);
        w.RecordDamage(1, 0.0, 30f);
        w.RecordDamage(2, 1.0, 20f);
        w.RecordDamage(2, 2.0, 15f);
        Assert.Equal(2, w.ResolveWrecker(3.0));
    }

    [Fact]
    public void Old_damage_does_not_count()
    {
        var w = new WreckAttribution(5f);
        w.RecordDamage(1, 0.0, 80f);
        w.RecordDamage(2, 6.0, 5f);
        Assert.Equal(2, w.ResolveWrecker(6.5));
        Assert.Null(new WreckAttribution(5f).ResolveWrecker(1.0));
    }
}
