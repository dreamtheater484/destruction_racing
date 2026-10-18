using System;
using System.Linq;
using DestructionRacing.Core;
using DestructionRacing.Modes;
using Godot;

namespace DestructionRacing.UI;

/// <summary>
/// The Time Trial HUD (plan §4.4) in the broadcast look: the stopwatch at the bottom of the screen (DD1) with the
/// last and best laps and the three sector splits against the best; the layout, car and medal targets top left; the
/// leaderboard for this layout, direction and car top right; popups for new bests and medals; pause.
/// </summary>
public partial class TimeTrialHud : CanvasLayer
{
    public TimeTrialMode Mode { get; set; } = null!;

    private Control _root = null!;
    private Label _clock = null!, _lastBest = null!, _speed = null!, _help = null!, _board = null!;
    private readonly Label[] _medals = new Label[3];
    private readonly Label[] _sectors = new Label[3];
    private VBoxContainer _popups = null!;
    private Control _pause = null!;
    private ActionMenu _pauseMenu = null!;
    private static bool FromMenu => Modes.Session.Request is { Kind: Modes.EventKind.TimeTrial };
    private float _sectorFlash;

    public override void _Ready()
    {
        // Above the screen effects (ScreenFx, layer 5), so the grain, the flash and the blur leave the HUD alone.
        Layer = 7;
        ProcessMode = ProcessModeEnum.Always;
        _root = new Control { Theme = HudStyle.Theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        // Top left: what this is, and the medal targets.
        var info = HudStyle.Place(Panel(HudStyle.Yellow), 0f, 0f, new Vector2(36, 26), new Vector2(330, 0));
        _root.AddChild(info);
        var infoBox = new VBoxContainer();
        infoBox.AddThemeConstantOverride("separation", -4);
        info.AddChild(infoBox);
        infoBox.AddChild(HudStyle.Label("TIME TRIAL", 22, HudStyle.Yellow, "ExtraBold", outline: 0));
        infoBox.AddChild(HudStyle.Label($"{Mode.Layout.Name.ToUpperInvariant()}", 34, HudStyle.White, "ExtraBold", outline: 0));
        infoBox.AddChild(HudStyle.Label(Mode.CarName.ToUpperInvariant(), 22, HudStyle.Grey, "SemiBold", outline: 0));
        for (int i = 0; i < 3; i++)
        {
            _medals[i] = HudStyle.Label("", 24, TimeTrialTargets.MedalColor(3 - i), "Bold", outline: 0);
            infoBox.AddChild(_medals[i]);
        }

        // Top right: the leaderboard.
        var board = HudStyle.Place(Panel(HudStyle.Yellow), 1f, 0f, new Vector2(-326, 26), new Vector2(290, 0));
        _root.AddChild(board);
        var boardBox = new VBoxContainer();
        boardBox.AddThemeConstantOverride("separation", -2);
        board.AddChild(boardBox);
        boardBox.AddChild(HudStyle.Label("BEST LAPS", 22, HudStyle.Yellow, "ExtraBold", outline: 0));
        _board = HudStyle.Label("", 22, HudStyle.White, "SemiBold", outline: 0);
        boardBox.AddChild(_board);

        // Bottom centre: the stopwatch.
        var watch = HudStyle.Place(Panel(HudStyle.Yellow, 0f), 0.5f, 1f, new Vector2(-330, -196), new Vector2(660, 0));
        _root.AddChild(watch);
        var watchBox = new VBoxContainer();
        watchBox.AddThemeConstantOverride("separation", -8);
        watch.AddChild(watchBox);
        _clock = HudStyle.Label("0:00.00", 84, HudStyle.White, "ExtraBold", HorizontalAlignment.Center, 0);
        watchBox.AddChild(_clock);
        var sectorRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        sectorRow.AddThemeConstantOverride("separation", 34);
        for (int i = 0; i < 3; i++)
        {
            _sectors[i] = HudStyle.Label("", 24, HudStyle.Grey, "Bold", HorizontalAlignment.Center, 0);
            sectorRow.AddChild(_sectors[i]);
        }
        watchBox.AddChild(sectorRow);
        _lastBest = HudStyle.Label("", 24, HudStyle.Grey, "SemiBold", HorizontalAlignment.Center, 0);
        watchBox.AddChild(_lastBest);

        _speed = HudStyle.Place(HudStyle.Label("", 40, HudStyle.White, "ExtraBold", HorizontalAlignment.Right, 8), 1f, 1f, new Vector2(-336, -86), new Vector2(300, 50));
        _root.AddChild(_speed);
        _help = HudStyle.Place(HudStyle.Label("", 19, HudStyle.White, "Medium", outline: 5), 0f, 1f, new Vector2(36, -48), new Vector2(900, 30));
        _root.AddChild(_help);

        _popups = HudStyle.Place(new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore },
            0.5f, 0.24f, new Vector2(-520, 0), new Vector2(1040, 0));
        _popups.AddThemeConstantOverride("separation", 4);
        _root.AddChild(_popups);

        var pause = new PanelContainer { CustomMinimumSize = new Vector2(640, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        var ps = HudStyle.Panel(HudStyle.Yellow, 0f, 0, 0.9f);
        ps.BorderWidthTop = 6;
        ps.ContentMarginLeft = ps.ContentMarginRight = 30;
        ps.ContentMarginTop = 18;
        ps.ContentMarginBottom = 22;
        pause.AddThemeStyleboxOverride("panel", ps);
        pause.AnchorLeft = pause.AnchorRight = pause.AnchorTop = pause.AnchorBottom = 0.5f;
        pause.GrowHorizontal = pause.GrowVertical = Control.GrowDirection.Both;
        var pauseBox = new VBoxContainer();
        pauseBox.AddChild(HudStyle.Label("PAUSED", 72, HudStyle.Yellow, "ExtraBold", HorizontalAlignment.Center, 0));
        _pauseMenu = new ActionMenu { Vertical = true, FontSize = 34 };
        pauseBox.AddChild(_pauseMenu);
        pause.AddChild(pauseBox);
        pause.Visible = false;
        _pause = pause;
        _root.AddChild(pause);

        Mode.LapCompleted += OnLap;
        Mode.SectorSplit += (n, delta) => _sectorFlash = 2.5f;
    }

    private PanelContainer Panel(Color accent, float skew = 0.18f)
    {
        var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        p.AddThemeStyleboxOverride("panel", HudStyle.Panel(accent, skew));
        return p;
    }

    private void OnLap(double lap, bool valid, int place, int medal)
    {
        if (!valid)
        {
            Popup("LAP NOT COUNTED", HudStyle.Orange);
            return;
        }
        if (place == 1) Popup($"NEW BEST LAP   {TimeTrialTargets.Format(lap)}", HudStyle.Yellow, big: true);
        else if (place > 0) Popup($"LAP {TimeTrialTargets.Format(lap)}   ·   #{place} ON THE BOARD", HudStyle.White);
        else Popup($"LAP {TimeTrialTargets.Format(lap)}", HudStyle.White);
        if (medal > 0 && place == 1) Popup($"{TimeTrialTargets.MedalName(medal)} MEDAL", TimeTrialTargets.MedalColor(medal), big: true);
        if (Mode.MedalColourOpened is { } colour)
        {
            Popup($"NEW COLOUR: {colour}", TimeTrialTargets.MedalColor(medal));
            Mode.MedalColourOpened = null;
        }
    }

    public void Popup(string text, Color color, bool big = false)
    {
        var label = HudStyle.Label(text, big ? 64 : 40, color, "ExtraBold", HorizontalAlignment.Center, 10);
        _popups.AddChild(label);
        var tween = label.CreateTween();
        tween.TweenInterval(2.2);
        tween.TweenProperty(label, "modulate:a", 0f, 0.5);
        tween.TweenCallback(Callable.From(label.QueueFree));
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsEcho() || e is not InputEventKey { Pressed: true } key) return;
        var tree = GetTree();
        if (e.IsActionPressed(Controls.Pause))
        {
            SetPaused(!tree.Paused);
        }
        else if (tree.Paused && _pauseMenu.HandleKey(key.PhysicalKeycode))
        {
        }
        else if (e.IsActionPressed(Controls.NextPreset))
        {
            Controls.Apply(Controls.Next(Controls.Current));
        }
        else return;
        GetViewport().SetInputAsHandled();
    }

    private void SetPaused(bool paused)
    {
        var tree = GetTree();
        tree.Paused = paused;
        _pause.Visible = paused;
        if (!paused) return;
        var actions = new System.Collections.Generic.List<MenuAction>
        {
            new("RESUME", Key.None, () => SetPaused(false)),
            new("RESTART", Key.R, () => { SetPaused(false); Mode.Restart(); }),
        };
        if (FromMenu)
            actions.Add(new MenuAction("MENU", Key.M, () =>
            {
                tree.Paused = false;
                DevTools.MarkSceneRequest();
                tree.ChangeSceneToFile(Modes.Session.FrontEndScene);
            }));
        actions.Add(new MenuAction("QUIT THE GAME", Key.Q, () => DevTools.QuitClean(tree, 0)));
        _pauseMenu.SetActions(actions);
    }

    public override void _Process(double delta)
    {
        _sectorFlash = MathF.Max(0f, _sectorFlash - (float)delta);
        var cur = Mode.CurrentLapTime;
        _clock.Text = cur is double t ? TimeTrialTargets.Format(t) : "0:00.00";
        _clock.AddThemeColorOverride("font_color", Mode.LapInvalid ? HudStyle.Orange : cur == null ? HudStyle.Grey : HudStyle.White);

        // Sectors: this lap's splits against the best lap's (green faster, red slower).
        var best = Mode.BestSplits;
        for (int i = 0; i < 3; i++)
        {
            string text = $"S{i + 1}  ";
            var color = HudStyle.Grey;
            if (i < 2 && i < Mode.SectorsDone)
            {
                double split = Mode.Splits[i];
                text += TimeTrialTargets.Format(split);
                if (best != null && i < best.Length && !double.IsNaN(best[i]))
                {
                    double d = split - best[i];
                    text += d < 0 ? $"  -{-d:0.00}" : $"  +{d:0.00}";
                    color = d < 0 ? HudStyle.Green : HudStyle.Red;
                }
                else color = HudStyle.White;
            }
            else text += "--.--";
            _sectors[i].Text = text;
            _sectors[i].AddThemeColorOverride("font_color", color);
        }
        string last = Mode.LastLap is double l ? TimeTrialTargets.Format(l) : "-:--.--";
        string bestText = Mode.BestLap is double b ? TimeTrialTargets.Format(b) : "-:--.--";
        _lastBest.Text = $"LAST  {last}      BEST  {bestText}";

        var m = Mode.Medals;
        int got = Mode.BestLap is double bl ? m.Of(bl) : 0;
        double[] targets = { m.Gold, m.Silver, m.Bronze };
        for (int i = 0; i < 3; i++)
        {
            int medal = 3 - i;
            _medals[i].Text = $"{TimeTrialTargets.MedalName(medal),-8}{TimeTrialTargets.Format(targets[i])}{(got >= medal ? "   WON" : "")}";
            var mc = TimeTrialTargets.MedalColor(medal);
            _medals[i].AddThemeColorOverride("font_color", got >= medal ? mc : new Color(mc.R, mc.G, mc.B, 0.55f));
        }

        var rows = Mode.Records.Board(Mode.Key).Take(5).Select((r, i) => $"{i + 1}.  {TimeTrialTargets.Format(r.Time)}   {r.Date}");
        string boardText = string.Join("\n", rows);
        _board.Text = boardText.Length > 0 ? boardText : "no laps yet";

        _speed.Text = $"{Mode.Car.SpeedKmh:000} KPH";
        _help.Text = $"R  restart   ·   G  ghost {(Mode.GhostVisible ? "on" : "off")}   ·   {Controls.KeyName(Controls.CameraNext)}  camera   ·   ESC  pause";
    }
}
