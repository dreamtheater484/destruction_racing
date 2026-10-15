using System;
using System.Collections.Generic;
using System.Linq;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.AI;

/// <summary>
/// One of the 19 named AI drivers (plan §10.2, as DD1's 19): who they are, what they drive, their number and
/// their signature livery, and the archetype that sets how they drive. All names are fictional.
/// </summary>
public sealed record DriverInfo(string Name, string Nation, string Car, int Number, Personality Personality, Livery Livery)
{
    /// <summary>Surname (or nickname) for the HUD and the results.</summary>
    public string Short => Name.Contains('"') ? Name.Split('"')[1] : Name.Split(' ')[^1];
    public string Archetype => Personality.Archetype;

    /// <summary>The signature livery with the event's division roof colour.</summary>
    public Livery LiveryFor(Color roof) => Livery with { Number = Number, Roof = roof };
}

public static class Drivers
{
    private static Color C(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f);
    private static readonly Color White = C(236, 236, 230), Black = C(18, 18, 20), Cream = C(230, 220, 190);

    /// <summary>
    /// The field. The archetype mix is the M4-balanced one (4 Bruisers, 4 Racers, 3 Vultures, 3 Grudge-holders,
    /// 2 Psychos, 3 Rookies); each car of the roster has two drivers, the Brougham three.
    /// </summary>
    public static readonly DriverInfo[] All =
    {
        // Bruisers: heavy metal, little finesse.
        new("Harlan \"Buck\" Doyle", "USA", "homestead", 3, Personality.Bruiser,
            new Livery(C(120, 30, 24), Cream, 4, Wear: 0.45f, Dirt: 0.5f, Sponsor: 6)),
        new("Wade Kettering", "USA", "brougham", 11, Personality.Bruiser,
            new Livery(C(22, 40, 88), White, 3, Metallic: 0.6f, Wear: 0.3f, Dirt: 0.35f, Sponsor: 7)),
        new("Sven Lindqvist", "Sweden", "nordvik", 45, Personality.Bruiser,
            new Livery(C(30, 70, 140), C(240, 200, 30), 1, Wear: 0.35f, Dirt: 0.4f, Sponsor: 3, Primer: "Fender_FL")),
        new("Shane O'Rourke", "Ireland", "brougham", 77, Personality.Bruiser,
            new Livery(C(26, 96, 46), White, 2, Wear: 0.5f, Dirt: 0.45f, Sponsor: 2, Primer: "Door_RL")),
        // Racers: clean and quick, they defend.
        new("Dieter Hollmann", "Germany", "adler", 9, Personality.Racer,
            new Livery(White, C(30, 90, 180), 1, Wear: 0.1f, Dirt: 0.2f, Sponsor: 1)),
        new("Théo Lacombe", "France", "volant", 14, Personality.Racer,
            new Livery(C(200, 30, 30), White, 5, Wear: 0.15f, Dirt: 0.25f, Sponsor: 0)),
        new("Kenji Moriyama", "Japan", "hikari", 86, Personality.Racer,
            new Livery(White, Black, 2, Wear: 0.1f, Dirt: 0.2f, Sponsor: 1)),
        new("Hiro Saeki", "Japan", "kitsune", 13, Personality.Racer,
            new Livery(C(20, 20, 24), C(220, 30, 40), 5, Metallic: 0.4f, Wear: 0.1f, Dirt: 0.2f, Sponsor: 5)),
        // Vultures: they circle and steal wrecks.
        new("Anke Vos", "Netherlands", "adler", 31, Personality.Vulture,
            new Livery(C(235, 110, 20), Black, 3, Wear: 0.25f, Dirt: 0.35f, Sponsor: 4)),
        new("Takeshi Okuda", "Japan", "ryujin", 32, Personality.Vulture,
            new Livery(C(90, 90, 96), C(230, 190, 30), 5, Metallic: 0.7f, Wear: 0.2f, Dirt: 0.3f, Sponsor: 0)),
        new("Earl \"Tank\" Mabry", "USA", "homestead", 66, Personality.Vulture,
            new Livery(C(70, 80, 50), Cream, 2, Wear: 0.6f, Dirt: 0.6f, Sponsor: 7, Primer: "Hood")),
        // Grudge-holders: hit them once and they remember.
        new("Marta Kowalczyk", "Poland", "nordvik", 18, Personality.GrudgeHolder,
            new Livery(C(110, 20, 60), White, 4, Wear: 0.3f, Dirt: 0.35f, Sponsor: 5)),
        new("Ryo Hasegawa", "Japan", "ryujin", 19, Personality.GrudgeHolder,
            new Livery(Black, C(200, 200, 205), 3, Metallic: 0.3f, Wear: 0.2f, Dirt: 0.3f, Sponsor: 3)),
        new("Darnell Price", "USA", "interceptor", 24, Personality.GrudgeHolder,
            new Livery(C(240, 190, 20), Black, 1, Wear: 0.25f, Dirt: 0.3f, Sponsor: 2)),
        // Psychos: they start the fights.
        new("Rosa Delgado", "Mexico", "interceptor", 22, Personality.Psycho,
            new Livery(C(150, 30, 140), C(240, 200, 30), 5, Metallic: 0.5f, Wear: 0.3f, Dirt: 0.3f, Sponsor: 4)),
        new("Giulia Ferretti", "Italy", "volant", 27, Personality.Psycho,
            new Livery(C(40, 160, 70), White, 1, Wear: 0.3f, Dirt: 0.35f, Sponsor: 6, Primer: "Door_FR")),
        // Rookies: keen, inconsistent.
        new("Aiko Tanabe", "Japan", "hikari", 4, Personality.Rookie,
            new Livery(C(80, 170, 220), White, 2, Wear: 0.1f, Dirt: 0.2f, Sponsor: 4)),
        new("Callum Reid", "Scotland", "kitsune", 8, Personality.Rookie,
            new Livery(C(240, 240, 235), C(20, 60, 160), 1, Wear: 0.15f, Dirt: 0.25f, Sponsor: 1)),
        new("Bram De Smet", "Belgium", "brougham", 51, Personality.Rookie,
            new Livery(C(200, 180, 140), C(120, 60, 30), 2, Wear: 0.35f, Dirt: 0.4f, Sponsor: 6)),
    };

    /// <summary>
    /// The 19 AI drivers in a seeded random grid order. With <paramref name="randomCars"/> (balance sims) every
    /// driver gets a random model instead of their own, so each car is tried with every archetype.
    /// </summary>
    public static List<DriverInfo> Field(Random rng, bool randomCars = false)
    {
        var field = All.OrderBy(_ => rng.Next()).ToList();
        if (randomCars)
            for (int i = 0; i < field.Count; i++)
                field[i] = field[i] with { Car = CarCatalog.Ids[rng.Next(CarCatalog.Ids.Length)] };
        return field;
    }

    /// <summary>The player's livery palette (DerbySetup's keyboard grid): 16 base colours, 2 rows of 8.</summary>
    public static readonly Color[] Palette =
    {
        C(204, 20, 15), C(235, 110, 20), C(240, 195, 20), C(40, 150, 60), C(25, 90, 50), C(50, 160, 210), C(25, 60, 160), C(20, 30, 70),
        C(130, 30, 130), C(220, 90, 140), C(236, 236, 230), C(160, 162, 168), C(70, 72, 78), C(18, 18, 20), C(120, 30, 24), C(200, 180, 140),
    };

    /// <summary>A second colour that reads on the given base (white on dark, black on light).</summary>
    public static Color Contrast(Color c) => c.Luminance > 0.45f ? Black : White;
}
