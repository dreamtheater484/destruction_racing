using System.Collections.Generic;
using Godot;

namespace DestructionRacing.Modes;

/// <summary>
/// Game speed once the player is out. "Straight to the results" simulates the rest of the event as fast as the CPU
/// allows, with the 3D view and the sound off, behind the HUD's progress line: a race's remaining laps take about a
/// tenth of their racing time. Watching at ×2 uses the same speed-up.
/// Godot's TimeScale alone lengthens every physics step (at ×8 the old skip raced at 15 Hz instead of 120 Hz, and
/// ran at 4 fps), so a speed-up raises the tick rate by the same factor: every step stays 1/120 s.
/// </summary>
public static class FastFinish
{
    /// <summary>The fast finish asks for ×32; the CPU caps it at about ×10 (a physics tick of 20 cars ≈ 0.8 ms).</summary>
    private const int Factor = 32;
    /// <summary>Physics ticks per drawn frame while finishing: about 10 frames a second for the progress line.</summary>
    private const int StepsPerFrame = 96;

    private static int _savedSteps = 8;
    private static readonly List<Node3D> Hidden = new();
    private static bool _savedMute;

    public static bool Active { get; private set; }

    private static int BaseTicks => (int)ProjectSettings.GetSetting("physics/common/physics_ticks_per_second", 120);

    /// <summary>×factor game speed (≥ 1) with the normal physics step.</summary>
    public static void SetSpeed(int factor)
    {
        factor = Mathf.Max(1, factor);
        Engine.PhysicsTicksPerSecond = BaseTicks * factor;
        Engine.TimeScale = factor;
    }

    /// <param name="hide">The cars' models, hidden until the end. Each physics tick a moving body sends its new transform to
    /// every visible part of its model; for 20 cars that was a fifth of the fast finish's time. Hidden, they stay on their
    /// bodies, so dents and the parts that break off still land where they should.</param>
    public static void Begin(Viewport viewport, IEnumerable<Node3D?>? hide = null)
    {
        if (Active) return;
        Active = true;
        if (hide != null)
            foreach (var n in hide)
            {
                if (n == null || !n.Visible) continue;
                Hidden.Add(n);
                n.Visible = false;
            }
        _savedSteps = Engine.MaxPhysicsStepsPerFrame;
        Engine.MaxPhysicsStepsPerFrame = StepsPerFrame;
        SetSpeed(Factor);
        viewport.Disable3D = true;
        int master = AudioServer.GetBusIndex("Master");
        _savedMute = AudioServer.IsBusMute(master);
        AudioServer.SetBusMute(master, true);
    }

    /// <summary>Back to normal speed, view and sound (the event's end, or leaving it). Safe to call at any time.</summary>
    public static void End(Viewport? viewport)
    {
        SetSpeed(1);
        if (!Active) return;
        Active = false;
        foreach (var n in Hidden)
            if (GodotObject.IsInstanceValid(n)) n.Visible = true;
        Hidden.Clear();
        Engine.MaxPhysicsStepsPerFrame = _savedSteps;
        if (viewport != null) viewport.Disable3D = false;
        AudioServer.SetBusMute(AudioServer.GetBusIndex("Master"), _savedMute);
    }
}
