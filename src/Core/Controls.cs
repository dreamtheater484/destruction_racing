using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DestructionRacing.Core;

/// <summary>Keyboard presets (plan §14). Physical keycodes, so the layout works on AZERTY/QWERTZ too.</summary>
public enum ControlPreset
{
    /// <summary>The layout of the DD1 PC manual: A gas, Z brake/reverse, arrows, Space turn-around, F2 views, P pause.</summary>
    ClassicDdPc,
    /// <summary>Modern arrows layout with Z instant reverse, X Fast Steer, Space turn-around, C camera, V look back.</summary>
    Arrows,
    Wasd,
    /// <summary>The player's own layout, rebound in the options (<see cref="Controls.CustomMap"/>).</summary>
    Custom,
}

public static class Controls
{
    private static readonly System.Collections.Generic.Dictionary<string, StringName> Names = new();

    /// <summary>
    /// The action as a StringName, made once: Input.IsActionPressed with a string makes and tracks a new one on
    /// every call, garbage for the .NET GC several times per frame (M8).
    /// </summary>
    public static StringName Name(string action)
    {
        if (!Names.TryGetValue(action, out var name)) Names[action] = name = action;
        return name;
    }

    public const string Accelerate = "accelerate";
    public const string Brake = "brake";
    public const string Reverse = "reverse";
    public const string SteerLeft = "steer_left";
    public const string SteerRight = "steer_right";
    public const string FastSteer = "fast_steer";
    public const string TurnAround = "turn_around";
    public const string CameraNext = "camera_next";
    public const string LookBack = "look_back";
    public const string Pause = "pause";
    public const string Standings = "standings";
    public const string DebugPanel = "debug_panel";
    public const string NextPreset = "next_preset";
    public const string ResetCar = "reset_car";
    /// <summary>The pit stop mini-game (plan §14): ←/→ choose the zone, Enter or Space repairs.</summary>
    public const string PitPrev = "pit_prev";
    public const string PitNext = "pit_next";
    public const string PitRepair = "pit_repair";

    public static ControlPreset Current { get; private set; } = ControlPreset.Arrows;

    /// <summary>The driving actions, in the order the rebinding screen lists them.</summary>
    public static readonly string[] GameActions =
    {
        Accelerate, Brake, Reverse, SteerLeft, SteerRight, FastSteer, TurnAround, CameraNext, LookBack, Pause, Standings,
    };

    /// <summary>The custom layout (starts as a copy of the preset it was made from).</summary>
    public static Dictionary<string, Key[]> CustomMap { get; set; } = PresetMap(ControlPreset.Arrows);

    public static string PresetName(ControlPreset preset) => preset switch
    {
        ControlPreset.ClassicDdPc => "CLASSIC DD PC",
        ControlPreset.Arrows => "ARROWS",
        ControlPreset.Wasd => "WASD",
        _ => "CUSTOM",
    };

    public static string ActionName(string action) => action switch
    {
        Accelerate => "ACCELERATE",
        Brake => "BRAKE / REVERSE",
        Reverse => "INSTANT REVERSE",
        SteerLeft => "STEER LEFT",
        SteerRight => "STEER RIGHT",
        FastSteer => "FAST STEER",
        TurnAround => "TURN-AROUND",
        CameraNext => "CHANGE CAMERA",
        LookBack => "LOOK BACK",
        Pause => "PAUSE",
        Standings => "STANDINGS",
        _ => action.ToUpperInvariant(),
    };

    public static string Describe(ControlPreset preset) => preset switch
    {
        ControlPreset.ClassicDdPc => "Classic DD PC: A gas · Z brake/reverse · ←→ steer · X fast steer · Space turn-around · F2 camera · V look back · P pause",
        ControlPreset.Arrows => "Arrows: ↑ gas · ↓ brake/reverse · ←→ steer · Z reverse · X fast steer · Space turn-around · C camera · V look back · Esc pause",
        ControlPreset.Wasd => "WASD: W gas · S brake/reverse · A/D steer · J reverse · K fast steer · Space turn-around · L camera · V look back · Esc pause",
        _ => $"Custom: {Keys(Accelerate)} gas · {Keys(Brake)} brake/reverse · {Keys(SteerLeft)}{Keys(SteerRight)} steer · {Keys(Reverse)} reverse · " +
             $"{Keys(FastSteer)} fast steer · {Keys(TurnAround)} turn-around · {Keys(CameraNext)} camera · {Keys(LookBack)} look back · {Keys(Pause)} pause",
    };

