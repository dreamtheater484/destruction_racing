using System;
using System.Text.Json;
using Godot;

namespace DestructionRacing.Vehicles;

/// <summary>
/// All handling tunables for one car (plan §15.3). Plain fields so the debug panel can edit them live
/// and they can be saved as JSON presets.
/// </summary>
public sealed class CarTuning
{
    /// <summary>Bumped when the defaults are retuned; saved files from an older version are ignored.</summary>
    public const int CurrentVersion = 3;
    public int Version = CurrentVersion;

    public CarTuning()
    {
        // Dev A/B (--classichandling): the M1-M8 handling, without DD's grip at speed and the yaw control.
        if (Core.DevTools.Instance?.Arg("classichandling") != null) { ArcadeGrip = 1f; YawControl = 0f; }
        // Dev A/B (--tune=RearGrip=0.9,YawControl=6): any tunable by name.
        if (Core.DevTools.Instance?.Arg("tune") is { } tune)
            foreach (string pair in tune.Split(','))
                if (pair.Split('=') is [var name, var value] && typeof(CarTuning).GetField(name) is { FieldType: var ft } field && ft == typeof(float)
                    && float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f))
                    field.SetValue(this, f);
    }

    // Body
    public float Mass = 1400f;
    public float CenterOfMassHeight = -0.28f;   // relative to the chassis centre
    public float YawInertiaScale = 0.85f;       // < 1 spins more easily (DD feel)

    // Geometry (car-local, forward = -Z)
    public float HalfTrack = 0.78f;
    public float FrontAxleZ = -1.35f;
    public float RearAxleZ = 1.35f;
    public float MountHeight = -0.05f;
    public float WheelRadius = 0.33f;

    // Suspension
    public float RestLength = 0.30f;
    public float SpringRate = 36_000f;
    public float DamperCompression = 3_400f;
    public float DamperRebound = 4_200f;
    public float AntiRollFront = 14_000f;
    public float AntiRollRear = 9_000f;

    // Tyres
    public float FrontGrip = 1.05f;
    public float RearGrip = 1.0f;
    public float SurfaceGrip = 1.0f;
    public float TyreStiffness = 9.0f;          // Pacejka-lite B
    public float TyreShape = 1.45f;             // Pacejka-lite C
    /// <summary>Arcade: drive and brake grip relative to cornering grip (friction ellipse). DD2 launches at ~1.2 g.</summary>
    public float LongitudinalGrip = 3.0f;
    /// <summary>0 = lateral forces at the contact patch, 1 = at the centre of mass (less body roll).</summary>
    public float LateralForceHeight = 0.55f;

    // Engine and brakes (arcade: power-limited drive force)
    public bool RearWheelDrive = true;
    /// <summary>Light traction control: drive force never uses more than this share of the grip left after cornering (Purist: 1.2 = off).</summary>
    public float TractionControl = 0.9f;
    // DD2 PC speedometer (docs/reference_feel.md): 0-100 km/h ~2.2 s, 0-160 ~4.6 s, ~240 km/h on long straights.
    public float EnginePower = 440_000f;        // W
    public float MaxDriveForce = 19_500f;       // N, launch
    public float ReverseForce = 6_000f;
    public float ReverseMaxSpeed = 11f;         // m/s
    public float BrakeForce = 15_000f;          // N total
    public float BrakeFrontBias = 0.62f;
    public float DragCoefficient = 1.2f;        // N per (m/s)^2
    public float RollingResistance = 14f;       // N per m/s

    // Steering
    public float MaxSteerLowSpeed = 34f;        // degrees
    public float MaxSteerHighSpeed = 11f;
    public float SteerSpeedForMin = 36f;        // m/s at which max steer reaches the high-speed value
    public float FastSteerLockScale = 1.35f;
    /// <summary>
    /// DD2 Fast Steer at derby speeds: while held with steering, the rear loosens and a yaw torque helps the car
    /// pivot (sharp turns, the "turn at the last moment" of the DD Code). Fades out above FastSteerMaxSpeed.
    /// </summary>
    public float FastSteerRearGrip = 0.6f;
    public float FastSteerYawTorque = 9_000f;   // N·m at full steer
    public float FastSteerMaxSpeed = 22f;       // m/s
    public float CounterSteerAssist = 0.35f;    // 0 = off (Purist), 0.35 = Low, 0.7 = High

    // DD1 turn-around (Space + gas/brake at low speed)
    public float TurnAroundTorque = 16_000f;
    public float TurnAroundMaxSpeed = 9f;
    public float TurnAroundRearGrip = 0.3f;

    // Hits and damage (plan §6.1, §15.3)
    /// <summary>Extra yaw from a hit's angular impulse (rear-quarter weighted). 0 = raw physics (calibration).</summary>
    public float SpinAssist = 1.5f;
    public float PostHitGripLossTime = 0.8f;    // s, decays linearly
    public float PostHitRearGrip = 0.2f;        // grip multiplier at the moment of a full-strength hit
    public float PostHitFrontGrip = 0.6f;
    public float MaxDamagePullDeg = 4f;         // steering pull with one side fully damaged
    public float BothSidesLockScale = 0.35f;    // steering lock left when both sides are destroyed
    public float RearDamagePowerLoss = 0.3f;    // acceleration and top-speed loss at 0 rear health

    // DD handling at speed (docs/reference_feel.md, DD2 PC: corners at 2.2-2.4 g from 100 to 170 km/h, the turn rate
    // follows the keys within 0.1 s, and the car stops turning when they are let go). Calibration, not an assist: on in
    // Purist too. Below the start speeds the car is the M1 car (the derby's shoving and turning round keep their approved feel).
    /// <summary>Cornering grip at speed relative to the tyres' own (×1 up to ArcadeGripStartSpeed, ×this from ArcadeGripFullSpeed).</summary>
    public float ArcadeGrip = 2.3f;
    public float ArcadeGripStartSpeed = 14f;    // m/s (50 km/h)
    public float ArcadeGripFullSpeed = 30f;     // m/s (108 km/h)
    /// <summary>
    /// Yaw control (1/s): a torque holds the car to the turn rate the steering asks for (the kinematic rate of the
    /// lock, capped by what the grip holds), so a held key is a steady turn and a released one straightens the car.
    /// Off during a hit's grip loss, in a spin (body slip over YawControlMaxSlip), turning round and in a Fast Steer pivot.
    /// </summary>
    public float YawControl = 8f;
    public float YawControlStartSpeed = 11f;    // m/s (40 km/h): off below
    public float YawControlFullSpeed = 22f;     // m/s (80 km/h)
    public float YawControlMaxSlip = 40f;       // degrees
    /// <summary>The share of the grip's cornering limit the yaw control asks for at full lock.</summary>
    public float YawControlGripUse = 0.92f;
    /// <summary>How much the brake relaxes the yaw control (the DD2 brake-dab powerslide).</summary>
    public float YawControlBrakeRelease = 0.7f;

    /// <summary>The cornering grip multiplier at this speed (m/s): 1 below ArcadeGripStartSpeed, ArcadeGrip from ArcadeGripFullSpeed.</summary>
    public float SpeedGrip(float speed) =>
        1f + (ArcadeGrip - 1f) * Mathf.Clamp((MathF.Abs(speed) - ArcadeGripStartSpeed) / MathF.Max(ArcadeGripFullSpeed - ArcadeGripStartSpeed, 0.1f), 0f, 1f);

    /// <summary>The default tuning (the AI's speed profiles are built for it).</summary>
    public static readonly CarTuning Defaults = new();

    // Safety
    public float MaxAngularSpeed = 14f;         // rad/s

    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static CarTuning FromJson(string json) => JsonSerializer.Deserialize<CarTuning>(json, Json) ?? new CarTuning();

    public void Save(string userPath)
    {
        using var f = FileAccess.Open(userPath, FileAccess.ModeFlags.Write);
        f?.StoreString(ToJson());
    }

    public static CarTuning LoadOrDefault(string userPath)
    {
        if (!FileAccess.FileExists(userPath)) return new CarTuning();
        try
        {
            using var f = FileAccess.Open(userPath, FileAccess.ModeFlags.Read);
            var saved = FromJson(f.GetAsText());
            if (saved.Version == CurrentVersion) return saved;
            GD.Print($"[Tuning] ignoring {userPath}: saved for tuning version {saved.Version}, current is {CurrentVersion}");
            return new CarTuning();
        }
        catch (Exception e)
        {
            GD.PushWarning($"Could not read tuning {userPath}: {e.Message}");
            return new CarTuning();
        }
    }
}
