using System;
using System.Collections.Generic;
using System.Linq;

namespace DestructionRacing.Rules;

/// <summary>The two championships (plan §4.5): Wreckin' (4 track events and the Bowl as the finale) and Stock Car (4 track events).</summary>
public enum ChampionshipKind { Wreckin, StockCar }

/// <summary>Where an event is held: the Crossroads Speedway layouts or the Bowl.</summary>
public enum Venue { Oval, Figure8, Bowl }

/// <summary>The three lighting presets of every venue.</summary>
public enum Light { Afternoon, GoldenHour, Night }

/// <summary>One event of a season.</summary>
public sealed record CalendarEvent(GameMode Mode, Venue Venue, bool Reverse, Light Light)
{
    public bool IsDerby => Mode == GameMode.DestructionDerby;
}

/// <summary>
/// The season calendars, one per division (plan §4.5). Each division brings new layout and variant combinations, so
/// promotion opens them for the other modes too (<see cref="Unlocks"/>): Division 4 races forward in the afternoon and
/// at golden hour, Division 3 in reverse, Division 2 adds night forward and the Bowl at night, Division 1 reverse at night.
/// A Wreckin' season is its four track events with the Bowl as the finale; a Stock Car season the same four track events.
/// </summary>
public static class Calendar
{
    private static CalendarEvent Track(Venue v, bool rev, Light l) => new(GameMode.WreckinRacing, v, rev, l);

    private static readonly CalendarEvent[][] TrackEvents =
    {
        // Division 1: everything at night or at dusk.
        new[] { Track(Venue.Figure8, true, Light.Night), Track(Venue.Oval, false, Light.Night), Track(Venue.Oval, true, Light.Night), Track(Venue.Figure8, false, Light.GoldenHour) },
        // Division 2: night arrives.
        new[] { Track(Venue.Oval, false, Light.Night), Track(Venue.Figure8, true, Light.GoldenHour), Track(Venue.Figure8, false, Light.Night), Track(Venue.Oval, true, Light.Afternoon) },
        // Division 3: both layouts in reverse.
        new[] { Track(Venue.Oval, true, Light.Afternoon), Track(Venue.Figure8, true, Light.Afternoon), Track(Venue.Oval, true, Light.GoldenHour), Track(Venue.Figure8, true, Light.GoldenHour) },
        // Division 4: forward, afternoon and golden hour.
        new[] { Track(Venue.Figure8, false, Light.Afternoon), Track(Venue.Oval, false, Light.Afternoon), Track(Venue.Figure8, false, Light.GoldenHour), Track(Venue.Oval, false, Light.GoldenHour) },
    };

    private static readonly Light[] BowlLight = { Light.Night, Light.Night, Light.GoldenHour, Light.Afternoon };

    public static IReadOnlyList<CalendarEvent> For(ChampionshipKind kind, int division)
    {
        int d = Math.Clamp(division, 1, 4) - 1;
        var mode = kind == ChampionshipKind.StockCar ? GameMode.StockCarRacing : GameMode.WreckinRacing;
        var events = TrackEvents[d].Select(e => e with { Mode = mode }).ToList();
        if (kind == ChampionshipKind.Wreckin)
            events.Add(new CalendarEvent(GameMode.DestructionDerby, Venue.Bowl, false, BowlLight[d]));
        return events;
    }
}

/// <summary>
/// What the championship has opened for Single Event, Practice and Time Trial (plan §4.5): every combination raced in
/// the calendars of the divisions reached so far; winning Division 1 adds the bonus variant (the Bowl's face-off start,
/// two facing rows) and the champion livery set.
/// </summary>
public static class Unlocks
{
    /// <summary>Open with <paramref name="bestDivision"/> reached (4 at the start, 1 = the top)?</summary>
    public static bool IsOpen(int bestDivision, Venue venue, bool reverse, Light light)
    {
        for (int d = 4; d >= Math.Clamp(bestDivision, 1, 4); d--)
            if (Calendar.For(ChampionshipKind.Wreckin, d).Any(e => e.Venue == venue && e.Reverse == reverse && e.Light == light))
                return true;
        return false;
    }

