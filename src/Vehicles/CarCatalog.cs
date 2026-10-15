using System;
using System.Collections.Generic;
using System.Linq;

namespace DestructionRacing.Vehicles;

public enum Region { US, EU, JP }

/// <summary>
/// One model of the v1 roster (plan §9.1): who makes it, and its five 1–10 ratings. The ratings are the design
/// intent; <see cref="Tuning"/> and <see cref="DamageToughness"/> turn them into the physics, compressed so the
/// heaviest car is 1.32× the lightest (plan §9.3: at most 1.5×) and toughness makes up the rest.
/// </summary>
public sealed record CarInfo(
    string Id, int Number, string Make, string Name, Region Region, string Body, string Engine, bool FrontDrive,
    int Speed, int Accel, int Handling, int Weight, int Toughness, string Hint)
{
    public string FullName => $"{Make} {Name}";
    /// <summary>
    /// The car-select verdicts (1 weak .. 5 best) from the balance sims (tools/dev/balance_report.py): derby points,
    /// Wreckin' points (nearly all from spins, so the heavy cars score well), and pace (Stock Car points for the
    /// race finishing order, an estimate until Stock Car Racing has its own sims in M6).
    /// </summary>
    public int DerbyStars { get; init; } = 3;
    public int RaceStars { get; init; } = 3;
    public int PaceStars { get; init; } = 3;
    /// <summary>Plan §9.3: about 32 ± 2 per car.</summary>
    public int Budget => Speed + Accel + Handling + Weight + Toughness;

    // How far one rating point moves the physics, from the shared baseline tuning (a "6.5" car): see RatingScale.
    private static float MassPerPoint => RatingScale.Mass;
    private static float AccelPerPoint => RatingScale.Accel;
    private static float TopSpeedPerPoint => RatingScale.TopSpeed;
    private static float GripPerPoint => RatingScale.Grip;
    private static float SteerPerPoint => RatingScale.Steer;
    private static float ToughPerPoint => RatingScale.Tough;

    public float Mass => 1400f * (1f + MassPerPoint * (Weight - 6.5f));

    /// <summary>The referee's per-car toughness (damage taken is divided by it): 0.76 at 3, 1.25 at 10.</summary>
    public float DamageToughness => 1f + ToughPerPoint * (Toughness - 6.5f);

    /// <summary>The car's handling: the shared baseline (<see cref="CarTuning"/>) scaled by the ratings.</summary>
    public CarTuning Tuning(CarTuning? baseline = null)
    {
        var b = baseline ?? new CarTuning();
        var t = CarTuning.FromJson(b.ToJson());
        float m = Mass / b.Mass;                       // forces scale with mass so the ratings mean the same per kg
        float accel = 1f + AccelPerPoint * (Accel - 6.5f);
        float top = 1f + TopSpeedPerPoint * (Speed - 7f);
        float grip = 1f + GripPerPoint * (Handling - 6f);
        float steer = 1f + SteerPerPoint * (Handling - 6f);
        t.Mass = Mass;
        t.SpringRate = b.SpringRate * m;
        t.DamperCompression = b.DamperCompression * m;
        t.DamperRebound = b.DamperRebound * m;
        t.AntiRollFront = b.AntiRollFront * m;
        t.AntiRollRear = b.AntiRollRear * m;
        t.BrakeForce = b.BrakeForce * m;
        t.ReverseForce = b.ReverseForce * m;
        t.TurnAroundTorque = b.TurnAroundTorque * m;
        t.FastSteerYawTorque = b.FastSteerYawTorque * m * steer;
        t.MaxDriveForce = b.MaxDriveForce * m * accel;
        t.EnginePower = b.EnginePower * m * accel;
        // Top speed: power = drag * v^3, so the drag sets where the power runs out.
        float v0 = MathF.Cbrt(b.EnginePower / b.DragCoefficient);
        float v = v0 * top;
        t.DragCoefficient = t.EnginePower / (v * v * v);
        t.RollingResistance = b.RollingResistance * m;
        t.FrontGrip = b.FrontGrip * grip;
        t.RearGrip = b.RearGrip * grip;
        t.MaxSteerLowSpeed = b.MaxSteerLowSpeed * steer;
        t.MaxSteerHighSpeed = b.MaxSteerHighSpeed * steer;
        t.RearWheelDrive = !FrontDrive;
        if (FrontDrive)
        {
            // Front drive: the fronts carry drive and steer (a little less cornering grip left), the tail is
            // stable and steady under power.
            t.FrontGrip *= 1.03f;
            t.RearGrip *= 1.04f;
        }
        return t;
    }
}

