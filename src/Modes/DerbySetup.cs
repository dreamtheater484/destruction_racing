using System;
using DestructionRacing.AI;
using DestructionRacing.Core;
using DestructionRacing.Tools;
using DestructionRacing.Tracks;
using DestructionRacing.UI;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Modes;

/// <summary>The settings the next derby starts with (kept for "race again").</summary>
public sealed record DerbyOptions(BowlPreset Preset, string Car, int Division, Livery? Livery = null);

/// <summary>
/// The derby's event set-up (the M7 front end will replace it): the Bowl in the chosen light behind your car on
/// a slow orbit; ↑/↓ pick a row, ←/→ change it, Enter starts the derby, Esc quits. Keyboard only. The car row
/// shows the five ratings and an honest Derby / Race verdict from the balance sims (plan §9.3); the colour row is
/// the keyboard palette grid (plan §9.4).
/// </summary>
public partial class DerbySetup : Node3D
{
    /// <summary>Set by this screen, read by <see cref="DerbyMode"/> when it is the main scene.</summary>
    public static DerbyOptions? Chosen { get; set; }

    private static readonly BowlPreset[] Presets = { BowlPreset.Afternoon, BowlPreset.GoldenHour, BowlPreset.Night };
    private static readonly string[] Rows = { "LIGHT", "CAR", "COLOUR", "STYLE", "DIVISION" };
    private const int RowLight = 0, RowCar = 1, RowColour = 2, RowStyle = 3, RowDivision = 4;

    private int _row, _preset, _car, _colour, _style, _division = 4;
    private Node3D? _bowl;
    private CarModel? _model;
    private Camera3D _camera = null!;
    private float _orbit;
    private readonly Label[] _values = new Label[Rows.Length];
    private readonly Label[] _captions = new Label[Rows.Length];
    private readonly ColorRect[,] _bars = new ColorRect[5, 10];
    private readonly Panel[] _swatches = new Panel[16];
    private Label _verdict = null!;
    private Label _hint = null!;

    public override void _Ready()
    {
        if (Chosen is { } c)
        {
            _preset = Array.IndexOf(Presets, c.Preset);
            _car = Math.Max(0, Array.IndexOf(CarCatalog.Ids, c.Car));
            _division = c.Division;
            if (c.Livery is { } l)
            {
                _colour = Math.Max(0, Array.IndexOf(Drivers.Palette, l.Paint));
                _style = l.Mode;
            }
        }
        else
        {
            _car = Math.Max(0, Array.IndexOf(CarCatalog.Ids, "kitsune"));
        }
        _camera = new Camera3D { Current = true, Fov = 40f, Near = 0.05f, Far = 4000f };
        AddChild(_camera);
        BuildUi();
        LoadBowl();
        LoadCar();
        Refresh();
    }

    private CarInfo Info => CarCatalog.All[_car];

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

        var title = HudStyle.Place(new VBoxContainer(), 0f, 0f, new Vector2(70, 60), new Vector2(900, 0));
        title.AddThemeConstantOverride("separation", -18);
        title.AddChild(HudStyle.Label("DESTRUCTION RACING", 40, HudStyle.Yellow, "Bold", outline: 8));
        title.AddChild(HudStyle.Label("DESTRUCTION DERBY", 104, HudStyle.White, "ExtraBold", outline: 10));
        title.AddChild(HudStyle.Label("THE BOWL  ·  20 CARS  ·  LAST CAR RUNNING WINS", 30, HudStyle.Grey, "SemiBold", outline: 6));
        root.AddChild(title);