    /// <summary>A preset's keys per driving action.</summary>
    public static Dictionary<string, Key[]> PresetMap(ControlPreset preset) => preset switch
    {
        ControlPreset.ClassicDdPc => new Dictionary<string, Key[]>
        {
            [Accelerate] = new[] { Key.A }, [Brake] = new[] { Key.Z }, [Reverse] = new Key[0],
            [SteerLeft] = new[] { Key.Left }, [SteerRight] = new[] { Key.Right },
            [FastSteer] = new[] { Key.X }, [TurnAround] = new[] { Key.Space },
            [CameraNext] = new[] { Key.F2 }, [LookBack] = new[] { Key.V },
            [Pause] = new[] { Key.P, Key.Escape }, [Standings] = new[] { Key.Tab },
        },
        ControlPreset.Arrows => new Dictionary<string, Key[]>
        {
            [Accelerate] = new[] { Key.Up }, [Brake] = new[] { Key.Down }, [Reverse] = new[] { Key.Z },
            [SteerLeft] = new[] { Key.Left }, [SteerRight] = new[] { Key.Right },
            [FastSteer] = new[] { Key.X }, [TurnAround] = new[] { Key.Space },
            [CameraNext] = new[] { Key.C }, [LookBack] = new[] { Key.V },
            [Pause] = new[] { Key.Escape, Key.P }, [Standings] = new[] { Key.Tab },
        },
        ControlPreset.Wasd => new Dictionary<string, Key[]>
        {
            [Accelerate] = new[] { Key.W }, [Brake] = new[] { Key.S }, [Reverse] = new[] { Key.J },
            [SteerLeft] = new[] { Key.A }, [SteerRight] = new[] { Key.D },
            [FastSteer] = new[] { Key.K }, [TurnAround] = new[] { Key.Space },
            [CameraNext] = new[] { Key.L }, [LookBack] = new[] { Key.V },
            [Pause] = new[] { Key.Escape, Key.P }, [Standings] = new[] { Key.Tab },
        },
        _ => GameActions.ToDictionary(a => a, a => CustomMap.TryGetValue(a, out var k) ? k.ToArray() : PresetMap(ControlPreset.Arrows)[a]),
    };

    public static void Apply(ControlPreset preset)
    {
        Current = preset;
        var map = PresetMap(preset);
        foreach (string action in GameActions) Bind(action, map[action]);
        // The pit mini-game: the arrows and the preset's steering keys choose, Enter or Space repairs.
        Bind(PitPrev, map[SteerLeft].Contains(Key.Left) ? map[SteerLeft] : map[SteerLeft].Append(Key.Left).ToArray());
        Bind(PitNext, map[SteerRight].Contains(Key.Right) ? map[SteerRight] : map[SteerRight].Append(Key.Right).ToArray());
        Bind(PitRepair, new[] { Key.Enter, Key.KpEnter, Key.Space });
        // Developer keys, identical in every preset.
        Bind(DebugPanel, new[] { Key.F1 });
        Bind(NextPreset, new[] { Key.F3 });
        Bind(ResetCar, new[] { Key.F5 });
    }

    /// <summary>
    /// Binds <paramref name="key"/> as the main key of <paramref name="action"/> in <paramref name="map"/>. Another
    /// action that used it gets this action's old main key instead (a swap), so no key drives two things.
    /// Returns the action that lost the key, if any.
    /// </summary>
    public static string? Rebind(Dictionary<string, Key[]> map, string action, Key key)
    {
        var old = map[action];
        Key? previous = old.Length > 0 ? old[0] : null;
        string? other = map.Keys.FirstOrDefault(a => a != action && map[a].Contains(key));
        if (other != null)
        {
            var keys = map[other].Where(k => k != key).ToList();
            if (previous is Key p && !keys.Contains(p)) keys.Insert(0, p);
            map[other] = keys.ToArray();
        }
        map[action] = new[] { key }.Concat(old.Skip(1).Where(k => k != key)).ToArray();
        return other;
    }

    /// <summary>True when any driving key is a Shift (five presses open the Windows Sticky Keys dialog, plan §14.2).</summary>
    public static bool UsesShift(Dictionary<string, Key[]> map) => map.Values.Any(keys => keys.Any(k => k == Key.Shift));

    /// <summary>The first key bound to an action, for on-screen hints ("↑", "W").</summary>
    public static string KeyName(string action)
    {
        if (!InputMap.HasAction(action)) return "?";
        foreach (var e in InputMap.ActionGetEvents(action))
            if (e is InputEventKey k) return KeyLabel(k.PhysicalKeycode);
        return "?";
    }

    /// <summary>All keys of an action in the current layout, e.g. "ESC / P".</summary>
    public static string Keys(string action, Dictionary<string, Key[]>? map = null)
    {
        var keys = (map ?? PresetMap(Current)).TryGetValue(action, out var k) ? k : System.Array.Empty<Key>();
        return keys.Length == 0 ? "—" : string.Join(" / ", keys.Select(KeyLabel));
    }

    public static string KeyLabel(Key key) => key switch
    {
        Key.Up => "↑", Key.Down => "↓", Key.Left => "←", Key.Right => "→",
        Key.Escape => "ESC", Key.Space => "SPACE", Key.Enter => "ENTER", Key.KpEnter => "NUM ENTER", Key.Tab => "TAB",
        Key.Backspace => "BACKSPACE", Key.Ctrl => "CTRL", Key.Alt => "ALT", Key.Shift => "SHIFT",
        _ => OS.GetKeycodeString(key).ToUpperInvariant(),
    };

    public static ControlPreset Next(ControlPreset p) => (ControlPreset)(((int)p + 1) % 4);

    public static ControlPreset Parse(string? name) =>
        System.Enum.TryParse(name, out ControlPreset p) ? p : ControlPreset.Arrows;

    private static void Bind(string action, Key[] keys)
    {
        if (InputMap.HasAction(action)) InputMap.EraseAction(action);
        InputMap.AddAction(action, 0.2f);
        foreach (var key in keys)
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }
}
