using System;
using System.Linq;
using DestructionRacing.AI;
using DestructionRacing.Core;
using DestructionRacing.Modes;
using DestructionRacing.Rules;
using DestructionRacing.Tracks;
using DestructionRacing.UI;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Menus;

/// <summary>Names and conversions shared by the event pages and the championship.</summary>
public static class EventText
{
    public static BowlPreset Preset(Light light) => (BowlPreset)(int)light;
    public static Light Light(BowlPreset preset) => (Light)(int)preset;
    public static SpeedwayLayout Layout(Venue venue) => venue == Venue.Oval ? SpeedwayLayout.Oval : SpeedwayLayout.Figure8;

    public static string VenueName(Venue venue) => venue switch
    {
        Venue.Bowl => "THE BOWL",
        Venue.Oval => "THE OVAL",
        _ => "THE FIGURE-8",
    };

    public static string LightName(BowlPreset p) => p switch
    {
        BowlPreset.GoldenHour => "GOLDEN HOUR",
        BowlPreset.Night => "NIGHT",
        _ => "AFTERNOON",
    };

    public static string KindName(EventKind kind) => kind switch
    {
        EventKind.Derby => "DESTRUCTION DERBY",
        EventKind.StockCar => "STOCK CAR RACING",
        EventKind.TimeTrial => "TIME TRIAL",
        EventKind.Duel => "DUEL",
        EventKind.TotalDestruction => "TOTAL DESTRUCTION",
        _ => "WRECKIN' RACING",
    };

    public static string ModeName(GameMode mode) => mode switch
    {
        GameMode.DestructionDerby => "DESTRUCTION DERBY",
        GameMode.StockCarRacing => "STOCK CAR RACING",
        _ => "WRECKIN' RACING",
    };

    /// <summary>"THE OVAL · REVERSE · NIGHT".</summary>
    public static string Where(Venue venue, bool reverse, BowlPreset light) =>
        venue == Venue.Bowl ? $"THE BOWL  ·  {LightName(light)}" : $"{VenueName(venue)}  ·  {(reverse ? "REVERSE" : "FORWARD")}  ·  {LightName(light)}";

    /// <summary>"Wreckin' Racing on the oval in reverse, at night".</summary>
    public static string Sentence(CalendarEvent e)
    {
        string mode = e.Mode switch
        {
            GameMode.DestructionDerby => "Destruction Derby",
            GameMode.StockCarRacing => "Stock Car Racing",
            _ => "Wreckin' Racing",
        };
        string light = e.Light switch { Rules.Light.Night => "at night", Rules.Light.GoldenHour => "at golden hour", _ => "in the afternoon" };
        if (e.Venue == Venue.Bowl) return $"{mode} in the Bowl, {light}";
        return $"{mode} on the {(e.Venue == Venue.Oval ? "oval" : "figure-8")}{(e.Reverse ? " in reverse" : "")}, {light}";
    }

    public static int LapsOf(Venue venue) => venue == Venue.Bowl ? 0 : SpeedwayData.Load().Get(Layout(venue)).Laps;

    /// <summary>Why a combination is locked, for the hint line.</summary>
    public static string LockedHint(Venue venue, bool reverse, BowlPreset light)
    {
        for (int d = 3; d >= 1; d--)
            if (Unlocks.IsOpen(d, venue, reverse, Light(light)))
                return $"LOCKED: reach Division {d} in a championship to open {Where(venue, reverse, light).ToLowerInvariant()} (or Options → Gameplay → Unlock everything).";
        return "LOCKED.";
    }
}

/// <summary>
/// The event set-up of Single Event, Practice and Time Trial (plan §13: mode, car, livery and number, layout): the
/// venue in the chosen light behind the panel with your car on pole or in the Bowl. Combinations the championship
/// has not opened yet are shown locked (plan §4.5).
/// </summary>
public partial class EventSetupPage : MenuPage
{
    public enum Flow { Single, Practice, TimeTrial }

    private readonly Flow _flow;
    private static readonly EventKind[] SingleKinds = { EventKind.Derby, EventKind.Wreckin, EventKind.StockCar, EventKind.Duel, EventKind.TotalDestruction };

