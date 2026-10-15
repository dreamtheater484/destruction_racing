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
    /// <summary>The caption under the big number.</summary>
    string HudRankLabel => "POSITION";
    /// <summary>First line top right ("12  Still Running", "Lap 2/6") — the plain-text form.</summary>
    string HudStatus { get; }
    /// <summary>The top-right panel: a caption ("STILL RUNNING", "LAP") and its value ("12", "2/6").</summary>
    string HudStatusLabel => "";
    string HudStatusValue => HudStatus;
    /// <summary>A flashing warning in the middle of the screen ("WRONG WAY"), or null.</summary>
    string? HudWarning { get; }
    /// <summary>The Tab standings panel as text (used when <see cref="StandingsRows"/> is null).</summary>
    string StandingsText();
    /// <summary>The Tab standings as a table, best first; null = use <see cref="StandingsText"/>.</summary>
    IReadOnlyList<EventHud.ResultRow>? StandingsRows() => null;
    void SkipToResults();
    void ToggleDoubleSpeed();
    void SpectateNext(int direction);
    /// <summary>While spectating: TV cameras or the chase camera.</summary>
    void ToggleSpectateCamera() { }
}

/// <summary>
/// The event HUD in the DD2 layout with a broadcast look (plan §13): race points and speed top left, the big
/// position top centre, "Still Running" or the lap and the clock top right, the wreck feed under it, the damage
/// diagram lower right; popups, live spin calls, countdown, banners (final cars, final duel), the wrecked overlay,
/// pause, Tab standings and the results table with an animated tally.
/// </summary>
public partial class EventHud : CanvasLayer
{
    /// <summary>One results line. Bonus is the survivor bonus (derby) or the finishing bonus (race).</summary>
    public readonly record struct ResultRow(int Place, int Number, string Driver, Color Roof, int Spins, int Wrecks, int Bonus, int Total, bool IsPlayer, string Note = "");

    public IHudMode Mode { get; set; } = null!;
    public Car PlayerCar { get; set; } = null!;
    public Func<string>? ViewName { get; set; }

    /// <summary>Each step of the results tally (for the tick sound) and its end.</summary>
    public event Action? TallyTicked;
    public event Action? TallyFinished;
    /// <summary>A banner slid in (for its sting).</summary>
    public event Action? BannerShown;
    /// <summary>Extra key on the results screen (e.g. the replay); return true when handled.</summary>
    public Func<Key, bool>? ResultsKey { get; set; }
    /// <summary>The hint line under the results table.</summary>
    public string ResultsHint { get; set; } = "R  race again   ·   Q  quit";
    /// <summary>The scene M returns to from the pause menu or the results (null: no menu).</summary>
    public string? MenuScene { get; set; }

    private Control _root = null!;
    private Label _points = null!, _speed = null!, _rank = null!, _rankLabel = null!, _statusLabel = null!, _status = null!, _clock = null!;
    private Label _countdown = null!, _spin = null!, _help = null!, _warning = null!;
    private PanelContainer _leaderTag = null!, _hunted = null!;
    private Label _huntedText = null!;
    private VBoxContainer _popups = null!, _feed = null!;
    private DamageDiagram _diagram = null!;
    private Control _pause = null!, _wrecked = null!, _standings = null!, _results = null!, _banner = null!;
    private Label _bannerTitle = null!, _bannerSub = null!, _wreckedTitle = null!;
    private GridContainer _standingsGrid = null!;
    private Label _standingsText = null!;
    private float _spinTimer, _shownPoints, _pointsFlash, _countdownScale;
    private int _lastCountdown = -1;
    private bool _resultsShown;
    private Tween? _bannerTween;
    private Label _pauseKeys = null!;

    /// <summary>How many AI cars hunt the player right now (shown when 3 or more), set by the mode.</summary>
    public int Hunters { get; set; }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _root = new Control { Theme = HudStyle.Theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        BuildTopLeft();
        BuildTopCentre();
        BuildTopRight();
        BuildCentre();
        BuildBottom();
        BuildOverlays();
    }

    // ------------------------------------------------------------------ layout

    private PanelContainer Panel(Color accent, float skew = 0.18f)
    {
        var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        p.AddThemeStyleboxOverride("panel", HudStyle.Panel(accent, skew));
        return p;
    }

