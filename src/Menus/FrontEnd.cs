using System.Collections.Generic;
using System.Linq;
using DestructionRacing.Audio;
using DestructionRacing.Core;
using DestructionRacing.Modes;
using DestructionRacing.Rules;
using DestructionRacing.Tracks;
using DestructionRacing.UI;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Menus;

/// <summary>
/// The front end (plan §13): Title → Main menu (Championship, Single Event, Time Trial, Practice, Options, Credits) →
/// car select in the 3D showroom → livery and number → layout (a live preview of the venue in its light) → loading →
/// the event → results → back here (the championship table, or the page the event was started from).
/// Pages sit on a stack; every screen works with the arrows, Enter and Esc. A page change plays a whoosh, the
/// focus a tick, and the menu music runs underneath.
/// </summary>
public partial class FrontEnd : Node3D
{
    public const string MenuKeys = "↑/↓ choose   ·   ←/→ change   ·   ENTER select   ·   ESC back";

    private static bool _booted;
    private bool _loadPrinted;
    private readonly List<Page> _stack = new();
    private Control _pages = null!;
    private Control _chrome = null!, _loading = null!;
    private Label _heading = null!, _sub = null!, _hint = null!, _keys = null!, _loadTitle = null!, _loadSub = null!;
    private Camera3D _camera = null!;
    private Node3D? _venue;
    private CarModel? _venueCar;
    private Vector3 _venueFocus;
    private float _orbit;
    private SfxPool _sfx = null!;
    private MusicPlayer _music = null!;
    private (Venue Venue, bool Reverse, BowlPreset Light)? _venueKey;

    public Showroom Showroom { get; private set; } = null!;
    public bool VenueShown => _venue != null;

    public override void _Ready()
    {
        GetTree().Paused = false;
        Engine.TimeScale = 1.0;
        Controls.Apply(Controls.Current);
        AudioMix.Ensure();
        _camera = new Camera3D { Current = true, Near = 0.05f, Far = 4000f, Fov = 30f };
        AddChild(_camera);
        Showroom = new Showroom { Camera = _camera };
        AddChild(Showroom);
        _sfx = new SfxPool { Voices3D = 0, Voices2D = 4 };
        AddChild(_sfx);
        _music = new MusicPlayer();
        AddChild(_music);
        BuildUi();

        // Back from an event: open the page it was started from.
        string page = Session.ReturnPage;
        Session.ReturnPage = "main";
        if (!_booted && DevTools.Instance?.Arg("fakecareer") is { } fake)
        {
            // --fakecareer=ROUNDS[,stockcar][,win] (captures, with --devuser): slot 3 with that many rounds played
            // (random results; "win": the player tops every round), opened on the hub.
            _booted = true;
            Push(new MainMenuPage());
            Push(new ChampionshipSlotsPage());
            Push(new HubPage(FakeCareer(fake)));
        }
        else if (!_booted)
        {
            _booted = true;
            Push(new TitlePage());
        }
        else
        {
            Push(new MainMenuPage());
            switch (page)
            {
                case "hub" when Session.Career is { } slot:
                    Push(new ChampionshipSlotsPage());
                    Push(new HubPage(slot));
                    break;
                case "single":
                    Push(new EventSetupPage(EventSetupPage.Flow.Single));
                    break;
                case "practice":
                    Push(new EventSetupPage(EventSetupPage.Flow.Practice));
                    break;
                case "timetrial":
                    Push(new EventSetupPage(EventSetupPage.Flow.TimeTrial));
                    break;
            }
        }
    }

    private void BuildUi()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        var root = new Control { Theme = HudStyle.Theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(root);

        // A dark wash on the left so the panels read over any backdrop.
        var wash = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = new Gradient { Colors = new[] { new Color(0f, 0f, 0f, 0.62f), new Color(0f, 0f, 0f, 0f) }, Offsets = new[] { 0.35f, 1f } },
                Width = 256, Height = 4,
            },
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        wash.AnchorRight = 0.62f;
        wash.AnchorBottom = 1f;
        root.AddChild(wash);

