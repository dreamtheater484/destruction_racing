using System.Collections.Generic;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.AI;

/// <summary>What a derby AI may know about the event (provided by the mode).</summary>
public interface IArenaView
{
    IReadOnlyList<Car> Cars { get; }
    bool IsRunning(Car car);
    /// <summary>Points leader (×2 target), if any.</summary>
    int? LeaderId { get; }
    /// <summary>Seconds since GO.</summary>
    double Time { get; }
    /// <summary>The Bowl's inner wall as an ellipse centred on the origin (x and z semi-axes).</summary>
    Vector2 ArenaSemiAxes { get; }
    Car? Player { get; }
    /// <summary>Max AI cars that may target the player at once (plan §10.4).</summary>
    int PlayerAttentionBudget { get; }
    /// <summary>How many AI cars other than <paramref name="asker"/> currently target <paramref name="target"/>.</summary>
    int CountTargeting(Car target, Car asker);
    /// <summary>Who last hit <paramref name="victim"/> and when (for revenge).</summary>
    (Car? Attacker, double Time) LastHitBy(Car victim);
    /// <summary>Extra aggression from the anti-stall director (0 = none).</summary>
    float AggressionBoost(Car car);
}
