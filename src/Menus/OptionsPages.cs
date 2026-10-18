using System;
using System.Collections.Generic;
using System.Linq;
using DestructionRacing.Core;
using DestructionRacing.UI;
using Godot;

namespace DestructionRacing.Menus;

/// <summary>The options (plan §13): one row per group; each opens its page. Every change applies and saves at once.</summary>
public partial class OptionsPage : MenuPage
{
    public override string Heading => "OPTIONS";

    protected override void Build()
    {
        Menu.FontSize = 40;
        Add("DISPLAY", "Window or full screen, vertical sync, the frame cap and the resolution scale.", OptionRows.Display);
        Add("GRAPHICS", "A preset, or shadows, anti-aliasing, ambient occlusion, glow, the night haze and the detail one by one.", OptionRows.Graphics);
        Add("CAMERA", "The view you start in, the field of view, camera shake and the slow motion on wrecks.", OptionRows.Camera);
        Add("SOUND", "The volumes, how often the announcer talks and how often the drivers taunt you.", OptionRows.Sound);
        Add("CONTROLS", "The key layout, your own keys, the steering assists and the keyboard test.", OptionRows.Controls);
        Add("GAMEPLAY", "Difficulty, and unlocking every layout and light without the championship.", OptionRows.Gameplay);
    }

    private void Add(string name, string hint, Action<MenuView, FrontEnd> rows) =>
        Menu.Add(new MenuItem(name) { Activate = () => Host.Push(new OptionsGroupPage(name, rows)), Hint = () => hint });
}

/// <summary>One group of options.</summary>
public partial class OptionsGroupPage : MenuPage
{
    private readonly string _name;
    private readonly Action<MenuView, FrontEnd> _rows;

    public OptionsGroupPage(string name, Action<MenuView, FrontEnd> rows)
    {
        _name = name;
        _rows = rows;
    }

    public override string Heading => _name;
    public override string Subheading => "OPTIONS";
    protected override float PanelWidth => 1040f;

    protected override void Build()
    {
        Menu.CaptionWidth = 420;
        Menu.FontSize = 32;
        _rows(Menu, Host);
    }
}

/// <summary>The rows of each options group, bound to <see cref="GameSettings.Current"/>.</summary>
public static class OptionRows
{
    private static void Change(Action<Settings> change, bool graphics = false)
    {
        var s = GameSettings.Current.Clone();
        change(s);
        if (graphics) s.GraphicsPreset = (int)GraphicsPreset.Custom;
        GameSettings.Commit(s);
    }

    private static MenuItem Choice(string caption, string[] names, Func<Settings, int> get, Action<Settings, int> set, string hint, bool graphics = false) => new(caption)
    {
        Value = () => names[Math.Clamp(get(GameSettings.Current), 0, names.Length - 1)],
        Change = d => Change(s => set(s, (Math.Clamp(get(s), 0, names.Length - 1) + d + names.Length) % names.Length), graphics),
        Hint = () => hint,
    };

    private static MenuItem Toggle(string caption, Func<Settings, bool> get, Action<Settings, bool> set, string hint, bool graphics = false) => new(caption)
    {
        Value = () => get(GameSettings.Current) ? "ON" : "OFF",
        Change = d => Change(s => set(s, !get(s)), graphics),
        Hint = () => hint,
    };

    private static MenuItem Slider(string caption, int min, int max, int step, Func<Settings, int> get, Action<Settings, int> set, Func<int, string> show, string hint) => new(caption)
    {
        Value = () => show(get(GameSettings.Current)),
        Change = d => Change(s => set(s, Math.Clamp(get(s) + d * step, min, max))),
        Hint = () => hint,
    };

    private static string Percent(int v) => v == 0 ? "OFF" : $"{v}%";

    public static void Display(MenuView m, FrontEnd host)
    {
        m.Add(Choice("WINDOW", new[] { "WINDOWED", "BORDERLESS FULL SCREEN", "FULL SCREEN" }, s => s.WindowMode, (s, v) => s.WindowMode = v,
            "Borderless is the quickest to switch away from; full screen can be a little smoother."));
        m.Add(Toggle("VERTICAL SYNC", s => s.VSync, (s, v) => s.VSync = v, "On: no tearing. Off: the lowest input lag."));
        int[] caps = { 0, 60, 120, 144, 165, 240 };
        m.Add(new MenuItem("FRAME CAP")
        {
            Value = () => GameSettings.Current.MaxFps == 0 ? "NONE" : $"{GameSettings.Current.MaxFps} FPS",
            Change = d => Change(s => s.MaxFps = caps[(Math.Max(0, Array.IndexOf(caps, s.MaxFps)) + d + caps.Length) % caps.Length]),
            Hint = () => "A cap saves power and heat; NONE runs as fast as the card can.",
        });
        m.Add(Slider("RESOLUTION SCALE", 50, 100, 5, s => s.RenderScale, (s, v) => s.RenderScale = v,
            v => v >= 100 ? "100%  (NATIVE)" : $"{v}%  (FSR UPSCALING)", "The 3D picture below 100% is upscaled with AMD FSR 1: faster, a little softer. The HUD stays sharp."));
    }

