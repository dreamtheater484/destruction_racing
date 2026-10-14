using System;
using System.Collections.Generic;
using System.Linq;
using DestructionRacing.Core;
using DestructionRacing.Rules;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.UI;

/// <summary>What the event HUD reads from the running mode (the derby or a race).</summary>
public interface IHudMode
{
    Referee Referee { get; }
    IReadOnlyList<Car> Cars { get; }
    double Time { get; }
    double CountdownLeft { get; }
    bool PlayerOut { get; }
    string DriverName(Car car);
    /// <summary>The big number top centre (points place in the derby, race position in a race).</summary>
    string HudRank { get; }
    /// <summary>First line top right ("12  Still Running", "Lap 2/6").</summary>
    string HudStatus { get; }
    /// <summary>A flashing warning in the middle of the screen ("WRONG WAY"), or null.</summary>
    string? HudWarning { get; }
    /// <summary>The Tab standings panel.</summary>
    string StandingsText();
    void SkipToResults();
    void ToggleDoubleSpeed();
    void SpectateNext(int direction);
}

/// <summary>
/// Grey-box event HUD in the DD2 layout (plan §13, reference feel sheet): Speed and Race Points top left, the big
/// position top centre, "Still Running" or the lap and the clock top right, the damage diagram lower right, popups,
/// the wreck feed, the countdown, the wrecked overlay, Tab standings and the results table.
/// </summary>
public partial class EventHud : CanvasLayer
{
    /// <summary>One results line. Bonus is the survivor bonus (derby) or the finishing bonus (race).</summary>
    public readonly record struct ResultRow(int Place, int Number, string Driver, Color Roof, int Spins, int Wrecks, int Bonus, int Total, bool IsPlayer, string Note = "");

    public IHudMode Mode { get; set; } = null!;
    public Car PlayerCar { get; set; } = null!;
    public Func<string>? ViewName { get; set; }

    private Label _speed = null!, _points = null!, _rank = null!, _running = null!, _clock = null!;
    private Label _countdown = null!, _spin = null!, _help = null!, _pause = null!, _wrecked = null!, _warning = null!;
    private VBoxContainer _popups = null!, _feed = null!;
    private DamageDiagram _diagram = null!;
    private PanelContainer _standings = null!, _results = null!;
    private Label _standingsText = null!;
    private float _spinTimer;
    private bool _resultsShown;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _speed = MakeLabel(30, Corner.TopLeft, new Vector2(40, 28));
        _points = MakeLabel(30, Corner.TopLeft, new Vector2(40, 66));
        _rank = MakeLabel(96, Corner.TopCenter, new Vector2(-70, 6));
        _rank.HorizontalAlignment = HorizontalAlignment.Center;
        _rank.CustomMinimumSize = new Vector2(140, 0);
        _running = MakeLabel(30, Corner.TopRight, new Vector2(-360, 28));
        _clock = MakeLabel(30, Corner.TopRight, new Vector2(-360, 66));

        _countdown = MakeLabel(160, Corner.Center, new Vector2(-200, -200));
        _countdown.HorizontalAlignment = HorizontalAlignment.Center;
        _countdown.CustomMinimumSize = new Vector2(400, 0);
        _spin = MakeLabel(54, Corner.Center, new Vector2(-300, -320));
        _spin.HorizontalAlignment = HorizontalAlignment.Center;
        _spin.CustomMinimumSize = new Vector2(600, 0);

        _popups = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _popups.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _popups.Position = new Vector2(-450, 230);
        _popups.CustomMinimumSize = new Vector2(900, 0);
        AddChild(_popups);

        _feed = new VBoxContainer();
        _feed.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _feed.Position = new Vector2(-420, 120);
        _feed.CustomMinimumSize = new Vector2(380, 0);
        AddChild(_feed);

        _diagram = new DamageDiagram { Target = PlayerCar };
        _diagram.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _diagram.Position = new Vector2(-170, -250);
        AddChild(_diagram);

        _help = MakeLabel(18, Corner.BottomLeft, new Vector2(40, -44));
        _pause = MakeLabel(40, Corner.Center, new Vector2(-450, -60));
        _pause.HorizontalAlignment = HorizontalAlignment.Center;
        _pause.CustomMinimumSize = new Vector2(900, 0);
        _pause.Text = "PAUSED\nEsc / P  resume   ·   R  restart   ·   Q  quit";
        _pause.Visible = false;

        _wrecked = MakeLabel(34, Corner.Center, new Vector2(-500, 120));
        _wrecked.HorizontalAlignment = HorizontalAlignment.Center;
        _wrecked.CustomMinimumSize = new Vector2(1000, 0);
        _wrecked.Visible = false;

