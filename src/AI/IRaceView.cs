using System.Collections.Generic;
using DestructionRacing.Tracks;
using DestructionRacing.Vehicles;

namespace DestructionRacing.AI;

/// <summary>What a racing AI may know about the race (provided by the mode).</summary>
public interface IRaceView
{
    /// <summary>The event's cars (a List, not an interface: every tick's foreach would box its enumerator).</summary>
    List<Car> Cars { get; }
    bool IsRunning(Car car);
    TrackPath Track { get; }
    /// <summary>Seconds since GO.</summary>
    double Time { get; }
    Car? Player { get; }
    /// <summary>The car in 1st place (the ×2 target).</summary>
    int? LeaderId { get; }
    /// <summary>A car's lap position (0..Length) and lateral offset (+ = right), updated every step.</summary>
    (float S, float Lateral) TrackPos(Car car);
    /// <summary>Lap positions of the crossover passes (empty for a track without one).</summary>
    IReadOnlyList<float> CrossoverPasses { get; }
    /// <summary>A car passing through the pits: no contact with it, so it is neither traffic nor a target.</summary>
    bool IsGhost(Car car) => false;
    /// <summary>The leader's lap (1 on the first lap).</summary>
    int LeaderLap => 1;
    /// <summary>Back-marker catch-up speed factor (1 = none; plan §4.2, off in Purist).</summary>
    float CatchUp(Car car);
}
