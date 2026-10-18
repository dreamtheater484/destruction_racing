using System;
using System.Collections.Generic;
using System.Linq;
using DestructionRacing.AI;
using DestructionRacing.Core;
using DestructionRacing.Modes;
using DestructionRacing.Rules;
using DestructionRacing.UI;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Menus;

/// <summary>The three championship save slots: continue one, start a new one, or delete one (Del twice).</summary>
public partial class ChampionshipSlotsPage : MenuPage
{
    private int _confirmDelete = -1;

    public override string Heading => "CHAMPIONSHIP";
    public override string KeysHint => "↑/↓ choose   ·   ENTER play   ·   DEL delete   ·   ESC back";
    protected override float PanelWidth => 1180f;

    protected override void Build()
    {
        Menu.CaptionWidth = 190;
        Menu.FontSize = 34;
        for (int i = 1; i <= CareerStore.Slots; i++)
        {
            int slot = i;
            Menu.Add(new MenuItem($"SLOT {slot}")
            {
                Value = () => CareerStore.Load(slot)?.Summary ?? "EMPTY  ·  START A NEW CHAMPIONSHIP",
                Activate = () => Open(slot),
                Hint = () =>
                {
                    if (_confirmDelete == slot) return "Press DEL again to delete this championship for good.";
                    var s = CareerStore.Load(slot);
                    if (s == null) return "A new championship: Wreckin' (four races and the Bowl) or Stock Car (four races), from Division 4.";
                    var car = CarCatalog.Find(s.Garage.Car)?.FullName ?? s.Garage.Car;
                    string best = s.Career.History.Count == 0 ? "first season" : $"{s.Career.History.Count} season(s) done";
                    return $"{car} #{s.Garage.Number}  ·  {best}  ·  last played {s.Updated}.";
                },
            });
        }
    }

    private void Open(int slot)
    {
        _confirmDelete = -1;
        if (CareerStore.Load(slot) is { } s) Host.Push(new HubPage(s));
        else Host.Push(new NewChampionshipPage(slot));
    }

    public override void Shown()
    {
        base.Shown();
        Host.ShowShowroom();
        var g = FrontEndState.Garage;
        Host.Showroom.ShowCar(g.Car, g.Livery(4), $"DD {g.Number:00}");
    }

    public override bool HandleKey(InputEventKey key)
    {
        if (key.PhysicalKeycode == Key.Delete)
        {
            int slot = Menu.FocusIndex + 1;
            if (!CareerStore.Exists(slot)) return true;
            if (_confirmDelete == slot)
            {
                CareerStore.Delete(slot);
                _confirmDelete = -1;
            }
            else _confirmDelete = slot;
            Menu.Refresh();
            return true;
        }
        if (key.PhysicalKeycode is Key.Up or Key.Down) _confirmDelete = -1;
        return base.HandleKey(key);
    }
}

/// <summary>A new championship: which one, your car and colours.</summary>
public partial class NewChampionshipPage : MenuPage
{
    private readonly int _slot;
    private ChampionshipKind _kind = ChampionshipKind.Wreckin;
    private Garage _garage = FrontEndState.Garage;

    public NewChampionshipPage(int slot) => _slot = slot;

    public override string Heading => "NEW CHAMPIONSHIP";
    public override string Subheading => $"SLOT {_slot}";

