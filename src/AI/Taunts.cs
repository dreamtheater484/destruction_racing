using System;
using System.Collections.Generic;
using DestructionRacing.Rules;

namespace DestructionRacing.AI;

/// <summary>
/// Driver taunts (plan §10.2): a radio line from a named driver after they spin or wreck you, or after you spin or
/// wreck them, by archetype. A per-driver cooldown and a global one keep it occasional; <see cref="Frequency"/>
/// is the setting (--taunts=off|some|all; the M7 options menu will show it). Text only: voiced taunts are v1.1.
/// </summary>
public sealed class Taunts
{
    public enum Level { Off, Some, All }

    /// <summary>How often drivers talk. Some = most wrecks and about half the spins.</summary>
    public static Level Frequency { get; set; } = Level.Some;

    public enum Moment { SpunYou, WreckedYou, YouSpunMe, YouWreckedMe }

    public const double DriverCooldown = 30.0;
    public const double GlobalCooldown = 8.0;

    private readonly Dictionary<DriverInfo, double> _lastBy = new();
    private readonly Random _rng;
    private double _last = -100.0;

    public Taunts(int seed)
    {
        _rng = new Random(seed * 31 + 7);
        Frequency = Core.DevTools.Instance?.Arg("taunts") switch
        {
            "off" => Level.Off,
            "all" => Level.All,
            "some" => Level.Some,
            _ => Frequency,
        };
    }

    /// <summary>
    /// The line a driver says for a referee event involving the player, or null (no taunt: wrong event, the
    /// cooldowns, the frequency setting). <paramref name="driverOf"/> maps car ids to the AI drivers.
    /// </summary>
    public (DriverInfo Driver, string Line)? For(RefereeEvent e, int player, Func<int, DriverInfo?> driverOf)
    {
        if (Frequency == Level.Off) return null;
        (int speaker, Moment moment)? m = e.Kind switch
        {
            RefereeEventKind.SpinScored when e.Other == player && e.Car != player => (e.Car, Moment.SpunYou),
            RefereeEventKind.SpinScored when e.Car == player && e.Other != player => (e.Other, Moment.YouSpunMe),
            RefereeEventKind.Wrecked when e.Car == player && e.Other > 0 && e.Other != player => (e.Other, Moment.WreckedYou),
            RefereeEventKind.Wrecked when e.Other == player && e.Car != player => (e.Car, Moment.YouWreckedMe),
            _ => null,
        };
        if (m is not var (speaker, moment) || driverOf(speaker) is not { } driver) return null;
        bool wreck = moment is Moment.WreckedYou or Moment.YouWreckedMe;
        if (e.Time - _last < (wreck ? 2.0 : GlobalCooldown)) return null;
        if (!wreck && _lastBy.TryGetValue(driver, out double t) && e.Time - t < DriverCooldown) return null;
        if (Frequency == Level.Some && _rng.NextDouble() > (wreck ? 0.9 : 0.45)) return null;
        var lines = Lines(driver.Archetype, moment);
        _last = e.Time;
        _lastBy[driver] = e.Time;
        return (driver, lines[_rng.Next(lines.Length)]);
    }

    /// <summary>Three-letter nation for the radio banner.</summary>
    public static string NationCode(string nation) => nation switch
    {
        "USA" => "USA", "Sweden" => "SWE", "Ireland" => "IRL", "Germany" => "GER", "France" => "FRA",
        "Japan" => "JPN", "Netherlands" => "NED", "Poland" => "POL", "Mexico" => "MEX", "Italy" => "ITA",
        "Scotland" => "SCO", "Belgium" => "BEL",
        _ => nation.Length >= 3 ? nation[..3].ToUpperInvariant() : nation.ToUpperInvariant(),
    };

    private static string[] Lines(string archetype, Moment m) => (archetype, m) switch
    {
        ("Bruiser", Moment.SpunYou) => new[] { "Out of my way!", "That's how it's done in the Bowl.", "Should've moved, kid." },
        ("Bruiser", Moment.WreckedYou) => new[] { "Scrap metal. Next!", "Tow truck's on its way for you." },
        ("Bruiser", Moment.YouSpunMe) => new[] { "Lucky shot.", "You'll pay for that." },
        ("Bruiser", Moment.YouWreckedMe) => new[] { "Next time I'm coming for you.", "Enjoy it while it lasts." },
        ("Racer", Moment.SpunYou) => new[] { "Sorry, you were in my line.", "Racing room's over there." },
        ("Racer", Moment.WreckedYou) => new[] { "Nothing personal.", "Should've stayed off my line." },
        ("Racer", Moment.YouSpunMe) => new[] { "Hey! Clean racing!", "Watch your mirrors." },
        ("Racer", Moment.YouWreckedMe) => new[] { "That was dirty.", "My crew's going to love this." },
        ("Vulture", Moment.SpunYou) => new[] { "Wobbling already? I'll be back.", "Just softening you up." },
        ("Vulture", Moment.WreckedYou) => new[] { "Thanks for the points.", "Couldn't let that one go to waste." },
        ("Vulture", Moment.YouSpunMe) => new[] { "Easy, I'm only passing by.", "Not me, there's easier meat." },
        ("Vulture", Moment.YouWreckedMe) => new[] { "I was only looking!", "Picked the wrong carcass." },
        ("Grudge", Moment.SpunYou) => new[] { "That's for last time.", "Remember me?" },
        ("Grudge", Moment.WreckedYou) => new[] { "Now we're even.", "Told you I'd get you." },
        ("Grudge", Moment.YouSpunMe) => new[] { "I won't forget this.", "Your number's noted." },
        ("Grudge", Moment.YouWreckedMe) => new[] { "This isn't over.", "Next event. You and me." },
        ("Psycho", Moment.SpunYou) => new[] { "Ha! Again! Again!", "Round and round you go!" },
        ("Psycho", Moment.WreckedYou) => new[] { "BOOM! Did you see that?!", "Best. Crash. Ever." },
        ("Psycho", Moment.YouSpunMe) => new[] { "Ooh, do it again!", "Now it's personal!" },
        ("Psycho", Moment.YouWreckedMe) => new[] { "Worth it!", "What a ride!" },
        ("Rookie", Moment.SpunYou) => new[] { "Oh! Sorry! Was that me?", "I meant to do that. Probably." },
        ("Rookie", Moment.WreckedYou) => new[] { "Did I just... wow!", "Wait till my mum sees this!" },
        ("Rookie", Moment.YouSpunMe) => new[] { "Go easy, it's my first season!", "Which way is the front?" },
        ("Rookie", Moment.YouWreckedMe) => new[] { "Already?!", "Still lasted longer than last time." },
        _ => new[] { "See you in the Bowl." },
    };
}
