using System;
using DestructionRacing.AI;
using DestructionRacing.Core;
using DestructionRacing.Rules;
using DestructionRacing.Tracks;
using DestructionRacing.UI;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Modes;

/// <summary>The Crossroads Speedway events of the set-up screen.</summary>
public enum SpeedwayEvent { WreckinRacing, StockCarRacing, TimeTrial }

/// <summary>The settings the next Speedway event starts with (kept for "race again").</summary>
public sealed record RaceOptions(SpeedwayEvent Event, SpeedwayLayout Layout, bool Reverse, string Car, int Division, Livery? Livery = null,
    BowlPreset Preset = BowlPreset.Afternoon)
{
    public GameMode Kind => Event == SpeedwayEvent.StockCarRacing ? GameMode.StockCarRacing : GameMode.WreckinRacing;
    /// <summary>AI pace by division (plan §10.5) ✱: Div 4 0.97 up to Div 1 1.015.</summary>
    public float AiPace => 0.97f + 0.015f * (4 - Division);
}

/// <summary>
/// The Speedway events' set-up (the M7 front end will replace it, like <see cref="DerbySetup"/>): Wreckin' Racing,
/// Stock Car Racing or Time Trial, the layout and direction, and the car on a slow orbit on the grid. ↑/↓ pick a row,
/// ←/→ change it, Enter starts, Esc quits. Keyboard only.
/// </summary>
public partial class RaceSetup : Node3D
{
    public const string ScenePath = "res://scenes/race/RaceSetup.tscn";

    /// <summary>Set by this screen, read by <see cref="RaceMode"/> and <see cref="TimeTrialMode"/> when they are the main scene.</summary>
    public static RaceOptions? Chosen { get; set; }

    private static readonly string[] Rows = { "EVENT", "LAYOUT", "DIRECTION", "LIGHT", "CAR", "COLOUR", "STYLE", "DIVISION" };
    private const int RowEvent = 0, RowLayout = 1, RowDirection = 2, RowLight = 3, RowCar = 4, RowColour = 5, RowStyle = 6, RowDivision = 7;
    private static readonly BowlPreset[] Presets = { BowlPreset.Afternoon, BowlPreset.GoldenHour, BowlPreset.Night };
    private int _preset;

    private int _row, _event, _car, _colour, _style, _division = 4;
    private SpeedwayLayout _layout = SpeedwayLayout.Figure8;
    private bool _reverse;
    private Speedway? _venue;
    private CarModel? _model;
    private Transform3D _carPose = Transform3D.Identity;
    private Camera3D _camera = null!;
    private float _orbit;
    private readonly Label[] _values = new Label[Rows.Length];
    private readonly Label[] _captions = new Label[Rows.Length];
    private readonly Control[] _rowBoxes = new Control[Rows.Length];
    private readonly ColorRect[,] _bars = new ColorRect[5, 10];
    private readonly Panel[] _swatches = new Panel[16];
    private Label _title = null!, _subtitle = null!, _verdict = null!, _hint = null!;

    private SpeedwayEvent Event => (SpeedwayEvent)_event;
    private CarInfo Info => CarCatalog.All[_car];

    public override void _Ready()
    {
        Controls.Apply(Controls.Current);
        if (Chosen is { } c)
        {
            _event = (int)c.Event;
            _layout = c.Layout;
            _reverse = c.Reverse;
            _preset = Math.Max(0, Array.IndexOf(Presets, c.Preset));
            _car = Math.Max(0, Array.IndexOf(CarCatalog.Ids, c.Car));
            _division = c.Division;
            if (c.Livery is { } l)
            {
                _colour = Math.Max(0, Array.IndexOf(Drivers.Palette, l.Paint));
                _style = l.Mode;
            }
        }
        else _car = Math.Max(0, Array.IndexOf(CarCatalog.Ids, "kitsune"));
        _camera = new Camera3D { Current = true, Fov = 40f, Near = 0.05f, Far = 4000f };
        AddChild(_camera);
        BuildUi();
        LoadVenue();
        LoadCar();
        Refresh();
    }

    private Livery PlayerLivery()
    {
        var paint = Drivers.Palette[_colour];
        return new Livery(paint, Drivers.Contrast(paint), _style, Wear: 0.15f, Dirt: 0.25f, Number: 7,
            Roof: DerbyMode.RoofColor(_division), Sponsor: 2);
    }