/// <summary>
/// How far one rating point moves the physics (the balance knobs, plan §9.3). Sims can override them with
/// --ratingscale=mass:0.035,tough:0.06 (keys: mass, accel, top, grip, steer, tough).
/// </summary>
public static class RatingScale
{
    /// <summary>
    /// Mass per weight point: weight 3 -> 1180 kg, weight 9 -> 1560 kg (1.32x). In races a hit spins a lighter car
    /// further, so mass decides the race points (0.055 put the heavy cars at +13%); in the derby it matters less.
    /// </summary>
    public static float Mass = 0.045f;
    /// <summary>Launch force and power per kg, per acceleration point.</summary>
    public static float Accel = 0.05f;
    /// <summary>Top speed per speed point.</summary>
    public static float TopSpeed = 0.03f;
    /// <summary>Cornering grip per handling point.</summary>
    public static float Grip = 0.025f;
    /// <summary>Steering lock and pivot per handling point.</summary>
    public static float Steer = 0.02f;
    /// <summary>
    /// Damage taken is divided by 1 + this per toughness point (from 6.5): 0.93 (toughness 3) to 1.07 (10). It
    /// decides the derby: 0.07 (0.76 to 1.25) put the tough cars at +18-27% there, 0.02 keeps all within the target.
    /// </summary>
    public static float Tough = 0.02f;

    static RatingScale()
    {
        if (Core.DevTools.Instance?.Arg("ratingscale") is not { } arg) return;
        foreach (var pair in arg.Split(','))
        {
            var kv = pair.Split(':');
            if (kv.Length != 2 || !float.TryParse(kv[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float v)) continue;
            switch (kv[0])
            {
                case "mass": Mass = v; break;
                case "accel": Accel = v; break;
                case "top": TopSpeed = v; break;
                case "grip": Grip = v; break;
                case "steer": Steer = v; break;
                case "tough": Tough = v; break;
            }
        }
        Godot.GD.Print($"[RatingScale] mass {Mass} accel {Accel} top {TopSpeed} grip {Grip} steer {Steer} tough {Tough}");
    }
}

/// <summary>The v1 roster: 9 cars, 3 per region (plan §9.1; the cut-line cars are 1, 3, 5, 7, 9 and 12).</summary>
public static class CarCatalog
{
    public static readonly CarInfo[] All =
    {
        new("brougham", 1, "Aldridge", "Brougham 400", Region.US, "sedan", "V8", false, 6, 5, 3, 9, 9,
            "Heavy and tough: a battering ram, slow to turn.") { DerbyStars = 5, RaceStars = 3, PaceStars = 1 },
        new("homestead", 2, "Aldridge", "Homestead", Region.US, "wagon", "V8", false, 5, 5, 3, 9, 10,
            "The toughest car in the Bowl: a long steel tail for reverse-ramming.") { DerbyStars = 5, RaceStars = 5, PaceStars = 1 },
        new("interceptor", 3, "Columbia", "Interceptor GT", Region.US, "fastback", "V8", false, 8, 8, 5, 6, 6,
            "Muscle: quick in a straight line, wide in the turns.") { DerbyStars = 2, RaceStars = 2, PaceStars = 2 },
        new("nordvik", 5, "Nordvik", "Fjord DL", Region.EU, "sedan", "I4", false, 5, 5, 5, 7, 9,
            "Balanced and solid: a strong rear for reverse-ramming.") { DerbyStars = 4, RaceStars = 4, PaceStars = 1 },
        new("adler", 6, "Adler", "Falke 2.7i", Region.EU, "coupé", "I6", false, 8, 7, 8, 5, 6,
            "A sharp all-rounder: fast and precise, medium weight.") { DerbyStars = 2, RaceStars = 1, PaceStars = 5 },
        new("volant", 7, "Volant", "105 GTi", Region.EU, "hot hatch", "I4", true, 7, 8, 8, 3, 4,
            "Front-drive hot hatch: darts into gaps, but fragile.") { DerbyStars = 4, RaceStars = 3, PaceStars = 5 },
        new("hikari", 9, "Hikari", "Kaze GT", Region.JP, "liftback", "I4", false, 7, 7, 9, 3, 4,
            "The best handling in the field: light, so stay out of the crush.") { DerbyStars = 2, RaceStars = 3, PaceStars = 5 },
        new("ryujin", 11, "Ryujin", "Raiden GTS", Region.JP, "turbo sedan", "I6 turbo", false, 8, 8, 6, 6, 6,
            "Turbo sedan: fast and medium-tough, a racer that can take a hit.") { DerbyStars = 3, RaceStars = 3, PaceStars = 3 },
        new("kitsune", 12, "Kitsune", "Rotary GS", Region.JP, "coupé", "rotary", false, 9, 7, 8, 4, 3,
            "The fastest car: light and nimble, but the nose is soft.") { DerbyStars = 1, RaceStars = 3, PaceStars = 5 },
    };

    private static readonly Dictionary<string, CarInfo> ById = All.ToDictionary(c => c.Id);

    /// <summary>Model ids in roster order (the AI cycle through them).</summary>
    public static readonly string[] Ids = All.Select(c => c.Id).ToArray();

    public static CarInfo? Find(string id) => ById.TryGetValue(id ?? "", out var c) ? c : null;

    /// <summary>The referee's toughness for a model (1 for the grey box or an unknown id).</summary>
    public static float ToughnessOf(string id) => Find(id)?.DamageToughness ?? 1f;
}
