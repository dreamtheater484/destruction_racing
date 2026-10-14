using DestructionRacing.Core;
using Godot;

namespace DestructionRacing.Vehicles;

/// <summary>
/// Keyboard driver with the digital-steering filter (plan §14.3): steering ramps in, returns to centre
/// faster than it ramps in, and Fast Steer raises the rate. Throttle ramps slightly.
/// </summary>
public sealed class PlayerInput : ICarInput
{
    public float SteerRampIn { get; set; } = 4.5f;      // per second
    public float SteerReturn { get; set; } = 8f;        // per second
    public float SteerCounter { get; set; } = 10f;      // when pressing the opposite way
    public float FastSteerRateScale { get; set; } = 1.7f;
    public float ThrottleRamp { get; set; } = 7f;

    private float _steer;
    private float _throttle;

    public CarControls GetControls(Car car, float delta)
    {
        bool left = Input.IsActionPressed(Controls.SteerLeft);
        bool right = Input.IsActionPressed(Controls.SteerRight);
        bool fast = Input.IsActionPressed(Controls.FastSteer);
        float target = (right ? 1f : 0f) - (left ? 1f : 0f);

        float rate;
        if (target == 0f) rate = SteerReturn;
        else if (_steer != 0f && Mathf.Sign(target) != Mathf.Sign(_steer)) rate = SteerCounter;
        else rate = SteerRampIn;
        if (fast) rate *= FastSteerRateScale;
        _steer = Mathf.MoveToward(_steer, target, rate * delta);

        float throttleTarget = Input.IsActionPressed(Controls.Accelerate) ? 1f : 0f;
        _throttle = Mathf.MoveToward(_throttle, throttleTarget, ThrottleRamp * delta);

        return new CarControls
        {
            Throttle = _throttle,
            Brake = Input.IsActionPressed(Controls.Brake) ? 1f : 0f,
            Steer = _steer,
            Reverse = Input.IsActionPressed(Controls.Reverse),
            FastSteer = fast,
            TurnAround = Input.IsActionPressed(Controls.TurnAround),
        };
    }
}
