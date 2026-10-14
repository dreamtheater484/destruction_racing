using System;
using Godot;

namespace DestructionRacing.Vehicles;

/// <summary>
/// Arcade raycast vehicle on a Jolt rigid body (plan §15.3): spring-damper suspension per wheel,
/// slip-angle tyre curve with a friction circle, power-limited drive, DD-style reverse and turn-around.
/// The grey-box visuals are built in code; the final car model replaces them in M2.
/// </summary>
public partial class Car : RigidBody3D
{
    public const uint WorldLayer = 1;
    public const uint CarLayer = 2;

    [Export] public Color PaintColor { get; set; } = new(0.80f, 0.08f, 0.06f);
    [Export] public Color RoofColor { get; set; } = new(0.95f, 0.80f, 0.10f);
    [Export] public int Number { get; set; } = 7;

    public CarTuning Tuning { get; private set; } = new();
    public ICarInput? Driver { get; set; }

    // Telemetry for the HUD, cameras and debug panel.
    public float ForwardSpeed { get; private set; }
    public float SpeedKmh => LinearVelocity.Length() * 3.6f;
    public float SteerAngleDeg { get; private set; }
    public float BodySlipDeg { get; private set; }
    public int Gear { get; private set; } = 1;
    public float Rpm { get; private set; } = 900f;
    public int WheelsGrounded { get; private set; }
    public bool TurningAround { get; private set; }
    public CarControls LastControls { get; private set; }
    public float DriveForceRequested { get; private set; }
    public float DriveForceApplied { get; private set; }
    public float WheelLoad(int i) => _wheels[i].Load;
    public float RearSlipDeg => Mathf.RadToDeg(0.5f * (_wheels[2].SlipAngle + _wheels[3].SlipAngle));
    public float FrontSlipDeg => Mathf.RadToDeg(0.5f * (_wheels[0].SlipAngle + _wheels[1].SlipAngle));

    private static readonly float[] GearTopSpeed = { 0f, 16f, 28f, 40f, 54f, 72f };

    private readonly Wheel[] _wheels = new Wheel[4];
    private Godot.Collections.Array<Rid> _exclude = new();
    private float _upsideDownTime;

    private sealed class Wheel
    {
        public Vector3 Mount;
        public bool Front;
        public bool Left;
        public float Compression;
        public float PrevCompression;
        public bool Grounded;
        public Vector3 ContactPoint;
        public Vector3 ContactNormal = Vector3.Up;
        public float Load;
        public float SlipAngle;
        public float Spin;
        public Node3D Visual = null!;
    }

    public override void _Ready()
    {
        CollisionLayer = CarLayer;
        CollisionMask = WorldLayer | CarLayer;
        ContinuousCd = true;
        CanSleep = false;
        // Drag and rolling resistance are modelled explicitly; the engine's default damping would act as a hidden brake.
        LinearDampMode = DampMode.Replace;
        LinearDamp = 0f;
        AngularDampMode = DampMode.Replace;
        AngularDamp = 0.6f;
        _exclude = new Godot.Collections.Array<Rid> { GetRid() };

        BuildBody();
        for (int i = 0; i < 4; i++) _wheels[i] = new Wheel { Front = i < 2, Left = i % 2 == 0 };
        BuildWheelVisuals();
        ApplyTuning(Tuning);
    }

    public void ApplyTuning(CarTuning t)
    {
        Tuning = t;
        Mass = t.Mass;
        CenterOfMassMode = CenterOfMassModeEnum.Custom;
        CenterOfMass = new Vector3(0f, t.CenterOfMassHeight, 0f);
        const float w = 1.8f, h = 1.1f, l = 4.6f;
        Inertia = new Vector3(
            t.Mass / 12f * (h * h + l * l),
            t.Mass / 12f * (w * w + l * l) * t.YawInertiaScale,
            t.Mass / 12f * (w * w + h * h));

        for (int i = 0; i < 4; i++)
        {
            var wheel = _wheels[i];
            wheel.Mount = new Vector3(wheel.Left ? -t.HalfTrack : t.HalfTrack, t.MountHeight, wheel.Front ? t.FrontAxleZ : t.RearAxleZ);
        }
    }