    protected override void Build()
    {
        Menu.CaptionWidth = 300;
        Menu.Add(new MenuItem("CHAMPIONSHIP")
        {
            Value = () => _kind == ChampionshipKind.StockCar ? "STOCK CAR" : "WRECKIN'",
            Change = d => _kind = _kind == ChampionshipKind.Wreckin ? ChampionshipKind.StockCar : ChampionshipKind.Wreckin,
            Hint = () => _kind == ChampionshipKind.StockCar
                ? "Four Stock Car races a season: points for the finishing order only. Pace wins."
                : "Four Wreckin' races and the Bowl as the finale: spins, wrecks and the finish all score. The Bowl can swing it.",
        });
        Menu.Add(new MenuItem("CAR")
        {
            Value = () => CarCatalog.Find(_garage.Car)?.FullName.ToUpperInvariant() ?? "",
            Activate = () => Host.Push(new CarPage(_garage, g => { _garage = g; Host.Pop(); }, "NEW CHAMPIONSHIP")),
            Hint = () => "You can change the car between rounds in the garage.",
        });
        Menu.Add(new MenuItem("COLOURS")
        {
            Value = () => $"{Livery.ModeNames[_garage.Style]}  ·  #{_garage.Number}",
            Swatch = () => PlayerColours.Colour(_garage.Colour),
            Activate = () => Host.Push(new LiveryPage(_garage, g => { _garage = g; Host.Pop(); }, "NEW CHAMPIONSHIP")),
            Hint = () => "Your paint, the livery style and your race number. Division 4 gives you a yellow roof.",
        });
        Menu.Add(new MenuItem("START")
        {
            Activate = () =>
            {
                var slot = CareerStore.New(_slot, _kind, _garage);
                FrontEndState.Garage = _garage;
                Host.Replace(new HubPage(slot));
            },
            Hint = () => "You start in Division 4 with the rookies. Top your division to go up.",
        });
    }

    public override void Shown()
    {
        base.Shown();
        Host.ShowShowroom();
        Host.Showroom.ShowCar(_garage.Car, _garage.Livery(4), $"DD {_garage.Number:00}");
    }
}

/// <summary>
/// The championship hub (plan §4.5, §13): the season's calendar, your division's table with roof colours, what you
/// need, and the next round. After an event the table tallies the round's points and re-sorts; after the last round the
/// season review follows.
/// </summary>
public partial class HubPage : Page
{
    private readonly CareerSlot _slot;
    private readonly MenuView _menu = new();
    private VBoxContainer _calendar = null!, _table = null!;
    private Label _stakes = null!, _tableTitle = null!;
    private int _shownDivision;
    private bool _tallying;
    private Tween? _tween;

    public HubPage(CareerSlot slot)
    {
        _slot = slot;
        _shownDivision = slot.Career.PlayerDivision;
    }

    private Career Career => _slot.Career;

    public override string Heading => _slot.KindName + " CHAMPIONSHIP";
    public override string Subheading => $"SEASON {Career.Season}  ·  DIVISION {Career.PlayerDivision}" +
        (Career.Attempts > 1 ? $"  ·  ATTEMPT {Career.Attempts}" : "") + (Career.Champion ? "  ·  CHAMPION" : "");
    public override string Hint => _tallying ? "The points of the round…" : _menu.Current?.Hint?.Invoke() ?? "";
    public override string KeysHint => "↑/↓ choose   ·   ←/→ other divisions   ·   ENTER select   ·   ESC back";

