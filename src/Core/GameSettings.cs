using System.Linq;
using DestructionRacing.Audio;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.Core;

/// <summary>
/// Applies the player's <see cref="Settings"/> (autoload): the window, the renderer (shadows, anti-aliasing, the
/// environment effects of every venue as it loads), the audio buses, the key layout and the assists.
/// The settings only take effect when the game runs through the front end (no <c>--scene</c>) or with
/// <c>--usersettings</c>: dev, test, capture and sim runs keep the defaults, so they stay reproducible.
/// </summary>
public partial class GameSettings : Node
{
    public static Settings Current { get; private set; } = new();
    public static bool Active { get; private set; }
    private static GameSettings? _instance;

    private static readonly int[] DirectionalShadow = { 2048, 4096, 8192, 8192 };
    private static readonly int[] PositionalShadow = { 2048, 4096, 4096, 8192 };
    private static readonly RenderingServer.ShadowQuality[] SoftShadow =
    {
        RenderingServer.ShadowQuality.SoftVeryLow, RenderingServer.ShadowQuality.SoftLow,
        RenderingServer.ShadowQuality.SoftMedium, RenderingServer.ShadowQuality.SoftHigh,
    };
    private static readonly float[] LodThreshold = { 4f, 2f, 1f };

    /// <summary>The soft-shadow filter of the spot and omni lights at the current shadow quality.</summary>
    public static RenderingServer.ShadowQuality PositionalSoftShadow => SoftShadow[Mathf.Clamp(Current.Shadows, 0, 3)];

    public override void _EnterTree()
    {
        _instance = this;
        ProcessMode = ProcessModeEnum.Always;
        Active = DevTools.Instance?.Arg("scene") == null || DevTools.Instance?.Arg("usersettings") != null;
        if (Active) Current = Settings.Load();
        GetTree().NodeAdded += OnNodeAdded;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        if (Active) ApplyAll();
    }

    // Godot's depth of field breaks up into black squares on a multisampled frame (distant cars and their lamps,
    // through the TV cameras' long lens; found post-M8, with MSAA off there were none). While the camera on screen
    // has depth of field, MSAA gives way to SMAA; the player's MSAA comes back with the next camera without it.
    private static Viewport.Msaa? _msaaHeld;
    private static Viewport.ScreenSpaceAAEnum _screenAaHeld;

    public override void _Process(double delta)
    {
        var vp = GetViewport();
        bool dof = vp.GetCamera3D()?.Attributes is CameraAttributesPractical a && (a.DofBlurFarEnabled || a.DofBlurNearEnabled);
        if (dof && _msaaHeld == null && vp.Msaa3D != Viewport.Msaa.Disabled)
        {
            _msaaHeld = vp.Msaa3D;
            _screenAaHeld = vp.ScreenSpaceAA;
            vp.Msaa3D = Viewport.Msaa.Disabled;
            vp.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Smaa;
        }
        else if (!dof && _msaaHeld is { } msaa)
        {
            vp.Msaa3D = msaa;
            vp.ScreenSpaceAA = _screenAaHeld;
            _msaaHeld = null;
        }
    }

    /// <summary>Saves and applies a changed copy of the settings (the options screen).</summary>
    public static void Commit(Settings settings)
    {
        Current = settings;
        settings.Save();
        if (Active) _instance?.ApplyAll();
    }