    /// <summary>Puts the car back on its wheels at its current spot, keeping its heading.</summary>
    public void RightCar()
    {
        var fwd = -GlobalTransform.Basis.Z;
        fwd.Y = 0f;
        if (fwd.LengthSquared() < 1e-4f) fwd = Vector3.Forward;
        var pos = GlobalPosition + Vector3.Up * 1.2f;
        GlobalTransform = new Transform3D(Basis.LookingAt(fwd.Normalized(), Vector3.Up), pos);
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        _upsideDownTime = 0f;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        var t = Tuning;
        var xf = GlobalTransform;
        Vector3 up = xf.Basis.Y.Normalized();
        Vector3 fwd = -xf.Basis.Z.Normalized();
        Vector3 right = xf.Basis.X.Normalized();
        Vector3 comWorld = xf * CenterOfMass;
        Vector3 v = LinearVelocity;

        var c = Driver?.GetControls(this, dt) ?? CarControls.Parked;
        LastControls = c;

        ForwardSpeed = v.Dot(fwd);
        float lateralSpeed = v.Dot(right);
        BodySlipDeg = ForwardSpeed > 2f ? Mathf.RadToDeg(Mathf.Atan2(lateralSpeed, Mathf.Abs(ForwardSpeed))) : 0f;

        // Steering angle: speed-sensitive lock, Fast Steer, counter-steer assist.
        float speedT = Mathf.Clamp(Mathf.Abs(ForwardSpeed) / t.SteerSpeedForMin, 0f, 1f);
        float maxSteer = Mathf.Lerp(t.MaxSteerLowSpeed, t.MaxSteerHighSpeed, speedT) * (c.FastSteer ? t.FastSteerLockScale : 1f);
        float steerDeg = c.Steer * maxSteer;
        if (t.CounterSteerAssist > 0f && ForwardSpeed > 4f && Mathf.Abs(BodySlipDeg) > 4f)
            steerDeg += t.CounterSteerAssist * BodySlipDeg;
        steerDeg = Mathf.Clamp(steerDeg, -maxSteer * 1.25f, maxSteer * 1.25f);
        SteerAngleDeg = steerDeg;
        float steerRad = Mathf.DegToRad(steerDeg);

        // Drive, brake, reverse (DD2: holding brake at a standstill reverses; DD1 PS: dedicated reverse).
        float brake = c.Brake;
        float drive = 0f;
        bool reversing = false;
        if (c.Reverse)
        {
            if (ForwardSpeed > 1.5f) brake = MathF.Max(brake, 1f);
            else reversing = true;
        }
        else if (c.Brake > 0.1f && ForwardSpeed < 0.8f && c.Throttle < 0.1f)
        {
            reversing = true;
        }

        if (reversing)
        {
            brake = 0f;
            if (ForwardSpeed > -t.ReverseMaxSpeed) drive = -t.ReverseForce * (c.Reverse ? 1f : c.Brake);
        }
        else if (c.Throttle > 0f)
        {
            float speed = MathF.Max(MathF.Abs(ForwardSpeed), 1f);
            drive = c.Throttle * MathF.Min(t.MaxDriveForce, t.EnginePower / speed);
        }

        TurningAround = c.TurnAround && MathF.Abs(ForwardSpeed) < t.TurnAroundMaxSpeed
                        && (c.Throttle > 0.1f || c.Brake > 0.1f || c.Reverse);

        // Pass 1: suspension raycasts.
        var space = GetWorld3D().DirectSpaceState;
        float rayLength = t.RestLength + t.WheelRadius;
        int grounded = 0, drivenGrounded = 0;
        foreach (var w in _wheels)
        {
            Vector3 mountWorld = xf * w.Mount;
            var query = PhysicsRayQueryParameters3D.Create(mountWorld, mountWorld - up * rayLength, WorldLayer, _exclude);
            var hit = space.IntersectRay(query);
            if (hit.Count > 0)
            {
                w.Grounded = true;
                w.ContactPoint = (Vector3)hit["position"];
                w.ContactNormal = (Vector3)hit["normal"];
                w.Compression = Mathf.Clamp(rayLength - mountWorld.DistanceTo(w.ContactPoint), 0f, t.RestLength);
                grounded++;
                if (IsDriven(w)) drivenGrounded++;
            }
            else
            {
                w.Grounded = false;
                w.Compression = 0f;
                w.Load = 0f;
            }
        }
        WheelsGrounded = grounded;

        // Pass 2: suspension, anti-roll and tyre forces.
        DriveForceRequested = drive;
        DriveForceApplied = 0f;
        float antiRollFront = (_wheels[0].Compression - _wheels[1].Compression) * t.AntiRollFront;
        float antiRollRear = (_wheels[2].Compression - _wheels[3].Compression) * t.AntiRollRear;
        float cornerMass = t.Mass * 0.25f;

        foreach (var w in _wheels)
        {
            float compressionSpeed = (w.Compression - w.PrevCompression) / dt;
            w.PrevCompression = w.Compression;
            if (!w.Grounded) continue;

            float damper = compressionSpeed > 0f ? t.DamperCompression : t.DamperRebound;
            float antiRoll = w.Front ? antiRollFront : antiRollRear;
            float load = t.SpringRate * w.Compression + damper * compressionSpeed + (w.Left ? antiRoll : -antiRoll);
            load = MathF.Max(0f, load);
            w.Load = load;
            ApplyForce(up * load, w.ContactPoint - xf.Origin);

            // Tyre frame on the contact plane.
            Vector3 n = w.ContactNormal;
            Vector3 wheelFwd = w.Front ? fwd.Rotated(up, -steerRad) : fwd;
            wheelFwd = (wheelFwd - n * wheelFwd.Dot(n)).Normalized();
            Vector3 wheelSide = wheelFwd.Cross(n).Normalized();

            Vector3 pointVelocity = v + AngularVelocity.Cross(w.ContactPoint - comWorld);
            float vLong = pointVelocity.Dot(wheelFwd);
            float vLat = pointVelocity.Dot(wheelSide);
            float slip = Mathf.Atan2(vLat, MathF.Max(MathF.Abs(vLong), 1.5f));
            w.SlipAngle = slip;

            float grip = t.SurfaceGrip * (w.Front ? t.FrontGrip : t.RearGrip);
            if (!w.Front && TurningAround) grip *= t.TurnAroundRearGrip;
            float maxForce = grip * load;
            float maxLong = maxForce * t.LongitudinalGrip;

            float fy = -maxForce * TyreCurve(slip, t.TyreStiffness, t.TyreShape);
            if (v.Length() < 1.5f)
                fy = Mathf.Clamp(-vLat * cornerMass / dt * 0.5f, -maxForce, maxForce); // stop low-speed creep

            float fx = 0f;
            if (IsDriven(w) && drivenGrounded > 0)
            {
                float lateralUse = maxForce > 0f ? fy / maxForce : 0f;
                float available = maxLong * MathF.Sqrt(MathF.Max(0f, 1f - lateralUse * lateralUse));
                float wheelDrive = drive / drivenGrounded;
                fx += Mathf.Clamp(wheelDrive, -available * t.TractionControl, available * t.TractionControl);
                DriveForceApplied += fx;
            }
            if (brake > 0f)
            {
                float bias = w.Front ? t.BrakeFrontBias : 1f - t.BrakeFrontBias;
                float brakeForce = brake * t.BrakeForce * bias * 0.5f;
                float stopForce = MathF.Abs(vLong) * cornerMass / dt;
                fx -= MathF.Sign(vLong) * MathF.Min(brakeForce, stopForce);
            }

            // Friction ellipse: drive/brake may use LongitudinalGrip times the cornering grip.
            if (maxForce > 0f)
            {
                float ex = fx / maxLong, ey = fy / maxForce;
                float use = MathF.Sqrt(ex * ex + ey * ey);
                if (use > 1f)
                {
                    fx /= use;
                    fy /= use;
                }
            }

            // Lateral forces act partly above the contact patch to tame body roll (arcade).
            float comHeight = (comWorld - w.ContactPoint).Dot(up);
            Vector3 applyAt = w.ContactPoint + up * (comHeight * t.LateralForceHeight);
            ApplyForce(wheelFwd * fx + wheelSide * fy, applyAt - xf.Origin);

            w.Spin += vLong / t.WheelRadius * dt;
        }

        // Aero drag and rolling resistance.
        Vector3 flat = v - up * v.Dot(up);
        ApplyCentralForce(-flat * flat.Length() * t.DragCoefficient - flat * t.RollingResistance);

        // DD1 turn-around: yaw the car round at low speed.
        if (TurningAround && grounded >= 3)
        {
            float direction = c.Steer != 0f ? -MathF.Sign(c.Steer) : 1f;
            ApplyTorque(up * direction * t.TurnAroundTorque);
        }

        if (AngularVelocity.Length() > t.MaxAngularSpeed)
            AngularVelocity = AngularVelocity.Normalized() * t.MaxAngularSpeed;

        UpdateGearbox(reversing);
        UpdateUpsideDown(up, v, dt);
        UpdateWheelVisuals(steerRad);
    }

