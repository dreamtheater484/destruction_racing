using System;
using System.Collections.Generic;
using System.Linq;
using DestructionRacing.Rules;
using DestructionRacing.Tracks;
using DestructionRacing.Vehicles;

namespace DestructionRacing.Modes;

/// <summary>What the front end can start (plan §4, §13).</summary>
public enum EventKind { Derby, Wreckin, StockCar, TimeTrial, Duel, TotalDestruction }

/// <summary>Where an event was started from, and so where its results go.</summary>
public enum EventOrigin { SingleEvent, Championship, Practice, TimeTrial }

/// <summary>
/// One event as the front end set it up: the venue and variant, the player's car and colours, the division (the AI's
/// pace and the derby's attention budget) and, in a championship, every AI driver's division for the roof colours.
/// </summary>
public sealed record EventRequest
{
    public EventKind Kind { get; init; }
    public EventOrigin Origin { get; init; }
    public SpeedwayLayout Layout { get; init; } = SpeedwayLayout.Figure8;
    public bool Reverse { get; init; }
    public BowlPreset Preset { get; init; }
    public string Car { get; init; } = "kitsune";
    public Livery? Livery { get; init; }
    public int Number { get; init; } = 7;
    /// <summary>The player's division (4 = rookies): the field's pace and aggression scale with it (plan §10.5).</summary>
    public int Division { get; init; } = 4;
    /// <summary>0 easy, 1 normal, 2 hard: shifts the division curve (plan §10.5).</summary>
    public int Difficulty { get; init; } = 1;
    /// <summary>Championship: each AI driver's division (by name), for the roof colours.</summary>
    public IReadOnlyDictionary<string, int>? DriverDivisions { get; init; }
    /// <summary>AI cars in the event (Practice; a Duel has one).</summary>
    public int Opponents { get; init; } = 19;
    /// <summary>Duel: the rival's name (one of the 19 drivers).</summary>
    public string? Rival { get; init; }
    /// <summary>The Bowl's face-off start: two facing rows instead of the ring (the champion's bonus variant).</summary>
    public bool FaceOff { get; init; }
    /// <summary>Practice in the Bowl: the AI cars stand still as targets.</summary>
    public bool Parked { get; init; }
    /// <summary>Laps; 0 = the layout's own.</summary>
    public int Laps { get; init; }
    public int Seed { get; init; } = 1;
    /// <summary>A line for the loading card and the HUD ("ROUND 3 OF 5 · DIVISION 4").</summary>
    public string Title { get; init; } = "";

    public bool IsRace => Kind is EventKind.Wreckin or EventKind.StockCar;
    public bool InBowl => Kind is EventKind.Derby or EventKind.Duel or EventKind.TotalDestruction;
    public GameMode Mode => Kind switch
    {
        EventKind.StockCar => GameMode.StockCarRacing,
        EventKind.Wreckin => GameMode.WreckinRacing,
        EventKind.TimeTrial => GameMode.TimeTrial,
        _ => GameMode.DestructionDerby,
    };

    /// <summary>The racing AI's pace: ×0.97 in Division 4 up to ×1.015 in Division 1 (✱ A22), ±0.02 for easy and hard.</summary>
    public float AiPace => 0.97f + 0.015f * (4 - Division) + 0.02f * (Difficulty - 1);
    /// <summary>
    /// The racing AI's aggression, how often it attacks (plan §10.5) ✱: ×0.8 in Division 4 up to ×1 (M6) in
    /// Division 1, and ∓0.2 for easy and hard. Wreckin' points come from spins, so the pace alone hardly changes them;
    /// in the M7 sims ×0.7 took about 7% off the AI's race points and still left 5 spins a lap.
    /// </summary>
    public float AiAggression => 1f - 0.2f * (Division - 1) / 3f + 0.2f * (Difficulty - 1);
    /// <summary>Extra (or fewer) derby AI allowed on the player at once: −1 easy, +1 hard.</summary>
    public int AttentionBonus => Difficulty - 1;