    /// <summary>Sets the graphics values of a preset (Custom leaves them).</summary>
    public static void UsePreset(Settings s, GraphicsPreset preset)
    {
        s.GraphicsPreset = (int)preset;
        switch (preset)
        {
            case GraphicsPreset.Low:
                s.Shadows = 0; s.AntiAliasing = 1; s.AmbientOcclusion = 0; s.Glow = true; s.VolumetricFog = false; s.Detail = 0; s.RenderScale = 77;
                break;
            case GraphicsPreset.Medium:
                s.Shadows = 1; s.AntiAliasing = 2; s.AmbientOcclusion = 1; s.Glow = true; s.VolumetricFog = false; s.Detail = 1; s.RenderScale = 100;
                break;
            case GraphicsPreset.High:
                s.Shadows = 2; s.AntiAliasing = 2; s.AmbientOcclusion = 2; s.Glow = true; s.VolumetricFog = true; s.Detail = 2; s.RenderScale = 100;
                break;
            case GraphicsPreset.Ultra:
                s.Shadows = 3; s.AntiAliasing = 3; s.AmbientOcclusion = 2; s.Glow = true; s.VolumetricFog = true; s.Detail = 2; s.RenderScale = 100;
                break;
        }
    }

    private void ApplyAll()
    {
        var s = Current;
        bool display = DisplayServer.GetName() != "headless";
        // --windowed (test runs): keep the window, whatever the settings say.
        if (display && DevTools.Instance?.Arg("windowed") == null)
        {
            DisplayServer.WindowSetMode(s.WindowMode switch
            {
                0 => DisplayServer.WindowMode.Windowed,
                2 => DisplayServer.WindowMode.ExclusiveFullscreen,
                _ => DisplayServer.WindowMode.Fullscreen,
            });
        }
        if (display) DisplayServer.WindowSetVsyncMode(s.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = s.MaxFps;
        var root = GetTree().Root;
        ApplyAntiAliasing(root);
        int shadows = Mathf.Clamp(s.Shadows, 0, 3);
        RenderingServer.DirectionalShadowAtlasSetSize(DirectionalShadow[shadows], true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(SoftShadow[shadows]);
        RenderingServer.PositionalSoftShadowFilterSetQuality(SoftShadow[shadows]);
        root.PositionalShadowAtlasSize = PositionalShadow[shadows];
        root.MeshLodThreshold = LodThreshold[Mathf.Clamp(s.Detail, 0, 2)];
        foreach (var env in FindAll<WorldEnvironment>(root)) ApplyEnvironment(env);

        AudioMix.MasterGain = s.MasterVolume / 100f;
        AudioMix.MusicGain = s.MusicVolume / 100f;
        AudioMix.EffectsGain = s.EffectsVolume / 100f;
        AudioMix.EnginesGain = s.EnginesVolume / 100f;
        AudioMix.CrowdGain = s.CrowdVolume / 100f;
        AudioMix.AnnouncerGain = s.AnnouncerVolume / 100f;
        AudioMix.Ensure();
        AudioMix.Refresh();

        Controls.CustomMap = Controls.GameActions.ToDictionary(a => a,
            a => s.CustomKeys.TryGetValue(a, out var keys) ? keys.Select(k => (Key)k).ToArray() : Controls.PresetMap(ControlPreset.Arrows)[a]);
        Controls.Apply(Controls.Parse(s.ControlPreset));
        if (DevTools.Instance?.Arg("taunts") == null) AI.Taunts.Frequency = (AI.Taunts.Level)Mathf.Clamp(s.Taunts, 0, 2);
    }

    /// <summary>
    /// The anti-aliasing and the 3D resolution of the window. The venues call this as they load; without the
    /// player's settings (dev, test and capture runs) it is the look-dev setting, MSAA ×4 + SMAA at full resolution.
    /// </summary>
    public static void ApplyAntiAliasing(Viewport vp)
    {
        _msaaHeld = null;   // a new setting replaces the one held during depth of field (see _Process)
        int aa = Active ? Mathf.Clamp(Current.AntiAliasing, 0, 5) : 3;
        float scale = Active ? Mathf.Clamp(Current.RenderScale, 50, 100) / 100f : 1f;
        // --scale3d=X (performance runs): the 3D resolution as a factor of the window, e.g. 1.5 renders 4K in 1440p.
        if (float.TryParse(DevTools.Instance?.Arg("scale3d"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float s3)) scale = Mathf.Clamp(s3, 0.25f, 2f);
        vp.Msaa3D = aa switch { 2 => Viewport.Msaa.Msaa2X, 3 => Viewport.Msaa.Msaa4X, _ => Viewport.Msaa.Disabled };
        vp.ScreenSpaceAA = aa switch { 1 => Viewport.ScreenSpaceAAEnum.Fxaa, 3 => Viewport.ScreenSpaceAAEnum.Smaa, _ => Viewport.ScreenSpaceAAEnum.Disabled };
        vp.UseTaa = aa == 4;
        // FSR 2 is temporal anti-aliasing and the upscaler in one (at 100% it only anti-aliases).
        vp.Scaling3DMode = aa == 5 ? Viewport.Scaling3DModeEnum.Fsr2
            : scale < 0.999f ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
        vp.Scaling3DScale = scale;
    }

    private static void OnNodeAdded(Node node)
    {
        if (Active && node is WorldEnvironment env) ApplyEnvironment(env);
    }

    /// <summary>Switches the venue's own effects off where the settings say so (and back on: the originals are kept).</summary>
    private static void ApplyEnvironment(WorldEnvironment node)
    {
        if (node.Environment is not { } env) return;
        var s = Current;
        bool Orig(string key, bool value)
        {
            if (!env.HasMeta(key)) env.SetMeta(key, value);
            return env.GetMeta(key).AsBool();
        }
        bool ssao = Orig("dr_ssao", env.SsaoEnabled), ssil = Orig("dr_ssil", env.SsilEnabled);
        bool glow = Orig("dr_glow", env.GlowEnabled), fog = Orig("dr_vfog", env.VolumetricFogEnabled);
        env.SsaoEnabled = ssao && s.AmbientOcclusion >= 1;
        env.SsilEnabled = ssil && s.AmbientOcclusion >= 2;
        env.GlowEnabled = glow && s.Glow;
        env.VolumetricFogEnabled = fog && s.VolumetricFog;
        if (env.HasMeta("dr_grade")) env.AdjustmentEnabled = s.FilmLook;
    }

    /// <summary>The player's assists on the player's own car (plan §14.3); the bots keep the defaults.</summary>
    public static void ApplyAssists(Car car)
    {
        if (!Active) return;
        if (Current.CounterSteer == 0) car.Tuning.CounterSteerAssist = 0f;
        else if (Current.CounterSteer == 2) car.Tuning.CounterSteerAssist = 0.7f;
        if (!Current.TractionControl) car.Tuning.TractionControl = 1.2f;
    }

    /// <summary>Announcer frequency for <see cref="Announcer.Frequency"/>: off, less, normal.</summary>
    public static float AnnouncerFrequency => !Active ? 1f : Current.AnnouncerFrequency switch { 0 => 0f, 1 => 0.5f, _ => 1f };

    public static float ShakeScale => Active ? Current.Shake / 100f : 1f;
    public static float FovOffset => Active ? Current.FovOffset : 0f;
    public static bool SlowMotion => !Active || Current.SlowMotion;
    public static bool CatchUp => !Active || Current.CatchUp;
    /// <summary>The film look (dev runs: on; <c>--nofilm</c> for before/after captures).</summary>
    public static bool FilmLook => Active ? Current.FilmLook : DevTools.Instance?.Arg("nofilm") == null;
    /// <summary>Motion blur strength, 0..1 (dev runs: the default, or <c>--motionblur=PERCENT</c>).</summary>
    public static float MotionBlur => (Active ? Current.MotionBlur
        : int.TryParse(DevTools.Instance?.Arg("motionblur"), out int mb) ? mb : 40) / 100f;

    private static System.Collections.Generic.IEnumerable<T> FindAll<T>(Node root) where T : Node
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is T t) yield return t;
            foreach (var n in FindAll<T>(child)) yield return n;
        }
    }
}