    private bool IsDriven(Wheel w) => Tuning.RearWheelDrive ? !w.Front : w.Front;

    /// <summary>Pacejka-lite lateral curve: rises to a peak, then falls off (slides stay controllable).</summary>
    private static float TyreCurve(float slip, float b, float c) => MathF.Sin(c * MathF.Atan(b * slip));

    private void UpdateGearbox(bool reversing)
    {
        if (reversing)
        {
            Gear = -1;
            Rpm = 900f + MathF.Min(MathF.Abs(ForwardSpeed) / Tuning.ReverseMaxSpeed, 1f) * 4000f;
            return;
        }
        if (Gear < 1) Gear = 1;
        float s = MathF.Abs(ForwardSpeed);
        if (Gear < 5 && s > GearTopSpeed[Gear] * 0.97f) Gear++;
        else if (Gear > 1 && s < GearTopSpeed[Gear - 1] * 0.8f) Gear--;
        float lo = Gear == 1 ? 0f : GearTopSpeed[Gear - 1] * 0.8f;
        float hi = GearTopSpeed[Gear];
        Rpm = 900f + Mathf.Clamp((s - lo) / (hi - lo), 0f, 1.1f) * 5600f;
    }

    /// <summary>DD2 rule (plan §6.0): a car on its roof or side is righted after a short penalty.</summary>
    private void UpdateUpsideDown(Vector3 up, Vector3 v, float dt)
    {
        if (up.Dot(Vector3.Up) < 0.35f && v.Length() < 3f)
        {
            _upsideDownTime += dt;
            if (_upsideDownTime > 2.5f) RightCar();
        }
        else
        {
            _upsideDownTime = 0f;
        }
    }