    public EventSetupPage(Flow flow) => _flow = flow;

    private EventKind Kind => _flow switch
    {
        Flow.TimeTrial => EventKind.TimeTrial,
        Flow.Practice => FrontEndState.Venue == Venue.Bowl ? EventKind.Derby : EventKind.Wreckin,
        _ => FrontEndState.SingleKind,
    };

    private bool InBowl => Kind is EventKind.Derby or EventKind.Duel or EventKind.TotalDestruction;
    private Venue Venue => InBowl ? Venue.Bowl : FrontEndState.Venue == Venue.Bowl ? Venue.Figure8 : FrontEndState.Venue;
    private bool Reverse => !InBowl && FrontEndState.Reverse;
    private bool Open => Profile.IsOpen(Venue, Reverse, EventText.Light(FrontEndState.Light)) && (!FrontEndState.FaceOff || !InBowl || Profile.ChampionOpen);

    public override string Heading => _flow switch
    {
        Flow.TimeTrial => "TIME TRIAL",
        Flow.Practice => "PRACTICE",
        _ => "SINGLE EVENT",
    };

    public override string Subheading => EventText.Where(Venue, Reverse, FrontEndState.Light);

    public override string Hint => Open || Menu.Current?.Caption is not ("START" or "LIGHT" or "DIRECTION" or "LAYOUT" or "LINE-UP")
        ? base.Hint
        : !Profile.IsOpen(Venue, Reverse, EventText.Light(FrontEndState.Light))
            ? EventText.LockedHint(Venue, Reverse, FrontEndState.Light)
            : "LOCKED: win Division 1 in a championship to open the face-off start.";

    protected override float PanelTop => 240f;