    private void BuildTopLeft()
    {
        var panel = HudStyle.Place(Panel(HudStyle.Yellow), 0f, 0f, new Vector2(36, 26), new Vector2(250, 0));
        _root.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", -6);
        panel.AddChild(box);
        box.AddChild(HudStyle.Label("RACE POINTS", 19, HudStyle.Grey, "SemiBold", outline: 0));
        _points = HudStyle.Label("000", 64, HudStyle.White, "ExtraBold", outline: 0);
        box.AddChild(_points);
        var speedRow = new HBoxContainer();
        speedRow.AddThemeConstantOverride("separation", 10);
        speedRow.AddChild(HudStyle.Label("SPEED", 19, HudStyle.Grey, "SemiBold", outline: 0));
        _speed = HudStyle.Label("000 KPH", 26, HudStyle.White, "Bold", outline: 0);
        speedRow.AddChild(_speed);
        box.AddChild(speedRow);

        _leaderTag = HudStyle.Place(new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore }, 0f, 0f, new Vector2(40, 186), new Vector2(0, 0));
        var tagStyle = HudStyle.Panel(HudStyle.Yellow, 0.18f, 0);
        tagStyle.BgColor = HudStyle.Yellow;
        _leaderTag.AddThemeStyleboxOverride("panel", tagStyle);
        _leaderTag.AddChild(HudStyle.Label("POINTS LEADER  ×2", 22, new Color(0.05f, 0.05f, 0.05f), "ExtraBold", outline: 0));
        _leaderTag.Visible = false;
        _root.AddChild(_leaderTag);