    public override void _Ready()
    {
        var left = LeftPanel(this, 820f, 240f);
        var lbox = new VBoxContainer();
        lbox.AddThemeConstantOverride("separation", 4);
        left.AddChild(lbox);
        lbox.AddChild(HudStyle.Label("THE SEASON", 24, HudStyle.Yellow, "Bold", outline: 0));
        _calendar = new VBoxContainer();
        _calendar.AddThemeConstantOverride("separation", 0);
        lbox.AddChild(_calendar);
        lbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });
        lbox.AddChild(_menu);
        _menu.Changed += () => Host.RefreshChrome();

        var right = HudStyle.Place(new PanelContainer { MouseFilter = MouseFilterEnum.Ignore }, 1f, 0f, new Vector2(-900, 240), new Vector2(830, 0));
        right.AddThemeStyleboxOverride("panel", HudStyle.Panel(HudStyle.Yellow, 0.04f));
        AddChild(right);
        var rbox = new VBoxContainer();
        rbox.AddThemeConstantOverride("separation", 4);
        right.AddChild(rbox);
        _tableTitle = HudStyle.Label("", 24, HudStyle.Yellow, "Bold", outline: 0);
        rbox.AddChild(_tableTitle);
        _table = new VBoxContainer();
        _table.AddThemeConstantOverride("separation", 2);
        rbox.AddChild(_table);
        rbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        _stakes = HudStyle.Label("", 28, HudStyle.White, "Bold", outline: 0);
        _stakes.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _stakes.CustomMinimumSize = new Vector2(780, 0);
        rbox.AddChild(_stakes);

        BuildMenu();
    }

    private void BuildMenu()
    {
        _menu.Clear();
        _menu.CaptionWidth = 300;
        _menu.Add(new MenuItem("START")
        {
            Value = () => Career.SeasonComplete ? "THE SEASON REVIEW" : $"ROUND {Career.EventIndex + 1}" + (Career.IsFinale ? "  ·  THE FINALE" : ""),
            Activate = StartNext,
            Hint = () => Career.NextEvent is { } e
                ? EventText.Sentence(e) +
                  (e.IsDerby ? ": the last car running wins, and spins and wrecks score." : $": {EventText.LapsOf(e.Venue)} laps, all twenty cars.")
                : "The final table, promotion and what the next season brings.",
        });
        _menu.Add(new MenuItem("GARAGE")
        {
            Value = () => $"{CarCatalog.Find(_slot.Garage.Car)?.FullName.ToUpperInvariant()}  ·  #{_slot.Garage.Number}",
            Swatch = () => PlayerColours.Colour(_slot.Garage.Colour),
            Activate = () => { if (!_tallying) Host.Push(new CarPage(_slot.Garage, g =>
            {
                Host.Replace(new LiveryPage(g, g2 =>
                {
                    _slot.Garage = g2;
                    CareerStore.Save(_slot);
                    Host.Pop();
                }, "GARAGE", Career.PlayerDivision));
            }, "GARAGE", Career.PlayerDivision)); },
            Hint = () => "Change your car or colours for the next round.",
        });
        _menu.Add(new MenuItem("BACK")
        {
            Activate = () => { if (!_tallying) Host.Pop(); },
            Hint = () => "The championship is saved after every round.",
        });
        _menu.FocusFirst();
    }

    public override void Shown()
    {
        Host.ShowShowroom();
        Host.Showroom.Offset = 0.6f;
        Host.Showroom.Far = true;
        Host.Showroom.ShowCar(_slot.Garage.Car, _slot.Garage.Livery(Career.PlayerDivision), $"DD {_slot.Garage.Number:00}");
        _shownDivision = Career.PlayerDivision;
        BuildCalendar();
        if (Session.TallyPending && Session.Career == _slot)
        {
            Session.TallyPending = false;
            Tally();
        }
        else BuildTable(_shownDivision, null);
        _menu.Refresh();
    }

    // ------------------------------------------------------------------ the calendar and the table

    private void BuildCalendar()
    {
        foreach (Node c in _calendar.GetChildren()) c.QueueFree();
        var events = Career.Events;
        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            bool done = i < Career.EventIndex, next = i == Career.EventIndex;
            string mark = done ? "✓" : next ? "▶" : " ";
            string what = e.IsDerby ? "DESTRUCTION DERBY" : e.Mode == GameMode.StockCarRacing ? "STOCK CAR" : "WRECKIN'";
            var l = HudStyle.Label($"{mark}  {i + 1}   {what}  ·  {EventText.Where(e.Venue, e.Reverse, EventText.Preset(e.Light))}", 25,
                next ? HudStyle.Yellow : done ? HudStyle.Grey : HudStyle.White, next ? "ExtraBold" : "SemiBold", outline: 0);
            _calendar.AddChild(l);
        }
    }

    private static string CarName(string id) => CarCatalog.Find(id)?.Name.ToUpperInvariant() ?? id.ToUpperInvariant();

    /// <summary>The division table; <paramref name="shown"/> overrides the points shown (for the tally).</summary>
    private void BuildTable(int division, Dictionary<int, int>? shown, IReadOnlyList<Standing>? order = null)
    {
        foreach (Node c in _table.GetChildren()) c.QueueFree();
        _tableTitle.Text = division == Career.PlayerDivision ? $"YOUR DIVISION  ·  DIVISION {division}" : $"DIVISION {division}";
        var rows = order ?? Career.Table.Table(division);
        _table.AddChild(Row("POS", null, "#", "DRIVER", "CAR", "PTS", "", HudStyle.Grey, 18, "SemiBold"));
        int pos = 0;
        foreach (var s in rows)
        {
            pos++;
            var d = s.Driver;
            var info = d.IsPlayer ? null : Drivers.ByName(d.Name);
            string name = d.IsPlayer ? "YOU" : (info?.Name ?? d.Name).ToUpperInvariant();
            string car = d.IsPlayer ? CarName(_slot.Garage.Car) : CarName(info?.Car ?? "");
            int number = d.IsPlayer ? _slot.Garage.Number : info?.Number ?? 0;
            int pts = shown != null && shown.TryGetValue(d.Id, out int p) ? p : s.Points;
            int last = Career.LastEventPoints.TryGetValue(d.Id, out int l) ? l : 0;
            var color = d.IsPlayer ? HudStyle.Yellow : HudStyle.White;
            var row = Row($"{pos}", DerbyMode.RoofColor(division), $"#{number}", name, car, $"{pts}",
                last > 0 && Career.EventIndex > 0 ? $"+{last}" : "", color, 26, d.IsPlayer ? "ExtraBold" : "SemiBold");
            row.SetMeta("driver", d.Id);
            _table.AddChild(row);
        }
        UpdateStakes();
    }

    private static HBoxContainer Row(string pos, Color? roof, string number, string name, string car, string points, string last, Color color, int size, string weight)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 14);
        Label Cell(string text, int width, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            var l = HudStyle.Label(text, size, color, weight, align, 0);
            l.CustomMinimumSize = new Vector2(width, 0);
            row.AddChild(l);
            return l;
        }
        Cell(pos, 50);
        var swatch = new ColorRect { Color = roof ?? Colors.Transparent, CustomMinimumSize = new Vector2(22, 22), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        row.AddChild(swatch);
        Cell(number, 60);
        Cell(name, 300);
        Cell(car, 150);
        Cell(points, 80, HorizontalAlignment.Right).Name = "Points";
        var lastLabel = Cell(last, 80, HorizontalAlignment.Right);
        lastLabel.Name = "Last";
        lastLabel.AddThemeColorOverride("font_color", HudStyle.Green);
        return row;
    }

    private void UpdateStakes()
    {
        var table = Career.PlayerTable;
        var me = table.First(s => s.Driver.IsPlayer);
        int left = Career.Events.Count - Career.EventIndex;
        string rounds = left == 0 ? "THE SEASON IS OVER" : left == 1 ? (Career.IsFinale ? "THE BOWL TO COME" : "ONE ROUND TO GO") : $"{left} ROUNDS TO GO";
        if (_shownDivision != Career.PlayerDivision)
        {
            _stakes.Text = "THE OTHER DIVISIONS RACE IN THE SAME EVENTS: THEIR WINNERS GO UP, THEIR LAST COME DOWN.";
            return;
        }
        if (table[0].Driver.IsPlayer)
        {
            int lead = me.Points - (table.Count > 1 ? table[1].Points : 0);
            _stakes.Text = Career.EventIndex == 0 ? $"ONLY THE TOP SPOT GOES UP  ·  {rounds}" : lead > 0 ? $"YOU LEAD BY {lead}  ·  {rounds}" : $"LEVEL AT THE TOP  ·  {rounds}";
        }
        else
        {
            var leader = table[0];
            string who = Drivers.ByName(leader.Driver.Name)?.Short.ToUpperInvariant() ?? leader.Driver.Name;
            _stakes.Text = $"{leader.Points - me.Points + 1} POINTS TO PASS {who}  ·  {rounds}" + (Career.IsFinale ? "  ·  THE BOWL CAN SWING IT" : "");
        }
    }

    /// <summary>The round's points: the table as it was, then each "+N" and the totals count up, then the re-sort.</summary>
    private void Tally()
    {
        _tallying = true;
        var now = Career.Table.Table(_shownDivision);
        var before = now.ToDictionary(s => s.Driver.Id, s => s.Points - (Career.LastEventPoints.TryGetValue(s.Driver.Id, out int l) ? l : 0));
        var beforeOrder = now.OrderByDescending(s => before[s.Driver.Id]).ThenBy(s => s.Driver.Id).ToList();
        BuildTable(_shownDivision, before, beforeOrder);
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenInterval(0.5);
        foreach (Node child in _table.GetChildren())
        {
            if (child is not HBoxContainer row || !row.HasMeta("driver")) continue;
            int id = row.GetMeta("driver").AsInt32();
            var last = row.GetNode<Label>("Last");
            var pts = row.GetNode<Label>("Points");
            last.Modulate = new Color(1, 1, 1, 0);
            int from = before[id], to = from + (Career.LastEventPoints.TryGetValue(id, out int g) ? g : 0);
            _tween.TweenCallback(Callable.From(() => { last.Modulate = Colors.White; Host.Play("tally_tick", -12f); }));
            _tween.TweenMethod(Callable.From<float>(v => pts.Text = $"{Mathf.RoundToInt(v)}"), (float)from, (float)to, 0.45);
        }
        _tween.TweenInterval(0.6);
        _tween.TweenCallback(Callable.From(() =>
        {
            BuildTable(_shownDivision, null);
            Host.Play("tally_done", -8f);
            _tallying = false;
            _menu.Refresh();
            Host.RefreshChrome();
        }));
        if (Career.SeasonComplete)
        {
            _tween.TweenInterval(1.4);
            _tween.TweenCallback(Callable.From(() => { if (Host.Top == this) Host.Push(new SeasonEndPage(_slot)); }));
        }
    }

    private void StartNext()
    {
        if (_tallying) return;
        if (Career.SeasonComplete)
        {
            Host.Push(new SeasonEndPage(_slot));
            return;
        }
        var e = Career.NextEvent!;
        int div = Career.PlayerDivision;
        var g = _slot.Garage;
        var req = new EventRequest
        {
            Kind = e.Mode switch { GameMode.DestructionDerby => EventKind.Derby, GameMode.StockCarRacing => EventKind.StockCar, _ => EventKind.Wreckin },
            Origin = EventOrigin.Championship,
            Layout = EventText.Layout(e.Venue),
            Reverse = e.Reverse,
            Preset = EventText.Preset(e.Light),
            Car = g.Car,
            Livery = g.Livery(div),
            Number = g.Number,
            Division = div,
            Difficulty = GameSettings.Current.Difficulty,
            DriverDivisions = Career.Drivers.Where(d => !d.IsPlayer).ToDictionary(d => d.Name, d => d.Division),
            Seed = Career.EventSeed,
            Title = $"ROUND {Career.EventIndex + 1} OF {Career.Events.Count}  ·  DIVISION {div}",
        };
        Session.Career = _slot;
        Host.StartEvent(req, "hub", EventText.ModeName(e.Mode),
            $"ROUND {Career.EventIndex + 1} OF {Career.Events.Count}  ·  {EventText.Where(e.Venue, e.Reverse, EventText.Preset(e.Light))}  ·  DIVISION {div}");
    }

    public override bool HandleKey(InputEventKey key)
    {
        if (key.PhysicalKeycode is Key.Left or Key.Right && !_tallying)
        {
            int d = key.PhysicalKeycode == Key.Right ? 1 : -1;
            _shownDivision = (_shownDivision - 1 + d + 4) % 4 + 1;
            BuildTable(_shownDivision, null);
            return true;
        }
        return _menu.HandleKey(key.PhysicalKeycode);
    }

    public override bool OnBack() => !_tallying;
}