    public string Scene => Kind switch
    {
        EventKind.TimeTrial => "res://scenes/race/TimeTrial.tscn",
        EventKind.Wreckin or EventKind.StockCar => "res://scenes/race/Race.tscn",
        _ => "res://scenes/derby/Derby.tscn",
    };
}

/// <summary>One driver's result in a championship event.</summary>
public sealed record DriverResult(string Key, int Points, int Wrecks, int Place);

/// <summary>
/// The hand-over between the front end and the event scenes: the event to play, the championship it belongs to, and
/// the page to go back to.
/// </summary>
public static class Session
{
    public const string FrontEndScene = "res://scenes/menu/FrontEnd.tscn";

    /// <summary>The event being played (set by the front end; the mode scenes read it when they are the main scene).</summary>
    public static EventRequest? Request { get; set; }
    /// <summary>The championship in play (its slot), when <see cref="Request"/> is a championship event.</summary>
    public static CareerSlot? Career { get; set; }
    /// <summary>The front-end page to open on the way back.</summary>
    public static string ReturnPage { get; set; } = "main";
    /// <summary>Set when a championship event's results were recorded: the hub shows the points tally.</summary>
    public static bool TallyPending { get; set; }
    /// <summary>The event's results are in the career (no second recording, no "race again").</summary>
    public static bool Recorded { get; private set; }

    /// <summary>Starts an event from the front end.</summary>
    public static void Begin(EventRequest request, string returnPage)
    {
        Request = request;
        ReturnPage = returnPage;
        Recorded = false;
    }

    public static bool InChampionship => Request is { Origin: EventOrigin.Championship } && Career != null;

    /// <summary>
    /// The live division table for the event HUD (plan §13): the season points as they were before this event plus
    /// each driver's points in it so far (<paramref name="eventPoints"/> by driver key), best first.
    /// </summary>
    public static Func<IReadOnlyList<UI.EventHud.DivisionRow>>? DivisionTable(Func<string, int> eventPoints)
    {
        if (!InChampionship || Career == null) return null;
        var career = Career.Career;
        var roof = DerbyMode.RoofColor(career.PlayerDivision);
        var baseline = career.PlayerTable.Select(s => (s.Driver, s.Points)).ToList();
        return () => baseline
            .Select(b =>
            {
                string key = b.Driver.IsPlayer ? Rules.Career.PlayerKey : b.Driver.Name;
                string name = b.Driver.IsPlayer ? "YOU" : AI.Drivers.ByName(b.Driver.Name)?.Short.ToUpperInvariant() ?? b.Driver.Name;
                return new UI.EventHud.DivisionRow(name, roof, b.Points, eventPoints(key), b.Driver.IsPlayer);
            })
            .OrderByDescending(r => r.Season + r.Event)
            .ToList();
    }

    /// <summary>"DIVISION 4  ·  ROUND 2 OF 5" for the HUD table.</summary>
    public static string DivisionTitle => Career is { } c ? $"DIVISION {c.Career.PlayerDivision}  ·  ROUND {c.Career.EventIndex + (Recorded ? 0 : 1)} OF {c.Career.Events.Count}" : "";

    /// <summary>
    /// A championship event finished: records every driver's result in the career and saves it at once, so the
    /// result stands even if the game is closed on the results screen.
    /// </summary>
    public static void ReportResults(IReadOnlyList<DriverResult> results)
    {
        if (!InChampionship || Career == null || Recorded) return;
        var career = Career.Career;
        var list = new List<EventResult>();
        foreach (var r in results)
            if (career.Find(r.Key) is { } driver)
                list.Add(new EventResult(driver.Id, r.Points, r.Wrecks, r.Place));
        career.RecordEvent(list);
        CareerStore.Save(Career);
        Recorded = true;
        TallyPending = true;
        ReturnPage = "hub";
    }
}
