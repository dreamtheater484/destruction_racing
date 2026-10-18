using System;
using System.Collections.Generic;
using DestructionRacing.Cameras;
using DestructionRacing.Rules;
using DestructionRacing.UI;
using DestructionRacing.Vehicles;

namespace DestructionRacing.Audio;

/// <summary>What <see cref="EventAudio"/> listens to: an event with cars, a player, a referee and a crowd (the derby, the races).</summary>
public interface IEventSound
{
    /// <summary>The event's cars (a List, not an interface: every tick's foreach would box its enumerator).</summary>
    List<Car> Cars { get; }
    Car PlayerCar { get; }
    bool PlayerOut { get; }
    double Time { get; }
    CameraRig? Rig { get; }
    EventHud? Hud { get; }
    /// <summary>The crowd's excitement 0..1 (the bed swells into cheering).</summary>
    float CrowdExcitement { get; }

    /// <summary>Every measured hit (car-car and car-wall), after the referee saw it.</summary>
    event Action<Impact>? ImpactHappened;
    /// <summary>Every referee event (hits, spins, damage, wheels lost, wrecks, bonuses).</summary>
    event Action<RefereeEvent>? RefereeEventRaised;
    /// <summary>The countdown ticks: 3, 2, 1.</summary>
    event Action<int>? CountdownTick;
    /// <summary>GO: the countdown ended.</summary>
    event Action? Started;
    /// <summary>The music's intensity 0..3 (the derby: cars left; a race: the laps), and the final sting at 3.</summary>
    event Action<int>? IntensityChanged;
    /// <summary>The event is over (results).</summary>
    event Action? Ended;
}
