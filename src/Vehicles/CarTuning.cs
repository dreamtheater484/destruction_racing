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
    /// <summary>0 = lateral forces at the contact patch, 1 = at the centre of mass (less body roll).</summary>
    public float LateralForceHeight = 0.55f;

    // Engine and brakes (arcade: power-limited drive force)
    public bool RearWheelDrive = true;
    /// <summary>Light traction control: drive force never uses more than this share of the grip left after cornering (Purist: 1.2 = off).</summary>
    public float TractionControl = 0.9f;
    public float EnginePower = 115_000f;        // W
    public float MaxDriveForce = 9_500f;        // N, traction-limited launch
    public float ReverseForce = 6_000f;
    public float ReverseMaxSpeed = 11f;         // m/s
    public float BrakeForce = 15_000f;          // N total
    public float BrakeFrontBias = 0.62f;
    public float DragCoefficient = 1.05f;       // N per (m/s)^2
    public float RollingResistance = 14f;       // N per m/s

    // Steering
    public float MaxSteerLowSpeed = 34f;        // degrees
    public float MaxSteerHighSpeed = 11f;
    public float SteerSpeedForMin = 36f;        // m/s at which max steer reaches the high-speed value
    public float FastSteerLockScale = 1.35f;
    public float CounterSteerAssist = 0.35f;    // 0 = off (Purist), 0.35 = Low, 0.7 = High

    // DD1 turn-around (Space + gas/brake at low speed)
    public float TurnAroundTorque = 16_000f;
    public float TurnAroundMaxSpeed = 9f;
    public float TurnAroundRearGrip = 0.3f;

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
            return FromJson(f.GetAsText());
        }
        catch (Exception e)
        {
            GD.PushWarning($"Could not read tuning {userPath}: {e.Message}");
            return new CarTuning();
        }
    }
}