    protected override void Build()
    {
        Menu.CaptionWidth = 250;
        if (_flow == Flow.Single)
            Menu.Add(new MenuItem("EVENT")
            {
                Value = () => EventText.KindName(Kind),
                Change = d =>
                {
                    int i = Array.IndexOf(SingleKinds, FrontEndState.SingleKind);
                    FrontEndState.SingleKind = SingleKinds[(i + d + SingleKinds.Length) % SingleKinds.Length];
                    UpdateVenue();
                },
                Hint = () => Kind switch
                {
                    EventKind.Derby => "Twenty cars in the Bowl, last car running wins. Spins and wrecks score, double on the leader.",
                    EventKind.StockCar => "Race for the finishing order (50, 40, 35 … points). Contact scores nothing, but bruisers still bump.",
                    EventKind.Duel => "You against one driver in the Bowl. Last car running wins.",
                    EventKind.TotalDestruction => "Every car in the Bowl hunts you. Your score: the seconds you survive plus your points." +
                        (Profile.Current.TotalDestructionBest > 0 ? $" Your best: {Profile.Current.TotalDestructionBest}." : ""),
                    _ => "Score for spinning and wrecking rivals, double on the leader, plus a finishing bonus. One pit stop.",
                },
            });
        if (_flow == Flow.Practice)
            Menu.Add(new MenuItem("VENUE")
            {
                Value = () => FrontEndState.Venue == Venue.Bowl ? "THE BOWL" : "CROSSROADS SPEEDWAY",
                Change = d =>
                {
                    FrontEndState.Venue = FrontEndState.Venue == Venue.Bowl ? Venue.Figure8 : Venue.Bowl;
                    UpdateVenue();
                },
                Hint = () => FrontEndState.Venue == Venue.Bowl ? "The Bowl: a derby with as many cars as you like, or parked targets for a free damage test."
                    : "The Speedway: free laps with as many racing AI cars as you like.",
            });
        Menu.Add(new MenuItem("LAYOUT")
        {
            Value = () => FrontEndState.Venue == Venue.Oval ? "OVAL" : "FIGURE-8",
            Change = d => { FrontEndState.Venue = FrontEndState.Venue == Venue.Oval ? Venue.Figure8 : Venue.Oval; UpdateVenue(); },
            Visible = () => !InBowl,
            Hint = () => FrontEndState.Venue == Venue.Oval ? "Banked turns and two long straights: flat out, three wide."
                : "The figure-8: the legs cross in the middle, and the crossover is blind.",
        });
        Menu.Add(new MenuItem("DIRECTION")
        {
            Value = () => (FrontEndState.Reverse ? "REVERSE" : "FORWARD") + (Profile.IsOpen(Venue, FrontEndState.Reverse, EventText.Light(FrontEndState.Light)) ? "" : "   LOCKED"),
            Change = d => { FrontEndState.Reverse = !FrontEndState.Reverse; UpdateVenue(); },
            Visible = () => !InBowl,
            Hint = () => "Every layout runs both ways.",
        });
        Menu.Add(new MenuItem("LIGHT")
        {
            Value = () => EventText.LightName(FrontEndState.Light) + (Profile.IsOpen(Venue, Reverse, EventText.Light(FrontEndState.Light)) ? "" : "   LOCKED"),
            Change = d => { FrontEndState.Light = (BowlPreset)(((int)FrontEndState.Light + d + 3) % 3); UpdateVenue(); },
            Hint = () => FrontEndState.Light switch
            {
                BowlPreset.GoldenHour => "Low sun, long shadows, the floodlights coming on.",
                BowlPreset.Night => "Under the floodlights, headlights on.",
                _ => "A bright afternoon in the hills.",
            },
        });
        Menu.Add(new MenuItem("LINE-UP")
        {
            Value = () => FrontEndState.FaceOff ? "FACE-OFF" + (Profile.ChampionOpen ? "" : "   LOCKED") : "THE RING",
            Change = d => FrontEndState.FaceOff = !FrontEndState.FaceOff,
            Visible = () => InBowl && Kind != EventKind.Duel && !FrontEndState.Parked,
            Hint = () => FrontEndState.FaceOff
                ? (Profile.ChampionOpen ? "Two rows facing each other across the Bowl." : "LOCKED: win Division 1 in a championship to open the face-off start.")
                : "All cars in a ring round the edge, facing the middle: everyone meets in about three seconds.",
        });
        Menu.Add(new MenuItem("RIVAL")
        {
            Value = () => $"{Drivers.All[FrontEndState.Rival].Name.ToUpperInvariant()}  ·  #{Drivers.All[FrontEndState.Rival].Number}",
            Change = d => FrontEndState.Rival = (FrontEndState.Rival + d + Drivers.All.Length) % Drivers.All.Length,
            Visible = () => Kind == EventKind.Duel,
            Hint = () =>
            {
                var r = Drivers.All[FrontEndState.Rival];
                var car = CarCatalog.Find(r.Car);
                return $"{r.Name} ({r.Nation}), a {r.Archetype.ToLowerInvariant()}, in the {car?.FullName ?? r.Car}.";
            },
        });
        Menu.Add(new MenuItem("AI CARS")
        {
            Value = () => FrontEndState.Opponents == 0 ? "NONE" : $"{FrontEndState.Opponents}",
            Change = d => FrontEndState.Opponents = Math.Clamp(FrontEndState.Opponents + d, 0, 19),
            Visible = () => _flow == Flow.Practice,
            Hint = () => "How many AI cars join you (0–19).",
        });
        Menu.Add(new MenuItem("THE AI")
        {
            Value = () => FrontEndState.Parked ? "PARKED TARGETS" : "HUNTING",
            Change = d => FrontEndState.Parked = !FrontEndState.Parked,
            Visible = () => _flow == Flow.Practice && InBowl,
            Hint = () => FrontEndState.Parked ? "The cars stand still: smash them to look at the damage." : "The cars drive a normal derby.",
        });
        Menu.Add(new MenuItem("LAPS")
        {
            Value = () => $"{FrontEndState.PracticeLaps}",
            Change = d => FrontEndState.PracticeLaps = Math.Clamp(FrontEndState.PracticeLaps + d, 1, 30),
            Visible = () => _flow == Flow.Practice && !InBowl,
        });
        Menu.Add(new MenuItem("DIVISION")
        {
            Value = () => $"DIVISION {FrontEndState.Division}" + (FrontEndState.Division == 4 ? "  (ROOKIES)" : FrontEndState.Division == 1 ? "  (THE BEST)" : ""),
            Change = d => FrontEndState.Division = Math.Clamp(FrontEndState.Division - d, 1, 4),
            Visible = () => _flow != Flow.TimeTrial,
            Hint = () => "How hard the field drives: from the rookies of Division 4 to the best of Division 1. Your roof shows it.",
        });
        Menu.Add(new MenuItem("CAR")
        {
            Value = () => CarCatalog.Find(FrontEndState.Garage.Car)?.FullName.ToUpperInvariant() ?? "",
            Activate = () => Host.Push(new CarPage(FrontEndState.Garage, g =>
            {
                FrontEndState.Garage = g;
                Host.Pop();
            }, Heading, FrontEndState.Division)),
            Hint = () => "Pick your car in the showroom.",
        });
        Menu.Add(new MenuItem("COLOURS")
        {
            Value = () => $"{Livery.ModeNames[FrontEndState.Garage.Style]}  ·  #{FrontEndState.Garage.Number}",
            Swatch = () => PlayerColours.Colour(FrontEndState.Garage.Colour),
            Activate = () => Host.Push(new LiveryPage(FrontEndState.Garage, g =>
            {
                FrontEndState.Garage = g;
                Host.Pop();
            }, Heading, FrontEndState.Division)),
            Hint = () => "Your paint, the livery style and your race number.",
        });
        Menu.Add(new MenuItem("START")
        {
            Activate = Start,
            Enabled = () => Open,
            Hint = () => _flow == Flow.TimeTrial && TimeTrialRecords.Load().Best(TimeTrialRecords.Key(EventText.Layout(Venue), Reverse, FrontEndState.Garage.Car)) is { } lap
                ? $"Your best lap here in this car: {TimeTrialTargets.Format(lap.Time)}."
                : "Go!",
        });
    }

