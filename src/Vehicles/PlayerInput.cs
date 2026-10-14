using DestructionRacing.Core;
using Godot;

namespace DestructionRacing.Vehicles;

/// <summary>
/// Keyboard driver with the digital-steering filter (plan §14.3): steering ramps in, returns to centre
/// faster than it ramps in, and Fast Steer raises the rate. Throttle ramps slightly.
/// </summary>
public sealed class PlayerInput : ICarInput
{
    /// <summary>Which keys are held, by action name. Defaults to the keyboard; the PlayerProxy bot injects its own.</summary>
    public System.Func<string, bool> IsHeld { get; set; } = action => Input.IsActionPressed(action);

    public float SteerRampIn { get; set; } = 4.5f;      // per second
    public float SteerReturn { get; set; } = 8f;        // per second
    public float SteerCounter { get; set; } = 10f;      // when pressing the opposite way
    public float FastSteerRateScale { get; set; } = 1.7f;
    public float ThrottleRamp { get; set; } = 7f;

    private float _steer;
    private float _throttle;

    public CarControls GetControls(Car car, float delta)
    {
        bool left = IsHeld(Controls.SteerLeft);
        bool right = IsHeld(Controls.SteerRight);
        bool fast = IsHeld(Controls.FastSteer);
        float target = (right ? 1f : 0f) - (left ? 1f : 0f);

        float rate;
        if (target == 0f) rate = SteerReturn;
        else if (_steer != 0f && Mathf.Sign(target) != Mathf.Sign(_steer)) rate = SteerCounter;
        else rate = SteerRampIn;
        if (fast) rate *= FastSteerRateScale;
        _steer = Mathf.MoveToward(_steer, target, rate * delta);

        float throttleTarget = IsHeld(Controls.Accelerate) ? 1f : 0f;
        _throttle = Mathf.MoveToward(_throttle, throttleTarget, ThrottleRamp * delta);

        return new CarControls
        {
            Throttle = _throttle,
            Brake = IsHeld(Controls.Brake) ? 1f : 0f,
            Steer = _steer,
            Reverse = IsHeld(Controls.Reverse),
            FastSteer = fast,
            TurnAround = IsHeld(Controls.TurnAround),
        };
    }
}
