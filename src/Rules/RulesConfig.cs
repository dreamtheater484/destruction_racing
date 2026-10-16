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
    /// <summary>
    /// Minimum hit energy (J) for a contact to open a spin window and count as a hit for the rules. DD scores spins
    /// from taps ("tap the rear, watch it spin"): a 12 km/h side tap between two 1400 kg cars is ~4 kJ, so the
    /// threshold sits at ~11 km/h closing. (The engagement metrics use their own, higher "deliberate hit" bar.)
    /// </summary>
    public float HitMinEnergy { get; init; } = 3_000f;
    /// <summary>Hit energy needed to take over attribution while the victim is already spinning (~30 km/h closing).</summary>
    public float StealMinEnergy { get; init; } = 25_000f;
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
    /// <summary>Health restored by one pit repair press (plan §6.1 PIT_REPAIR_PER_PRESS) ✱.</summary>
    public float PitRepairPerPress { get; init; } = 3.5f;
    /// <summary>Repair presses per second at most (key echo is ignored as well).</summary>
    public float PitMaxPressRate { get; init; } = 15f;

    // Damage
    public float FrontToughness { get; init; } = 1.0f;
    public float SideToughness { get; init; } = 1.5f;
    public float RearToughness { get; init; } = 2.5f;
    /// <summary>Energy (J) below which a hit causes no damage (~15 km/h closing car-car, ~10 km/h into a wall).</summary>
    public float DamageEnergyThreshold { get; init; } = 6_000f;
    /// <summary>
    /// Repeated contact between the same two bodies within this window is grinding (piles, squeezes): it deals only
    /// GrindDamageScale of the damage, so pile-ups don't strip every car in seconds.
    /// </summary>
    public float GrindWindow { get; init; } = 0.8f;
    public float GrindDamageScale { get; init; } = 0.3f;
    /// <summary>
    /// Calibration ✱: damage starts at this share at GO and ramps up to full over OpeningCrunchTime seconds, so the
    /// opening crunch and first minute hurt everyone but rarely eliminate anyone (frustration targets: player
    /// wrecked in 30 s ≤ 5%, in 60 s ≤ 15%). M4: ×0.4 over 90 s (M1b: ×0.5 over 75 s), for the harder-hitting M4 AI.
    /// </summary>
    public float OpeningCrunchDamageScale { get; init; } = 0.4f;
    public float OpeningCrunchTime { get; init; } = 90f;
    /// <summary>
    /// Calibration ✱ (M1c): all damage in a race is scaled by this. Race contact is constant and mostly incidental
    /// (packs, crossover traffic, wrecks on the line), where the derby calibration assumes deliberate rams; at full
    /// damage most of the field and the player were wrecked before half distance (target: player ≤ 10%).
    /// </summary>
    public float RaceDamageScale { get; init; } = 0.5f;
    /// <summary>
    /// Scraping (DD Code T4): sustained contact sliding faster than ScrapeMinSpeed (m/s) grinds the touching zone
    /// at ScrapeRate health per second per m/s above it.
    /// </summary>
    public float ScrapeMinSpeed { get; init; } = 5.5f;
    public float ScrapeRate { get; init; } = 1.2f;
    /// <summary>
    /// Calibration ✱ (M4): in a one-sided ram the attacking car takes this share of the contact damage (its nose
    /// hits the other car's flank or tail); head-ons, where both attack, stay at full damage on both. Ramming still
    /// costs engine (plan §6.1), but less than it costs the car that was hit.
    /// </summary>
    public float AttackerDamageShare { get; init; } = 0.7f;
    /// <summary>Damage beyond a destroyed side or rear passes this share on to the front (so wrecks get finished off).</summary>
    public float OverflowToFront { get; init; } = 0.5f;
    /// <summary>
    /// Damage scale: health points per J^0.8 above the threshold. Tuned so a 40 km/h closing ram costs a front
    /// ~22 of 100 and an 80 km/h one ~30 after the knee (✱ A14, tuned with the derby sims: several solid front hits to wreck).
    /// </summary>
    public float DamageScale { get; init; } = 0.0045f;
    public float DamageExponent { get; init; } = 0.8f;
    /// <summary>Above this much damage from one hit, extra damage is compressed (DamageKneeSlope) …</summary>
    public float DamageKnee { get; init; } = 25f;
    public float DamageKneeSlope { get; init; } = 0.35f;
    /// <summary>… and one hit never takes more than this from a zone, so no single crash wrecks a car (DD feel).</summary>
    public float MaxDamagePerHit { get; init; } = 45f;
    /// <summary>Share of a corner hit's zone damage that also goes to the nearest wheel.</summary>
    public float WheelDamageShare { get; init; } = 0.5f;
    /// <summary>Below this rear health, rear wheels take double damage (and can come off, Standard).</summary>
    public float RearCriticalHealth { get; init; } = 25f;
    public float WreckAttributionWindow { get; init; } = 5f;
    /// <summary>A car that cannot move for this long counts as wrecked (✱ A9).</summary>
    public float ImmobilisedWreckTime { get; init; } = 10f;
    /// <summary>
    /// A car that tries to move for <see cref="ImmobilisedWreckTime"/> without getting further than this (m) from
    /// where it was also counts as immobilised (two cars locked together rock in place for ever otherwise).
    /// </summary>
    public float ImmobilisedRadius { get; init; } = 1.5f;
    /// <summary>How long a car may rock in place (see <see cref="ImmobilisedRadius"/>) before it counts as immobilised.</summary>
    public float PinnedWreckTime { get; init; } = 20f;

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