        var panel = HudStyle.Place(new PanelContainer(), 0f, 1f, new Vector2(70, -56), new Vector2(820, 0));   // grows up from here
        panel.AddThemeStyleboxOverride("panel", HudStyle.Panel(HudStyle.Yellow, 0.05f));   // a tall panel: little slant
        root.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        panel.AddChild(box);
        for (int i = 0; i < Rows.Length; i++)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 20);
            _captions[i] = HudStyle.Label(Rows[i], 26, HudStyle.Grey, "SemiBold", outline: 0);
            _captions[i].CustomMinimumSize = new Vector2(150, 0);
            row.AddChild(_captions[i]);
            _values[i] = HudStyle.Label("", 40, HudStyle.White, "ExtraBold", outline: 0);
            row.AddChild(_values[i]);
            box.AddChild(row);
            if (i == RowCar) box.AddChild(BuildStats());
            if (i == RowColour)
            {
                _values[i].Visible = false;      // the palette grid is the value
                box.AddChild(BuildPalette());
            }
        }
        _hint = HudStyle.Label("", 24, HudStyle.White, "Medium", outline: 0);
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _hint.CustomMinimumSize = new Vector2(760, 0);
        box.AddChild(_hint);
        box.AddChild(HudStyle.Label("↑/↓ choose   ·   ←/→ change   ·   ENTER start   ·   ESC quit", 24, HudStyle.Yellow, "SemiBold", outline: 0));
    }

    /// <summary>Five rating bars (10 segments each) and the Derby / Race verdict.</summary>
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
            name.CustomMinimumSize = new Vector2(150, 0);
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

    /// <summary>The keyboard palette grid: 2 rows of 8 colours, ←/→ steps through them.</summary>
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
        margin.AddThemeConstantOverride("margin_left", 170);
        margin.AddThemeConstantOverride("margin_bottom", 6);
        margin.AddChild(grid);
        return margin;
    }

    private void LoadBowl()
    {
        _bowl?.QueueFree();
        var look = GD.Load<PackedScene>(Bowl.ScenePath(Presets[_preset])).Instantiate<BowlLook>();
        look.Empty = true;
        AddChild(look);
        _bowl = look;
        look.AddChild(new Crowd { Night = Presets[_preset] == BowlPreset.Night, BaseExcitement = 0.3f });
        _camera.Current = true;
        if (_model != null) _model.Root.GetParent()?.MoveChild(_model.Root, -1);
    }

    private void LoadCar()
    {
        if (_model != null) _model.Root.QueueFree();
        _model = CarModel.Load(Info.Id, PlayerLivery());
        _model.SetPlate("DD 07");
        AddChild(_model.Root);
        _model.Root.Position = new Vector3(-30f, 0f, 14f);
        _model.Root.RotationDegrees = new Vector3(0f, -35f, 0f);
        // Lit lamps at dusk and at night.
        if (Presets[_preset] != BowlPreset.Afternoon)
        {
            (_model.Material("lens_clear") as ShaderMaterial)?.SetShaderParameter("emission_energy", 5f);
            (_model.Material("lens_red") as ShaderMaterial)?.SetShaderParameter("emission_energy", 1.6f);
        }
        _model.Flush();
    }

    private static string Verdict(int stars) => stars switch { >= 5 => "BEST", 4 => "STRONG", 3 => "GOOD", 2 => "FAIR", _ => "WEAK" };

    private void Refresh()
    {
        var info = Info;
        _values[RowLight].Text = Bowl.DisplayName(Presets[_preset]).ToUpperInvariant();
        _values[RowCar].Text = $"{info.FullName.ToUpperInvariant()}  ({info.Region} {info.Body})";
        _values[RowColour].Text = "";
        _values[RowStyle].Text = Livery.ModeNames[_style];
        _values[RowDivision].Text = $"DIVISION {_division}" + (_division == 4 ? "  (rookies)" : _division == 1 ? "  (the best)" : "");
        int[] ratings = { info.Speed, info.Accel, info.Handling, info.Weight, info.Toughness };
        for (int s = 0; s < 5; s++)
            for (int k = 0; k < 10; k++)
                _bars[s, k].Color = k < ratings[s] ? HudStyle.Yellow : new Color(1f, 1f, 1f, 0.12f);
        // Wreckin' points are mostly spin points (the heavy cars bash well); pace is the finishing order.
        _verdict.Text = $"DERBY  {Verdict(info.DerbyStars)}\nWRECKIN'  {Verdict(info.RaceStars)}\nPACE  {Verdict(info.PaceStars)}\n" +
            $"{info.Engine}  ·  {(info.FrontDrive ? "FWD" : "RWD")}";
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
            RowLight => Presets[_preset] switch
            {
                BowlPreset.GoldenHour => "Low sun, long shadows, the floodlights coming on.",
                BowlPreset.Night => "Under the floodlights: headlights, flashbulbs in the stands.",
                _ => "Bright afternoon under the mountains.",
            },
            RowCar => info.Hint,
            RowColour => "Your paint: ←/→ through the palette.",
            RowStyle => "The second colour: stripes, two-tone, a side band or a flash.",
            _ => $"The AI field gets tougher from Division 4 to 1; at most {(_division == 4 ? 2 : _division == 1 ? 4 : 3)} cars hunt you at once.",
        };
        for (int i = 0; i < Rows.Length; i++)
            _captions[i].AddThemeColorOverride("font_color", i == _row ? HudStyle.Yellow : HudStyle.Grey);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsEcho() || e is not InputEventKey { Pressed: true } key) return;
        switch (key.PhysicalKeycode)
        {
            case Key.Up: _row = (_row + Rows.Length - 1) % Rows.Length; break;
            case Key.Down: _row = (_row + 1) % Rows.Length; break;
            case Key.Left or Key.Right:
            {
                int d = key.PhysicalKeycode == Key.Right ? 1 : -1;
                switch (_row)
                {
                    case RowLight: _preset = (_preset + d + Presets.Length) % Presets.Length; LoadBowl(); LoadCar(); break;
                    case RowCar: _car = (_car + d + CarCatalog.All.Length) % CarCatalog.All.Length; LoadCar(); break;
                    case RowColour: _colour = (_colour + d + Drivers.Palette.Length) % Drivers.Palette.Length; _model?.ApplyLivery(PlayerLivery()); break;
                    case RowStyle: _style = (_style + d + Livery.Modes) % Livery.Modes; _model?.ApplyLivery(PlayerLivery()); break;
                    default: _division = Math.Clamp(_division - d, 1, 4); _model?.ApplyLivery(PlayerLivery()); break;
                }
                break;
            }
            case Key.Enter or Key.KpEnter or Key.Space:
                Chosen = new DerbyOptions(Presets[_preset], Info.Id, _division, PlayerLivery());
                GetTree().ChangeSceneToFile("res://scenes/derby/Derby.tscn");
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
        var centre = new Vector3(-30f, 0.7f, 14f);
        var eye = centre + new Vector3(Mathf.Cos(_orbit) * 7.5f, 1.6f, Mathf.Sin(_orbit) * 7.5f);
        // Look a little to the car's left, so it sits right of the menu panel.
        var right = (centre - eye).Cross(Vector3.Up).Normalized();
        _camera.LookAtFromPosition(eye, centre - right * 2.6f + new Vector3(0f, -0.3f, 0f), Vector3.Up);
    }
}
