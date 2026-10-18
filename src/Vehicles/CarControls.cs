namespace DestructionRacing.Vehicles;

/// <summary>One frame of driver intent. Steer is normalised: -1 = full left, +1 = full right.</summary>
public struct CarControls
{
    public float Throttle;
    public float Brake;
    public float Steer;
    /// <summary>Instant reverse (the dedicated Reverse key, DD1 PS).</summary>
    public bool Reverse;
    /// <summary>DD2 Fast Steer: faster steering rate and more lock.</summary>
    public bool FastSteer;
    /// <summary>DD1 turn-around: Space plus gas or brake spins the car round at low speed.</summary>
    public bool TurnAround;
    /// <summary>Brake without DD2's brake-to-reverse at a standstill (parked, countdown, wrecked).</summary>
    public bool Hold;

    public static CarControls Idle => new() { Brake = 0f };
    public static CarControls Parked => new() { Brake = 1f, Hold = true };
}

/// <summary>Anything that drives a car: the player, the AI, a scenario autopilot.</summary>
public interface ICarInput
{
    CarControls GetControls(Car car, float delta);
}

/// <summary>A car that stands still with the brake on (the Practice targets in the Bowl).</summary>
public sealed class ParkedDriver : ICarInput
{
    public CarControls GetControls(Car car, float delta) => CarControls.Parked;
}
