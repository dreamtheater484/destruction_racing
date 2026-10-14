using System;
using System.Numerics;

namespace DestructionRacing.Rules;

/// <summary>A body's state cached before the physics solve (plan §5.3).</summary>
public readonly record struct BodyState(Vector3 LinearVelocity, Vector3 AngularVelocity, Vector3 CenterOfMass, float Mass)
{
    public Vector3 PointVelocity(Vector3 worldPoint) =>
        LinearVelocity + Vector3.Cross(AngularVelocity, worldPoint - CenterOfMass);
}

public enum HitAttacker
{
    None,
    A,
    B,
    Both,
}

public readonly record struct HitResult(float ClosingSpeed, float Energy, HitAttacker Attacker);

/// <summary>
/// Computes impact energy from pre-solve velocities instead of the physics engine's impulse
/// estimate, which is unreliable in multi-body pileups.
/// </summary>
public static class HitEnergy
{
    /// <param name="normalAtoB">Unit contact normal pointing from body A towards body B.</param>
    /// <param name="headOnRatio">If both cars drive into each other and min/max ≥ this ratio, both are attackers.</param>
    public static HitResult CarVsCar(in BodyState a, in BodyState b, Vector3 contactPoint, Vector3 normalAtoB, float headOnRatio = 0.75f)
    {
        Vector3 va = a.PointVelocity(contactPoint);
        Vector3 vb = b.PointVelocity(contactPoint);
        float closing = Vector3.Dot(va - vb, normalAtoB);
        if (closing <= 0f) return new HitResult(0f, 0f, HitAttacker.None);

        float reducedMass = a.Mass * b.Mass / (a.Mass + b.Mass);
        float energy = 0.5f * reducedMass * closing * closing;

        float aToward = Vector3.Dot(va, normalAtoB);
        float bToward = Vector3.Dot(vb, -normalAtoB);
        HitAttacker attacker;
        if (aToward > 0f && bToward > 0f)
        {
            float ratio = MathF.Min(aToward, bToward) / MathF.Max(aToward, bToward);
            attacker = ratio >= headOnRatio ? HitAttacker.Both : (aToward > bToward ? HitAttacker.A : HitAttacker.B);
        }
        else
        {
            attacker = aToward >= bToward ? HitAttacker.A : HitAttacker.B;
        }
        return new HitResult(closing, energy, attacker);
    }

    /// <summary>Impact of a car against a static object (wall, barrier); the static side has infinite mass.</summary>
    /// <param name="normalIntoWall">Unit normal pointing from the car into the wall.</param>
    public static HitResult CarVsStatic(in BodyState car, Vector3 contactPoint, Vector3 normalIntoWall)
    {
        float closing = Vector3.Dot(car.PointVelocity(contactPoint), normalIntoWall);
        if (closing <= 0f) return new HitResult(0f, 0f, HitAttacker.None);
        return new HitResult(closing, 0.5f * car.Mass * closing * closing, HitAttacker.A);
    }
}
