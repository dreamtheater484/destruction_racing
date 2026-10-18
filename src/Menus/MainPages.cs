using System;
using System.Linq;
using System.Text.RegularExpressions;
using DestructionRacing.AI;
using DestructionRacing.Core;
using DestructionRacing.Modes;
using DestructionRacing.UI;
using Godot;

namespace DestructionRacing.Menus;

/// <summary>The title: the game's name over a hero car in the showroom; Enter goes on (plan §13).</summary>
public partial class TitlePage : Page
{
    private Label _press = null!;
    private float _t;

    public override bool ShowChrome => false;

    public override void _Ready()
    {
        var box = HudStyle.Place(new VBoxContainer(), 0f, 0.5f, new Vector2(90, -230), new Vector2(1200, 0));
        box.AddThemeConstantOverride("separation", -34);
        box.AddChild(HudStyle.Label("DESTRUCTION", 168, HudStyle.White, "ExtraBold", outline: 14));
        box.AddChild(HudStyle.Label("RACING", 168, HudStyle.Yellow, "ExtraBold", outline: 14));
        var sub = HudStyle.Label("SPIN THEM. WRECK THEM. WIN.", 36, HudStyle.Grey, "Bold", outline: 8);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 76) });
        box.AddChild(sub);
        AddChild(box);
        var stripe = new TextureRect { Texture = HudStyle.Hazard, StretchMode = TextureRect.StretchModeEnum.Tile, MouseFilter = MouseFilterEnum.Ignore };
        HudStyle.Place(stripe, 0f, 0.5f, new Vector2(96, 190), new Vector2(560, 14));
        AddChild(stripe);
        _press = HudStyle.Label("PRESS ENTER", 40, HudStyle.White, "Bold", HorizontalAlignment.Left, 8);
        HudStyle.Place(_press, 0f, 1f, new Vector2(96, -170), new Vector2(800, 50));
        AddChild(_press);
        AddChild(HudStyle.Place(HudStyle.Label("A spiritual successor to Destruction Derby 1 and 2  ·  keyboard only", 24, HudStyle.Grey, "SemiBold", outline: 6),
            0f, 1f, new Vector2(96, -100), new Vector2(1200, 40)));
    }

    public override void Shown()
    {
        Host.ShowShowroom();
        // A random driver's car in their signature colours.
        var d = Drivers.All[(int)(Time.GetTicksMsec() % (ulong)Drivers.All.Length)];
        Host.Showroom.ShowCar(d.Car, d.LiveryFor(DerbyMode.RoofColor(1)), $"DD {d.Number:00}");
        Host.Showroom.Offset = 2.6f;
    }

    public override void Tick(float dt)
    {
        _t += dt;
        _press.Modulate = new Color(1f, 1f, 1f, 0.55f + 0.45f * Mathf.Cos(_t * 3.2f));
    }

    public override bool HandleKey(InputEventKey key)
    {
        if (key.PhysicalKeycode is not (Key.Enter or Key.KpEnter or Key.Space)) return false;
        Host.Showroom.Offset = 1.7f;
        if (GameSettings.Current.ControlPreset == null) Host.Replace(new PresetPage(firstLaunch: true));
        else Host.Replace(new MainMenuPage());
        return true;
    }

    public override bool OnBack() => false;
}

/// <summary>The main menu (plan §13).</summary>
public partial class MainMenuPage : MenuPage
{
    public override string Heading => "MAIN MENU";
    public override string KeysHint => "↑/↓ choose   ·   ENTER select   ·   ESC title";

    protected override void Build()
    {
        Menu.FontSize = 44;
        Menu.Add(new MenuItem("CHAMPIONSHIP")
        {
            Activate = () => Host.Push(new ChampionshipSlotsPage()),
            Hint = () => "Twenty drivers, four divisions. Top your division to go up; every promotion opens new layouts and lights.",
        });
        Menu.Add(new MenuItem("SINGLE EVENT")
        {
            Activate = () => Host.Push(new EventSetupPage(EventSetupPage.Flow.Single)),
            Hint = () => "One derby, race, duel or Total Destruction, on anything the championship has opened.",
        });
        Menu.Add(new MenuItem("TIME TRIAL")
        {
            Activate = () => Host.Push(new EventSetupPage(EventSetupPage.Flow.TimeTrial)),
            Hint = () => "Alone against the clock and your ghost: medals and a leaderboard per car.",
        });
        Menu.Add(new MenuItem("PRACTICE")
        {
            Activate = () => Host.Push(new EventSetupPage(EventSetupPage.Flow.Practice)),
            Hint = () => "Any layout and light with as many AI cars as you like, or parked targets in the Bowl to smash.",
        });
        Menu.Add(new MenuItem("OPTIONS")
        {
            Activate = () => Host.Push(new OptionsPage()),
            Hint = () => "Display, graphics, camera, sound, keys and assists, difficulty.",
        });
        Menu.Add(new MenuItem("CREDITS")
        {
            Activate = () => Host.Push(new CreditsPage()),
            Hint = () => "Who made what: the fonts, the music, the textures and the tools.",
        });
        Menu.Add(new MenuItem("QUIT")
        {
            Activate = () => DevTools.QuitClean(GetTree(), 0),
            Hint = () => "Back to Windows.",
        });
    }

    public override void Shown()
    {
        base.Shown();
        Host.ShowShowroom();
        var g = FrontEndState.Garage;
        Host.Showroom.ShowCar(g.Car, g.Livery(4), $"DD {g.Number:00}");
    }

    public override bool OnBack()
    {
        Host.Replace(new TitlePage());
        return false;
    }
}

