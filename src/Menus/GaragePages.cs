using System;
using System.Collections.Generic;
using System.Linq;
using DestructionRacing.AI;
using DestructionRacing.Modes;
using DestructionRacing.UI;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Menus;

/// <summary>
/// Car select in the showroom (plan §13): ←/→ through the nine cars on the turntable, with the five ratings as bars,
/// the engine and drive, and the honest verdicts of the balance sims for the derby, Wreckin' and pace (plan §9.3).
/// </summary>
public partial class CarPage : Page
{
    private readonly Action<Garage> _done;
    private readonly string _subtitle;
    private Garage _garage;
    private int _car;
    private Label _name = null!, _line = null!, _verdict = null!;
    private readonly ColorRect[,] _bars = new ColorRect[5, 10];
    private readonly int _division;

    public CarPage(Garage garage, Action<Garage> done, string subtitle = "", int division = 4)
    {
        _garage = garage;
        _done = done;
        _subtitle = subtitle;
        _division = division;
        _car = Math.Max(0, Array.IndexOf(CarCatalog.Ids, garage.Car));
    }

    private CarInfo Info => CarCatalog.All[_car];

    public override string Heading => "CHOOSE YOUR CAR";
    public override string Subheading => _subtitle;
    public override string Hint => Info.Hint;
    public override string KeysHint => "←/→ car   ·   ENTER choose   ·   ESC back";

    public override void _Ready()
    {
        var panel = LeftPanel(this, 760f, 260f);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        panel.AddChild(box);
        _name = HudStyle.Label("", 60, HudStyle.White, "ExtraBold", outline: 0);
        box.AddChild(_name);
        _line = HudStyle.Label("", 26, HudStyle.Grey, "SemiBold", outline: 0);
        box.AddChild(_line);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        string[] names = { "SPEED", "ACCEL", "HANDLING", "WEIGHT", "TOUGH" };
        for (int s = 0; s < 5; s++)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 4);
            var n = HudStyle.Label(names[s], 22, HudStyle.Grey, "SemiBold", outline: 0);
            n.CustomMinimumSize = new Vector2(170, 0);
            row.AddChild(n);
            for (int k = 0; k < 10; k++)
            {
                _bars[s, k] = new ColorRect { CustomMinimumSize = new Vector2(38, 16), SizeFlagsVertical = SizeFlags.ShrinkCenter };
                row.AddChild(_bars[s, k]);
            }
            box.AddChild(row);
        }
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        _verdict = HudStyle.Label("", 28, HudStyle.White, "Bold", outline: 0);
        box.AddChild(_verdict);
        var dots = HudStyle.Label("", 22, HudStyle.Yellow, "SemiBold", outline: 0);
        box.AddChild(dots);
        Refresh();
    }

    public static string Verdict(int stars) => stars switch { >= 5 => "BEST", 4 => "STRONG", 3 => "GOOD", 2 => "FAIR", _ => "WEAK" };

    private void Refresh()
    {
        var info = Info;
        _name.Text = info.FullName.ToUpperInvariant();
        string region = info.Region.ToString().ToUpperInvariant();
        _line.Text = $"{_car + 1} OF {CarCatalog.All.Length}   ·   {region}  ·  {info.Body.ToUpperInvariant()}  ·  {info.Engine.ToUpperInvariant()}  ·  {(info.FrontDrive ? "FRONT-DRIVE" : "REAR-DRIVE")}";
        int[] r = { info.Speed, info.Accel, info.Handling, info.Weight, info.Toughness };
        for (int s = 0; s < 5; s++)
            for (int k = 0; k < 10; k++)
                _bars[s, k].Color = k < r[s] ? HudStyle.Yellow : new Color(1f, 1f, 1f, 0.12f);
        _verdict.Text = $"DERBY  {Verdict(info.DerbyStars)}      WRECKIN'  {Verdict(info.RaceStars)}      PACE  {Verdict(info.PaceStars)}";
        _garage = _garage with { Car = info.Id };
        Host?.Showroom.ShowCar(info.Id, _garage.Livery(_division), $"DD {_garage.Number:00}");
        Host?.RefreshChrome();
    }

    public override void Shown()
    {
        Host.ShowShowroom();
        Host.Showroom.Offset = 1.7f;
        Refresh();
    }

    public override bool HandleKey(InputEventKey key)
    {
        switch (key.PhysicalKeycode)
        {
            case Key.Left or Key.Up: _car = (_car + CarCatalog.All.Length - 1) % CarCatalog.All.Length; Refresh(); return true;
            case Key.Right or Key.Down: _car = (_car + 1) % CarCatalog.All.Length; Refresh(); return true;
            case Key.Enter or Key.KpEnter: _done(_garage); return true;
        }
        return false;
    }
}

