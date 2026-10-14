using System;
using System.Numerics;

namespace DestructionRacing.Rules;

/// <summary>The four zones of the DD damage diagram.</summary>
public enum DamageZone
{
    Front,
    Rear,
    Left,
    Right,
}

public enum Wheel
{
    FrontLeft,
    FrontRight,
    RearLeft,
    RearRight,
}

/// <summary>A hit split over a primary zone and (on corner hits) a neighbouring zone.</summary>
public readonly record struct ZoneSplit(DamageZone Primary, DamageZone Secondary, float SecondaryWeight);

/// <summary>Damage math (plan §6.1). Car-local axes follow Godot: forward = -Z, right = +X.</summary>
public static class DamageMath
{
    /// <summary>Classifies a car-local contact point into the DD zones, with spill-over on corners.</summary>
    public static ZoneSplit ZoneFromLocalPoint(Vector3 localPoint, Vector3 halfExtents)
    {
        float nx = localPoint.X / MathF.Max(halfExtents.X, 0.01f);
        float nz = localPoint.Z / MathF.Max(halfExtents.Z, 0.01f);
        float ax = MathF.Abs(nx);
        float az = MathF.Abs(nz);

        DamageZone longitudinal = nz < 0f ? DamageZone.Front : DamageZone.Rear;
        DamageZone lateral = nx > 0f ? DamageZone.Right : DamageZone.Left;

        bool longitudinalPrimary = az >= ax;
        float ratio = longitudinalPrimary ? ax / MathF.Max(az, 1e-4f) : az / MathF.Max(ax, 1e-4f);
        // Spill-over only near the corners: ratio 0.6 → 0, ratio 1.0 → 0.5.
        float secondaryWeight = Math.Clamp((ratio - 0.6f) / 0.4f, 0f, 1f) * 0.5f;

        return longitudinalPrimary
            ? new ZoneSplit(longitudinal, lateral, secondaryWeight)
            : new ZoneSplit(lateral, longitudinal, secondaryWeight);
    }

    public static float ZoneToughness(RulesConfig rules, DamageZone zone) => zone switch
    {
        DamageZone.Front => rules.FrontToughness,
        DamageZone.Rear => rules.RearToughness,
        _ => rules.SideToughness,
    };

    /// <summary>Health points of damage for a hit of the given energy on a zone.</summary>
    public static float DamageFor(RulesConfig rules, float energy, DamageZone zone, float carToughness)
    {
        float excess = energy - rules.DamageEnergyThreshold;
        if (excess <= 0f) return 0f;
        float toughness = ZoneToughness(rules, zone) * MathF.Max(carToughness, 0.1f);
        return rules.DamageScale * MathF.Pow(excess, rules.DamageExponent) / toughness;
    }
}

/// <summary>
/// Health of one car (0–100 per zone and wheel). The primary wreck rule of both games:
/// a destroyed front means the radiator is gone and the car is wrecked.
/// </summary>
public sealed class DamageState
{
    public const float MaxHealth = 100f;

    private readonly float[] _zones = { MaxHealth, MaxHealth, MaxHealth, MaxHealth };
    private readonly float[] _wheels = { MaxHealth, MaxHealth, MaxHealth, MaxHealth };
    private readonly bool[] _wheelLost = new bool[4];

    public bool IsWrecked { get; private set; }

    public float Health(DamageZone zone) => _zones[(int)zone];
    public float WheelHealth(Wheel wheel) => _wheels[(int)wheel];
    public bool IsWheelLost(Wheel wheel) => _wheelLost[(int)wheel];

    public int WheelsLost
    {
        get
        {
            int n = 0;
            foreach (bool lost in _wheelLost) if (lost) n++;
            return n;
        }
    }

    /// <summary>Applies a hit and returns the total damage dealt (for wreck attribution).</summary>
    public float ApplyHit(RulesConfig rules, float energy, ZoneSplit split, float carToughness)
    {
        if (IsWrecked) return 0f;
        float primary = DamageMath.DamageFor(rules, energy, split.Primary, carToughness) * (1f - split.SecondaryWeight);
        float secondary = DamageMath.DamageFor(rules, energy, split.Secondary, carToughness) * split.SecondaryWeight;
        float dealt = Damage(split.Primary, primary) + Damage(split.Secondary, secondary);
        if (_zones[(int)DamageZone.Front] <= 0f) IsWrecked = true;
        return dealt;
    }

    public float DamageWheel(Wheel wheel, float amount, bool wheelLossEnabled)
    {
        int i = (int)wheel;
        if (_wheelLost[i] || amount <= 0f) return 0f;
        float before = _wheels[i];
        _wheels[i] = MathF.Max(0f, before - amount);
        if (_wheels[i] <= 0f && wheelLossEnabled) _wheelLost[i] = true;
        return before - _wheels[i];
    }

    /// <summary>Pit repair of one zone. Lost wheels are never restored.</summary>
    public void Repair(DamageZone zone, float amount)
    {
        if (IsWrecked) return;
        int i = (int)zone;
        _zones[i] = MathF.Min(MaxHealth, _zones[i] + amount);
    }

    /// <summary>Marks the car wrecked for reasons other than the front (e.g. immobilised, ✱ A9).</summary>
    public void ForceWreck() => IsWrecked = true;

    private float Damage(DamageZone zone, float amount)
    {
        if (amount <= 0f) return 0f;
        int i = (int)zone;
        float before = _zones[i];
        _zones[i] = MathF.Max(0f, before - amount);
        return before - _zones[i];
    }
}
