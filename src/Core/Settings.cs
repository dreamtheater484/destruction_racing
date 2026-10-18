using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace DestructionRacing.Core;

/// <summary>
/// The player's options (plan §13), saved as <c>user://settings.json</c>. Plain values only; <see cref="GameSettings"/>
/// applies them to the window, the renderer, the audio buses and the controls.
/// </summary>
public sealed class Settings
{
    public static string FilePath => UserPaths.Of("settings.json");

    // Display
    /// <summary>0 windowed, 1 borderless full screen, 2 exclusive full screen.</summary>
    public int WindowMode { get; set; } = 1;
    public bool VSync { get; set; } = true;
    /// <summary>Frame cap; 0 = none.</summary>
    public int MaxFps { get; set; }
    /// <summary>3D resolution in percent of the window (below 100 upscaled with AMD FSR 1).</summary>
    public int RenderScale { get; set; } = 100;

    // Graphics: <see cref="Graphics"/> sets these from a preset; changing one makes the preset "custom".
    public int GraphicsPreset { get; set; } = (int)Core.GraphicsPreset.High;
    /// <summary>0 low … 3 ultra: the shadow map sizes and the soft-shadow filter.</summary>
    public int Shadows { get; set; } = 2;
    /// <summary>0 off, 1 FXAA, 2 MSAA ×2, 3 MSAA ×4.</summary>
    public int AntiAliasing { get; set; } = 2;
    /// <summary>0 off, 1 SSAO, 2 SSAO + SSIL (the venues' bounce light).</summary>
    public int AmbientOcclusion { get; set; } = 2;
    public bool Glow { get; set; } = true;
    /// <summary>The floodlights' haze at night.</summary>
    public bool VolumetricFog { get; set; } = true;
    /// <summary>0 low, 1 medium, 2 high: how early the models switch to their simpler LODs.</summary>
    public int Detail { get; set; } = 2;

    // Camera
    /// <summary>The view an event starts in (CameraRig.View: 0 chase, 1 far chase, 2 bumper, 3 in-car).</summary>
    public int DefaultView { get; set; }
    /// <summary>Field of view added to every view, in degrees (−10 … +15).</summary>
    public int FovOffset { get; set; }
    /// <summary>Camera shake in percent (0 = none).</summary>
    public int Shake { get; set; } = 100;
    /// <summary>The brief slow motion when you wreck a car in the Bowl (plan §11; Purist: off).</summary>
    public bool SlowMotion { get; set; } = true;

    // Audio, in percent of the mix
    public int MasterVolume { get; set; } = 100;
    public int MusicVolume { get; set; } = 100;
    public int EffectsVolume { get; set; } = 100;
    public int EnginesVolume { get; set; } = 100;
    public int CrowdVolume { get; set; } = 100;
    public int AnnouncerVolume { get; set; } = 100;
    /// <summary>0 off, 1 less, 2 normal.</summary>
    public int AnnouncerFrequency { get; set; } = 2;
    /// <summary>Radio taunts: 0 off, 1 some, 2 all.</summary>
    public int Taunts { get; set; } = 1;

    // Controls
    /// <summary>The key layout (<see cref="ControlPreset"/>); null until the first launch asked for it.</summary>
    public string? ControlPreset { get; set; }
    /// <summary>The custom layout: action → physical key codes.</summary>
    public Dictionary<string, long[]> CustomKeys { get; set; } = new();
    /// <summary>Counter-steer assist: 0 off, 1 low, 2 high (plan §14.3).</summary>
    public int CounterSteer { get; set; } = 1;
    public bool TractionControl { get; set; } = true;

    // Gameplay
    /// <summary>0 easy, 1 normal, 2 hard (plan §10.5): shifts the whole division curve.</summary>
    public int Difficulty { get; set; } = 1;
    /// <summary>Every layout, direction and light in Single Event, Practice and Time Trial without the championship.</summary>
    public bool UnlockAll { get; set; }

    // The last garage of Single Event, Time Trial and Practice
    public string LastCar { get; set; } = "kitsune";
    public int LastColour { get; set; }
    public int LastStyle { get; set; }
    public int LastNumber { get; set; } = 7;

    public Settings Clone()
    {
        var c = (Settings)MemberwiseClone();
        c.CustomKeys = new Dictionary<string, long[]>();
        foreach (var (k, v) in CustomKeys) c.CustomKeys[k] = (long[])v.Clone();
        return c;
    }

    public static Settings Load()
    {
        if (!FileAccess.FileExists(FilePath)) return new Settings();
        try
        {
            using var f = FileAccess.Open(FilePath, FileAccess.ModeFlags.Read);
            return JsonSerializer.Deserialize<Settings>(f.GetAsText()) ?? new Settings();
        }
        catch (Exception e)
        {
            GD.PushWarning($"Settings unreadable ({e.Message}); using the defaults.");
            return new Settings();
        }
    }

    public void Save()
    {
        using var f = FileAccess.Open(FilePath, FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>The graphics presets of the options screen.</summary>
public enum GraphicsPreset { Low, Medium, High, Ultra, Custom }
