using DestructionRacing.Core;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.UI;

/// <summary>Grey-box HUD for the driving test: speed, gear, view, controls help and the pause overlay.</summary>
public partial class DriveHud : CanvasLayer
{
    public Car? Target { get; set; }
    public System.Func<string>? ViewName { get; set; }

    private Label _speed = null!;
    private Label _info = null!;
    private Label _help = null!;
    private Label _pause = null!;

    public override void _Ready()
    {
        // Above the screen effects (ScreenFx, layer 5), so the grain, the flash and the blur leave the HUD alone.
        Layer = 7;
        ProcessMode = ProcessModeEnum.Always;
        _speed = MakeLabel(48, new Vector2(40, 30));
        _info = MakeLabel(20, new Vector2(42, 96));
        _help = MakeLabel(18, new Vector2(40, 0));
        _help.AnchorTop = 1f;
        _help.AnchorBottom = 1f;
        _help.OffsetTop = -44f;
        _pause = MakeLabel(40, Vector2.Zero);
        _pause.SetAnchorsPreset(Control.LayoutPreset.Center);
        _pause.HorizontalAlignment = HorizontalAlignment.Center;
        _pause.Text = "PAUSED\nEsc / P  resume   ·   Q  quit";
        _pause.Visible = false;
    }

    private Label MakeLabel(int size, Vector2 position)
    {
        var label = new Label { Position = position };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", Colors.White);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        label.AddThemeConstantOverride("outline_size", 6);
        AddChild(label);
        return label;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsEcho()) return;
        if (e.IsActionPressed(Controls.Pause))
        {
            GetTree().Paused = !GetTree().Paused;
            _pause.Visible = GetTree().Paused;
            GetViewport().SetInputAsHandled();
        }
        else if (GetTree().Paused && e is InputEventKey { PhysicalKeycode: Key.Q, Pressed: true })
        {
            DevTools.QuitClean(GetTree(), 0);
        }
        else if (e.IsActionPressed(Controls.NextPreset))
        {
            Controls.Apply(Controls.Next(Controls.Current));
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (Target == null) return;
        string gear = Target.Gear < 0 ? "R" : Target.Gear.ToString();
        _speed.Text = $"{Target.SpeedKmh:0} km/h";
        _info.Text = $"Gear {gear}   {Target.Rpm:0} rpm   View: {ViewName?.Invoke()}" +
                     (Target.LastControls.FastSteer ? "   FAST STEER" : "") +
                     (Target.TurningAround ? "   TURN-AROUND" : "");
        _help.Text = Controls.Describe(Controls.Current) + "   ·   F3 next layout · F1 tuning · F5 reset car";
    }
}