    public static void Graphics(MenuView m, FrontEnd host)
    {
        m.Add(new MenuItem("PRESET")
        {
            Value = () => ((GraphicsPreset)GameSettings.Current.GraphicsPreset).ToString().ToUpperInvariant(),
            Change = d => Change(s =>
            {
                int p = ((s.GraphicsPreset >= 4 ? 2 : s.GraphicsPreset) + d + 4) % 4;
                GameSettings.UsePreset(s, (GraphicsPreset)p);
            }),
            Hint = () => "LOW for older cards (GTX 1660 class: 1080p with FSR), HIGH for an RTX 3060 at 1080p, ULTRA for the fastest cards.",
        });
        m.Add(Choice("SHADOWS", new[] { "LOW", "MEDIUM", "HIGH", "ULTRA" }, s => s.Shadows, (s, v) => s.Shadows = v,
            "The size of the shadow maps and how soft their edges are.", graphics: true));
        m.Add(Choice("ANTI-ALIASING", new[] { "OFF", "FXAA", "MSAA ×2", "MSAA ×4" }, s => s.AntiAliasing, (s, v) => s.AntiAliasing = v,
            "Smooths jagged edges. MSAA is sharper, FXAA cheaper.", graphics: true));
        m.Add(Choice("AMBIENT OCCLUSION", new[] { "OFF", "SSAO", "SSAO + BOUNCE LIGHT" }, s => s.AmbientOcclusion, (s, v) => s.AmbientOcclusion = v,
            "Contact shadows under and between things; the bounce light adds colour bleeding.", graphics: true));
        m.Add(Toggle("GLOW", s => s.Glow, (s, v) => s.Glow = v, "The bloom on lights, sparks and fire.", graphics: true));
        m.Add(Toggle("NIGHT HAZE", s => s.VolumetricFog, (s, v) => s.VolumetricFog = v, "The floodlights' beams in the haze at night (volumetric fog).", graphics: true));
        m.Add(Choice("DETAIL", new[] { "LOW", "MEDIUM", "HIGH" }, s => s.Detail, (s, v) => s.Detail = v,
            "How early distant models switch to their simpler versions.", graphics: true));
    }

    public static void Camera(MenuView m, FrontEnd host)
    {
        m.Add(Choice("START VIEW", new[] { "CHASE (DD2)", "FAR CHASE (DD1)", "BUMPER", "IN THE CAR" }, s => s.DefaultView, (s, v) => s.DefaultView = v,
            "The view an event starts in; C (or your camera key) still cycles all four."));
        m.Add(Slider("FIELD OF VIEW", -10, 15, 1, s => s.FovOffset, (s, v) => s.FovOffset = v, v => v == 0 ? "STANDARD" : $"{v:+0;-0}°",
            "Added to every view: wider shows more of the field, narrower feels faster."));
        m.Add(Slider("CAMERA SHAKE", 0, 100, 10, s => s.Shake, (s, v) => s.Shake = v, Percent, "How much the camera shakes on big hits."));
        m.Add(Toggle("SLOW MOTION", s => s.SlowMotion, (s, v) => s.SlowMotion = v, "A quarter of a second of slow motion when you wreck a car in the Bowl (at most every 10 s)."));
    }

