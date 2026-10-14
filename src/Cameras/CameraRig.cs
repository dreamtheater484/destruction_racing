using DestructionRacing.Core;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Cameras;

/// <summary>The 4 in-game views of DD (plan §15.4) plus look-back. The camera always stays upright.</summary>
public partial class CameraRig : Camera3D
{
    public enum View
    {
        ChaseFar,
        ChaseNear,
        Bumper,
        InCar,
    }

    public Car? Target { get; set; }
    public View Mode { get; private set; } = View.ChaseFar;

    private Vector3 _smoothedPosition;
    private Vector3 _smoothedHeading = Vector3.Forward;
    private bool _initialised;
    private float _shake;

    public override void _Ready()
    {
        Current = true;
        Near = 0.05f;
        Far = 2000f;
    }

    /// <summary>Adds camera shake (0..1), e.g. from big hits.</summary>
    public void AddShake(float amount) => _shake = Mathf.Min(1f, _shake + amount);

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed(Controls.CameraNext) && !e.IsEcho())
        {
            Mode = (View)(((int)Mode + 1) % 4);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (Target == null || !IsInstanceValid(Target)) return;
        float dt = (float)delta;
        var xf = Target.GlobalTransform;
        Vector3 carFwd = -xf.Basis.Z;
        Vector3 flatFwd = new Vector3(carFwd.X, 0f, carFwd.Z);
        if (flatFwd.LengthSquared() < 1e-4f) flatFwd = _smoothedHeading;
        flatFwd = flatFwd.Normalized();
        bool lookBack = Input.IsActionPressed(Controls.LookBack);

        // Heading follows the car body, not the velocity, like DD's chase cam; smoothed.
        _smoothedHeading = _initialised ? _smoothedHeading.Slerp(flatFwd, 1f - Mathf.Exp(-6f * dt)).Normalized() : flatFwd;
        Vector3 heading = lookBack ? -_smoothedHeading : _smoothedHeading;
        float speed = Target.LinearVelocity.Length();

        Vector3 desired;
        Vector3 lookAt;
        float fov;
        switch (Mode)
        {
            case View.ChaseNear:
                desired = xf.Origin - heading * 6.2f + Vector3.Up * 2.1f;
                lookAt = xf.Origin + heading * 4f + Vector3.Up * 0.8f;
                fov = 66f;
                break;
            case View.Bumper:
                desired = xf * new Vector3(0f, 0.25f, lookBack ? 2.45f : -2.45f);
                lookAt = desired + (lookBack ? xf.Basis.Z : -xf.Basis.Z) * 10f;
                fov = 72f;
                break;
            case View.InCar:
                desired = xf * new Vector3(-0.38f, 0.72f, lookBack ? 0.2f : 0.35f);
                lookAt = desired + (lookBack ? xf.Basis.Z : -xf.Basis.Z) * 10f;
                fov = 70f;
                break;
            default:
                desired = xf.Origin - heading * 9.5f + Vector3.Up * 3.4f;
                lookAt = xf.Origin + heading * 5f + Vector3.Up * 0.7f;
                fov = 62f;
                break;
        }

        bool attached = Mode is View.Bumper or View.InCar;
        if (!_initialised || attached)
        {
            _smoothedPosition = desired;
            _initialised = true;
        }
        else
        {
            _smoothedPosition = _smoothedPosition.Lerp(desired, 1f - Mathf.Exp(-10f * dt));
        }

        Fov = Mathf.Lerp(Fov, fov + Mathf.Min(speed * 0.35f, 14f), 1f - Mathf.Exp(-4f * dt));
        Vector3 shake = Vector3.Zero;
        if (_shake > 0.001f)
        {
            shake = new Vector3(GD.Randf() - 0.5f, GD.Randf() - 0.5f, GD.Randf() - 0.5f) * _shake * 0.35f;
            _shake = Mathf.MoveToward(_shake, 0f, dt * 2.5f);
        }

        Vector3 up = attached ? xf.Basis.Y.Lerp(Vector3.Up, 0.6f).Normalized() : Vector3.Up;
        GlobalTransform = new Transform3D(Basis.Identity, _smoothedPosition + shake).LookingAt(lookAt, up);
    }
}
