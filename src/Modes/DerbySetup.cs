using System;
using DestructionRacing.Core;
using DestructionRacing.Tools;
using DestructionRacing.Tracks;
using DestructionRacing.UI;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Modes;

/// <summary>The settings the next derby starts with (kept for "race again").</summary>
public sealed record DerbyOptions(BowlPreset Preset, string Car, int Division);

/// <summary>
/// The derby's event set-up (the M7 front end will replace it): the Bowl in the chosen light behind your car on
/// a slow orbit; ↑/↓ pick a row, ←/→ change it, Enter starts the derby, Esc quits. Keyboard only.
/// </summary>
public partial class DerbySetup : Node3D
{
    /// <summary>Set by this screen, read by <see cref="DerbyMode"/> when it is the main scene.</summary>
    public static DerbyOptions? Chosen { get; set; }

    private static readonly BowlPreset[] Presets = { BowlPreset.Afternoon, BowlPreset.GoldenHour, BowlPreset.Night };
    private static readonly string[] Cars = { "kitsune", "brougham", "nordvik" };
    private static readonly string[] CarNames = { "KITSUNE  (JP coupé)", "ALDRIDGE BROUGHAM  (US sedan)", "NORDVIK  (EU sedan)" };
    private static readonly string[] CarHints =
    {
        "Light and nimble: quick to turn, but the nose is soft.",
        "Heavy and tough: a battering ram, slow to turn.",
        "Balanced and solid: a strong rear for reverse-ramming.",
    };

    private int _row, _preset, _car, _division = 4;
    private Node3D? _bowl;
    private CarModel? _model;
    private Camera3D _camera = null!;
    private float _orbit;
    private readonly Label[] _values = new Label[3];
    private readonly Label[] _captions = new Label[3];
    private Label _hint = null!;

    public override void _Ready()
    {
        if (Chosen is { } c)
        {
            _preset = Array.IndexOf(Presets, c.Preset);
            _car = Math.Max(0, Array.IndexOf(Cars, c.Car));
            _division = c.Division;
        }
        _camera = new Camera3D { Current = true, Fov = 40f, Near = 0.05f, Far = 4000f };
        AddChild(_camera);
        BuildUi();
        LoadBowl();
        LoadCar();
        Refresh();
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

        var panel = HudStyle.Place(new PanelContainer(), 0f, 1f, new Vector2(70, -420), new Vector2(760, 0));
        panel.AddThemeStyleboxOverride("panel", HudStyle.Panel(HudStyle.Yellow, 0.12f));
        root.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        panel.AddChild(box);
        string[] rows = { "LIGHT", "CAR", "DIVISION" };
        for (int i = 0; i < 3; i++)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 20);
            _captions[i] = HudStyle.Label(rows[i], 26, HudStyle.Grey, "SemiBold", outline: 0);
            _captions[i].CustomMinimumSize = new Vector2(150, 0);
            row.AddChild(_captions[i]);
            _values[i] = HudStyle.Label("", 40, HudStyle.White, "ExtraBold", outline: 0);
            row.AddChild(_values[i]);
            box.AddChild(row);
        }
        _hint = HudStyle.Label("", 24, HudStyle.White, "Medium", outline: 0);
        box.AddChild(_hint);
        box.AddChild(HudStyle.Label("↑/↓ choose   ·   ←/→ change   ·   ENTER start   ·   ESC quit", 24, HudStyle.Yellow, "SemiBold", outline: 0));
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
        var livery = new Livery(DerbyMode.Liveries[0], new Color(0.93f, 0.93f, 0.9f), 0, Wear: 0.15f, Dirt: 0.25f, Number: 7,
            Roof: DerbyMode.RoofColor(_division));
        _model = CarModel.Load(Cars[_car], livery);
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

    private void Refresh()
    {
        _values[0].Text = Bowl.DisplayName(Presets[_preset]).ToUpperInvariant();
        _values[1].Text = CarNames[_car];
        _values[2].Text = $"DIVISION {_division}" + (_division == 4 ? "  (rookies)" : _division == 1 ? "  (the best)" : "");
        _hint.Text = _row switch
        {
            0 => Presets[_preset] switch
            {
                BowlPreset.GoldenHour => "Low sun, long shadows, the floodlights coming on.",
                BowlPreset.Night => "Under the floodlights: headlights, flashbulbs in the stands.",
                _ => "Bright afternoon under the mountains.",
            },
            1 => CarHints[_car],
            _ => $"The AI field gets tougher from Division 4 to 1; at most {(_division == 4 ? 2 : _division == 1 ? 4 : 3)} cars hunt you at once.",
        };
        for (int i = 0; i < 3; i++)
            _captions[i].AddThemeColorOverride("font_color", i == _row ? HudStyle.Yellow : HudStyle.Grey);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsEcho() || e is not InputEventKey { Pressed: true } key) return;
        switch (key.PhysicalKeycode)
        {
            case Key.Up: _row = (_row + 2) % 3; break;
            case Key.Down: _row = (_row + 1) % 3; break;
            case Key.Left or Key.Right:
            {
                int d = key.PhysicalKeycode == Key.Right ? 1 : -1;
                if (_row == 0) { _preset = (_preset + d + Presets.Length) % Presets.Length; LoadBowl(); LoadCar(); }
                else if (_row == 1) { _car = (_car + d + Cars.Length) % Cars.Length; LoadCar(); }
                else { _division = Math.Clamp(_division - d, 1, 4); LoadCar(); }
                break;
            }
            case Key.Enter or Key.KpEnter or Key.Space:
                Chosen = new DerbyOptions(Presets[_preset], Cars[_car], _division);
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
