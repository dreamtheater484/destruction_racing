using System;
using DestructionRacing.Rules;
using DestructionRacing.Vehicles;
using Godot;

namespace DestructionRacing.UI;

/// <summary>
/// The DD damage diagram (plan §13, lower right): a top view of the car with Front, Rear, Left, Right and the
/// four wheels, each green → yellow → orange → red → black, flashing when hit. A lost wheel is an empty outline.
/// </summary>
public partial class DamageDiagram : Control
{
    public Car? Target { get; set; }

    private readonly float[] _pulse = new float[4];

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(120, 210);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Pulse(DamageZone zone) => _pulse[(int)zone] = 1f;

    public static Color HealthColor(float health) => health switch
    {
        <= 0f => new Color(0.05f, 0.05f, 0.05f),
        < 25f => new Color(0.85f, 0.10f, 0.08f),
        < 50f => new Color(0.95f, 0.50f, 0.08f),
        < 75f => new Color(0.95f, 0.85f, 0.10f),
        _ => new Color(0.20f, 0.80f, 0.25f),
    };

    public override void _Process(double delta)
    {
        for (int i = 0; i < 4; i++) _pulse[i] = MathF.Max(0f, _pulse[i] - (float)delta * 3f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var d = Target?.Damage;
        Color Zone(DamageZone z) =>
            HealthColor(d?.Health(z) ?? 100f).Lerp(Colors.White, _pulse[(int)z] * 0.8f);

        // Body: 60 × 150 centred, with side strips and front/rear blocks.
        float cx = Size.X * 0.5f, top = 30f, w = 60f, h = 150f;
        var outline = new Color(0f, 0f, 0f, 0.85f);
        DrawRect(new Rect2(cx - w / 2 - 4, top - 4, w + 8, h + 8), new Color(0f, 0f, 0f, 0.45f));
        DrawRect(new Rect2(cx - w / 2, top, w, 38), Zone(DamageZone.Front));
        DrawRect(new Rect2(cx - w / 2, top + h - 38, w, 38), Zone(DamageZone.Rear));
        DrawRect(new Rect2(cx - w / 2, top + 40, 16, h - 80), Zone(DamageZone.Left));
        DrawRect(new Rect2(cx + w / 2 - 16, top + 40, 16, h - 80), Zone(DamageZone.Right));
        DrawRect(new Rect2(cx - w / 2 + 18, top + 40, w - 36, h - 80), new Color(0.12f, 0.12f, 0.14f, 0.9f)); // cabin
        DrawRect(new Rect2(cx - w / 2, top, w, h), outline, filled: false, width: 2f);

        // Wheels outside the body.
        var wheels = new (Rules.Wheel Wheel, float X, float Y)[]
        {
            (Rules.Wheel.FrontLeft, cx - w / 2 - 14, top + 14),
            (Rules.Wheel.FrontRight, cx + w / 2 + 2, top + 14),
            (Rules.Wheel.RearLeft, cx - w / 2 - 14, top + h - 44),
            (Rules.Wheel.RearRight, cx + w / 2 + 2, top + h - 44),
        };
        foreach (var (wheel, x, y) in wheels)
        {
            var r = new Rect2(x, y, 12, 30);
            if (d != null && d.IsWheelLost(wheel))
            {
                DrawRect(r, new Color(0.9f, 0.1f, 0.1f), filled: false, width: 2f);
                continue;
            }
            DrawRect(r, HealthColor(d?.WheelHealth(wheel) ?? 100f));
            DrawRect(r, outline, filled: false, width: 1.5f);
        }
    }
}
