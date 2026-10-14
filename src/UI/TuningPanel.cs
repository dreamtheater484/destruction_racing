using System;
using System.Collections.Generic;
using DestructionRacing.Core;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.UI;

/// <summary>
/// Developer tuning panel (F1): live telemetry plus sliders for the handling tunables (plan §15.2).
/// Changes apply immediately; "Save" writes user://car_tuning.json, which the driving test loads on start.
/// </summary>
public partial class TuningPanel : CanvasLayer
{
    public const string TuningPath = "user://car_tuning.json";

    public Car? Target { get; set; }
    public PlayerInput? PlayerControls { get; set; }

    private PanelContainer _panel = null!;
    private Label _telemetry = null!;

    private readonly record struct Param(string Name, float Min, float Max, float Step, Func<float> Get, Action<float> Set);

    public override void _Ready()
    {
        Layer = 10;
        _panel = new PanelContainer { Visible = false, Position = new Vector2(1490, 20), CustomMinimumSize = new Vector2(410, 0) };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.72f), ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 10, ContentMarginBottom = 10 });
        AddChild(_panel);

        var box = new VBoxContainer();
        _panel.AddChild(box);
        _telemetry = new Label();
        _telemetry.AddThemeFontSizeOverride("font_size", 14);
        box.AddChild(_telemetry);

        foreach (var p in BuildParams()) box.AddChild(MakeSlider(p));

        var buttons = new HBoxContainer();
        box.AddChild(buttons);
        var save = new Button { Text = "Save" };
        save.Pressed += () => { Target?.Tuning.Save(TuningPath); GD.Print($"[Tuning] saved {ProjectSettings.GlobalizePath(TuningPath)}"); };
        var reset = new Button { Text = "Defaults" };
        reset.Pressed += () => { Target?.ApplyTuning(new CarTuning()); Rebuild(); };
        buttons.AddChild(save);
        buttons.AddChild(reset);
    }

    private void Rebuild()
    {
        var box = (VBoxContainer)_panel.GetChild(0);
        var keep = new List<Node> { box.GetChild(0), box.GetChild(box.GetChildCount() - 1) };
        foreach (Node child in box.GetChildren())
            if (!keep.Contains(child)) { box.RemoveChild(child); child.QueueFree(); }
        int index = 1;
        foreach (var p in BuildParams())
        {
            var row = MakeSlider(p);
            box.AddChild(row);
            box.MoveChild(row, index++);
        }
    }

    private IEnumerable<Param> BuildParams()
    {
        CarTuning T() => Target!.Tuning;
        void Reapply() => Target!.ApplyTuning(Target.Tuning);
        yield return new("Engine power (kW)", 60, 600, 5, () => T().EnginePower / 1000f, v => T().EnginePower = v * 1000f);
        yield return new("Launch force (kN)", 5, 25, 0.5f, () => T().MaxDriveForce / 1000f, v => T().MaxDriveForce = v * 1000f);
        yield return new("Drive grip ×", 1, 4, 0.05f, () => T().LongitudinalGrip, v => T().LongitudinalGrip = v);
        yield return new("Front grip", 0.5f, 1.6f, 0.01f, () => T().FrontGrip, v => T().FrontGrip = v);
        yield return new("Rear grip", 0.5f, 1.6f, 0.01f, () => T().RearGrip, v => T().RearGrip = v);
        yield return new("Tyre stiffness", 3, 16, 0.1f, () => T().TyreStiffness, v => T().TyreStiffness = v);
        yield return new("Mass (kg)", 800, 2200, 10, () => T().Mass, v => { T().Mass = v; Reapply(); });
        yield return new("CoM height", -0.6f, 0.2f, 0.01f, () => T().CenterOfMassHeight, v => { T().CenterOfMassHeight = v; Reapply(); });
        yield return new("Yaw inertia scale", 0.4f, 1.5f, 0.01f, () => T().YawInertiaScale, v => { T().YawInertiaScale = v; Reapply(); });
        yield return new("Lateral force height", 0, 1, 0.01f, () => T().LateralForceHeight, v => T().LateralForceHeight = v);
        yield return new("Max steer low (°)", 15, 45, 0.5f, () => T().MaxSteerLowSpeed, v => T().MaxSteerLowSpeed = v);
        yield return new("Max steer high (°)", 4, 25, 0.5f, () => T().MaxSteerHighSpeed, v => T().MaxSteerHighSpeed = v);
        yield return new("Counter-steer assist", 0, 1, 0.05f, () => T().CounterSteerAssist, v => T().CounterSteerAssist = v);
        yield return new("Traction control", 0.5f, 1.2f, 0.01f, () => T().TractionControl, v => T().TractionControl = v);
        yield return new("Brake force (kN)", 5, 30, 0.5f, () => T().BrakeForce / 1000f, v => T().BrakeForce = v * 1000f);
        yield return new("Spring rate (kN/m)", 15, 80, 1, () => T().SpringRate / 1000f, v => T().SpringRate = v * 1000f);
        yield return new("Anti-roll front (kN/m)", 0, 40, 0.5f, () => T().AntiRollFront / 1000f, v => T().AntiRollFront = v * 1000f);
        yield return new("Anti-roll rear (kN/m)", 0, 40, 0.5f, () => T().AntiRollRear / 1000f, v => T().AntiRollRear = v * 1000f);
        yield return new("Turn-around torque (kNm)", 2, 30, 0.5f, () => T().TurnAroundTorque / 1000f, v => T().TurnAroundTorque = v * 1000f);
        if (PlayerControls != null)
        {
            yield return new("Steer ramp-in (/s)", 1, 12, 0.1f, () => PlayerControls.SteerRampIn, v => PlayerControls.SteerRampIn = v);
            yield return new("Steer return (/s)", 2, 20, 0.1f, () => PlayerControls.SteerReturn, v => PlayerControls.SteerReturn = v);
        }
    }

    private Control MakeSlider(Param p)
    {
        var row = new HBoxContainer();
        var name = new Label { Text = p.Name, CustomMinimumSize = new Vector2(190, 0) };
        name.AddThemeFontSizeOverride("font_size", 13);
        var value = new Label { CustomMinimumSize = new Vector2(60, 0) };
        value.AddThemeFontSizeOverride("font_size", 13);
        var slider = new HSlider { MinValue = p.Min, MaxValue = p.Max, Step = p.Step, CustomMinimumSize = new Vector2(140, 0), FocusMode = Control.FocusModeEnum.None };
        slider.Value = p.Get();
        value.Text = p.Get().ToString("0.###");
        slider.ValueChanged += v => { p.Set((float)v); value.Text = ((float)v).ToString("0.###"); };
        row.AddChild(name);
        row.AddChild(slider);
        row.AddChild(value);
        return row;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed(Controls.DebugPanel) && !e.IsEcho())
        {
            _panel.Visible = !_panel.Visible;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (!_panel.Visible || Target == null) return;
        var c = Target.LastControls;
        _telemetry.Text =
            $"Speed {Target.SpeedKmh:0.0} km/h  fwd {Target.ForwardSpeed:0.0} m/s\n" +
            $"Steer {Target.SteerAngleDeg:0.0}°  input {c.Steer:0.00}  body slip {Target.BodySlipDeg:0.0}°\n" +
            $"Slip F {Target.FrontSlipDeg:0.0}°  R {Target.RearSlipDeg:0.0}°  wheels down {Target.WheelsGrounded}\n" +
            $"Throttle {c.Throttle:0.00}  brake {c.Brake:0.00}  rev {(c.Reverse ? 1 : 0)}  yaw {Mathf.RadToDeg(Target.AngularVelocity.Y):0}°/s";
    }
}
