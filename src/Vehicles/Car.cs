using System;
using DestructionRacing.Rules;
using Godot;

namespace DestructionRacing.Vehicles;

/// <summary>
/// Arcade raycast vehicle on a Jolt rigid body (plan §15.3): spring-damper suspension per wheel,
/// slip-angle tyre curve with a friction ellipse, power-limited drive, DD-style reverse and turn-around.
/// Mechanical damage (plan §6.1) comes from the referee's <see cref="DamageState"/>; spin assist and post-hit
/// grip loss reproduce DD's "tap the rear, watch it spin". With a <see cref="ModelId"/> the car is a generated
/// model (M3): its collider, axles and track come from the model (<see cref="CarSpec"/>) and a <see cref="CarVisual"/>
/// shows the damage; without one it is the grey box (tests, headless sims).
/// </summary>
public partial class Car : RigidBody3D
{
    public const uint WorldLayer = 1;
    public const uint CarLayer = 2;

    [Export] public Color PaintColor { get; set; } = new(0.80f, 0.08f, 0.06f);
    [Export] public Color RoofColor { get; set; } = new(0.95f, 0.80f, 0.10f);
    [Export] public int Number { get; set; } = 7;
    /// <summary>Generated model (assets/cars/&lt;id&gt;); empty = grey box.</summary>
    [Export] public string ModelId { get; set; } = "";
    /// <summary>Paint scheme of the model (default: <see cref="PaintColor"/>, <see cref="Number"/> and <see cref="RoofColor"/>).</summary>
    public Livery? Livery { get; set; }
    /// <summary>False in headless sims: the model's size still sets the physics, but nothing is drawn.</summary>
    public bool BuildVisuals { get; set; } = true;
    public CarSpec? Spec { get; private set; }
    public CarVisual? Visual { get; private set; }

    /// <summary>The generated cars of the M2/M3 roster (AI cycle through them; plan §9 grows this in M5).</summary>
    public static readonly string[] Roster = { "kitsune", "brougham", "nordvik" };

    /// <summary>Car-local half extents of the grey-box body (forward = -Z).</summary>
    public static readonly Vector3 DefaultHalfExtents = new(0.9f, 0.35f, 2.3f);
    /// <summary>Car-local half extents used for the damage zones (the model's body with a model).</summary>
    public Vector3 HalfExtents { get; private set; } = DefaultHalfExtents;
    /// <summary>Height of the ground below the chassis origin at static ride height (the model sits on it).</summary>
    public float GroundY { get; private set; } = -0.585f;
    public float FrontAxleZ => _wheels[0].Mount.Z;
    public float RearAxleZ => _wheels[2].Mount.Z;

    /// <summary>Prints every hit reaction (scenario debugging).</summary>
    public static bool DebugHits;

    public CarTuning Tuning { get; private set; } = new();
    public ICarInput? Driver { get; set; }

    /// <summary>Unique id in the event (the referee's car id).</summary>
    public int Id { get; set; }
    /// <summary>Health per zone and wheel, owned by the referee. Null = indestructible (driving test).</summary>
    public DamageState? Damage { get; set; }
    public bool IsWrecked => IsReplaying ? ReplayWrecked : Damage?.IsWrecked ?? false;

    /// <summary>
    /// Replay playback drives the car (plan §15.5): no physics or input; the pose, wheels and zone health come
    /// from the recording (<see cref="Replay.ReplayPlayer"/>), so crush, smoke and fire replay with it.
    /// </summary>
    public bool IsReplaying { get; private set; }
    public bool ReplayWrecked { get; set; }
    public float ReplaySteer { get; set; }
    public readonly float[] ReplaySpin = new float[4];
    public readonly float[] ReplayHealth = { 1f, 1f, 1f, 1f };
    public float ReplaySpeed { get; set; }

    public void BeginReplay()
    {
        IsReplaying = true;
        FreezeMode = FreezeModeEnum.Kinematic;
        Freeze = true;
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
    }
    /// <summary>Holds the car on the brakes and ignores the driver (countdown).</summary>
    public bool InputLocked { get; set; }

