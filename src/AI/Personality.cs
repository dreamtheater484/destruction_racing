namespace DestructionRacing.AI;

/// <summary>Driver traits, 0–1 (plan §10.2). The 19 named drivers arrive in M5; M1b uses the archetypes.</summary>
public sealed record Personality(
    string Archetype,
    float Skill,
    float Aggression,
    float Vengefulness,
    float Opportunism,
    float SelfPreservation)
{
    /// <summary>Bashes everyone, even in Stock Car (the manual's "Bouncer").</summary>
    public static Personality Bruiser => new("Bruiser", 0.6f, 0.9f, 0.5f, 0.4f, 0.25f);
    public static Personality Racer => new("Racer", 0.75f, 0.55f, 0.3f, 0.5f, 0.6f);
    /// <summary>Steals wrecks: goes for badly damaged cars.</summary>
    public static Personality Vulture => new("Vulture", 0.65f, 0.6f, 0.2f, 0.95f, 0.5f);
    public static Personality GrudgeHolder => new("Grudge", 0.6f, 0.7f, 0.95f, 0.4f, 0.4f);
    /// <summary>Provokes collisions and never backs off (DD1).</summary>
    public static Personality Psycho => new("Psycho", 0.5f, 1.0f, 0.6f, 0.3f, 0.0f);
    public static Personality Rookie => new("Rookie", 0.3f, 0.6f, 0.3f, 0.3f, 0.3f);

    /// <summary>The PlayerProxy bot's "average returning fan" profile (plan §17.1).</summary>
    public static Personality AverageFan => new("AverageFan", 0.55f, 0.65f, 0.4f, 0.6f, 0.5f);
    public static Personality SkilledPlayer => new("Skilled", 0.85f, 0.7f, 0.3f, 0.7f, 0.6f);
    /// <summary>Wreckin' strategy bots at equal skill (plan §17.4: racer vs. hunter points within ±15%).</summary>
    public static Personality RacerPlayer => new("RacerBot", 0.55f, 0.25f, 0.2f, 0.4f, 0.7f);
    public static Personality HunterPlayer => new("HunterBot", 0.55f, 0.95f, 0.4f, 0.8f, 0.3f);

    /// <summary>The M1b field mix for 19 AI cars.</summary>
    public static Personality[] FieldMix() => new[]
    {
        Bruiser, Bruiser, Bruiser, Bruiser,
        Racer, Racer, Racer, Racer,
        Vulture, Vulture, Vulture,
        GrudgeHolder, GrudgeHolder, GrudgeHolder,
        Psycho, Psycho,
        Rookie, Rookie, Rookie,
    };
}