/// <summary>
/// Livery and number (plan §9.4, §13): the palette grid (16 colours, and the champion's metallic set once Division 1
/// is won), the livery style and the race number, on the car in the showroom.
/// </summary>
public partial class LiveryPage : MenuPage
{
    private readonly Action<Garage> _done;
    private readonly string _subtitle;
    private readonly int _division;
    private Garage _garage;
    private readonly List<Panel> _swatches = new();
    private GridContainer _grid = null!;

    public LiveryPage(Garage garage, Action<Garage> done, string subtitle = "", int division = 4)
    {
        _garage = garage;
        _done = done;
        _subtitle = subtitle;
        _division = division;
    }

    public override string Heading => "YOUR COLOURS";
    public override string Subheading => _subtitle;
    protected override float PanelWidth => 760f;

    /// <summary>Race numbers the 19 drivers carry are taken.</summary>
    private static readonly HashSet<int> Taken = Drivers.All.Select(d => d.Number).ToHashSet();

    private readonly IReadOnlyList<int> _colours = PlayerColours.Available();

    protected override void Build()
    {
        Menu.CaptionWidth = 220;
        Menu.Add(new MenuItem("COLOUR")
        {
            Value = () => PlayerColours.Name(_garage.Colour),
            Change = d =>
            {
                int i = Math.Max(0, IndexOf(_garage.Colour));
                Set(_garage with { Colour = _colours[(i + d + _colours.Count) % _colours.Count] });
            },
            Swatch = () => PlayerColours.Colour(_garage.Colour),
            Hint = () => "Your paint: ←/→ through the colours." +
                (Profile.ChampionOpen ? "" : " Winning Division 1 adds a metallic champion set.") +
                (Profile.MedalsOpen >= PlayerColours.Medal.Length ? "" : " Time Trial medals add bronze, silver and gold."),
        });
        Menu.Add(new MenuItem("STYLE")
        {
            Value = () => Livery.ModeNames[_garage.Style],
            Change = d => Set(_garage with { Style = (_garage.Style + d + Livery.Modes) % Livery.Modes }),
            Hint = () => "The second colour: stripes, two-tone, a side band or a flash.",
        });
        Menu.Add(new MenuItem("NUMBER")
        {
            Value = () => $"#{_garage.Number}",
            Change = d => Set(_garage with { Number = NextNumber(_garage.Number, d) }),
            Hint = () => "Your race number on the doors and the roof. The drivers' own numbers are taken.",
        });
        Menu.Add(new MenuItem("CONTINUE") { Activate = () => _done(_garage), Hint = () => "Keep these colours." });

        _grid = new GridContainer { Columns = 8 };
        _grid.AddThemeConstantOverride("h_separation", 6);
        _grid.AddThemeConstantOverride("v_separation", 6);
        for (int i = 0; i < _colours.Count; i++)
        {
            var sw = new Panel { CustomMinimumSize = new Vector2(66, 34) };
            _swatches.Add(sw);
            _grid.AddChild(sw);
        }
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 20);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddChild(_grid);
        Box.AddChild(margin);
        Box.MoveChild(margin, 0);
        Paint();
    }

    private int IndexOf(int colour)
    {
        for (int i = 0; i < _colours.Count; i++) if (_colours[i] == colour) return i;
        return -1;
    }

    private static int NextNumber(int n, int d)
    {
        for (int i = 0; i < 99; i++)
        {
            n = (n - 1 + d + 99) % 99 + 1;
            if (!Taken.Contains(n)) return n;
        }
        return 7;
    }

    private void Set(Garage g)
    {
        _garage = g;
        Paint();
    }

    private void Paint()
    {
        for (int i = 0; i < _swatches.Count; i++)
        {
            bool on = _colours[i] == _garage.Colour;
            _swatches[i].AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = PlayerColours.Colour(_colours[i]),
                BorderColor = on ? HudStyle.Yellow : new Color(0f, 0f, 0f, 0.6f),
                BorderWidthLeft = on ? 4 : 1, BorderWidthRight = on ? 4 : 1, BorderWidthTop = on ? 4 : 1, BorderWidthBottom = on ? 4 : 1,
            });
        }
        Host?.Showroom.ShowCar(_garage.Car, _garage.Livery(_division), $"DD {_garage.Number:00}");
    }

    public override void Shown()
    {
        base.Shown();
        Host.ShowShowroom();
        Paint();
    }
}