/// <summary>The first launch asks which key layout you want (plan §14.1); the options can change it later.</summary>
public partial class PresetPage : MenuPage
{
    private readonly bool _first;

    public PresetPage(bool firstLaunch) => _first = firstLaunch;

    public override string Heading => "YOUR KEYS";
    public override string Subheading => "PICK A LAYOUT  ·  YOU CAN REBIND EVERY KEY IN THE OPTIONS";
    public override string KeysHint => "↑/↓ choose   ·   ENTER select" + (_first ? "" : "   ·   ESC back");

    protected override void Build()
    {
        Menu.FontSize = 40;
        foreach (var p in new[] { ControlPreset.Arrows, ControlPreset.ClassicDdPc, ControlPreset.Wasd })
        {
            var preset = p;
            Menu.Add(new MenuItem(Controls.PresetName(preset))
            {
                Activate = () => Choose(preset),
                Hint = () => Controls.Describe(preset),
            });
        }
    }

    private void Choose(ControlPreset preset)
    {
        var s = GameSettings.Current.Clone();
        s.ControlPreset = preset.ToString();
        GameSettings.Commit(s);
        if (_first) Host.Replace(new MainMenuPage());
        else Host.Pop();
    }

    public override bool OnBack() => !_first;
}

/// <summary>The credits (CREDITS.md), scrolling; ↑/↓ scroll by hand.</summary>
public partial class CreditsPage : Page
{
    private ScrollContainer _scroll = null!;
    private float _pos;

    public override string Heading => "CREDITS";
    public override string KeysHint => "↑/↓ scroll   ·   ESC back";

    public override void _Ready()
    {
        var panel = LeftPanel(this, 1180f, 230f);
        panel.CustomMinimumSize = new Vector2(1180, 700);
        _scroll = new ScrollContainer { CustomMinimumSize = new Vector2(1120, 680), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Ignore };
        _scroll.VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever;
        panel.AddChild(_scroll);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(1100, 0) };
        box.AddThemeConstantOverride("separation", 6);
        _scroll.AddChild(box);
        box.AddChild(HudStyle.Label("DESTRUCTION RACING", 48, HudStyle.Yellow, "ExtraBold", outline: 0));
        box.AddChild(Text("Made by dreamtheater484 with Claude (Anthropic). Cars, tracks, the Bowl, the sounds and the announcer are this project's own work.", 26));
        foreach (string line in CreditLines())
        {
            if (line.StartsWith("#"))
            {
                box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 14) });
                box.AddChild(HudStyle.Label(line.TrimStart('#', ' ').ToUpperInvariant(), 32, HudStyle.Yellow, "Bold", outline: 0));
            }
            else box.AddChild(Text(line, 24));
        }
    }

    private static Label Text(string text, int size)
    {
        var l = HudStyle.Label(text, size, HudStyle.White, "Medium", outline: 0);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(1090, 0);
        return l;
    }

    /// <summary>CREDITS.md without its Markdown (links become their text, list bullets stay).</summary>
    private static string[] CreditLines()
    {
        const string path = "res://CREDITS.md";
        if (!FileAccess.FileExists(path)) return new[] { "See CREDITS.md next to the game." };
        string text = FileAccess.GetFileAsString(path);
        return text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Trim().Length > 0 && !l.StartsWith("# "))
            .Select(l => Regex.Replace(l, @"\[([^\]]+)\]\([^)]+\)", "$1"))
            .Select(l => Regex.Replace(l, @"<(https?://[^>]+)>", "$1"))
            .Select(l => l.Replace("**", "").Replace("`", "").Replace("- ", "·  "))
            .ToArray();
    }

    public override void Tick(float dt)
    {
        _pos += dt * 28f;
        _scroll.ScrollVertical = (int)_pos;
        if (_pos > _scroll.GetVScrollBar().MaxValue) _pos = 0f;
    }

    public override bool HandleKey(InputEventKey key)
    {
        if (key.PhysicalKeycode == Key.Up) { _pos = Math.Max(0f, _pos - 120f); return true; }
        if (key.PhysicalKeycode == Key.Down) { _pos += 120f; return true; }
        return false;
    }
}

/// <summary>What the front end remembers between screens and events in one session (and the garage across launches).</summary>
public static class FrontEndState
{
    private static Garage? _garage;

    /// <summary>The car and colours of Single Event, Time Trial and Practice (saved with the settings).</summary>
    public static Garage Garage
    {
        get
        {
            if (_garage != null) return _garage;
            var s = GameSettings.Current;
            _garage = new Garage(Vehicles.CarCatalog.Find(s.LastCar) != null ? s.LastCar : "kitsune", s.LastColour, s.LastStyle, s.LastNumber);
            return _garage;
        }
        set
        {
            _garage = value;
            var s = GameSettings.Current.Clone();
            (s.LastCar, s.LastColour, s.LastStyle, s.LastNumber) = (value.Car, value.Colour, value.Style, value.Number);
            GameSettings.Commit(s);
        }
    }

    public static EventKind SingleKind { get; set; } = EventKind.Wreckin;
    public static Rules.Venue Venue { get; set; } = Rules.Venue.Figure8;
    public static bool Reverse { get; set; }
    public static Tracks.BowlPreset Light { get; set; } = Tracks.BowlPreset.Afternoon;
    public static int Division { get; set; } = 4;
    public static int Opponents { get; set; } = 19;
    public static bool Parked { get; set; }
    public static bool FaceOff { get; set; }
    public static int Rival { get; set; }
    public static int PracticeLaps { get; set; } = 5;
}