/// <summary>
/// The end of a season (plan §4.5): the final table, promotion or another go, who moved between the divisions and what
/// the promotion opened. The season is closed (and saved) as this page opens.
/// </summary>
public partial class SeasonEndPage : Page
{
    private readonly CareerSlot _slot;

    public SeasonEndPage(CareerSlot slot) => _slot = slot;

    private int _season, _division;

    public override string Heading => $"SEASON {_season} REVIEW";
    public override string Subheading => $"{_slot.KindName}  ·  DIVISION {_division}";
    public override string KeysHint => "ENTER continue";

    public override void _Ready()
    {
        var career = _slot.Career;
        _season = career.Season;
        _division = career.PlayerDivision;
        var final = career.PlayerTable.Select(s => (s.Driver, s.Points)).ToList();
        int place = career.PlayerPlace;
        bool wasChampion = career.Champion;
        var outcome = career.EndSeason();
        CareerStore.Save(_slot);

        var panel = LeftPanel(this, 1240f, 240f);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        panel.AddChild(box);
        int pos = 0;
        foreach (var (d, pts) in final)
        {
            pos++;
            var info = d.IsPlayer ? null : Drivers.ByName(d.Name);
            string name = d.IsPlayer ? "YOU" : (info?.Name ?? d.Name).ToUpperInvariant();
            var l = HudStyle.Label($"{pos}.   {name}   {pts}", 30, d.IsPlayer ? HudStyle.Yellow : HudStyle.White, d.IsPlayer ? "ExtraBold" : "SemiBold", outline: 0);
            box.AddChild(l);
        }
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });

        string verdict, detail;
        Color colour = HudStyle.Yellow;
        if (outcome.PlayerWonChampionship)
        {
            verdict = wasChampion ? "CHAMPION AGAIN!" : "DIVISION 1 CHAMPION!";
            detail = wasChampion ? "You defended the title. The best of the best, again." : "NOW OPEN: THE FACE-OFF START IN THE BOWL  ·  THE CHAMPION COLOURS";
        }
        else if (outcome.PlayerPromoted)
        {
            verdict = $"PROMOTED TO DIVISION {career.PlayerDivision}!";
            var opened = Unlocks.OpenedBy(career.PlayerDivision);
            detail = opened.Count > 0 ? "NOW OPEN: " + string.Join("  ·  ", opened) : "";
        }
        else
        {
            colour = HudStyle.Orange;
            verdict = $"{Ordinal(place)} IN DIVISION {_division}";
            detail = "Only the top spot goes up: the season starts again with the same rounds.";
        }
        box.AddChild(HudStyle.Label(verdict, 64, colour, "ExtraBold", outline: 0));
        var dl = HudStyle.Label(detail, 28, HudStyle.White, "Bold", outline: 0);
        dl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        dl.CustomMinimumSize = new Vector2(1180, 0);
        box.AddChild(dl);

        var moves = outcome.Moves.Where(m => m.DriverId != Career.PlayerId).Select(m =>
        {
            var d = career.Drivers[m.DriverId];
            string who = Drivers.ByName(d.Name)?.Short.ToUpperInvariant() ?? d.Name;
            return (Up: m.To < m.From, Text: $"{who} ({m.From} → {m.To})");
        }).ToList();
        if (moves.Count > 0)
        {
            box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
            var up = string.Join("   ", moves.Where(m => m.Up).Select(m => m.Text));
            var down = string.Join("   ", moves.Where(m => !m.Up).Select(m => m.Text));
            box.AddChild(HudStyle.Label($"UP:   {up}", 24, HudStyle.Green, "SemiBold", outline: 0));
            box.AddChild(HudStyle.Label($"DOWN:   {down}", 24, HudStyle.Grey, "SemiBold", outline: 0));
        }
        Host.Play(outcome.PlayerPromoted || outcome.PlayerWonChampionship ? "crowd_cheer" : "tally_done", -6f);
        Host.Announce(outcome.PlayerWonChampionship ? "champ_title" : outcome.PlayerPromoted ? "champ_promoted" : "champ_missed");
    }

    private static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "TH" : (n % 10) switch { 1 => "ST", 2 => "ND", 3 => "RD", _ => "TH" });

    public override bool HandleKey(InputEventKey key)
    {
        if (key.PhysicalKeycode is not (Key.Enter or Key.KpEnter or Key.Space)) return false;
        Host.Pop();
        return true;
    }

    public override bool OnBack()
    {
        Host.Pop();
        return false;
    }
}