        _hunted = HudStyle.Place(Panel(HudStyle.Red), 0f, 0f, new Vector2(40, 232), new Vector2(0, 0));
        _huntedText = HudStyle.Label("HUNTED", 22, HudStyle.Red, "ExtraBold", outline: 0);
        _hunted.AddChild(_huntedText);
        _hunted.Visible = false;
        _root.AddChild(_hunted);
    }

    private void BuildTopCentre()
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = HudStyle.Panel(HudStyle.Yellow, 0f, 0);
        style.BorderWidthBottom = 5;
        style.ContentMarginLeft = style.ContentMarginRight = 26;
        panel.AddThemeStyleboxOverride("panel", style);
        HudStyle.Place(panel, 0.5f, 0f, new Vector2(-75, 18), new Vector2(150, 0));
        _root.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", -14);
        panel.AddChild(box);
        _rank = HudStyle.Label("01", 92, HudStyle.White, "ExtraBold", HorizontalAlignment.Center, 0);
        box.AddChild(_rank);
        _rankLabel = HudStyle.Label("POSITION", 18, HudStyle.Yellow, "SemiBold", HorizontalAlignment.Center, 0);
        box.AddChild(_rankLabel);
    }

    private void BuildTopRight()
    {
        var panel = HudStyle.Place(Panel(HudStyle.Yellow), 1f, 0f, new Vector2(-286, 26), new Vector2(250, 0));
        _root.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", -6);
        panel.AddChild(box);
        _statusLabel = HudStyle.Label("STILL RUNNING", 19, HudStyle.Grey, "SemiBold", outline: 0);
        box.AddChild(_statusLabel);
        _status = HudStyle.Label("20", 64, HudStyle.White, "ExtraBold", outline: 0);
        box.AddChild(_status);
        var clockRow = new HBoxContainer();
        clockRow.AddThemeConstantOverride("separation", 10);
        clockRow.AddChild(HudStyle.Label("TIME", 19, HudStyle.Grey, "SemiBold", outline: 0));
        _clock = HudStyle.Label("0:00", 26, HudStyle.White, "Bold", outline: 0);
        clockRow.AddChild(_clock);
        box.AddChild(clockRow);

        _feed = HudStyle.Place(new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }, 1f, 0f, new Vector2(-420, 214), new Vector2(384, 0));
        _feed.AddThemeConstantOverride("separation", 6);
        _root.AddChild(_feed);
    }

    private void BuildCentre()
    {
        _popups = HudStyle.Place(new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore },
            0.5f, 0.2f, new Vector2(-520, 0), new Vector2(1040, 0));
        _popups.AddThemeConstantOverride("separation", 4);
        _root.AddChild(_popups);

        _spin = HudStyle.Place(HudStyle.Label("", 60, HudStyle.White, "ExtraBold", HorizontalAlignment.Center, 10), 0.5f, 0.36f, new Vector2(-400, 0), new Vector2(800, 80));
        _spin.PivotOffset = new Vector2(400, 40);
        _root.AddChild(_spin);

        _countdown = HudStyle.Place(HudStyle.Label("", 220, HudStyle.White, "ExtraBold", HorizontalAlignment.Center, 18), 0.5f, 0.5f, new Vector2(-300, -220), new Vector2(600, 260));
        _countdown.PivotOffset = new Vector2(300, 130);
        _root.AddChild(_countdown);

        _warning = HudStyle.Place(HudStyle.Label("", 88, HudStyle.Red, "ExtraBold", HorizontalAlignment.Center, 12), 0.5f, 0.5f, new Vector2(-450, -170), new Vector2(900, 100));
        _warning.Visible = false;
        _root.AddChild(_warning);

        // The banner: a full-width band with hazard edges (final cars, final duel, the winner).
        _banner = HudStyle.Place(new Control { MouseFilter = Control.MouseFilterEnum.Ignore }, 0f, 0.6f, new Vector2(0, 0), new Vector2(0, 150));
        _banner.AnchorRight = 1f;
        _banner.OffsetRight = 0f;
        _banner.Visible = false;
        _root.AddChild(_banner);
        var band = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
        band.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.02f, 0.02f, 0.025f, 0.9f) });
        band.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _banner.AddChild(band);
        foreach (float y in new[] { 0f, 1f })
        {
            var stripe = new TextureRect { Texture = HudStyle.Hazard, StretchMode = TextureRect.StretchModeEnum.Tile, MouseFilter = Control.MouseFilterEnum.Ignore };
            stripe.AnchorLeft = 0f; stripe.AnchorRight = 1f; stripe.AnchorTop = stripe.AnchorBottom = y;
            stripe.OffsetTop = y > 0f ? -10 : 0; stripe.OffsetBottom = y > 0f ? 0 : 10;
            _banner.AddChild(stripe);
        }
        var text = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        text.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        text.AddThemeConstantOverride("separation", -10);
        _banner.AddChild(text);
        _bannerTitle = HudStyle.Label("", 84, HudStyle.Yellow, "ExtraBold", HorizontalAlignment.Center, 0);
        _bannerSub = HudStyle.Label("", 30, HudStyle.White, "SemiBold", HorizontalAlignment.Center, 0);
        text.AddChild(_bannerTitle);
        text.AddChild(_bannerSub);
    }

    private void BuildBottom()
    {
        var panel = HudStyle.Place(Panel(HudStyle.Yellow, 0f), 1f, 1f, new Vector2(-196, -290), new Vector2(160, 254));
        _root.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 0);
        panel.AddChild(box);
        box.AddChild(HudStyle.Label("DAMAGE", 18, HudStyle.Grey, "SemiBold", HorizontalAlignment.Center, 0));
        _diagram = new DamageDiagram { Target = PlayerCar, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        box.AddChild(_diagram);

        _help = HudStyle.Place(HudStyle.Label("", 19, HudStyle.White, "Medium", outline: 5), 0f, 1f, new Vector2(36, -48), new Vector2(1500, 30));
        _root.AddChild(_help);
    }

    private void BuildOverlays()
    {
        _wrecked = HudStyle.Place(new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore },
            0.5f, 0.7f, new Vector2(-600, 0), new Vector2(1200, 0));
        ((VBoxContainer)_wrecked).AddThemeConstantOverride("separation", -8);
        _wreckedTitle = HudStyle.Label("WRECKED", 96, HudStyle.Red, "ExtraBold", HorizontalAlignment.Center, 12);
        _wrecked.AddChild(_wreckedTitle);
        _wrecked.AddChild(HudStyle.Label("RACE OVER", 38, HudStyle.White, "Bold", HorizontalAlignment.Center, 8));
        _wrecked.AddChild(HudStyle.Label("←/→  watch another car    ·    T  TV / chase camera    ·    SPACE  ×2 speed    ·    ENTER  skip to the results", 24, HudStyle.Yellow, "SemiBold", HorizontalAlignment.Center, 6));
        _wrecked.Visible = false;
        _root.AddChild(_wrecked);

        _pause = CentrePanel(new Vector2(640, 0));
        var pauseBox = new VBoxContainer();
        pauseBox.AddChild(HudStyle.Label("PAUSED", 72, HudStyle.Yellow, "ExtraBold", HorizontalAlignment.Center, 0));
        _pauseKeys = HudStyle.Label("ESC / P  resume     ·     R  restart     ·     Q  quit", 26, HudStyle.White, "SemiBold", HorizontalAlignment.Center, 0);
        pauseBox.AddChild(_pauseKeys);
        _pause.AddChild(pauseBox);
        _pause.Visible = false;

        _standings = CentrePanel(new Vector2(620, 0));
        var standingsBox = new VBoxContainer();
        standingsBox.AddChild(HudStyle.Label("STANDINGS", 40, HudStyle.Yellow, "ExtraBold", HorizontalAlignment.Center, 0));
        _standingsGrid = new GridContainer { Columns = 5 };
        _standingsGrid.AddThemeConstantOverride("h_separation", 24);
        _standingsGrid.AddThemeConstantOverride("v_separation", 0);
        standingsBox.AddChild(_standingsGrid);
        _standingsText = HudStyle.Label("", 22, HudStyle.White, "Medium", outline: 0);
        standingsBox.AddChild(_standingsText);
        _standings.AddChild(standingsBox);
        _standings.Visible = false;

        _results = CentrePanel(new Vector2(980, 0));
        _results.Visible = false;
    }

    private PanelContainer CentrePanel(Vector2 minSize)
    {
        var panel = new PanelContainer { CustomMinimumSize = minSize, MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = HudStyle.Panel(HudStyle.Yellow, 0f, 0, 0.9f);
        style.BorderWidthTop = 6;
        style.ContentMarginLeft = style.ContentMarginRight = 30;
        style.ContentMarginTop = 18;
        style.ContentMarginBottom = 22;
        panel.AddThemeStyleboxOverride("panel", style);
        panel.AnchorLeft = panel.AnchorRight = 0.5f;
        panel.AnchorTop = panel.AnchorBottom = 0.5f;
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        _root.AddChild(panel);
        return panel;
    }

    private Tween RealTimeTween(Node node)
    {
        var tween = node.CreateTween();
        tween.SetIgnoreTimeScale(true);
        return tween;
    }

    // ------------------------------------------------------------------ API used by the mode

    public void Popup(string text, Color color, bool big = false)
    {
        var label = HudStyle.Label(text, big ? 84 : 50, color, "ExtraBold", HorizontalAlignment.Center, big ? 12 : 9);
        label.PivotOffset = new Vector2(520, big ? 50 : 30);
        _popups.AddChild(label);
        if (_popups.GetChildCount() > 4) _popups.GetChild(0).QueueFree();
        label.Scale = new Vector2(1.35f, 1.35f);
        var tween = RealTimeTween(label);
        tween.TweenProperty(label, "scale", Vector2.One, 0.18).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenInterval(big ? 1.5 : 2.0);
        tween.TweenProperty(label, "modulate:a", 0.0, 0.45);
        tween.TweenCallback(Callable.From(label.QueueFree));
    }

    public void Feed(string text)
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
        var style = HudStyle.Panel(text.Contains("WRECKED") ? HudStyle.Red : HudStyle.Yellow, 0.18f, 5, 0.8f);
        style.ContentMarginTop = 2;
        style.ContentMarginBottom = 3;
        panel.AddThemeStyleboxOverride("panel", style);
        panel.AddChild(HudStyle.Label(text, 23, new Color(1f, 0.9f, 0.7f), "Bold", HorizontalAlignment.Right, 0));
        _feed.AddChild(panel);
        if (_feed.GetChildCount() > 5) _feed.GetChild(0).QueueFree();
        panel.Modulate = new Color(1, 1, 1, 0);
        var tween = RealTimeTween(panel);
        tween.TweenProperty(panel, "modulate:a", 1.0, 0.15);
        tween.TweenInterval(6.0);
        tween.TweenProperty(panel, "modulate:a", 0.0, 0.8);
        tween.TweenCallback(Callable.From(panel.QueueFree));
    }

    /// <summary>A broadcast banner across the screen (final cars, final duel, the winner).</summary>
    public void Banner(string title, string subtitle = "", Color? accent = null)
    {
        _bannerTitle.Text = title;
        _bannerTitle.AddThemeColorOverride("font_color", accent ?? HudStyle.Yellow);
        _bannerSub.Text = subtitle;
        _bannerSub.Visible = subtitle.Length > 0;
        _bannerTween?.Kill();
        _banner.Visible = true;
        float width = _root.Size.X > 0 ? _root.Size.X : 1920f;
        _banner.Position = new Vector2(-width, _banner.Position.Y);
        _banner.Modulate = Colors.White;
        _bannerTween = RealTimeTween(_banner);
        _bannerTween.TweenProperty(_banner, "position:x", 0f, 0.35).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _bannerTween.TweenInterval(2.6);
        _bannerTween.TweenProperty(_banner, "position:x", width, 0.35).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        _bannerTween.TweenCallback(Callable.From(() => _banner.Visible = false));
        BannerShown?.Invoke();
    }

    /// <summary>Live spin popup for the player's victim: "SPIN 90" → "SPIN 180" → "SPIN 360".</summary>
    public void SpinProgress(SpinTier tier)
    {
        _spin.Text = $"SPIN {(int)tier}!";
        _spin.AddThemeColorOverride("font_color", tier == SpinTier.Full ? HudStyle.Yellow : HudStyle.White);
        _spinTimer = 1.2f;
        _spin.Scale = new Vector2(1.3f, 1.3f);
        RealTimeTween(_spin).TweenProperty(_spin, "scale", Vector2.One, 0.2).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    public void PulseZone(DamageZone zone) => _diagram.Pulse(zone);

    public void ShowWrecked(string headline = "WRECKED  –  RACE OVER")
    {
        _wrecked.Visible = true;
        _wreckedTitle.Text = headline.Split('–')[0].Trim();
        _wrecked.Modulate = new Color(1, 1, 1, 0);
        RealTimeTween(_wrecked).TweenProperty(_wrecked, "modulate:a", 1.0, 0.4);
    }

    public void ShowResults(string heading, string bonusHeader, IReadOnlyList<ResultRow> rows) => ShowResults(heading, bonusHeader, rows, null);

    public void ShowResults(string heading, string bonusHeader, IReadOnlyList<ResultRow> rows, string? summary)
    {
        _resultsShown = true;
        _wrecked.Visible = false;
        _standings.Visible = false;
        foreach (Node child in _results.GetChildren()) child.QueueFree();

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        _results.AddChild(box);
        box.AddChild(HudStyle.Label(heading.ToUpperInvariant(), 46, HudStyle.Yellow, "ExtraBold", HorizontalAlignment.Center, 0));
        if (summary != null) box.AddChild(HudStyle.Label(summary, 28, HudStyle.White, "Bold", HorizontalAlignment.Center, 0));

        bool notes = rows.Any(r => r.Note.Length > 0);
        var grid = new GridContainer { Columns = notes ? 9 : 8 };
        grid.AddThemeConstantOverride("h_separation", 26);
        grid.AddThemeConstantOverride("v_separation", 0);
        box.AddChild(grid);
        var headers = new List<string> { "POS", "", "CAR", "DRIVER" };
        if (notes) headers.Add("");
        headers.AddRange(new[] { "SPINS", "WRECKS", bonusHeader.ToUpperInvariant(), "POINTS" });
        foreach (string h in headers) grid.AddChild(HudStyle.Label(h, 18, HudStyle.Grey, "SemiBold", outline: 0));

        // Rows appear from the bottom of the table up, then the player's total counts up.
        var cells = new List<List<Control>>();
        Label? playerTotal = null;
        int playerPoints = 0;
        foreach (var r in rows)
        {
            var color = r.IsPlayer ? HudStyle.Yellow : HudStyle.White;
            string weight = r.IsPlayer ? "ExtraBold" : "SemiBold";
            var row = new List<Control>
            {
                HudStyle.Label($"{r.Place}", 24, color, weight, outline: 0),
                new ColorRect { Color = r.Roof, CustomMinimumSize = new Vector2(20, 20), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter },
                HudStyle.Label($"#{r.Number}", 24, color, weight, outline: 0),
                HudStyle.Label(r.Driver, 24, color, weight, outline: 0),
            };
            if (notes) row.Add(HudStyle.Label(r.Note, 24, color, weight, outline: 0));
            row.Add(HudStyle.Label($"{r.Spins}", 24, color, weight, outline: 0));
            row.Add(HudStyle.Label($"{r.Wrecks}", 24, color, weight, outline: 0));
            row.Add(HudStyle.Label($"{r.Bonus}", 24, color, weight, outline: 0));
            var total = HudStyle.Label($"{r.Total}", 24, color, "ExtraBold", outline: 0);
            row.Add(total);
            if (r.IsPlayer) { playerTotal = total; playerPoints = r.Total; total.Text = "0"; }
            foreach (var c in row) { c.Modulate = new Color(1, 1, 1, 0); grid.AddChild(c); }
            cells.Add(row);
        }
        box.AddChild(HudStyle.Label(ResultsHint, 24, HudStyle.Grey, "SemiBold", HorizontalAlignment.Center, 0));
        _results.Visible = true;

        var tween = RealTimeTween(_results);
        for (int i = cells.Count - 1; i >= 0; i--)
        {
            var row = cells[i];
            tween.TweenCallback(Callable.From(() =>
            {
                foreach (var c in row) c.Modulate = Colors.White;
                TallyTicked?.Invoke();
            }));
            tween.TweenInterval(cells.Count > 12 ? 0.06 : 0.12);
        }
        if (playerTotal != null)
        {
            var label = playerTotal;
            int target = playerPoints;
            tween.TweenMethod(Callable.From<float>(v =>
            {
                string text = $"{Mathf.RoundToInt(v)}";
                if (label.Text != text) { label.Text = text; if (Mathf.RoundToInt(v) % 10 == 0) TallyTicked?.Invoke(); }
            }), 0f, (float)target, Mathf.Clamp(target / 250f, 0.4f, 1.6f));
        }
        tween.TweenCallback(Callable.From(() => TallyFinished?.Invoke()));
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
            _pauseKeys.Text = MenuScene != null
                ? "ESC / P  resume     ·     R  restart     ·     M  menu     ·     Q  quit"
                : "ESC / P  resume     ·     R  restart     ·     Q  quit";
        }
        else if (key.PhysicalKeycode == Key.Q && (tree.Paused || _resultsShown))
        {
            DevTools.QuitClean(tree, 0);
        }
        else if (key.PhysicalKeycode == Key.M && MenuScene != null && (tree.Paused || _resultsShown))
        {
            tree.Paused = false;
            Engine.TimeScale = 1.0;
            tree.ChangeSceneToFile(MenuScene);
        }
        else if (key.PhysicalKeycode == Key.R && (tree.Paused || _resultsShown))
        {
            tree.Paused = false;
            Engine.TimeScale = 1.0;
            tree.ReloadCurrentScene();
        }
        else if (_resultsShown && ResultsKey != null && ResultsKey(key.PhysicalKeycode))
        {
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
            else if (key.PhysicalKeycode == Key.T) Mode.ToggleSpectateCamera();
            else return;
        }
        else return;
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Hides the in-event widgets (e.g. while a replay plays).</summary>
    public bool InEventVisible
    {
        get => _root.Visible;
        set => _root.Visible = value;
    }

    public override void _Process(double delta)
    {
        if (Mode == null || PlayerCar == null) return;
        float rdt = (float)(delta / Math.Max(Engine.TimeScale, 0.01));
        var referee = Mode.Referee;
        int points = referee.Score(PlayerCar.Id).Total;

        // Points roll up like a counter and flash when they change.
        if (_shownPoints < points)
        {
            _shownPoints = Mathf.Min(points, _shownPoints + Mathf.Max(60f, (points - _shownPoints) * 6f) * rdt);
            _pointsFlash = 1f;
        }
        else _shownPoints = points;
        _pointsFlash = Mathf.Max(0f, _pointsFlash - rdt * 2.5f);
        _points.Text = $"{Mathf.RoundToInt(_shownPoints):000}";
        _points.AddThemeColorOverride("font_color", HudStyle.White.Lerp(HudStyle.Yellow, _pointsFlash));
        _speed.Text = $"{PlayerCar.SpeedKmh:000} KPH";
        _rank.Text = Mode.HudRank;
        _rankLabel.Text = Mode.HudRankLabel;
        _statusLabel.Text = Mode.HudStatusLabel;
        _statusLabel.Visible = Mode.HudStatusLabel.Length > 0;
        _status.Text = Mode.HudStatusValue;
        var t = TimeSpan.FromSeconds(Mode.Time);
        _clock.Text = $"{(int)t.TotalMinutes}:{t.Seconds:00}";
        _leaderTag.Visible = !_resultsShown && !Mode.PlayerOut && referee.Leader == PlayerCar.Id && points > 0;
        // More cars on you than the opening budget allows: worth knowing (plan §10.4 caps it at 2/3/4).
        _hunted.Visible = !_resultsShown && !Mode.PlayerOut && Hunters >= 3;
        if (_hunted.Visible) _huntedText.Text = $"HUNTED BY {Hunters}";

        // Countdown: each number pops in.
        if (Mode.CountdownLeft > 0f)
        {
            int n = Mathf.CeilToInt(Mode.CountdownLeft);
            if (n != _lastCountdown)
            {
                _lastCountdown = n;
                _countdownScale = 1.6f;
            }
            _countdownScale = Mathf.Lerp(_countdownScale, 1f, 1f - Mathf.Exp(-14f * rdt));
            _countdown.Visible = true;
            _countdown.Text = $"{n}";
            _countdown.Scale = new Vector2(_countdownScale, _countdownScale);
            _countdown.AddThemeColorOverride("font_color", n == 1 ? HudStyle.Yellow : HudStyle.White);
        }
        else _countdown.Visible = false;

        _spinTimer -= rdt;
        _spin.Visible = _spinTimer > 0f;

        string? warning = _resultsShown ? null : Mode.HudWarning;
        _warning.Visible = warning != null && (Time.GetTicksMsec() / 350) % 2 == 0;
        if (warning != null) _warning.Text = warning;

        bool showHelp = GetTree().Paused || Mode.Time < 12.0;
        _help.Visible = showHelp && !_resultsShown;
        _help.Text = Controls.Describe(Controls.Current) + "   ·   Tab standings · F3 next layout";

        _standings.Visible = !_resultsShown && Input.IsActionPressed(Controls.Standings);
        if (_standings.Visible) RefreshStandings();
    }

    private void RefreshStandings()
    {
        var rows = Mode.StandingsRows();
        _standingsGrid.Visible = rows != null;
        _standingsText.Visible = rows == null;
        if (rows == null)
        {
            _standingsText.Text = Mode.StandingsText();
            return;
        }
        foreach (Node child in _standingsGrid.GetChildren()) child.QueueFree();
        foreach (string h in new[] { "", "", "CAR", "DRIVER", "POINTS" })
            _standingsGrid.AddChild(HudStyle.Label(h, 18, HudStyle.Grey, "SemiBold", outline: 0));
        foreach (var r in rows.Take(12))
        {
            var color = r.IsPlayer ? HudStyle.Yellow : r.Note.Length > 0 ? HudStyle.Grey : HudStyle.White;
            _standingsGrid.AddChild(HudStyle.Label($"{r.Place}", 24, color, "Bold", outline: 0));
            _standingsGrid.AddChild(new ColorRect { Color = r.Roof, CustomMinimumSize = new Vector2(18, 18), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            _standingsGrid.AddChild(HudStyle.Label($"#{r.Number}", 24, color, "Bold", outline: 0));
            _standingsGrid.AddChild(HudStyle.Label(r.Driver + (r.Note.Length > 0 ? $"  ({r.Note})" : ""), 24, color, "SemiBold", outline: 0));
            _standingsGrid.AddChild(HudStyle.Label($"{r.Total}", 24, color, "ExtraBold", outline: 0));
        }
    }
}
