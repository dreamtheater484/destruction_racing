using DestructionRacing.Core;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Cameras;

/// <summary>
/// The 4 in-game views of DD (plan §15.4) plus look-back. The camera always stays upright.
/// Chase framings are measured from DD1/DD2 PC gameplay video (docs/reference_feel.md).
/// </summary>
public partial class CameraRig : Camera3D
{
    public enum View
    {
        /// <summary>DD2 default: close, low and level.</summary>
        Chase,
        /// <summary>DD1 default: further back, slightly looking down.</summary>
        ChaseFar,
        Bumper,
        InCar,
    }

    public Car? Target { get; set; }
    public View Mode { get; set; } = View.Chase;

    private float _smoothedHeight;
    private Vector3 _smoothedHeading = Vector3.Forward;
    private bool _initialised;
    private float _shake;
    private float _fovPunch;
    private float _baseFov = 55f;

    public override void _Ready()
    {
        Current = true;
        Near = 0.05f;
        Far = 4000f;
    }

    /// <summary>Adds camera shake (0..1), e.g. from big hits.</summary>
    public void AddShake(float amount) => _shake = Mathf.Min(1f, _shake + amount);

    /// <summary>A quick FOV kick (0..1) for big hits (plan §11).</summary>
    public void Punch(float amount) => _fovPunch = Mathf.Min(1f, Mathf.Max(_fovPunch, amount));

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

        // Chase views stay at a fixed distance (a world-space position lag would pull the camera back at speed);
        // only the heading swing and the height are smoothed. The car origin is ~0.6 m above the ground.
        _smoothedHeight = _initialised ? Mathf.Lerp(_smoothedHeight, xf.Origin.Y, 1f - Mathf.Exp(-8f * dt)) : xf.Origin.Y;
        Vector3 anchor = new(xf.Origin.X, _smoothedHeight, xf.Origin.Z);
        _initialised = true;

        Vector3 position;
        Vector3 lookAt;
        float fov;
        switch (Mode)
        {
            case View.Chase:
                // DD2 PC: ~6.5 m behind the car centre, ~2 m above the ground, level, vfov ~55°.
                position = anchor - heading * 6.5f + Vector3.Up * 1.45f;
                lookAt = anchor + heading * 10f + Vector3.Up * 1.25f;
                fov = 55f;
                break;
            case View.Bumper:
                position = xf * new Vector3(0f, 0.25f, lookBack ? 2.45f : -2.45f);
                lookAt = position + (lookBack ? xf.Basis.Z : -xf.Basis.Z) * 10f;
                fov = 72f;
                break;
            case View.InCar:
                position = xf * new Vector3(-0.38f, 0.72f, lookBack ? 0.2f : 0.35f);
                lookAt = position + (lookBack ? xf.Basis.Z : -xf.Basis.Z) * 10f;
                fov = 70f;
                break;
            default:
                // DD1 PC: ~9 m behind the car centre, ~2 m above the ground, pitched ~2.5° down, vfov ~55°.
                position = anchor - heading * 9.0f + Vector3.Up * 1.4f;
                lookAt = anchor + heading * 6f + Vector3.Up * 0.75f;
                fov = 55f;
                break;
        }

        bool attached = Mode is View.Bumper or View.InCar;

        _baseFov = Mathf.Lerp(_baseFov, fov + Mathf.Min(speed * 0.15f, 6f), 1f - Mathf.Exp(-4f * dt));
        _fovPunch = Mathf.MoveToward(_fovPunch, 0f, dt * 4f);
        Fov = _baseFov + 9f * _fovPunch * _fovPunch;
        Vector3 shake = Vector3.Zero;
        if (_shake > 0.001f)
        {
            shake = new Vector3(GD.Randf() - 0.5f, GD.Randf() - 0.5f, GD.Randf() - 0.5f) * _shake * 0.35f;
            _shake = Mathf.MoveToward(_shake, 0f, dt * 2.5f);
        }

        Vector3 up = attached ? xf.Basis.Y.Lerp(Vector3.Up, 0.6f).Normalized() : Vector3.Up;
        GlobalTransform = new Transform3D(Basis.Identity, position + shake).LookingAt(lookAt, up);
    }
}