        _warning = MakeLabel(80, Corner.Center, new Vector2(-450, -150));
        _warning.HorizontalAlignment = HorizontalAlignment.Center;
        _warning.CustomMinimumSize = new Vector2(900, 0);
        _warning.AddThemeColorOverride("font_color", new Color(1f, 0.25f, 0.2f));
        _warning.Visible = false;

        _standings = MakePanel(new Vector2(-260, -330), new Vector2(520, 0));
        _standingsText = new Label();
        _standingsText.AddThemeFontSizeOverride("font_size", 20);
        _standings.AddChild(_standingsText);
        _standings.Visible = false;

        _results = MakePanel(new Vector2(-430, -400), new Vector2(860, 0));
        _results.Visible = false;
    }

    private enum Corner { TopLeft, TopCenter, TopRight, Center, BottomLeft }

    private Label MakeLabel(int size, Corner corner, Vector2 offset)
    {
        var label = new Label();
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", Colors.White);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        label.AddThemeConstantOverride("outline_size", Math.Max(6, size / 8));
        label.SetAnchorsPreset(corner switch
        {
            Corner.TopLeft => Control.LayoutPreset.TopLeft,
            Corner.TopCenter => Control.LayoutPreset.CenterTop,
            Corner.TopRight => Control.LayoutPreset.TopRight,
            Corner.BottomLeft => Control.LayoutPreset.BottomLeft,
            _ => Control.LayoutPreset.Center,
        });
        label.Position += offset;
        AddChild(label);
        return label;
    }

    private PanelContainer MakePanel(Vector2 offset, Vector2 minSize)
    {
        var panel = new PanelContainer { CustomMinimumSize = minSize };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.04f, 0.05f, 0.88f),
            BorderColor = new Color(1f, 0.8f, 0.1f),
            BorderWidthTop = 4,
            ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 18, ContentMarginBottom = 18,
        });
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.Position += offset;
        AddChild(panel);
        return panel;
    }

    // ------------------------------------------------------------------ API used by the mode

    public void Popup(string text, Color color, bool big = false)
    {
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", big ? 72 : 44);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        label.AddThemeConstantOverride("outline_size", 10);
        _popups.AddChild(label);
        if (_popups.GetChildCount() > 4) _popups.GetChild(0).QueueFree();
        var tween = label.CreateTween();
        tween.TweenInterval(big ? 1.6 : 2.2);
        tween.TweenProperty(label, "modulate:a", 0.0, 0.5);
        tween.TweenCallback(Callable.From(label.QueueFree));
    }

    public void Feed(string text)
    {
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Right, CustomMinimumSize = new Vector2(380, 0) };
        label.AddThemeFontSizeOverride("font_size", 22);
        label.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.6f));
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        label.AddThemeConstantOverride("outline_size", 6);
        _feed.AddChild(label);
        if (_feed.GetChildCount() > 5) _feed.GetChild(0).QueueFree();
        var tween = label.CreateTween();
        tween.TweenInterval(6.0);
        tween.TweenProperty(label, "modulate:a", 0.0, 0.8);
        tween.TweenCallback(Callable.From(label.QueueFree));
    }

    /// <summary>Live spin popup for the player's victim: "SPIN 90" → "SPIN 180" → "SPIN 360".</summary>
    public void SpinProgress(SpinTier tier)
    {
        _spin.Text = $"SPIN {(int)tier}!";
        _spin.AddThemeColorOverride("font_color", tier == SpinTier.Full ? new Color(1f, 0.8f, 0.2f) : Colors.White);
        _spinTimer = 1.2f;
    }

    public void PulseZone(DamageZone zone) => _diagram.Pulse(zone);

    public void ShowWrecked(string headline = "WRECKED  –  RACE OVER")
    {
        _wrecked.Visible = true;
        _wrecked.Text = headline + "\n←/→ watch another car   ·   Space ×2 speed   ·   Enter skip to results";
        Popup("WRECKED!", new Color(1f, 0.3f, 0.2f), big: true);
    }

    public void ShowResults(string heading, string bonusHeader, IReadOnlyList<ResultRow> rows)
    {
        _resultsShown = true;
        _wrecked.Visible = false;
        _standings.Visible = false;
        foreach (Node child in _results.GetChildren()) child.QueueFree();

        var box = new VBoxContainer();
        _results.AddChild(box);
        var title = new Label { Text = heading, HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 34);
        title.AddThemeColorOverride("font_color", new Color(1f, 0.8f, 0.1f));
        box.AddChild(title);

        bool notes = rows.Any(r => r.Note.Length > 0);
        var grid = new GridContainer { Columns = notes ? 9 : 8 };
        grid.AddThemeConstantOverride("h_separation", 22);
        box.AddChild(grid);
        var headers = new List<string> { "Pos", "", "Car", "Driver" };
        if (notes) headers.Add("");
        headers.AddRange(new[] { "Spins", "Wrecks", bonusHeader, "Points" });
        foreach (string h in headers) grid.AddChild(Cell(h, new Color(0.7f, 0.7f, 0.7f)));
        foreach (var r in rows)
        {
            var color = r.IsPlayer ? new Color(1f, 0.85f, 0.3f) : Colors.White;
            grid.AddChild(Cell($"{r.Place}", color));
            grid.AddChild(new ColorRect { Color = r.Roof, CustomMinimumSize = new Vector2(18, 18), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            grid.AddChild(Cell($"#{r.Number}", color));
            grid.AddChild(Cell(r.Driver, color));
            if (notes) grid.AddChild(Cell(r.Note, color));
            grid.AddChild(Cell($"{r.Spins}", color));
            grid.AddChild(Cell($"{r.Wrecks}", color));
            grid.AddChild(Cell($"{r.Bonus}", color));
            grid.AddChild(Cell($"{r.Total}", color));
        }
        var hint = new Label { Text = "R  race again   ·   Q  quit", HorizontalAlignment = HorizontalAlignment.Center };
        hint.AddThemeFontSizeOverride("font_size", 22);
        box.AddChild(hint);
        _results.Visible = true;
    }

    private static Label Cell(string text, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 19);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    // ------------------------------------------------------------------ input and refresh

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsEcho() || e is not InputEventKey { Pressed: true } key) return;
        var tree = GetTree();
        if (e.IsActionPressed(Controls.Pause) && !_resultsShown)
        {
            tree.Paused = !tree.Paused;
            _pause.Visible = tree.Paused;
        }
        else if (key.PhysicalKeycode == Key.Q && (tree.Paused || _resultsShown))
        {
            DevTools.QuitClean(tree, 0);
        }
        else if (key.PhysicalKeycode == Key.R && (tree.Paused || _resultsShown))
        {
            tree.Paused = false;
            Engine.TimeScale = 1.0;
            tree.ReloadCurrentScene();
        }
        else if (e.IsActionPressed(Controls.NextPreset))
        {
            Controls.Apply(Controls.Next(Controls.Current));
        }
        else if (Mode.PlayerOut && !_resultsShown && !tree.Paused)
        {
            if (key.PhysicalKeycode is Key.Enter or Key.KpEnter) Mode.SkipToResults();
            else if (key.PhysicalKeycode == Key.Space) Mode.ToggleDoubleSpeed();
            else if (key.PhysicalKeycode == Key.Left) Mode.SpectateNext(-1);
            else if (key.PhysicalKeycode == Key.Right) Mode.SpectateNext(1);
            else return;
        }
        else return;
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (Mode == null || PlayerCar == null) return;
        var referee = Mode.Referee;
        int points = referee.Score(PlayerCar.Id).Total;

        _speed.Text = $"Speed  {PlayerCar.SpeedKmh:000} KPH";
        _points.Text = $"Race Points  {points:000}";
        _rank.Text = Mode.HudRank;
        _running.Text = Mode.HudStatus;
        var t = TimeSpan.FromSeconds(Mode.Time);
        _clock.Text = $"Time  {(int)t.TotalMinutes}:{t.Seconds:00}";

        if (Mode.CountdownLeft > 0f)
        {
            _countdown.Visible = true;
            _countdown.Text = $"{Mathf.CeilToInt(Mode.CountdownLeft)}";
        }
        else _countdown.Visible = false;

        _spinTimer -= (float)delta;
        _spin.Visible = _spinTimer > 0f;

        string? warning = _resultsShown ? null : Mode.HudWarning;
        _warning.Visible = warning != null && (Time.GetTicksMsec() / 350) % 2 == 0;
        if (warning != null) _warning.Text = warning;

        bool showHelp = GetTree().Paused || Mode.Time < 12.0;
        _help.Visible = showHelp;
        _help.Text = Controls.Describe(Controls.Current) + "   ·   Tab standings · F3 next layout";

        _standings.Visible = !_resultsShown && Input.IsActionPressed(Controls.Standings);
        if (_standings.Visible) _standingsText.Text = Mode.StandingsText();
    }
}
