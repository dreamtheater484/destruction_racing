namespace DestructionRacing.Rules;

/// <summary>When a race ends (DD2 PC vs DD1 PC behaviour).</summary>
public enum RaceEndRule
{
    /// <summary>DD2: the race continues until the player finishes or is wrecked.</summary>
    PlayerFinishesOrWrecked,
    /// <summary>DD1: the race ends when the first car crosses the line.</summary>
    FirstCarFinishes,
}

/// <summary>Tunables for spin detection and attribution (plan §5.3).</summary>
public sealed record SpinTuning
{
    /// <summary>Minimum hit energy (J) for a contact to count as a hit.</summary>
    public float HitMinEnergy { get; init; } = 4_000f;
    /// <summary>Hit energy needed to take over attribution while the victim is already spinning.</summary>
    public float StealMinEnergy { get; init; } = 12_000f;
    /// <summary>Max seconds between the hit and the start of rotation.</summary>
    public float AttributionWindow { get; init; } = 1.0f;
    /// <summary>Yaw rate (deg/s) above which the victim counts as rotating.</summary>
    public float MinYawRateDeg { get; init; } = 60f;
    /// <summary>Seconds below the yaw-rate threshold before a spin is closed.</summary>
    public float StopGrace { get; init; } = 0.35f;
    /// <summary>Hard cap on a spin window's length (s).</summary>
    public float MaxDuration { get; init; } = 5f;
    /// <summary>The victim must be driving at least this fast (m/s) when hit.</summary>
    public float MinVictimSpeed { get; init; } = 2f;
    /// <summary>Tolerance (deg) below each tier threshold that still awards the tier.</summary>
    public float TierToleranceDeg { get; init; } = 5f;
}

/// <summary>
/// All rule values for one ruleset. Values come from the DD1/DD2 PC manuals (plan §4.0, §20);
/// values marked ✱ in the plan are assumptions and stay tunable here.
/// </summary>
public sealed record RulesConfig
{
    public required string Name { get; init; }

    // Field and championship structure
    public required int DivisionCount { get; init; }
    public required int DriversPerDivision { get; init; }
    public int GridSize => DivisionCount * DriversPerDivision;
    public required int WreckinSeasonTrackEvents { get; init; }
    public required int StockCarSeasonEvents { get; init; }
    public bool PlayerCanBeRelegated { get; init; }

    // Scoring
    public required int Spin90Points { get; init; }
    public required int Spin180Points { get; init; }
    public required int Spin360Points { get; init; }
    public required int WreckPoints { get; init; }
    /// <summary>All points are multiplied when the damaged car is in 1st place.</summary>
    public int LeaderMultiplier { get; init; } = 2;
    public required int EventPointsCap { get; init; }
    public required int[] WreckinFinishBonus { get; init; }
    public required int[] StockCarFinishPoints { get; init; }
    public bool BowlScoresSpins { get; init; } = true;
    public required int BowlSurvivorBonus { get; init; }

    // Race flow and physics features
    public required RaceEndRule RaceEnd { get; init; }
    public required bool Rollovers { get; init; }
    public required bool WheelLoss { get; init; }
    public required bool PitStops { get; init; }
    /// <summary>Pit stops allowed per Wreckin' race; -1 = unlimited.</summary>
    public int WreckinPitStopsAllowed { get; init; } = 1;
    /// <summary>Pit stops allowed per Stock Car race; -1 = unlimited.</summary>
    public int StockCarPitStopsAllowed { get; init; } = -1;
    public float PitStopTimeLimit { get; init; } = 5f;

    // Damage
    public float FrontToughness { get; init; } = 1.0f;
    public float SideToughness { get; init; } = 1.2f;
    public float RearToughness { get; init; } = 2.5f;
    /// <summary>Energy (J) below which a hit causes no damage.</summary>
    public float DamageEnergyThreshold { get; init; } = 3_000f;
    /// <summary>Damage scale: health points per J^0.8 above the threshold.</summary>
    public float DamageScale { get; init; } = 0.012f;
    public float DamageExponent { get; init; } = 0.8f;
    public float WreckAttributionWindow { get; init; } = 5f;
    /// <summary>A car that cannot move for this long counts as wrecked (✱ A9).</summary>
    public float ImmobilisedWreckTime { get; init; } = 10f;

    public SpinTuning Spin { get; init; } = new();

    private static readonly int[] StockCarTable =
        { 50, 40, 35, 30, 25, 20, 18, 16, 14, 12, 10, 8, 7, 6, 5, 4, 3, 2, 1, 0 };

    /// <summary>v1 default: DD2 PC rules (4 leagues × 5 drivers).</summary>
    public static RulesConfig Standard { get; } = new()
    {
        Name = "Standard (DD2 PC)",
        DivisionCount = 4,
        DriversPerDivision = 5,
        WreckinSeasonTrackEvents = 4,
        StockCarSeasonEvents = 4,
        PlayerCanBeRelegated = false,
        Spin90Points = 10,
        Spin180Points = 25,
        Spin360Points = 50,
        WreckPoints = 25,
        EventPointsCap = 999,
        WreckinFinishBonus = new[] { 25, 15, 10 },          // ✱ A1
        StockCarFinishPoints = StockCarTable,               // ✱ A2
        BowlScoresSpins = true,
        BowlSurvivorBonus = 25,                             // ✱ A15
        RaceEnd = RaceEndRule.PlayerFinishesOrWrecked,      // ✱ A11
        Rollovers = true,
        WheelLoss = true,
        PitStops = true,
        WreckinPitStopsAllowed = 1,
        StockCarPitStopsAllowed = -1,
    };

    /// <summary>v1.1: DD1 PC rules (4 leagues × 4 drivers, 99-point cap).</summary>
    public static RulesConfig DD1Classic { get; } = new()
    {
        Name = "DD1 Classic (PC)",
        DivisionCount = 4,
        DriversPerDivision = 4,
        WreckinSeasonTrackEvents = 5,
        StockCarSeasonEvents = 5,
        PlayerCanBeRelegated = false,
        Spin90Points = 2,
        Spin180Points = 4,
        Spin360Points = 10,
        WreckPoints = 10,
        EventPointsCap = 99,
        WreckinFinishBonus = new[] { 10, 6, 4 },
        StockCarFinishPoints = StockCarTable[..16],
        BowlScoresSpins = true,
        BowlSurvivorBonus = 10,                             // ✱ A15
        RaceEnd = RaceEndRule.FirstCarFinishes,
        Rollovers = false,
        WheelLoss = false,
        PitStops = false,
        WreckinPitStopsAllowed = 0,
        StockCarPitStopsAllowed = 0,
    };
}