        _pages = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _pages.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_pages);

        _chrome = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _chrome.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_chrome);
        var title = HudStyle.Place(new VBoxContainer(), 0f, 0f, new Vector2(70, 52), new Vector2(1400, 0));
        title.AddThemeConstantOverride("separation", -16);
        title.AddChild(HudStyle.Label("DESTRUCTION RACING", 34, HudStyle.Yellow, "Bold", outline: 8));
        _heading = HudStyle.Label("", 92, HudStyle.White, "ExtraBold", outline: 10);
        title.AddChild(_heading);
        _sub = HudStyle.Label("", 28, HudStyle.Grey, "SemiBold", outline: 6);
        title.AddChild(_sub);
        _chrome.AddChild(title);

        var foot = HudStyle.Place(new VBoxContainer(), 0f, 1f, new Vector2(70, -112), new Vector2(1500, 0));
        foot.AddThemeConstantOverride("separation", 4);
        _hint = HudStyle.Label("", 26, HudStyle.White, "Medium", outline: 6);
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _hint.CustomMinimumSize = new Vector2(1100, 0);
        foot.AddChild(_hint);
        _keys = HudStyle.Label(MenuKeys, 24, HudStyle.Yellow, "SemiBold", outline: 6);
        foot.AddChild(_keys);
        _chrome.AddChild(foot);

        // The loading card (plan §13 flow: … → Loading → Race).
        _loading = new ColorRect { Color = new Color(0.02f, 0.02f, 0.025f, 0.94f), MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _loading.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var card = HudStyle.Place(new VBoxContainer(), 0f, 0.5f, new Vector2(140, -150), new Vector2(1600, 0));
        card.AddThemeConstantOverride("separation", -10);
        card.AddChild(HudStyle.Label("LOADING", 34, HudStyle.Yellow, "Bold", outline: 0));
        _loadTitle = HudStyle.Label("", 110, HudStyle.White, "ExtraBold", outline: 0);
        card.AddChild(_loadTitle);
        _loadSub = HudStyle.Label("", 34, HudStyle.Grey, "SemiBold", outline: 0);
        card.AddChild(_loadSub);
        var stripe = new TextureRect { Texture = HudStyle.Hazard, StretchMode = TextureRect.StretchModeEnum.Tile, CustomMinimumSize = new Vector2(0, 18), MouseFilter = Control.MouseFilterEnum.Ignore };
        stripe.AnchorTop = stripe.AnchorBottom = 1f;
        stripe.AnchorRight = 1f;
        stripe.OffsetTop = -18;
        _loading.AddChild(stripe);
        _loading.AddChild(card);
        root.AddChild(_loading);
    }

    private static CareerSlot FakeCareer(string spec)
    {
        var parts = spec.Split(',');
        int rounds = int.TryParse(parts[0], out int r) ? r : 4;
        var kind = parts.Contains("stockcar") ? ChampionshipKind.StockCar : ChampionshipKind.Wreckin;
        bool win = parts.Contains("win");
        var slot = CareerStore.New(3, kind, FrontEndState.Garage);
        var rng = new System.Random(5);
        var career = slot.Career;
        for (int i = 0; i < rounds && !career.SeasonComplete; i++)
        {
            var results = career.Drivers.Select((d, k) => new EventResult(d.Id,
                d.IsPlayer && win ? 120 : rng.Next(0, 110), rng.Next(0, 3), d.IsPlayer && win ? 1 : rng.Next(2, 21))).ToList();
            career.RecordEvent(results);
        }
        CareerStore.Save(slot);
        return slot;
    }

    // ------------------------------------------------------------------ the page stack

    public Page Top => _stack[^1];

    public void Push(Page page)
    {
        if (_stack.Count > 0) Top.Visible = false;
        page.Host = this;
        _stack.Add(page);
        _pages.AddChild(page);
        page.Shown();
        RefreshChrome();
        if (_stack.Count > 1) Play("banner_whoosh", -14f);
    }

    public void Pop()
    {
        if (_stack.Count <= 1) return;
        var page = Top;
        _stack.RemoveAt(_stack.Count - 1);
        page.QueueFree();
        Top.Visible = true;
        Top.Shown();
        RefreshChrome();
        Play("banner_whoosh", -16f, 0.9f);
    }

    /// <summary>Closes pages until <paramref name="page"/> is on top.</summary>
    public void PopTo(Page page)
    {
        while (_stack.Count > 1 && Top != page)
        {
            var p = Top;
            _stack.RemoveAt(_stack.Count - 1);
            p.QueueFree();
        }
        Top.Visible = true;
        Top.Shown();
        RefreshChrome();
    }

    /// <summary>Replaces the top page.</summary>
    public void Replace(Page page)
    {
        var old = Top;
        _stack.RemoveAt(_stack.Count - 1);
        old.QueueFree();
        Push(page);
    }

    public void RefreshChrome()
    {
        if (_stack.Count == 0) return;
        var top = Top;
        _chrome.Visible = top.ShowChrome;
        _heading.Text = top.Heading;
        _sub.Text = top.Subheading;
        _sub.Visible = top.Subheading.Length > 0;
        _hint.Text = top.Hint;
        _keys.Text = top.KeysHint;
    }

    public void Play(string sound, float db = -12f, float pitch = 1f) => _sfx.Play2D(sound, db, pitch);

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsEcho() || e is not InputEventKey { Pressed: true } key || _loading.Visible || _stack.Count == 0) return;
        var top = Top;
        if (top.HandleKey(key))
        {
            if (key.PhysicalKeycode is Key.Up or Key.Down or Key.Left or Key.Right) Play("tally_tick", -16f, 0.95f + GD.Randf() * 0.1f);
            else if (key.PhysicalKeycode is Key.Enter or Key.KpEnter) Play("tally_done", -14f);
            if (_stack.Count > 0 && Top == top) RefreshChrome();
        }
        else if (key.PhysicalKeycode is Key.Escape or Key.Backspace)
        {
            if (top.OnBack()) Pop();
        }
        else return;
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        // --loadtime (plan §17.4): the first frame of the menu, after the launch or after leaving an event.
        if (!_loadPrinted && DevTools.Instance?.Arg("loadtime") != null)
        {
            _loadPrinted = true;
            ulong now = Time.GetTicksMsec();
            bool back = DevTools.SceneRequestedAt > 0;
            GD.Print($"[Load] front end on screen: {(back ? now - DevTools.SceneRequestedAt : now) / 1000.0:0.00} s {(back ? "after leaving the event" : "after launch")}");
        }
        if (_stack.Count > 0) Top.Tick((float)delta);
        if (_venue != null)
        {
            _orbit += (float)delta * 0.12f;
            var c = _venueFocus + new Vector3(0f, 0.6f, 0f);
            var eye = c + new Vector3(Mathf.Cos(_orbit) * 8.5f, 1.8f, Mathf.Sin(_orbit) * 8.5f);
            var right = (c - eye).Cross(Vector3.Up).Normalized();
            _camera.Fov = 40f;
            _camera.LookAtFromPosition(eye, c - right * 2.8f + new Vector3(0f, -0.3f, 0f), Vector3.Up);
        }
    }

    public override void _ExitTree()
    {
        // The showroom is out of the tree while a venue is shown: free it with the front end.
        if (Showroom.GetParent() == null) Showroom.Free();
    }

    // ------------------------------------------------------------------ backdrops

    /// <summary>The showroom behind the menus (the venue preview goes away).</summary>
    public void ShowShowroom()
    {
        if (_venue != null)
        {
            RemoveChild(_venue);
            _venue.QueueFree();
            _venue = null;
            _venueCar = null;
            _venueKey = null;
        }
        if (Showroom.GetParent() == null) AddChild(Showroom);
        Showroom.Visible = true;
        Showroom.Offset = 1.7f;
        Showroom.Far = false;
    }

    /// <summary>The venue in its light with the player's car on pole (Speedway) or in the middle of the Bowl.</summary>
    public void ShowVenue(Venue venue, bool reverse, BowlPreset light, string car, Livery livery, string plate)
    {
        if (_venueKey is { } k && k.Venue == venue && k.Reverse == reverse && k.Light == light && _venueCar != null)
        {
            if (_venueCar.Root.GetMeta("car", "").AsString() == car) { _venueCar.ApplyLivery(livery); _venueCar.SetPlate(plate); return; }
        }
        if (Showroom.GetParent() != null) RemoveChild(Showroom);
        if (_venue != null && (_venueKey is not { } old || old.Venue != venue || old.Reverse != reverse || old.Light != light))
        {
            RemoveChild(_venue);
            _venue.QueueFree();
            _venue = null;
        }
        Transform3D pose;
        if (_venue == null)
        {
            if (venue == Venue.Bowl)
            {
                _venue = new Bowl { Preset = light, Visuals = true };
                AddChild(_venue);
            }
            else
            {
                var sw = new Speedway
                {
                    LayoutId = venue == Venue.Oval ? SpeedwayLayout.Oval : SpeedwayLayout.Figure8,
                    Reverse = reverse, Visuals = true, Preset = light,
                };
                _venue = sw;
                AddChild(_venue);
            }
            _venueKey = (venue, reverse, light);
            _venueCar = null;
        }
        if (_venue is Speedway s)
        {
            var slot = s.Layout.GridSlot(0);
            pose = slot with { Origin = slot.Origin - slot.Basis.Y * 0.62f };
        }
        else pose = new Transform3D(Basis.Identity.Rotated(Vector3.Up, 0.6f), Vector3.Zero);
        _venueCar?.Root.QueueFree();
        _venueCar = CarModel.Load(car, livery);
        _venueCar.Root.SetMeta("car", car);
        _venueCar.SetPlate(plate);
        _venue.AddChild(_venueCar.Root);
        _venueCar.Root.Transform = pose;
        _venueCar.Flush();
        _venueFocus = pose.Origin;
        _camera.Current = true;
    }

    // ------------------------------------------------------------------ starting an event

    /// <summary>Shows the loading card, hands the event to the mode scene and loads it.</summary>
    public async void StartEvent(EventRequest request, string returnPage, string title, string subtitle)
    {
        _loadTitle.Text = title;
        _loadSub.Text = subtitle;
        _loading.Visible = true;
        _music.Muted = true;
        Session.Begin(request, returnPage);
        // Two frames, so the card is on screen while the scene loads.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        DevTools.MarkSceneRequest();
        GetTree().ChangeSceneToFile(request.Scene);
    }
}
