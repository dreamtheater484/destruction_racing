using System.Collections.Generic;
using DestructionRacing.Tracks;
using DestructionRacing.Vehicles;

namespace DestructionRacing.AI;

/// <summary>What a racing AI may know about the race (provided by the mode).</summary>
public interface IRaceView
{
    IReadOnlyList<Car> Cars { get; }
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
    /// <summary>Back-marker catch-up speed factor (1 = none; plan §4.2, off in Purist).</summary>
    float CatchUp(Car car);
}
