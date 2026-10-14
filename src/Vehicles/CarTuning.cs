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
    public const int CurrentVersion = 2;
    public int Version = CurrentVersion;

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