    /// <summary>What reaching <paramref name="division"/> opens, for the promotion screen ("REVERSE ON BOTH LAYOUTS").</summary>
    public static IReadOnlyList<string> OpenedBy(int division)
    {
        if (division >= 4) return Array.Empty<string>();
        var list = new List<string>();
        foreach (var e in Calendar.For(ChampionshipKind.Wreckin, division))
        {
            if (IsOpen(division + 1, e.Venue, e.Reverse, e.Light)) continue;
            string name = e.Venue switch
            {
                Venue.Bowl => "THE BOWL",
                Venue.Oval => "THE OVAL",
                _ => "THE FIGURE-8",
            };
            string variant = (e.Venue == Venue.Bowl ? "" : e.Reverse ? " IN REVERSE" : " FORWARD") + e.Light switch
            {
                Light.Night => " AT NIGHT",
                Light.GoldenHour => " AT GOLDEN HOUR",
                _ => " IN THE AFTERNOON",
            };
            string text = name + variant;
            if (!list.Contains(text)) list.Add(text);
        }
        return list;
    }
}

/// <summary>One finished season, for the career history.</summary>
public sealed record SeasonRecord(int Season, int Division, int Place, int Points, bool Promoted);

/// <summary>
/// A career in one championship (plan §4.5): the 20-driver field in 4 divisions of 5, the season in progress (its
/// calendar comes from the player's division) and what happened in earlier seasons. Every event has all 20 drivers;
/// the player is ranked within their division and goes up by topping it, otherwise they repeat the same events.
/// The AI drivers swap between divisions each season (<see cref="Championship.EndSeason"/>).
/// </summary>
public sealed class Career
{
    public const int PlayerId = 0;
    public const string PlayerKey = "@player";

    private readonly List<Driver> _drivers;
    private readonly Dictionary<int, int> _lastEvent = new();
    private readonly List<SeasonRecord> _history = new();

    /// <summary>A new career: the player starts in the bottom division (or <paramref name="playerDivision"/>).</summary>
    public Career(ChampionshipKind kind, RulesConfig rules, IEnumerable<(string Name, int Division)> ai, int seed, int playerDivision = 0)
    {
        Kind = kind;
        Rules = rules;
        Seed = seed;
        _drivers = new List<Driver> { new(PlayerId, PlayerKey, true, playerDivision > 0 ? playerDivision : rules.DivisionCount) };
        int id = 1;
        foreach (var (name, division) in ai) _drivers.Add(new Driver(id++, name, false, division));
        Table = new Championship(rules, _drivers);
        Attempts = 1;
    }

    public ChampionshipKind Kind { get; }
    public RulesConfig Rules { get; }
    public Championship Table { get; }
    public int Seed { get; }
    public int Season { get; private set; } = 1;
    /// <summary>The next event of the season (0-based); equal to the calendar's length once the season is done.</summary>
    public int EventIndex { get; private set; }
    /// <summary>Seasons started in the player's current division (1 in the first).</summary>
    public int Attempts { get; private set; }
    public bool Champion { get; private set; }
    public IReadOnlyList<Driver> Drivers => _drivers;
    public Driver Player => _drivers[0];
    public int PlayerDivision => Player.Division;
    public IReadOnlyList<CalendarEvent> Events => Calendar.For(Kind, PlayerDivision);
    public CalendarEvent? NextEvent => EventIndex < Events.Count ? Events[EventIndex] : null;
    public bool SeasonComplete => EventIndex >= Events.Count;
    public bool IsFinale => Kind == ChampionshipKind.Wreckin && EventIndex == Events.Count - 1;
    /// <summary>Each driver's points in the last event recorded (for the points tally).</summary>
    public IReadOnlyDictionary<int, int> LastEventPoints => _lastEvent;
    public IReadOnlyList<SeasonRecord> History => _history;

    public Driver? Find(string name) => _drivers.FirstOrDefault(d => d.Name == name);

    /// <summary>The seed of the next event, so the same save always meets the same field.</summary>
    public int EventSeed => unchecked(Seed * 7919 + Season * 101 + EventIndex * 13 + 1);