    public static void Sound(MenuView m, FrontEnd host)
    {
        m.Add(Slider("MASTER", 0, 100, 10, s => s.MasterVolume, (s, v) => s.MasterVolume = v, Percent, "Everything."));
        m.Add(Slider("MUSIC", 0, 100, 10, s => s.MusicVolume, (s, v) => s.MusicVolume = v, Percent, "The music (also your own files in the Music folder)."));
        m.Add(Slider("EFFECTS", 0, 100, 10, s => s.EffectsVolume, (s, v) => s.EffectsVolume = v, Percent, "Crashes, scrapes, glass, the menus."));
        m.Add(Slider("ENGINES", 0, 100, 10, s => s.EnginesVolume, (s, v) => s.EnginesVolume = v, Percent, "Every engine on the track."));
        m.Add(Slider("CROWD", 0, 100, 10, s => s.CrowdVolume, (s, v) => s.CrowdVolume = v, Percent, "The crowd in the stands."));
        m.Add(Slider("ANNOUNCER", 0, 100, 10, s => s.AnnouncerVolume, (s, v) => s.AnnouncerVolume = v, Percent, "The announcer's voice."));
        m.Add(Choice("ANNOUNCER CALLS", new[] { "OFF", "LESS", "NORMAL" }, s => s.AnnouncerFrequency, (s, v) => s.AnnouncerFrequency = v,
            "How often the announcer talks. LESS keeps the important calls."));
        m.Add(Choice("RADIO TAUNTS", new[] { "OFF", "SOME", "ALL" }, s => s.Taunts, (s, v) => s.Taunts = v,
            "The drivers' radio messages when they spin or wreck you, or you them."));
    }

    public static void Controls(MenuView m, FrontEnd host)
    {
        var presets = new[] { ControlPreset.ClassicDdPc, ControlPreset.Arrows, ControlPreset.Wasd, ControlPreset.Custom };
        m.Add(new MenuItem("KEY LAYOUT")
        {
            Value = () => Core.Controls.PresetName(Core.Controls.Parse(GameSettings.Current.ControlPreset)),
            Change = d => Change(s =>
            {
                var p = Core.Controls.Parse(s.ControlPreset);
                int i = Array.IndexOf(presets, p);
                p = presets[(i + d + presets.Length) % presets.Length];
                if (p == ControlPreset.Custom && s.CustomKeys.Count == 0)
                    s.CustomKeys = Core.Controls.PresetMap(Core.Controls.Current).ToDictionary(kv => kv.Key, kv => kv.Value.Select(k => (long)k).ToArray());
                s.ControlPreset = p.ToString();
            }),
            Hint = () => Core.Controls.Describe(Core.Controls.Current),
        });
        m.Add(new MenuItem("REBIND KEYS") { Activate = () => host.Push(new RebindPage()), Hint = () => "Choose your own key for every action (this makes the layout CUSTOM)." });
        m.Add(Choice("COUNTER-STEER", new[] { "OFF", "LOW", "HIGH" }, s => s.CounterSteer, (s, v) => s.CounterSteer = v,
            "Takes some steering off when the tail steps out. LOW is the default; OFF is the purist's choice."));
        m.Add(Toggle("TRACTION CONTROL", s => s.TractionControl, (s, v) => s.TractionControl = v,
            "Light wheelspin control for the rear-drive V8s. OFF lets them light up the tyres."));
        m.Add(new MenuItem("KEYBOARD TEST") { Activate = () => host.Push(new KeyTestPage()), Hint = () => "Check which keys your keyboard registers when you hold several at once (ghosting)." });
    }

    public static void Gameplay(MenuView m, FrontEnd host)
    {
        m.Add(Choice("DIFFICULTY", new[] { "EASY", "NORMAL", "HARD" }, s => s.Difficulty, (s, v) => s.Difficulty = v,
            "Shifts every division: the racing AI's pace and how many derby cars may hunt you at once. NORMAL is the tuned one."));
        m.Add(Toggle("UNLOCK EVERYTHING", s => s.UnlockAll, (s, v) => s.UnlockAll = v,
            "Every layout, direction and light (and the face-off start and the champion colours) in Single Event, Practice and Time Trial, without the championship."));
    }
}

/// <summary>
/// Key rebinding (plan §14): Enter on an action, then press its new key (Esc cancels). A key another action used is
/// swapped onto that action, so no key does two things. Rebinding makes the layout CUSTOM.
/// </summary>
public partial class RebindPage : MenuPage
{
    private string? _capturing;
    private string _message = "";
    private Dictionary<string, Key[]> _map = new();
    private ControlPreset _from = ControlPreset.Arrows;
    private static readonly HashSet<Key> Reserved = new() { Key.F1, Key.F3, Key.F5, Key.Enter, Key.KpEnter };

    public override string Heading => "REBIND KEYS";
    public override string Subheading => "OPTIONS  ·  CONTROLS";
    protected override float PanelWidth => 1040f;
    public override string Hint => _capturing != null
        ? $"PRESS THE NEW KEY FOR {Core.Controls.ActionName(_capturing)}   ·   ESC CANCELS"
        : _message.Length > 0 ? _message : base.Hint;
    public override string KeysHint => _capturing != null ? "ANY KEY  bind   ·   ESC cancel" : FrontEnd.MenuKeys;