    private void BuildUi()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        var root = new Control { Theme = HudStyle.Theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(root);

        var title = HudStyle.Place(new VBoxContainer(), 0f, 0f, new Vector2(70, 60), new Vector2(1100, 0));
        title.AddThemeConstantOverride("separation", -18);
        title.AddChild(HudStyle.Label("CROSSROADS SPEEDWAY", 40, HudStyle.Yellow, "Bold", outline: 8));
        _title = HudStyle.Label("", 104, HudStyle.White, "ExtraBold", outline: 10);
        title.AddChild(_title);
        _subtitle = HudStyle.Label("", 30, HudStyle.Grey, "SemiBold", outline: 6);
        title.AddChild(_subtitle);
        root.AddChild(title);

        var panel = HudStyle.Place(new PanelContainer(), 0f, 1f, new Vector2(70, -56), new Vector2(820, 0));
        panel.AddThemeStyleboxOverride("panel", HudStyle.Panel(HudStyle.Yellow, 0.05f));
        root.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        panel.AddChild(box);
        for (int i = 0; i < Rows.Length; i++)
        {
            var group = new VBoxContainer();
            group.AddThemeConstantOverride("separation", 2);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 20);
            _captions[i] = HudStyle.Label(Rows[i], 26, HudStyle.Grey, "SemiBold", outline: 0);
            _captions[i].CustomMinimumSize = new Vector2(170, 0);
            row.AddChild(_captions[i]);
            _values[i] = HudStyle.Label("", 38, HudStyle.White, "ExtraBold", outline: 0);
            row.AddChild(_values[i]);
            group.AddChild(row);
            if (i == RowCar) group.AddChild(BuildStats());
            if (i == RowColour)
            {
                _values[i].Visible = false;
                group.AddChild(BuildPalette());
            }
            _rowBoxes[i] = group;
            box.AddChild(group);
        }
        _hint = HudStyle.Label("", 24, HudStyle.White, "Medium", outline: 0);
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _hint.CustomMinimumSize = new Vector2(760, 0);
        box.AddChild(_hint);
        box.AddChild(HudStyle.Label("↑/↓ choose   ·   ←/→ change   ·   ENTER start   ·   ESC quit", 24, HudStyle.Yellow, "SemiBold", outline: 0));
    }

    private Control BuildStats()
    {
        var outer = new HBoxContainer();
        outer.AddThemeConstantOverride("separation", 28);
        var bars = new VBoxContainer();
        bars.AddThemeConstantOverride("separation", 3);
        string[] names = { "SPEED", "ACCEL", "HANDLING", "WEIGHT", "TOUGH" };
        for (int s = 0; s < 5; s++)
        {
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 3);
            var name = HudStyle.Label(names[s], 18, HudStyle.Grey, "SemiBold", outline: 0);
            name.CustomMinimumSize = new Vector2(170, 0);
            line.AddChild(name);
            for (int k = 0; k < 10; k++)
            {
                _bars[s, k] = new ColorRect { CustomMinimumSize = new Vector2(20, 13), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
                line.AddChild(_bars[s, k]);
            }
            bars.AddChild(line);
        }
        outer.AddChild(bars);
        _verdict = HudStyle.Label("", 24, HudStyle.White, "Bold", outline: 0);
        _verdict.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        outer.AddChild(_verdict);
        return outer;
    }

    private Control BuildPalette()
    {
        var grid = new GridContainer { Columns = 8 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        for (int i = 0; i < _swatches.Length; i++)
        {
            _swatches[i] = new Panel { CustomMinimumSize = new Vector2(44, 26) };
            grid.AddChild(_swatches[i]);
        }
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 190);
        margin.AddThemeConstantOverride("margin_bottom", 6);
        margin.AddChild(grid);
        return margin;
    }

    private void LoadVenue()
    {
        _venue?.QueueFree();
        _venue = new Speedway { Name = "Speedway", LayoutId = _layout, Reverse = _reverse, Visuals = true, Preset = Presets[_preset] };
        AddChild(_venue);
        // The car stands on pole, facing the first turn.
        _carPose = _venue.Layout.GridSlot(0);
        if (_model != null) _model.Root.Transform = _carPose with { Origin = _carPose.Origin - _carPose.Basis.Y * 0.62f };
        _camera.Current = true;
    }

    private void LoadCar()
    {
        if (_model != null) _model.Root.QueueFree();
        _model = CarModel.Load(Info.Id, PlayerLivery());
        _model.SetPlate("DD 07");
        AddChild(_model.Root);
        _model.Root.Transform = _carPose with { Origin = _carPose.Origin - _carPose.Basis.Y * 0.62f };
        _model.Flush();
    }

    private static string Verdict(int stars) => stars switch { >= 5 => "BEST", 4 => "STRONG", 3 => "GOOD", 2 => "FAIR", _ => "WEAK" };

    private void Refresh()
    {
        var info = Info;
        bool trial = Event == SpeedwayEvent.TimeTrial;
        _title.Text = Event switch
        {
            SpeedwayEvent.StockCarRacing => "STOCK CAR RACING",
            SpeedwayEvent.TimeTrial => "TIME TRIAL",
            _ => "WRECKIN' RACING",
        };
        int laps = SpeedwayData.Load().Get(_layout).Laps;
        _subtitle.Text = Event switch
        {
            SpeedwayEvent.StockCarRacing => $"{RaceLayout.DisplayName(_layout, _reverse).ToUpperInvariant()}  ·  20 CARS  ·  {laps} LAPS  ·  POINTS FOR THE FINISH ONLY",
            SpeedwayEvent.TimeTrial => $"{RaceLayout.DisplayName(_layout, _reverse).ToUpperInvariant()}  ·  FLYING LAPS AGAINST THE CLOCK AND YOUR GHOST",
            _ => $"{RaceLayout.DisplayName(_layout, _reverse).ToUpperInvariant()}  ·  20 CARS  ·  {laps} LAPS  ·  SPINS AND WRECKS SCORE",
        };
        _values[RowEvent].Text = _title.Text;
        _values[RowLayout].Text = _layout == SpeedwayLayout.Oval ? "OVAL" : "FIGURE-8";
        _values[RowDirection].Text = _reverse ? "REVERSE" : "FORWARD";
        _values[RowLight].Text = Bowl.DisplayName(Presets[_preset]).ToUpperInvariant();
        _values[RowCar].Text = $"{info.FullName.ToUpperInvariant()}  ({info.Region} {info.Body})";
        _values[RowStyle].Text = Livery.ModeNames[_style];
        _values[RowDivision].Text = $"DIVISION {_division}" + (_division == 4 ? "  (rookies)" : _division == 1 ? "  (the best)" : "");
        _rowBoxes[RowDivision].Visible = !trial;
        if (trial && _row == RowDivision) _row = RowStyle;
        int[] ratings = { info.Speed, info.Accel, info.Handling, info.Weight, info.Toughness };
        for (int s = 0; s < 5; s++)
            for (int k = 0; k < 10; k++)
                _bars[s, k].Color = k < ratings[s] ? HudStyle.Yellow : new Color(1f, 1f, 1f, 0.12f);
        string best = "";
        if (trial && TimeTrialRecords.Load().Best(TimeTrialRecords.Key(_layout, _reverse, info.Id)) is { } lap)
            best = $"\nYOUR BEST  {TimeTrialTargets.Format(lap.Time)}";
        _verdict.Text = trial
            ? $"PACE  {Verdict(info.PaceStars)}{best}\n{info.Engine}  ·  {(info.FrontDrive ? "FWD" : "RWD")}"
            : $"WRECKIN'  {Verdict(info.RaceStars)}\nPACE  {Verdict(info.PaceStars)}\n{info.Engine}  ·  {(info.FrontDrive ? "FWD" : "RWD")}";
        for (int i = 0; i < _swatches.Length; i++)
        {
            bool on = i == _colour;
            _swatches[i].AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = Drivers.Palette[i],
                BorderColor = on ? (_row == RowColour ? HudStyle.Yellow : HudStyle.White) : new Color(0f, 0f, 0f, 0.6f),
                BorderWidthLeft = on ? 4 : 1, BorderWidthRight = on ? 4 : 1, BorderWidthTop = on ? 4 : 1, BorderWidthBottom = on ? 4 : 1,
            });
        }
        _hint.Text = _row switch
        {
            RowEvent => Event switch
            {
                SpeedwayEvent.StockCarRacing => "Race for the finishing order (50, 40, 35 … points). Contact scores nothing, but bruisers still bump. Pit as often as you like.",
                SpeedwayEvent.TimeTrial => "Alone on the track. Three sector splits, the ghost of your best lap, medals and a leaderboard per car. R restarts at once.",
                _ => "Score for spinning and wrecking rivals, double on the leader, plus a finishing bonus. One pit stop.",
            },
            RowLayout => _layout == SpeedwayLayout.Oval
                ? "Banked turns and two long straights: flat out, three wide."
                : "The figure-8: the legs cross in the middle, and the crossover is blind.",
            RowDirection => "Every layout runs both ways.",
            RowLight => Presets[_preset] switch
            {
                BowlPreset.GoldenHour => "Low sun, long shadows, the floodlights coming on.",
                BowlPreset.Night => "Under the floodlights, headlights on.",
                _ => "A bright afternoon in the hills.",
            },
            RowCar => info.Hint,
            RowColour => "Your paint: ←/→ through the palette.",
            RowStyle => "The second colour: stripes, two-tone, a side band or a flash.",
            _ => $"The AI field gets faster from Division 4 to 1.",
        };
        for (int i = 0; i < Rows.Length; i++)
            _captions[i].AddThemeColorOverride("font_color", i == _row ? HudStyle.Yellow : HudStyle.Grey);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsEcho() || e is not InputEventKey { Pressed: true } key) return;
        int rows = Event == SpeedwayEvent.TimeTrial ? Rows.Length - 1 : Rows.Length;
        switch (key.PhysicalKeycode)
        {
            case Key.Up: _row = (_row + rows - 1) % rows; break;
            case Key.Down: _row = (_row + 1) % rows; break;
            case Key.Left or Key.Right:
            {
                int d = key.PhysicalKeycode == Key.Right ? 1 : -1;
                switch (_row)
                {
                    case RowEvent: _event = (_event + d + 3) % 3; break;
                    case RowLayout: _layout = _layout == SpeedwayLayout.Oval ? SpeedwayLayout.Figure8 : SpeedwayLayout.Oval; LoadVenue(); break;
                    case RowDirection: _reverse = !_reverse; LoadVenue(); break;
                    case RowLight: _preset = (_preset + d + Presets.Length) % Presets.Length; LoadVenue(); break;
                    case RowCar: _car = (_car + d + CarCatalog.All.Length) % CarCatalog.All.Length; LoadCar(); break;
                    case RowColour: _colour = (_colour + d + Drivers.Palette.Length) % Drivers.Palette.Length; _model?.ApplyLivery(PlayerLivery()); break;
                    case RowStyle: _style = (_style + d + Livery.Modes) % Livery.Modes; _model?.ApplyLivery(PlayerLivery()); break;
                    default: _division = Math.Clamp(_division - d, 1, 4); _model?.ApplyLivery(PlayerLivery()); break;
                }
                break;
            }
            case Key.Enter or Key.KpEnter or Key.Space:
                Chosen = new RaceOptions(Event, _layout, _reverse, Info.Id, _division, PlayerLivery(), Presets[_preset]);
                DevTools.MarkSceneRequest();
                GetTree().ChangeSceneToFile(Event == SpeedwayEvent.TimeTrial ? "res://scenes/race/TimeTrial.tscn" : "res://scenes/race/Race.tscn");
                break;
            case Key.Escape:
                DevTools.QuitClean(GetTree(), 0);
                break;
            default: return;
        }
        Refresh();
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        _orbit += (float)delta * 0.12f;
        var centre = _carPose.Origin + new Vector3(0f, 0.1f, 0f);
        var eye = centre + new Vector3(Mathf.Cos(_orbit) * 7.5f, 1.6f, Mathf.Sin(_orbit) * 7.5f);
        var right = (centre - eye).Cross(Vector3.Up).Normalized();
        _camera.LookAtFromPosition(eye, centre - right * 2.6f + new Vector3(0f, -0.3f, 0f), Vector3.Up);
    }
}
