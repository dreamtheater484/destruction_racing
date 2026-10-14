using System.Collections.Generic;
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
}

public static class Controls
{
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

    public static ControlPreset Current { get; private set; } = ControlPreset.Arrows;

    private static readonly string[] GameActions =
    {
        Accelerate, Brake, Reverse, SteerLeft, SteerRight, FastSteer, TurnAround, CameraNext, LookBack, Pause, Standings,
    };

    public static string Describe(ControlPreset preset) => preset switch
    {
        ControlPreset.ClassicDdPc => "Classic DD PC: A gas · Z brake/reverse · ←→ steer · X fast steer · Space turn-around · F2 camera · V look back · P pause",
        ControlPreset.Arrows => "Arrows: ↑ gas · ↓ brake/reverse · ←→ steer · Z reverse · X fast steer · Space turn-around · C camera · V look back · Esc pause",
        _ => "WASD: W gas · S brake/reverse · A/D steer · J reverse · K fast steer · Space turn-around · L camera · V look back · Esc pause",
    };

    public static void Apply(ControlPreset preset)
    {
        Current = preset;
        var map = preset switch
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
            _ => new Dictionary<string, Key[]>
            {
                [Accelerate] = new[] { Key.W }, [Brake] = new[] { Key.S }, [Reverse] = new[] { Key.J },
                [SteerLeft] = new[] { Key.A }, [SteerRight] = new[] { Key.D },
                [FastSteer] = new[] { Key.K }, [TurnAround] = new[] { Key.Space },
                [CameraNext] = new[] { Key.L }, [LookBack] = new[] { Key.V },
                [Pause] = new[] { Key.Escape, Key.P }, [Standings] = new[] { Key.Tab },
            },
        };

        foreach (string action in GameActions) Bind(action, map[action]);
        // Developer keys, identical in every preset.
        Bind(DebugPanel, new[] { Key.F1 });
        Bind(NextPreset, new[] { Key.F3 });
        Bind(ResetCar, new[] { Key.F5 });
    }

    public static ControlPreset Next(ControlPreset p) => (ControlPreset)(((int)p + 1) % 3);

    private static void Bind(string action, Key[] keys)
    {
        if (InputMap.HasAction(action)) InputMap.EraseAction(action);
        InputMap.AddAction(action, 0.2f);
        foreach (var key in keys)
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }
}