    protected override void Build()
    {
        Menu.CaptionWidth = 420;
        _map = Core.Controls.PresetMap(Core.Controls.Current);
        _from = Core.Controls.Current == ControlPreset.Custom ? ControlPreset.Arrows : Core.Controls.Current;
        foreach (string a in Core.Controls.GameActions)
        {
            string action = a;
            Menu.Add(new MenuItem(Core.Controls.ActionName(action))
            {
                Value = () => Core.Controls.Keys(action, _map),
                Activate = () => { _capturing = action; _message = ""; },
                Hint = () => "ENTER, then press the key." + (Core.Controls.UsesShift(_map)
                    ? "  Note: SHIFT pressed five times opens Windows' Sticky Keys dialog; you can switch that shortcut off in Windows' accessibility settings."
                    : ""),
            });
        }
        Menu.Add(new MenuItem("START AGAIN FROM")
        {
            Value = () => Core.Controls.PresetName(_from),
            Change = d => _from = (ControlPreset)(((int)_from + d + 3) % 3),
            Activate = () =>
            {
                _map = Core.Controls.PresetMap(_from);
                Save();
                _message = $"Your keys are a copy of {Core.Controls.PresetName(_from)} again.";
            },
            Hint = () => "←/→ pick a layout, ENTER copies it over your keys.",
        });
    }

    private void Save()
    {
        var s = GameSettings.Current.Clone();
        s.CustomKeys = _map.ToDictionary(kv => kv.Key, kv => kv.Value.Select(k => (long)k).ToArray());
        s.ControlPreset = ControlPreset.Custom.ToString();
        GameSettings.Commit(s);
    }

    public override bool HandleKey(InputEventKey key)
    {
        if (_capturing == null) return base.HandleKey(key);
        var k = key.PhysicalKeycode;
        if (k == Key.Escape)
        {
            _capturing = null;
            Menu.Refresh();
            return true;
        }
        if (Reserved.Contains(k) || k == Key.None)
        {
            _message = $"{Core.Controls.KeyLabel(k)} is kept for the menus and the developer keys; pick another.";
            _capturing = null;
            Menu.Refresh();
            return true;
        }
        string? lost = Core.Controls.Rebind(_map, _capturing, k);
        _message = lost != null
            ? $"{Core.Controls.KeyLabel(k)} is now {Core.Controls.ActionName(_capturing)}; {Core.Controls.ActionName(lost)} took over {Core.Controls.Keys(lost, _map)}."
            : $"{Core.Controls.KeyLabel(k)} is now {Core.Controls.ActionName(_capturing)}.";
        _capturing = null;
        Save();
        Menu.Refresh();
        return true;
    }

    public override bool OnBack() => _capturing == null;
}

/// <summary>
/// The keyboard ghosting test (plan §14.2): a keyboard on screen lights every key you hold, counts how many register
/// at once, and ticks off the driving combinations of your layout once all their keys were held together.
/// </summary>
public partial class KeyTestPage : Page
{
    private readonly HashSet<Key> _held = new();
    private readonly Dictionary<Key, Panel> _keys = new();
    private readonly List<(string Name, Key[] Keys, Label Label)> _combos = new();
    private readonly HashSet<string> _seen = new();
    private Label _count = null!;
    private int _most;

    public override string Heading => "KEYBOARD TEST";
    public override string Subheading => "HOLD SEVERAL KEYS AT ONCE";
    public override string Hint => "Cheap keyboards drop a key when too many are held together (ghosting). Hold the combinations below; each turns green when every key registered.";
    public override string KeysHint => "ESC back";

    private static readonly (string Label, Key Key, float Width)[][] Rows =
    {
        new[] { ("ESC", Key.Escape, 1.2f), ("1", Key.Key1, 1f), ("2", Key.Key2, 1f), ("3", Key.Key3, 1f), ("4", Key.Key4, 1f), ("5", Key.Key5, 1f), ("6", Key.Key6, 1f), ("7", Key.Key7, 1f), ("8", Key.Key8, 1f), ("9", Key.Key9, 1f), ("0", Key.Key0, 1f), ("⌫", Key.Backspace, 1.6f) },
        new[] { ("TAB", Key.Tab, 1.5f), ("Q", Key.Q, 1f), ("W", Key.W, 1f), ("E", Key.E, 1f), ("R", Key.R, 1f), ("T", Key.T, 1f), ("Y", Key.Y, 1f), ("U", Key.U, 1f), ("I", Key.I, 1f), ("O", Key.O, 1f), ("P", Key.P, 1f) },
        new[] { ("CAPS", Key.Capslock, 1.8f), ("A", Key.A, 1f), ("S", Key.S, 1f), ("D", Key.D, 1f), ("F", Key.F, 1f), ("G", Key.G, 1f), ("H", Key.H, 1f), ("J", Key.J, 1f), ("K", Key.K, 1f), ("L", Key.L, 1f), ("ENTER", Key.Enter, 1.9f) },
        new[] { ("SHIFT", Key.Shift, 2.3f), ("Z", Key.Z, 1f), ("X", Key.X, 1f), ("C", Key.C, 1f), ("V", Key.V, 1f), ("B", Key.B, 1f), ("N", Key.N, 1f), ("M", Key.M, 1f), (",", Key.Comma, 1f), (".", Key.Period, 1f) },
        new[] { ("CTRL", Key.Ctrl, 1.5f), ("ALT", Key.Alt, 1.3f), ("SPACE", Key.Space, 6f), ("←", Key.Left, 1f), ("↑", Key.Up, 1f), ("↓", Key.Down, 1f), ("→", Key.Right, 1f) },
    };