    // ---------------------------------------------------------------- grey-box visuals

    private void BuildBody()
    {
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.8f, 0.7f, 4.6f) } });
        AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(1.5f, 0.52f, 2.3f) },
            Position = new Vector3(0f, 0.61f, 0.25f),
        });

        var paint = new StandardMaterial3D { AlbedoColor = PaintColor, Metallic = 0.35f, Roughness = 0.32f, Clearcoat = 0.8f };
        var roof = new StandardMaterial3D { AlbedoColor = RoofColor, Roughness = 0.4f };
        var glass = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.07f, 0.09f), Metallic = 0.6f, Roughness = 0.08f };
        var trim = new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.18f, 0.18f), Roughness = 0.6f };
        var headlight = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.97f, 0.85f), EmissionEnabled = true, Emission = new Color(1f, 0.95f, 0.8f), EmissionEnergyMultiplier = 2f };
        var taillight = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.02f, 0.02f), EmissionEnabled = true, Emission = new Color(0.9f, 0.05f, 0.03f), EmissionEnergyMultiplier = 1.5f };

        AddBox(new Vector3(1.8f, 0.7f, 4.6f), Vector3.Zero, paint);
        AddBox(new Vector3(1.52f, 0.5f, 2.2f), new Vector3(0f, 0.6f, 0.25f), glass);
        AddBox(new Vector3(1.42f, 0.06f, 2.0f), new Vector3(0f, 0.88f, 0.28f), roof);
        AddBox(new Vector3(1.86f, 0.18f, 0.16f), new Vector3(0f, -0.18f, -2.33f), trim);
        AddBox(new Vector3(1.86f, 0.18f, 0.16f), new Vector3(0f, -0.18f, 2.33f), trim);
        foreach (float x in new[] { -0.62f, 0.62f })
        {
            AddBox(new Vector3(0.36f, 0.16f, 0.04f), new Vector3(x, 0.12f, -2.31f), headlight);
            AddBox(new Vector3(0.36f, 0.14f, 0.04f), new Vector3(x, 0.12f, 2.31f), taillight);
        }

        // Big DD-style racing numbers on the doors and the roof.
        AddNumber(new Vector3(-0.91f, 0.02f, 0.1f), new Vector3(0f, -90f, 0f));
        AddNumber(new Vector3(0.91f, 0.02f, 0.1f), new Vector3(0f, 90f, 0f));
        AddNumber(new Vector3(0f, 0.92f, 0.28f), new Vector3(-90f, 0f, 0f));
    }

    private void AddBox(Vector3 size, Vector3 position, Material material) =>
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Position = position });

    private void AddNumber(Vector3 position, Vector3 rotationDegrees) =>
        AddChild(new Label3D
        {
            Text = Number.ToString(),
            FontSize = 220,
            PixelSize = 0.0025f,
            OutlineSize = 28,
            Modulate = Colors.White,
            OutlineModulate = new Color(0.05f, 0.05f, 0.05f),
            Position = position,
            RotationDegrees = rotationDegrees,
            Shaded = true,
            DoubleSided = false,
        });

    private void BuildWheelVisuals()
    {
        var tyre = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.05f, 0.05f), Roughness = 0.9f };
        var hub = new StandardMaterial3D { AlbedoColor = new Color(0.7f, 0.7f, 0.72f), Metallic = 0.9f, Roughness = 0.3f };
        foreach (var w in _wheels)
        {
            var pivot = new Node3D();
            AddChild(pivot);
            var spinner = new Node3D { Name = "Spin" };
            pivot.AddChild(spinner);
            spinner.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.33f, BottomRadius = 0.33f, Height = 0.22f, RadialSegments = 24 },
                MaterialOverride = tyre,
                RotationDegrees = new Vector3(0f, 0f, 90f),
            });
            spinner.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.2f, Height = 0.225f, RadialSegments = 12 },
                MaterialOverride = hub,
                RotationDegrees = new Vector3(0f, 0f, 90f),
            });
            w.Visual = pivot;
        }
    }

    private void UpdateWheelVisuals(float steerRad)
    {
        var t = Tuning;
        foreach (var w in _wheels)
        {
            float extension = t.RestLength - w.Compression;
            w.Visual.Position = w.Mount + Vector3.Down * extension;
            w.Visual.Rotation = new Vector3(0f, w.Front ? -steerRad : 0f, 0f);
            ((Node3D)w.Visual.GetChild(0)).Rotation = new Vector3(-w.Spin, 0f, 0f);
        }
    }
}