    public override void Shown()
    {
        base.Shown();
        UpdateVenue();
    }

    private void UpdateVenue()
    {
        var g = FrontEndState.Garage;
        Host.ShowVenue(Venue, Reverse, FrontEndState.Light, g.Car, g.Livery(_flow == Flow.TimeTrial ? 4 : FrontEndState.Division), $"DD {g.Number:00}");
        Menu.Refresh();
        Host.RefreshChrome();
    }

    private void Start()
    {
        if (!Open) return;
        var g = FrontEndState.Garage;
        var kind = Kind;
        int division = _flow == Flow.TimeTrial ? 4 : FrontEndState.Division;
        int laps = _flow == Flow.Practice && !InBowl ? FrontEndState.PracticeLaps : 0;
        var req = new EventRequest
        {
            Kind = kind,
            Origin = _flow switch { Flow.Practice => EventOrigin.Practice, Flow.TimeTrial => EventOrigin.TimeTrial, _ => EventOrigin.SingleEvent },
            Layout = EventText.Layout(Venue),
            Reverse = Reverse,
            Preset = FrontEndState.Light,
            Car = g.Car,
            Livery = g.Livery(division),
            Number = g.Number,
            Division = division,
            Difficulty = GameSettings.Current.Difficulty,
            Opponents = _flow == Flow.Practice ? FrontEndState.Opponents : 19,
            Rival = kind == EventKind.Duel ? Drivers.All[FrontEndState.Rival].Name : null,
            FaceOff = InBowl && FrontEndState.FaceOff && !FrontEndState.Parked && kind != EventKind.Duel,
            Parked = _flow == Flow.Practice && InBowl && FrontEndState.Parked,
            Laps = laps,
            Seed = (int)(Time.GetUnixTimeFromSystem() % 100000),
        };
        string returnPage = _flow switch { Flow.Practice => "practice", Flow.TimeTrial => "timetrial", _ => "single" };
        int cars = kind == EventKind.Duel ? 2 : 1 + req.Opponents;
        string detail = kind == EventKind.TimeTrial ? "FLYING LAPS AGAINST THE CLOCK"
            : InBowl ? $"{cars} CARS" : $"{cars} CARS  ·  {(laps > 0 ? laps : EventText.LapsOf(Venue))} LAPS";
        Host.StartEvent(req, returnPage, _flow == Flow.Practice ? "PRACTICE" : EventText.KindName(kind),
            $"{EventText.Where(Venue, Reverse, FrontEndState.Light)}  ·  {detail}");
    }
}