    public override void _Ready()
    {
        var panel = LeftPanel(this, 940f, 240f);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        panel.AddChild(box);
        foreach (var row in Rows)
        {
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 6);
            foreach (var (label, key, width) in row)
            {
                var p = new Panel { CustomMinimumSize = new Vector2(64 * width + 6 * (width - 1), 56) };
                var l = HudStyle.Label(label, 22, HudStyle.White, "Bold", HorizontalAlignment.Center, 0);
                l.SetAnchorsPreset(LayoutPreset.FullRect);
                l.VerticalAlignment = VerticalAlignment.Center;
                p.AddChild(l);
                line.AddChild(p);
                _keys[key] = p;
            }
            box.AddChild(line);
        }
        _count = HudStyle.Label("", 32, HudStyle.Yellow, "Bold", outline: 0);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        box.AddChild(_count);

        var map = Core.Controls.PresetMap(Core.Controls.Current);
        Key First(string a) => map.TryGetValue(a, out var k) && k.Length > 0 ? k[0] : Key.None;
        void Combo(string name, params string[] actions)
        {
            var keys = actions.Select(First).Where(k => k != Key.None).Distinct().ToArray();
            var label = HudStyle.Label("", 26, HudStyle.Grey, "SemiBold", outline: 0);
            box.AddChild(label);
            _combos.Add((name, keys, label));
        }
        Combo("GAS + STEER LEFT + FAST STEER + TURN-AROUND", Core.Controls.Accelerate, Core.Controls.SteerLeft, Core.Controls.FastSteer, Core.Controls.TurnAround);
        Combo("BRAKE + STEER RIGHT + INSTANT REVERSE", Core.Controls.Brake, Core.Controls.SteerRight, Core.Controls.Reverse);
        Combo("GAS + BRAKE + STEER LEFT (THE BRAKE DAB)", Core.Controls.Accelerate, Core.Controls.Brake, Core.Controls.SteerLeft);
        Combo("GAS + STEER RIGHT + FAST STEER + LOOK BACK", Core.Controls.Accelerate, Core.Controls.SteerRight, Core.Controls.FastSteer, Core.Controls.LookBack);
        Refresh();
    }

    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey k || k.IsEcho()) return;
        if (k.Pressed) _held.Add(k.PhysicalKeycode);
        else _held.Remove(k.PhysicalKeycode);
        _most = Math.Max(_most, _held.Count);
        foreach (var c in _combos)
            if (c.Keys.Length > 0 && c.Keys.All(_held.Contains)) _seen.Add(c.Name);
        Refresh();
    }

    private void Refresh()
    {
        foreach (var (key, panel) in _keys)
        {
            bool on = _held.Contains(key);
            panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = on ? HudStyle.Yellow : new Color(1f, 1f, 1f, 0.08f),
                BorderColor = new Color(1f, 1f, 1f, 0.25f),
                BorderWidthBottom = 3, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1,
                CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            });
            if (panel.GetChildCount() > 0 && panel.GetChild(0) is Label l) l.AddThemeColorOverride("font_color", on ? Colors.Black : HudStyle.White);
        }
        _count.Text = $"HELD NOW: {_held.Count}      MOST AT ONCE: {_most}";
        foreach (var (name, keys, label) in _combos)
        {
            bool ok = _seen.Contains(name);
            label.Text = $"{(ok ? "✓" : "·")}  {name}:  {string.Join(" + ", keys.Select(Core.Controls.KeyLabel))}";
            label.AddThemeColorOverride("font_color", ok ? HudStyle.Green : HudStyle.Grey);
        }
    }
}