    // Pre-solve state, cached by the ImpactSystem before every physics step (plan §5.3).
    public Vector3 PreLinearVelocity;
    public Vector3 PreAngularVelocity;
    public Vector3 PreCenterOfMass;

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
    // Reused every tick: allocating RefCounted query objects per wheel per tick floods the .NET finalizer.
    private PhysicsRayQueryParameters3D _rayQuery = null!;
    private float _upsideDownTime;
    private float _gripLoss;        // 0..1, decays after a hit
    private float _pullDeg;         // smoothed steering pull from side damage
    private float _stutterTimer;
    /// <summary>Per-car random numbers (seeded by Id) so sims are reproducible; Godot's global RNG is seeded per launch.</summary>
    private System.Random? _rng;
    private bool _wreckShown;
    private StandardMaterial3D _paint = null!;
    private readonly System.Collections.Generic.List<StandardMaterial3D> _lights = new();
    private CpuParticles3D? _smoke;
    private CpuParticles3D? _fire;
    private float _wheelRadius = 0.33f;
    private float _steerRad;
    private CollisionShape3D? _nose, _tail;
    private float _noseLength, _tailLength, _noseZ, _tailZ;

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
        /// <summary>How hard the tyre slides, locks or spins on the ground (0..1), for the ground marks.</summary>
        public float Mark;
        public float Spin;
        public Node3D Visual = null!;
        public Rules.Wheel Index;
    }

    public override void _Ready()
    {
        CollisionLayer = CarLayer;
        CollisionMask = WorldLayer | CarLayer;
        ContinuousCd = true;
        CanSleep = false;
        // Contacts feed the ImpactSystem. Car-on-car friction is low so cars glance off each other, as in DD.
        ContactMonitor = true;
        // The model collider is four boxes: a pileup reports many points per car (--maxcontacts=N for experiments).
        MaxContactsReported = int.TryParse(DestructionRacing.Core.DevTools.Instance?.Arg("maxcontacts"), out int mc) ? mc : 48;
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.35f, Bounce = 0.12f };
        // Drag and rolling resistance are modelled explicitly; the engine's default damping would act as a hidden brake.
        LinearDampMode = DampMode.Replace;
        LinearDamp = 0f;
        AngularDampMode = DampMode.Replace;
        AngularDamp = 0.6f;
        _exclude = new Godot.Collections.Array<Rid> { GetRid() };
        _rayQuery = new PhysicsRayQueryParameters3D { CollisionMask = WorldLayer, Exclude = _exclude };

        for (int i = 0; i < 4; i++) _wheels[i] = new Wheel { Front = i < 2, Left = i % 2 == 0, Index = (Rules.Wheel)i };
        // --greybox (sims, tests): every car is the grey box, for comparing tuning against the M1 baselines.
        if (DestructionRacing.Core.DevTools.Instance?.Arg("greybox") != null) ModelId = "";
        if (ModelId != "")
        {
            Spec = CarSpec.For(ModelId);
            ApplyTuning(Tuning);
            BuildModelCollider(Spec);
            if (BuildVisuals)
            {
                var livery = Livery ?? new Livery(PaintColor, new Color(0.93f, 0.93f, 0.9f), 0, Wear: 0.15f, Dirt: 0.25f, Number: Number, Roof: RoofColor);
                Visual = new CarVisual(this, ModelId, livery, $"DD {Number:00}");
                AddChild(Visual);
            }
        }
        else
        {
            BuildBody();
            BuildWheelVisuals();
            BuildDamageEffects();
            ApplyTuning(Tuning);
        }
    }

    /// <summary>
    /// Collider from the model: a lower body in three boxes (nose, middle, tail; the end boxes shorten as the
    /// car is crushed) and a cabin box, all in car-local space (model space shifted down by <see cref="GroundY"/>).
    /// </summary>
    private void BuildModelCollider(CarSpec spec)
    {
        var b = spec.Body;
        float y0 = GroundY + b.Position.Y + 0.04f;
        float belt = GroundY + b.Position.Y + b.Size.Y * 0.55f;
        float halfW = Mathf.Max(Mathf.Abs(b.Position.X), Mathf.Abs(b.End.X));
        float front = b.Position.Z, rear = b.End.Z;
        float noseEnd = spec.FrontAxleZ + 0.15f, tailStart = spec.RearAxleZ - 0.15f;
        _noseLength = noseEnd - front;
        _tailLength = rear - tailStart;
        _noseZ = noseEnd;
        _tailZ = tailStart;
        float h = belt - y0, yc = 0.5f * (belt + y0);
        _nose = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(halfW * 2f, h, _noseLength) }, Position = new Vector3(0f, yc, noseEnd - _noseLength * 0.5f) };
        _tail = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(halfW * 2f, h, _tailLength) }, Position = new Vector3(0f, yc, tailStart + _tailLength * 0.5f) };
        AddChild(_nose);
        AddChild(_tail);
        AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(halfW * 2f, h, tailStart - noseEnd) },
            Position = new Vector3(0f, yc, 0.5f * (noseEnd + tailStart)),
        });
        // Cabin: from the windscreen base to the back of the roof, a little narrower than the body.
        float cabFront = spec.TryPart("Glass_Front", out var ws) ? ws.Position.Z : -0.4f;
        float cabRear = spec.TryPart("Glass_Rear", out var bw) ? bw.End.Z : 1.4f;
        float roof = GroundY + b.End.Y;
        AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(halfW * 1.7f, roof - belt, cabRear - cabFront) },
            Position = new Vector3(0f, 0.5f * (roof + belt), 0.5f * (cabFront + cabRear)),
        });
        HalfExtents = new Vector3(halfW, 0.5f * (roof - y0), Mathf.Max(-front, rear));
    }

    /// <summary>Shortens the nose and tail boxes by the crush depth (m): crushed cars really get shorter.</summary>
    public void SetCrushCollider(float frontDepth, float rearDepth)
    {
        if (_nose?.Shape is BoxShape3D nb)
        {
            float len = Mathf.Max(0.2f, _noseLength - frontDepth);
            nb.Size = new Vector3(nb.Size.X, nb.Size.Y, len);
            _nose.Position = new Vector3(0f, _nose.Position.Y, _noseZ - len * 0.5f);
        }
        if (_tail?.Shape is BoxShape3D tb)
        {
            float len = Mathf.Max(0.2f, _tailLength - rearDepth);
            tb.Size = new Vector3(tb.Size.X, tb.Size.Y, len);
            _tail.Position = new Vector3(0f, _tail.Position.Y, _tailZ + len * 0.5f);
        }
    }

    /// <summary>A hit from the ImpactSystem, for the visuals (<paramref name="intoCar"/> points into this car).</summary>
    public void OnVisualHit(Vector3 point, Vector3 intoCar, float energy) => Visual?.OnHit(point, intoCar, energy);

    /// <summary>Sustained sliding contact, for the visuals (scrape streaks and sparks).</summary>
    public void OnVisualScrape(Vector3 point, Vector3 normalOut, Vector3 slideVelocity, float dt, bool wall = false) =>
        Visual?.OnScrape(point, normalOut, slideVelocity, dt, wall);

    /// <summary>Wheel centre in car-local space (follows the suspension).</summary>
    /// <summary>For the ground marks: wheel i's contact point and how hard it slides, locks or spins (0..1). False in the air.</summary>
    public bool WheelMark(int i, out Vector3 point, out float skid)
    {
        var w = _wheels[i];
        point = w.ContactPoint;
        skid = w.Mark;
        return w.Grounded;
    }

    public Vector3 WheelCentre(int i) => _wheels[i].Mount + Vector3.Down * (Tuning.RestLength - _wheels[i].Compression);
    public float WheelSteer(int i) => _wheels[i].Front ? -(IsReplaying ? ReplaySteer : _steerRad) : 0f;
    public float WheelSpin(int i) => IsReplaying ? ReplaySpin[i] : _wheels[i].Spin;
    /// <summary>The front wheels' steering angle (rad), as recorded for replays.</summary>
    public float SteerAngle => _steerRad;

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

        // With a model, the axles, track and wheels are the model's; the chassis origin keeps the grey box's
        // height above the ground (so spawn heights and the centre of mass stay as tuned).
        float staticCompression = t.Mass * 9.81f / (4f * t.SpringRate);
        float halfTrack = t.HalfTrack, frontZ = t.FrontAxleZ, rearZ = t.RearAxleZ, mountY = t.MountHeight;
        _wheelRadius = t.WheelRadius;
        if (Spec != null)
        {
            GroundY = -0.585f;
            _wheelRadius = Spec.WheelRadius;
            halfTrack = Spec.HalfTrack;
            frontZ = Spec.FrontAxleZ;
            rearZ = Spec.RearAxleZ;
            mountY = GroundY + Spec.WheelCentreY + t.RestLength - staticCompression;
        }
        else
        {
            GroundY = t.MountHeight - t.RestLength + staticCompression - t.WheelRadius;
        }
        for (int i = 0; i < 4; i++)
        {
            var wheel = _wheels[i];
            wheel.Mount = new Vector3(wheel.Left ? -halfTrack : halfTrack, mountY, wheel.Front ? frontZ : rearZ);
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
        if (IsReplaying) return;
        var t = Tuning;
        var xf = GlobalTransform;
        Vector3 up = xf.Basis.Y.Normalized();
        Vector3 fwd = -xf.Basis.Z.Normalized();
        Vector3 right = xf.Basis.X.Normalized();
        Vector3 comWorld = xf * CenterOfMass;
        Vector3 v = LinearVelocity;

        var c = Driver?.GetControls(this, dt) ?? CarControls.Parked;
        if (InputLocked) c = CarControls.Parked;
        // A wrecked car's engine is dead: it coasts to a stop and stays as an obstacle.
        if (IsWrecked) c = new CarControls { Brake = 0.35f, Hold = true };
        LastControls = c;

        // Mechanical damage (plan §6.1).
        float frontH = Health(DamageZone.Front), rearH = Health(DamageZone.Rear);
        float leftH = Health(DamageZone.Left), rightH = Health(DamageZone.Right);
        float powerFactor = (0.55f + 0.45f * frontH) * (1f - t.RearDamagePowerLoss * (1f - rearH));
        if (frontH < 0.35f && !IsWrecked)
        {
            // Critical front: the engine stutters.
            _stutterTimer -= dt;
            if (_stutterTimer < -0.25f)
            {
                _rng ??= new System.Random(Id * 7919 + 17);
                _stutterTimer = (0.4f + 1.2f * (float)_rng.NextDouble()) * (0.3f + frontH * 2f);
            }
            if (_stutterTimer < 0f) powerFactor *= 0.25f;
        }
        // Side damage pulls toward the damaged side; both sides damaged → the car barely turns.
        float pullTarget = t.MaxDamagePullDeg * ((1f - rightH) - (1f - leftH));
        _pullDeg = Mathf.MoveToward(_pullDeg, pullTarget, 6f * dt);
        float lockScale = 1f - (1f - t.BothSidesLockScale) * MathF.Min(1f - leftH, 1f - rightH);
        _gripLoss = MathF.Max(0f, _gripLoss - dt / MathF.Max(t.PostHitGripLossTime, 0.01f));

        ForwardSpeed = v.Dot(fwd);
        float lateralSpeed = v.Dot(right);
        BodySlipDeg = ForwardSpeed > 2f ? Mathf.RadToDeg(Mathf.Atan2(lateralSpeed, Mathf.Abs(ForwardSpeed))) : 0f;

        // Steering angle: speed-sensitive lock, Fast Steer, counter-steer assist.
        float speedT = Mathf.Clamp(Mathf.Abs(ForwardSpeed) / t.SteerSpeedForMin, 0f, 1f);
        float maxSteer = Mathf.Lerp(t.MaxSteerLowSpeed, t.MaxSteerHighSpeed, speedT) * (c.FastSteer ? t.FastSteerLockScale : 1f) * lockScale;
        float steerDeg = c.Steer * maxSteer + _pullDeg;
        if (t.CounterSteerAssist > 0f && ForwardSpeed > 4f && Mathf.Abs(BodySlipDeg) > 4f)
            steerDeg += t.CounterSteerAssist * BodySlipDeg;
        steerDeg = Mathf.Clamp(steerDeg, -maxSteer * 1.25f, maxSteer * 1.25f);
        SteerAngleDeg = steerDeg;
        float steerRad = Mathf.DegToRad(steerDeg);
        _steerRad = steerRad;

        // Drive, brake, reverse (DD2: holding brake at a standstill reverses; DD1 PS: dedicated reverse).
        float brake = c.Brake;
        float drive = 0f;
        bool reversing = false;
        if (c.Reverse)
        {
            if (ForwardSpeed > 1.5f) brake = MathF.Max(brake, 1f);
            else reversing = true;
        }
        else if (c.Brake > 0.1f && ForwardSpeed < 0.8f && c.Throttle < 0.1f && !c.Hold)
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
        drive *= powerFactor;

        TurningAround = c.TurnAround && MathF.Abs(ForwardSpeed) < t.TurnAroundMaxSpeed
                        && (c.Throttle > 0.1f || c.Brake > 0.1f || c.Reverse);
        // DD2 Fast Steer pivot, strongest at low speed and gone by FastSteerMaxSpeed.
        float fastPivot = c.FastSteer && MathF.Abs(c.Steer) > 0.3f && ForwardSpeed > 2f
            ? MathF.Abs(c.Steer) * Mathf.Clamp(1.2f - MathF.Abs(ForwardSpeed) / t.FastSteerMaxSpeed, 0f, 1f)
            : 0f;

        // Pass 1: suspension raycasts.
        var space = GetWorld3D().DirectSpaceState;
        float rayLength = t.RestLength + _wheelRadius;
        int grounded = 0, drivenGrounded = 0;
        foreach (var w in _wheels)
        {
            if (Damage != null && Damage.IsWheelLost(w.Index))
            {
                w.Grounded = false;
                w.Compression = 0f;
                w.Load = 0f;
                continue;
            }
            Vector3 mountWorld = xf * w.Mount;
            _rayQuery.From = mountWorld;
            _rayQuery.To = mountWorld - up * rayLength;
            var hit = space.IntersectRay(_rayQuery);
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
            float mark = Mathf.Clamp((MathF.Abs(vLat) - 1.5f) / 5f, 0f, 1f);

            float grip = t.SurfaceGrip * (w.Front ? t.FrontGrip : t.RearGrip);
            if (!w.Front && TurningAround) grip *= t.TurnAroundRearGrip;
            if (!w.Front && fastPivot > 0f) grip *= Mathf.Lerp(1f, t.FastSteerRearGrip, fastPivot);
            // Post-hit grip loss lets a hit car keep rotating (calibration, plan §3).
            grip *= Mathf.Lerp(1f, w.Front ? t.PostHitFrontGrip : t.PostHitRearGrip, _gripLoss);
            // A damaged wheel wobbles and grips less.
            if (Damage != null) grip *= 0.6f + 0.4f * Damage.WheelHealth(w.Index) / DamageState.MaxHealth;
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
                if (MathF.Abs(wheelDrive) > available * t.TractionControl && MathF.Abs(vLong) < 9f)
                    mark = MathF.Max(mark, 0.6f * (1f - MathF.Abs(vLong) / 9f));        // wheelspin
                DriveForceApplied += fx;
            }
            if (brake > 0f)
            {
                float bias = w.Front ? t.BrakeFrontBias : 1f - t.BrakeFrontBias;
                float brakeForce = brake * t.BrakeForce * bias * 0.5f;
                float stopForce = MathF.Abs(vLong) * cornerMass / dt;
                fx -= MathF.Sign(vLong) * MathF.Min(brakeForce, stopForce);
                if (brakeForce > maxLong && MathF.Abs(vLong) > 2f) mark = MathF.Max(mark, 0.7f);  // locked
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
            w.Mark = mark;

            w.Spin += vLong / _wheelRadius * dt;
        }

        // Aero drag and rolling resistance.
        Vector3 flat = v - up * v.Dot(up);
        ApplyCentralForce(-flat * flat.Length() * t.DragCoefficient - flat * t.RollingResistance);

        if (fastPivot > 0f && grounded >= 3)
            ApplyTorque(up * (-MathF.Sign(c.Steer) * fastPivot * t.FastSteerYawTorque));

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
        UpdateRidingOff(grounded, up, c, dt);
        if (Spec == null) UpdateWheelVisuals(steerRad);
    }

    private bool IsDriven(Wheel w) => Tuning.RearWheelDrive ? !w.Front : w.Front;

    /// <summary>Zone health as 0..1 (1 when the car has no damage model).</summary>
    public float Health(DamageZone zone) =>
        IsReplaying ? ReplayHealth[(int)zone] : Damage == null ? 1f : Damage.Health(zone) / DamageState.MaxHealth;

    /// <summary>
    /// Calibration on car-car hits (plan §15.3): extra yaw from the hit's angular impulse, strongest on the
    /// rear quarter, plus a short loss of grip, so a rear-quarter tap spins the car as in DD (~0.5 s).
    /// </summary>
    /// <param name="point">World contact point.</param>
    /// <param name="impulse">Estimated impulse on this car (N·s).</param>
    public void ApplyHitReaction(Vector3 point, Vector3 impulse)
    {
        var t = Tuning;
        var xf = GlobalTransform;
        Vector3 local = xf.AffineInverse() * point;
        float halfLength = HalfExtents.Z;
        // Lever weighting: rear half up to 1 (from the centre to the rear axle and beyond), front quarter up to 0.25.
        bool rearHalf = local.Z > 0f;
        float w = rearHalf
            ? Mathf.Clamp(local.Z / (halfLength - 0.4f), 0f, 1f)
            : 0.25f * Mathf.Clamp((-local.Z - 0.6f) / (halfLength - 0.6f), 0f, 1f);
        Vector3 r = point - xf * CenterOfMass;
        float yawImpulse = r.Cross(impulse).Dot(Vector3.Up);
        if (DebugHits)
            GD.Print($"[HitReaction] car#{Id} local={local} w={w:0.00} |J|={impulse.Length():0} yawJ={yawImpulse:0} " +
                     $"ω before={Mathf.RadToDeg(AngularVelocity.Y):0}°/s assist={Mathf.RadToDeg(yawImpulse / Inertia.Y * t.SpinAssist * w):0}°/s");
        if (w <= 0f) return;
        AngularVelocity += Vector3.Up * (yawImpulse / Inertia.Y * t.SpinAssist * w);
        // Saturates on taps (a 12 km/h side tap is ~2.5 kN·s): DD spins cars from light contact.
        float strength = Mathf.Clamp(impulse.Length() / 2500f, 0f, 1f);
        _gripLoss = MathF.Max(_gripLoss, strength * (rearHalf ? 1f : 0.4f));
    }

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

    private float _ridingTime;

    /// <summary>
    /// A car that ended up on another car's hood or boot (wheels off the ground, resting on its body) slides off it
    /// while its driver tries to get going. The wheels only touch the ground, so without this it has no grip up
    /// there and rides along for good. Parked cars and wrecks stay put (a pile settles).
    /// </summary>
    private void UpdateRidingOff(int grounded, Vector3 up, CarControls c, float dt)
    {
        bool trying = !IsWrecked && (c.Throttle > 0.1f || c.Reverse || (c.Brake > 0.1f && !c.Hold));
        if (grounded >= 3 || up.Y < 0.5f || !trying) { _ridingTime = 0f; return; }
        Car? below = null;
        foreach (var body in GetCollidingBodies())
            if (body is Car other && other.GlobalPosition.Y < GlobalPosition.Y - 0.15f && (below == null || other.Id < below.Id))
                below = other;       // lowest id: the same choice every run, whatever order the contacts come in
        // Only a car being carried (moving with the one below) for a while: a car climbing over another in a
        // crash, or sliding across it, is left to the physics.
        if (below == null) { _ridingTime = 0f; return; }
        Vector3 relative = LinearVelocity - below.LinearVelocity;
        relative.Y = 0f;
        if (relative.Length() > 2f) { _ridingTime = 0f; return; }
        _ridingTime += dt;
        if (_ridingTime < 1.5f) return;
        Vector3 away = GlobalPosition - below.GlobalPosition;
        away.Y = 0f;
        if (away.LengthSquared() < 0.01f) away = GlobalBasis.X;
        ApplyCentralForce(away.Normalized() * Mass * 6f);
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
        _paint = paint;
        var roof = new StandardMaterial3D { AlbedoColor = RoofColor, Roughness = 0.4f };
        var glass = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.07f, 0.09f), Metallic = 0.6f, Roughness = 0.08f };
        var trim = new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.18f, 0.18f), Roughness = 0.6f };
        var headlight = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.97f, 0.85f), EmissionEnabled = true, Emission = new Color(1f, 0.95f, 0.8f), EmissionEnergyMultiplier = 2f };
        var taillight = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.02f, 0.02f), EmissionEnabled = true, Emission = new Color(0.9f, 0.05f, 0.03f), EmissionEnergyMultiplier = 1.5f };
        _lights.Add(headlight);
        _lights.Add(taillight);

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

    /// <summary>Grey-box damage readability (plan §6.1): steam → grey → black smoke from the bonnet, then fire.</summary>
    private void BuildDamageEffects()
    {
        _smoke = new CpuParticles3D
        {
            Emitting = false,
            Amount = 28,
            Lifetime = 1.6f,
            LocalCoords = false,
            Position = new Vector3(0f, 0.45f, -1.7f),
            Direction = Vector3.Up,
            Spread = 18f,
            Gravity = new Vector3(0f, 1.2f, 0f),
            InitialVelocityMin = 1.2f,
            InitialVelocityMax = 2.4f,
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.4f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.3f,
            Mesh = new SphereMesh
            {
                Radius = 0.35f, Height = 0.7f, RadialSegments = 8, Rings = 4,
                Material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    VertexColorUseAsAlbedo = true,
                },
            },
        };
        var curve = new Curve();
        curve.AddPoint(new Vector2(0f, 0.4f));
        curve.AddPoint(new Vector2(1f, 2.2f));
        _smoke.ScaleAmountCurve = curve;
        AddChild(_smoke);

        _fire = new CpuParticles3D
        {
            Emitting = false,
            Amount = 24,
            Lifetime = 0.5f,
            LocalCoords = false,
            Position = new Vector3(0f, 0.4f, -1.8f),
            Direction = Vector3.Up,
            Spread = 25f,
            Gravity = new Vector3(0f, 3f, 0f),
            InitialVelocityMin = 1.5f,
            InitialVelocityMax = 3f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.35f,
            Color = new Color(1f, 0.55f, 0.1f),
            Mesh = new SphereMesh
            {
                Radius = 0.18f, Height = 0.36f, RadialSegments = 6, Rings = 3,
                Material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    VertexColorUseAsAlbedo = true,
                    EmissionEnabled = true,
                    Emission = new Color(1f, 0.45f, 0.05f),
                    EmissionEnergyMultiplier = 3f,
                },
            },
        };
        AddChild(_fire);
    }

    public override void _Process(double delta)
    {
        if (Damage == null || _smoke == null || _fire == null) return;
        float front = Health(DamageZone.Front);
        bool wrecked = IsWrecked;
        _smoke.Emitting = front < 0.6f || wrecked;
        _smoke.Color = wrecked || front < 0.15f ? new Color(0.08f, 0.08f, 0.08f, 0.75f)
            : front < 0.35f ? new Color(0.45f, 0.45f, 0.45f, 0.6f)
            : new Color(0.95f, 0.95f, 0.95f, 0.45f);
        _fire.Emitting = front < 0.08f;
        if (wrecked && !_wreckShown)
        {
            _wreckShown = true;
            _paint.AlbedoColor = PaintColor.Darkened(0.65f);
            _paint.Clearcoat = 0f;
            _paint.Roughness = 0.8f;
            foreach (var light in _lights) light.EmissionEnabled = false;
        }
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
            w.Visual.Visible = Damage == null || !Damage.IsWheelLost(w.Index);
            float extension = t.RestLength - w.Compression;
            w.Visual.Position = w.Mount + Vector3.Down * extension;
            w.Visual.Rotation = new Vector3(0f, w.Front ? -steerRad : 0f, 0f);
            ((Node3D)w.Visual.GetChild(0)).Rotation = new Vector3(-w.Spin, 0f, 0f);
        }
    }
}