    public IReadOnlyList<Standing> PlayerTable => Table.Table(PlayerDivision);
    public int PlayerPlace => PlayerTable.ToList().FindIndex(s => s.Driver.IsPlayer) + 1;

    /// <summary>Records the results of the next event (every driver of the field) and moves on.</summary>
    public void RecordEvent(IEnumerable<EventResult> results)
    {
        if (SeasonComplete) throw new InvalidOperationException("The season is over; call EndSeason first.");
        var list = results.ToList();
        Table.RecordEvent(list);
        _lastEvent.Clear();
        foreach (var r in list) _lastEvent[r.DriverId] = r.Points;
        EventIndex++;
    }

    /// <summary>
    /// Ends the season (promotion, the AI swaps, points reset) and starts the next one in the player's new division.
    /// </summary>
    public SeasonOutcome EndSeason()
    {
        if (!SeasonComplete) throw new InvalidOperationException("The season is not over yet.");
        int division = PlayerDivision, place = PlayerPlace;
        int points = Table.All.First(s => s.Driver.IsPlayer).Points;
        var outcome = Table.EndSeason();
        _history.Add(new SeasonRecord(Season, division, place, points, outcome.PlayerPromoted || outcome.PlayerWonChampionship));
        if (outcome.PlayerWonChampionship) Champion = true;
        Attempts = outcome.PlayerPromoted ? 1 : Attempts + 1;
        Season++;
        EventIndex = 0;
        _lastEvent.Clear();
        return outcome;
    }

    // ------------------------------------------------------------------ save and load

    /// <summary>The save file's content (serialized as JSON by the game).</summary>
    public sealed class Save
    {
        public int Version { get; set; } = 1;
        public string Kind { get; set; } = "";
        public int Season { get; set; }
        public int EventIndex { get; set; }
        public int Attempts { get; set; }
        public int Seed { get; set; }
        public bool Champion { get; set; }
        public List<DriverSave> Drivers { get; set; } = new();
        public Dictionary<string, int> LastEvent { get; set; } = new();
        public List<SeasonRecord> History { get; set; } = new();
    }

    public sealed record DriverSave(string Name, int Division, int Points, int Wrecks, int BestFinish);

    public Save ToSave() => new()
    {
        Kind = Kind.ToString(),
        Season = Season,
        EventIndex = EventIndex,
        Attempts = Attempts,
        Seed = Seed,
        Champion = Champion,
        Drivers = Table.All.OrderBy(s => s.Driver.Id)
            .Select(s => new DriverSave(s.Driver.Name, s.Driver.Division, s.Points, s.Wrecks, s.BestFinish)).ToList(),
        LastEvent = _lastEvent.ToDictionary(kv => _drivers[kv.Key].Name, kv => kv.Value),
        History = _history.ToList(),
    };

    /// <summary>
    /// Restores a career. Drivers the save does not know (a newer roster) are left out and the ones it names but the
    /// roster lost are kept by name, so a save stays loadable; the division sizes must still add up.
    /// </summary>
    public static Career Load(Save save, RulesConfig rules)
    {
        if (!Enum.TryParse(save.Kind, out ChampionshipKind kind)) throw new FormatException($"Unknown championship '{save.Kind}'.");
        var player = save.Drivers.FirstOrDefault(d => d.Name == PlayerKey) ?? throw new FormatException("The save has no player.");
        var ai = save.Drivers.Where(d => d.Name != PlayerKey).ToList();
        Career career;
        try { career = new Career(kind, rules, ai.Select(d => (d.Name, d.Division)), save.Seed, player.Division); }
        catch (ArgumentException e) { throw new FormatException(e.Message); }
        foreach (var d in save.Drivers)
            if (career.Find(d.Name) is { } driver)
                career.Table.Restore(driver.Id, d.Points, d.Wrecks, d.BestFinish);
        career.Season = Math.Max(1, save.Season);
        career.EventIndex = Math.Clamp(save.EventIndex, 0, career.Events.Count);
        career.Attempts = Math.Max(1, save.Attempts);
        career.Champion = save.Champion;
        foreach (var (name, points) in save.LastEvent)
            if (career.Find(name) is { } driver) career._lastEvent[driver.Id] = points;
        career._history.AddRange(save.History);
        return career;
    }
}
